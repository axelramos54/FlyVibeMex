using System.Security.Claims;
using FlyVibeMex.Data;
using FlyVibeMex.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FlyVibeMex.Services;

/// <summary>Implementación de <see cref="IMiCuentaService"/> con EF Core (todas las consultas con LINQ).</summary>
public sealed class MiCuentaService : IMiCuentaService
{
    private readonly FlyVibeMexDbContext _db;
    private readonly IPasswordHasher<UsuariosGlobales> _passwordHasher;
    private readonly ILogger<MiCuentaService> _logger;

    public MiCuentaService(FlyVibeMexDbContext db, IPasswordHasher<UsuariosGlobales> passwordHasher, ILogger<MiCuentaService> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    private static DateOnly Hoy() => DateOnly.FromDateTime(DateTime.Today);

    // =============================================================================================
    // Contexto ("Ver como cliente")
    // =============================================================================================

    public async Task<ContextoCuenta?> ContextoAsync(ClaimsPrincipal usuario, ulong? idCliente, CancellationToken cancellationToken = default)
    {
        if (!ulong.TryParse(usuario.FindFirstValue(ClaimTypes.NameIdentifier), out var idUsuario)) return null;

        var contexto = new ContextoCuenta
        {
            IdUsuario = idUsuario,
            IdCuenta = idUsuario,
            EsInterno = Roles.EsInterno(usuario)
        };

        // Solo un interno puede ver otra cuenta, y solo de un CLIENTE (nunca la de otro interno).
        if (contexto.EsInterno && idCliente is ulong id && id != idUsuario)
        {
            var cliente = await (
                    from u in _db.Usuarios.AsNoTracking()
                    join r in _db.RolesUsuarios.AsNoTracking() on u.id_rol equals r.id
                    where u.id == id && r.nombre == Roles.Cliente
                    select new { u.id, u.nombre, u.apellido_paterno, u.correo })
                .FirstOrDefaultAsync(cancellationToken);

            if (cliente is not null)
            {
                contexto.IdCuenta = cliente.id;
                contexto.IdClienteVisto = cliente.id;
                contexto.NombreClienteVisto = $"{cliente.nombre} {cliente.apellido_paterno}";
                contexto.CorreoClienteVisto = cliente.correo;
                _logger.LogInformation("El usuario interno {IdUsuario} consultó la cuenta del cliente {IdCliente} (solo lectura).",
                    idUsuario, cliente.id);
            }
        }

        return contexto;
    }

    public async Task<ulong?> BuscarClientePorCorreoAsync(string? correo, CancellationToken cancellationToken = default)
    {
        var texto = correo?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(texto) || texto.Length > 150) return null;

        var ids = await (
                from u in _db.Usuarios.AsNoTracking()
                join r in _db.RolesUsuarios.AsNoTracking() on u.id_rol equals r.id
                where u.correo == texto && r.nombre == Roles.Cliente
                select u.id)
            .Take(1)
            .ToListAsync(cancellationToken);
        return ids.Count > 0 ? ids[0] : null;
    }

    // =============================================================================================
    // Perfil y contraseña
    // =============================================================================================

    public async Task<MiPerfilViewModel?> PerfilAsync(ContextoCuenta contexto, CancellationToken cancellationToken = default)
    {
        var datos = await (
                from u in _db.Usuarios.AsNoTracking()
                join r in _db.RolesUsuarios.AsNoTracking() on u.id_rol equals r.id
                where u.id == contexto.IdCuenta
                select new { u.nombre, u.apellido_paterno, u.apellido_materno, u.telefono, u.correo, rol = r.nombre, u.creado_en })
            .FirstOrDefaultAsync(cancellationToken);
        if (datos is null) return null;

        var ahora = DateTime.Now;
        var idCancelada = await _db.EstadosReservacion.AsNoTracking()
            .Where(e => e.es_final)
            .Select(e => e.id)
            .ToListAsync(cancellationToken);

        var proximas = await (
                from r in _db.Reservaciones.AsNoTracking()
                join d in _db.DetallesReservacion.AsNoTracking() on r.id equals d.id_reservacion
                where r.id_usuarioglobal == contexto.IdCuenta && d.fecha_fin >= ahora
                select new { r.id, r.id_estado_reservacion })
            .ToListAsync(cancellationToken);

        var ticketsAbiertos = await _db.SolicitudesSoporte.AsNoTracking()
            .CountAsync(t => t.id_usuarioglobal == contexto.IdCuenta && t.estado != "CERRADA", cancellationToken);

        return new MiPerfilViewModel
        {
            Contexto = contexto,
            Perfil = new PerfilFormViewModel
            {
                nombre = datos.nombre,
                apellido_paterno = datos.apellido_paterno,
                apellido_materno = datos.apellido_materno,
                telefono = datos.telefono
            },
            correo = datos.correo,
            rol = datos.rol,
            creado_en = datos.creado_en,
            reservaciones_proximas = proximas.Where(p => !idCancelada.Contains(p.id_estado_reservacion)).Select(p => p.id).Distinct().Count(),
            tickets_abiertos = ticketsAbiertos
        };
    }

    public async Task<ResultadoOperacion> ActualizarPerfilAsync(ulong idUsuario, PerfilFormViewModel perfil, CancellationToken cancellationToken = default)
    {
        var telefono = perfil.telefono.Trim();

        // teléfono es UNIQUE: se revisa antes para dar un mensaje claro.
        if (await _db.Usuarios.AsNoTracking().AnyAsync(u => u.telefono == telefono && u.id != idUsuario, cancellationToken))
        {
            return new ResultadoOperacion(false, "Revisa los datos.",
                new() { (nameof(perfil.telefono), "Ese teléfono ya está registrado en otra cuenta.") });
        }

        var filas = await _db.Usuarios
            .Where(u => u.id == idUsuario && u.activo)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.nombre, perfil.nombre.Trim())
                .SetProperty(u => u.apellido_paterno, perfil.apellido_paterno.Trim())
                .SetProperty(u => u.apellido_materno, perfil.apellido_materno.Trim())
                .SetProperty(u => u.telefono, telefono), cancellationToken);

        return filas == 1
            ? ResultadoOperacion.Ok("Tus datos se guardaron.")
            : ResultadoOperacion.Falla("No se pudieron guardar tus datos. Vuelve a iniciar sesión.");
    }

    public async Task<ResultadoOperacion> CambiarPasswordAsync(ulong idUsuario, CambioPasswordViewModel datos, CancellationToken cancellationToken = default)
    {
        var hashActual = await _db.Usuarios.AsNoTracking()
            .Where(u => u.id == idUsuario && u.activo)
            .Select(u => u.password_hash)
            .FirstOrDefaultAsync(cancellationToken);
        if (hashActual is null) return ResultadoOperacion.Falla("No se encontró tu cuenta. Vuelve a iniciar sesión.");

        PasswordVerificationResult verificacion;
        try
        {
            verificacion = _passwordHasher.VerifyHashedPassword(new UsuariosGlobales { id = idUsuario }, hashActual, datos.password_actual);
        }
        catch (FormatException)
        {
            verificacion = PasswordVerificationResult.Failed;
        }

        if (verificacion == PasswordVerificationResult.Failed)
        {
            return new ResultadoOperacion(false, "Revisa los datos.",
                new() { (nameof(datos.password_actual), "La contraseña actual no es correcta.") });
        }

        var nuevoHash = _passwordHasher.HashPassword(new UsuariosGlobales { id = idUsuario }, datos.password_nueva);
        await _db.Usuarios
            .Where(u => u.id == idUsuario)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.password_hash, nuevoHash), cancellationToken);

        _logger.LogInformation("El usuario {IdUsuario} cambió su contraseña.", idUsuario);
        return ResultadoOperacion.Ok("Tu contraseña se cambió correctamente.");
    }

    // =============================================================================================
    // Tarjetas
    // =============================================================================================

    public async Task<MisTarjetasViewModel> TarjetasAsync(ContextoCuenta contexto, CancellationToken cancellationToken = default)
    {
        var hoy = Hoy();
        var tarjetas = await _db.TarjetasUsuarios.AsNoTracking()
            .Where(t => t.id_usuariog == contexto.IdCuenta && t.activo)
            .OrderByDescending(t => t.creado_en)
            .Select(t => new TarjetaListadoViewModel
            {
                id = t.id,
                marca = t.marca,
                ultimos4 = t.ultimos4,
                mes_expiracion = t.mes_expiracion,
                anio_expiracion = t.anio_expiracion,
                alias = t.alias
            })
            .ToListAsync(cancellationToken);

        var usosPorTarjeta = await _db.Pagos.AsNoTracking()
            .Join(_db.TarjetasUsuarios.AsNoTracking().Where(t => t.id_usuariog == contexto.IdCuenta),
                  p => p.id_tarjeta, t => t.id, (p, t) => p.id_tarjeta)
            .ToListAsync(cancellationToken);

        foreach (var t in tarjetas)
        {
            t.vigente = PasarelaSimulada.Vigente(t.mes_expiracion, t.anio_expiracion, hoy);
            t.pagos = usosPorTarjeta.Count(id => id == t.id);
        }

        return new MisTarjetasViewModel { Contexto = contexto, Tarjetas = tarjetas };
    }

    public async Task<ResultadoOperacion> AgregarTarjetaAsync(ulong idUsuario, TarjetaNuevaViewModel tarjeta, CancellationToken cancellationToken = default)
    {
        var errores = PasarelaSimulada.ValidarTarjeta(tarjeta.numero_tarjeta, tarjeta.mes_expiracion, tarjeta.anio_expiracion, tarjeta.cvv, Hoy());
        if (errores.Count > 0) return new ResultadoOperacion(false, "Revisa los datos de la tarjeta.", errores);

        var digitos = PasarelaSimulada.SoloDigitos(tarjeta.numero_tarjeta);
        _db.TarjetasUsuarios.Add(new TarjetasUsuarios
        {
            id_usuariog = idUsuario,
            id_institucion = null,
            token_referencia = PasarelaSimulada.NuevoToken(),
            marca = PasarelaSimulada.Marca(digitos)!,
            ultimos4 = digitos[^4..],
            mes_expiracion = tarjeta.mes_expiracion!.Value,
            anio_expiracion = tarjeta.anio_expiracion!.Value,
            alias = string.IsNullOrWhiteSpace(tarjeta.alias) ? null : tarjeta.alias.Trim(),
            activo = true,
            creado_en = DateTime.Now
        });
        await _db.SaveChangesAsync(cancellationToken);
        return ResultadoOperacion.Ok($"Tarjeta terminada en {digitos[^4..]} agregada.");
    }

    public async Task<ResultadoOperacion> CambiarAliasAsync(ulong idUsuario, ulong idTarjeta, string? alias, CancellationToken cancellationToken = default)
    {
        var texto = string.IsNullOrWhiteSpace(alias) ? null : alias.Trim();
        if (texto is { Length: > 40 }) return ResultadoOperacion.Falla("El alias puede tener máximo 40 caracteres.");

        var filas = await _db.TarjetasUsuarios
            .Where(t => t.id == idTarjeta && t.id_usuariog == idUsuario && t.activo)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.alias, texto), cancellationToken);

        return filas == 1 ? ResultadoOperacion.Ok("Alias actualizado.") : ResultadoOperacion.Falla("No encontramos esa tarjeta en tu cuenta.");
    }

    public async Task<ResultadoOperacion> EliminarTarjetaAsync(ulong idUsuario, ulong idTarjeta, CancellationToken cancellationToken = default)
    {
        var filas = await _db.TarjetasUsuarios
            .Where(t => t.id == idTarjeta && t.id_usuariog == idUsuario && t.activo)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.activo, false), cancellationToken);

        return filas == 1 ? ResultadoOperacion.Ok("Tarjeta eliminada.") : ResultadoOperacion.Falla("No encontramos esa tarjeta en tu cuenta.");
    }
}
