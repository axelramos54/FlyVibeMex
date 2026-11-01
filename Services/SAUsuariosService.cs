using System.Security.Cryptography;
using FlyVibeMex.Data;
using FlyVibeMex.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace FlyVibeMex.Services;

/// <summary>Implementación de <see cref="ISAUsuariosService"/> con EF Core (consultas LINQ) y bitácora.</summary>
public sealed class SAUsuariosService : ISAUsuariosService
{
    private static readonly string[] RolesInternos = { Roles.Soporte, Roles.Agente, Roles.Administrador, Roles.SA };

    private readonly FlyVibeMexDbContext _db;
    private readonly IAuthService _auth;
    private readonly ILogger<SAUsuariosService> _logger;

    public SAUsuariosService(FlyVibeMexDbContext db, IAuthService auth, ILogger<SAUsuariosService> logger)
    {
        _db = db;
        _auth = auth;
        _logger = logger;
    }

    // =============================================================================================
    // Consultas
    // =============================================================================================

    public async Task<ListaUsuariosViewModel> ListaAsync(FiltroUsuarios filtro, CancellationToken cancellationToken = default)
    {
        filtro.texto = Recortar(filtro.texto, 150);
        filtro.estado = ListaUsuariosViewModel.NormalizarEstado(filtro.estado);
        filtro.rol = Recortar(filtro.rol, 30);

        var q = from u in _db.Usuarios.AsNoTracking()
                join r in _db.RolesUsuarios.AsNoTracking() on u.id_rol equals r.id
                select new { u, rol = r.nombre };

        if (filtro.texto is string texto)
            q = q.Where(x => x.u.correo.Contains(texto) || x.u.telefono.Contains(texto) || x.u.nombre.Contains(texto)
                             || x.u.apellido_paterno.Contains(texto) || x.u.apellido_materno.Contains(texto));
        if (filtro.rol is string rol) q = q.Where(x => x.rol == rol);
        if (filtro.estado == "activos") q = q.Where(x => x.u.activo);
        else if (filtro.estado == "inactivos") q = q.Where(x => !x.u.activo);

        var total = await q.CountAsync(cancellationToken);
        var paginas = Math.Max(1, (int)Math.Ceiling(total / (double)ListaUsuariosViewModel.PorPagina));
        filtro.pagina = Math.Clamp(filtro.pagina, 1, paginas);

        var filas = await q.OrderByDescending(x => x.u.creado_en).ThenByDescending(x => x.u.id)
            .Skip((filtro.pagina - 1) * ListaUsuariosViewModel.PorPagina)
            .Take(ListaUsuariosViewModel.PorPagina)
            .Select(x => new UsuarioListadoItem
            {
                id = x.u.id,
                nombre = x.u.nombre + " " + x.u.apellido_paterno + " " + x.u.apellido_materno,
                correo = x.u.correo,
                telefono = x.u.telefono,
                rol = x.rol,
                activo = x.u.activo,
                creado_en = x.u.creado_en
            })
            .ToListAsync(cancellationToken);

        return new ListaUsuariosViewModel
        {
            Filtro = filtro,
            Total = total,
            Resultados = filas,
            Roles = await OpcionesRolAsync(soloInternos: false, filtro.rol, cancellationToken)
        };
    }

    public async Task<List<SelectListItem>> OpcionesRolAsync(bool soloInternos, string? elegido, CancellationToken cancellationToken = default)
    {
        var roles = await _db.RolesUsuarios.AsNoTracking().Where(r => r.activo).OrderBy(r => r.id).Select(r => r.nombre).ToListAsync(cancellationToken);
        return roles
            .Where(r => !soloInternos || RolesInternos.Contains(r))
            .Select(r => new SelectListItem(r, r, r == elegido))
            .ToList();
    }

    public async Task<UsuarioEdicionViewModel?> EdicionAsync(ulong idCuenta, ulong idSA, CancellationToken cancellationToken = default)
    {
        var fila = await (
                from u in _db.Usuarios.AsNoTracking()
                join r in _db.RolesUsuarios.AsNoTracking() on u.id_rol equals r.id
                where u.id == idCuenta
                select new { u.id, u.nombre, u.apellido_paterno, u.apellido_materno, u.telefono, u.correo, u.activo, u.creado_en, rol = r.nombre })
            .FirstOrDefaultAsync(cancellationToken);
        if (fila is null) return null;

        var movimientos = await SABitacoraService.ItemsAsync(_db,
            _db.Bitacora.AsNoTracking()
                .Where(b => b.entidad == "usuarios" && b.id_entidad == idCuenta)
                .OrderByDescending(b => b.fecha).ThenByDescending(b => b.id)
                .Take(20),
            cancellationToken);

        return new UsuarioEdicionViewModel
        {
            id = fila.id,
            Datos = new UsuarioDatosForm
            {
                nombre = fila.nombre,
                apellido_paterno = fila.apellido_paterno,
                apellido_materno = fila.apellido_materno,
                telefono = fila.telefono
            },
            correo = fila.correo,
            rol = fila.rol,
            activo = fila.activo,
            creado_en = fila.creado_en,
            EsPropia = fila.id == idSA,
            reservaciones = await _db.Reservaciones.AsNoTracking().CountAsync(r => r.id_usuarioglobal == idCuenta, cancellationToken),
            tickets = await _db.SolicitudesSoporte.AsNoTracking().CountAsync(t => t.id_usuarioglobal == idCuenta, cancellationToken),
            Roles = await OpcionesRolAsync(soloInternos: false, fila.rol, cancellationToken),
            Movimientos = movimientos
        };
    }

    public async Task<List<RolListadoItem>> RolesAsync(CancellationToken cancellationToken = default)
    {
        var roles = await _db.RolesUsuarios.AsNoTracking().OrderBy(r => r.id).ToListAsync(cancellationToken);
        var cuentas = await _db.Usuarios.AsNoTracking()
            .GroupBy(u => new { u.id_rol, u.activo })
            .Select(g => new { g.Key.id_rol, g.Key.activo, n = g.Count() })
            .ToListAsync(cancellationToken);

        return roles.Select(r => new RolListadoItem
        {
            id = r.id,
            nombre = r.nombre,
            descripcion = r.descripcion,
            activas = cuentas.Where(c => c.id_rol == r.id && c.activo).Sum(c => c.n),
            inactivas = cuentas.Where(c => c.id_rol == r.id && !c.activo).Sum(c => c.n)
        }).ToList();
    }

    // =============================================================================================
    // Alta y edición
    // =============================================================================================

    public async Task<(ResultadoOperacion resultado, ContrasenaTemporalViewModel? temporal)> CrearInternoAsync(
        UsuarioAltaViewModel datos, ulong idSA, CancellationToken cancellationToken = default)
    {
        var errores = new List<(string campo, string mensaje)>();
        var correo = _auth.NormalizarCorreo(datos.correo ?? string.Empty);
        var telefono = (datos.telefono ?? string.Empty).Trim();

        if (!RolesInternos.Contains(datos.rol))
            errores.Add((nameof(datos.rol), "Elige un rol interno (SOPORTE, AGENTE, ADMINISTRADOR o SA)."));
        var rol = await _db.RolesUsuarios.AsNoTracking().Where(r => r.nombre == datos.rol && r.activo)
            .Select(r => new { r.id, r.nombre }).FirstOrDefaultAsync(cancellationToken);
        if (rol is null && errores.Count == 0)
            errores.Add((nameof(datos.rol), "Ese rol no existe o está inactivo."));
        if (await _db.Usuarios.AsNoTracking().AnyAsync(u => u.correo == correo, cancellationToken))
            errores.Add((nameof(datos.correo), "Ya existe una cuenta con ese correo."));
        if (await _db.Usuarios.AsNoTracking().AnyAsync(u => u.telefono == telefono, cancellationToken))
            errores.Add((nameof(datos.telefono), "Ese teléfono ya está registrado en otra cuenta."));
        if (errores.Count > 0) return (new ResultadoOperacion(false, "Revisa los datos marcados.", errores), null);

        var contrasena = GenerarContrasenaTemporal();
        var cuenta = new UsuariosGlobales
        {
            id_rol = rol!.id,
            nombre = datos.nombre.Trim(),
            apellido_paterno = datos.apellido_paterno.Trim(),
            apellido_materno = datos.apellido_materno.Trim(),
            correo = correo,
            telefono = telefono,
            password_hash = _auth.GenerarHash(contrasena),
            activo = true,
            creado_en = DateTime.Now
        };

        try
        {
            await GestorBd.EnTransaccionAsync(_db, async () =>
            {
                cuenta.id = 0;
                _db.Usuarios.Add(cuenta);
                await _db.SaveChangesAsync(cancellationToken);
                _db.RegistrarBitacora(idSA, "CREAR", "usuarios", cuenta.id, $"Alta de la cuenta interna {correo} con rol {rol.nombre}.");
                await _db.SaveChangesAsync(cancellationToken);
            }, cancellationToken);
        }
        catch (Exception ex) when (GestorBd.EsErrorDeBd(ex))
        {
            // Lo más común: otra persona registró el mismo correo o teléfono al mismo tiempo (UNIQUE).
            _logger.LogError(ex, "No se pudo crear la cuenta interna {Correo}.", correo);
            return (ResultadoOperacion.Falla("No se pudo crear la cuenta. Revisa que el correo y el teléfono no estén registrados."), null);
        }

        _logger.LogInformation("SA {IdSA}: alta de la cuenta interna {IdCuenta} ({Rol}).", idSA, cuenta.id, rol.nombre);
        return (ResultadoOperacion.Ok($"Cuenta de {cuenta.nombre} creada."), new ContrasenaTemporalViewModel
        {
            id = cuenta.id,
            nombre = $"{cuenta.nombre} {cuenta.apellido_paterno}",
            correo = correo,
            rol = rol.nombre,
            contrasena = contrasena,
            EsAlta = true
        });
    }

    public async Task<ResultadoOperacion> ActualizarDatosAsync(ulong idCuenta, UsuarioDatosForm datos, ulong idSA, CancellationToken cancellationToken = default)
    {
        var telefono = (datos.telefono ?? string.Empty).Trim();
        if (!await _db.Usuarios.AsNoTracking().AnyAsync(u => u.id == idCuenta, cancellationToken))
            return ResultadoOperacion.Falla("La cuenta no existe.");
        if (await _db.Usuarios.AsNoTracking().AnyAsync(u => u.telefono == telefono && u.id != idCuenta, cancellationToken))
            return new ResultadoOperacion(false, "Revisa los datos marcados.",
                new() { (nameof(datos.telefono), "Ese teléfono ya está registrado en otra cuenta.") });

        var nombre = datos.nombre.Trim();
        var paterno = datos.apellido_paterno.Trim();
        var materno = datos.apellido_materno.Trim();
        try
        {
            await GestorBd.EnTransaccionAsync(_db, async () =>
            {
                await _db.Usuarios.Where(u => u.id == idCuenta)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(u => u.nombre, nombre)
                        .SetProperty(u => u.apellido_paterno, paterno)
                        .SetProperty(u => u.apellido_materno, materno)
                        .SetProperty(u => u.telefono, telefono), cancellationToken);
                _db.RegistrarBitacora(idSA, "ACTUALIZAR", "usuarios", idCuenta, $"Datos actualizados: {nombre} {paterno} {materno}, tel. {telefono}.");
                await _db.SaveChangesAsync(cancellationToken);
            }, cancellationToken);
        }
        catch (Exception ex) when (GestorBd.EsErrorDeBd(ex))
        {
            _logger.LogError(ex, "No se pudieron actualizar los datos de la cuenta {IdCuenta}.", idCuenta);
            return ResultadoOperacion.Falla("No se pudieron guardar los datos. Revisa que el teléfono no esté registrado.");
        }
        return ResultadoOperacion.Ok("Datos guardados.");
    }

    // =============================================================================================
    // Rol, estado y contraseña
    // =============================================================================================

    public async Task<ResultadoOperacion> CambiarRolAsync(ulong idCuenta, string? rol, ulong idSA, CancellationToken cancellationToken = default)
    {
        if (idCuenta == idSA) return ResultadoOperacion.Falla("No puedes cambiar tu propio rol.");

        var nuevo = await _db.RolesUsuarios.AsNoTracking().Where(r => r.nombre == rol && r.activo)
            .Select(r => new { r.id, r.nombre }).FirstOrDefaultAsync(cancellationToken);
        if (nuevo is null) return ResultadoOperacion.Falla("Elige un rol de la lista.");

        var actual = await (
                from u in _db.Usuarios.AsNoTracking()
                join r in _db.RolesUsuarios.AsNoTracking() on u.id_rol equals r.id
                where u.id == idCuenta
                select new { u.id_rol, rol = r.nombre, u.correo })
            .FirstOrDefaultAsync(cancellationToken);
        if (actual is null) return ResultadoOperacion.Falla("La cuenta no existe.");
        if (actual.id_rol == nuevo.id) return ResultadoOperacion.Falla("La cuenta ya tiene ese rol.");

        try
        {
            var idRolAnterior = actual.id_rol;
            var idRolNuevo = nuevo.id;
            await GestorBd.EnTransaccionAsync(_db, async () =>
            {
                var filas = await _db.Usuarios.Where(u => u.id == idCuenta && u.id_rol == idRolAnterior)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.id_rol, idRolNuevo), cancellationToken);
                if (filas != 1) throw new ReglaSAException("La cuenta cambió mientras la editabas. Vuelve a abrirla.");
                await ExigirUnSAActivoAsync(cancellationToken);

                _db.RegistrarBitacora(idSA, "CAMBIAR_ROL", "usuarios", idCuenta, $"Rol de {actual.correo}: {actual.rol} → {nuevo.nombre}.");
                await _db.SaveChangesAsync(cancellationToken);
            }, cancellationToken);
        }
        catch (ReglaSAException ex)
        {
            return ResultadoOperacion.Falla(ex.Message);
        }
        catch (Exception ex) when (GestorBd.EsErrorDeBd(ex))
        {
            _logger.LogError(ex, "No se pudo cambiar el rol de la cuenta {IdCuenta}.", idCuenta);
            return ResultadoOperacion.Falla("No se pudo cambiar el rol. Intenta de nuevo.");
        }

        _logger.LogInformation("SA {IdSA}: rol de la cuenta {IdCuenta} {Anterior} → {Nuevo}.", idSA, idCuenta, actual.rol, nuevo.nombre);
        return ResultadoOperacion.Ok($"Ahora es {nuevo.nombre}. Si tenía la sesión abierta, se cerró y debe volver a entrar.");
    }

    public async Task<ResultadoOperacion> CambiarEstadoAsync(ulong idCuenta, bool activo, ulong idSA, CancellationToken cancellationToken = default)
    {
        if (idCuenta == idSA && !activo) return ResultadoOperacion.Falla("No puedes desactivar tu propia cuenta.");

        var actual = await _db.Usuarios.AsNoTracking().Where(u => u.id == idCuenta)
            .Select(u => new { u.activo, u.correo }).FirstOrDefaultAsync(cancellationToken);
        if (actual is null) return ResultadoOperacion.Falla("La cuenta no existe.");
        if (actual.activo == activo) return ResultadoOperacion.Falla(activo ? "La cuenta ya está activa." : "La cuenta ya está inactiva.");

        try
        {
            await GestorBd.EnTransaccionAsync(_db, async () =>
            {
                var filas = await _db.Usuarios.Where(u => u.id == idCuenta && u.activo != activo)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.activo, activo), cancellationToken);
                if (filas != 1) throw new ReglaSAException("La cuenta cambió mientras la editabas. Vuelve a abrirla.");
                if (!activo) await ExigirUnSAActivoAsync(cancellationToken);

                _db.RegistrarBitacora(idSA, activo ? "REACTIVAR" : "BAJA", "usuarios", idCuenta,
                    $"Cuenta {actual.correo} {(activo ? "activada" : "desactivada")}.");
                await _db.SaveChangesAsync(cancellationToken);
            }, cancellationToken);
        }
        catch (ReglaSAException ex)
        {
            return ResultadoOperacion.Falla(ex.Message);
        }
        catch (Exception ex) when (GestorBd.EsErrorDeBd(ex))
        {
            _logger.LogError(ex, "No se pudo cambiar el estado de la cuenta {IdCuenta}.", idCuenta);
            return ResultadoOperacion.Falla("No se pudo cambiar el estado. Intenta de nuevo.");
        }

        return ResultadoOperacion.Ok(activo
            ? $"Cuenta {actual.correo} activada: ya puede iniciar sesión."
            : $"Cuenta {actual.correo} desactivada: ya no puede iniciar sesión y, si estaba dentro, su sesión se cerró.");
    }

    public async Task<(ResultadoOperacion resultado, ContrasenaTemporalViewModel? temporal)> RestablecerContrasenaAsync(
        ulong idCuenta, ulong idSA, CancellationToken cancellationToken = default)
    {
        if (idCuenta == idSA)
            return (ResultadoOperacion.Falla("Tu propia contraseña se cambia en Mi perfil."), null);

        var cuenta = await (
                from u in _db.Usuarios.AsNoTracking()
                join r in _db.RolesUsuarios.AsNoTracking() on u.id_rol equals r.id
                where u.id == idCuenta
                select new { u.nombre, u.apellido_paterno, u.correo, rol = r.nombre })
            .FirstOrDefaultAsync(cancellationToken);
        if (cuenta is null) return (ResultadoOperacion.Falla("La cuenta no existe."), null);

        var contrasena = GenerarContrasenaTemporal();
        var hash = _auth.GenerarHash(contrasena);
        try
        {
            await GestorBd.EnTransaccionAsync(_db, async () =>
            {
                await _db.Usuarios.Where(u => u.id == idCuenta)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.password_hash, hash), cancellationToken);
                // Nunca se registra la contraseña: solo que se restableció.
                _db.RegistrarBitacora(idSA, "RESTABLECER_PASS", "usuarios", idCuenta, $"Contraseña temporal generada para {cuenta.correo}.");
                await _db.SaveChangesAsync(cancellationToken);
            }, cancellationToken);
        }
        catch (Exception ex) when (GestorBd.EsErrorDeBd(ex))
        {
            _logger.LogError(ex, "No se pudo restablecer la contraseña de la cuenta {IdCuenta}.", idCuenta);
            return (ResultadoOperacion.Falla("No se pudo restablecer la contraseña. Intenta de nuevo."), null);
        }

        return (ResultadoOperacion.Ok("Contraseña restablecida."), new ContrasenaTemporalViewModel
        {
            id = idCuenta,
            nombre = $"{cuenta.nombre} {cuenta.apellido_paterno}",
            correo = cuenta.correo,
            rol = cuenta.rol,
            contrasena = contrasena,
            EsAlta = false
        });
    }

    public async Task<ResultadoOperacion> ActualizarDescripcionRolAsync(ulong idRol, string? descripcion, ulong idSA, CancellationToken cancellationToken = default)
    {
        descripcion = GestorBd.Limpiar(descripcion);
        if (descripcion is { Length: > 150 }) return ResultadoOperacion.Falla("La descripción puede tener máximo 150 caracteres.");

        var rol = await _db.RolesUsuarios.AsNoTracking().Where(r => r.id == idRol).Select(r => r.nombre).FirstOrDefaultAsync(cancellationToken);
        if (rol is null) return ResultadoOperacion.Falla("El rol no existe.");

        try
        {
            await GestorBd.EnTransaccionAsync(_db, async () =>
            {
                await _db.RolesUsuarios.Where(r => r.id == idRol)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.descripcion, descripcion), cancellationToken);
                _db.RegistrarBitacora(idSA, "ACTUALIZAR", "roles_usuarios", idRol, $"Descripción del rol {rol}: {descripcion ?? "(vacía)"}");
                await _db.SaveChangesAsync(cancellationToken);
            }, cancellationToken);
        }
        catch (Exception ex) when (GestorBd.EsErrorDeBd(ex))
        {
            _logger.LogError(ex, "No se pudo guardar la descripción del rol {IdRol}.", idRol);
            return ResultadoOperacion.Falla("No se pudo guardar la descripción. Intenta de nuevo.");
        }
        return ResultadoOperacion.Ok($"Descripción del rol {rol} guardada.");
    }

    // =============================================================================================
    // Ayudantes
    // =============================================================================================

    /// <summary>Dentro de la transacción: si ya no queda ningún SA activo, se deshace todo.</summary>
    private async Task ExigirUnSAActivoAsync(CancellationToken cancellationToken)
    {
        var quedan = await (
                from u in _db.Usuarios
                join r in _db.RolesUsuarios on u.id_rol equals r.id
                where r.nombre == Roles.SA && u.activo
                select u.id)
            .AnyAsync(cancellationToken);
        if (!quedan) throw new ReglaSAException("Debe quedar al menos un SA activo.");
    }

    /// <summary>
    /// Contraseña temporal de 12 caracteres con al menos una letra, un número y un símbolo (cumple la regla de
    /// Mi perfil). Se genera con <see cref="RandomNumberGenerator"/> (aleatorio criptográfico) y sin caracteres
    /// confusos (0/O, 1/l/I).
    /// </summary>
    private static string GenerarContrasenaTemporal()
    {
        const string letras = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz";
        const string digitos = "23456789";
        const string simbolos = "#$%*-+?";
        const string todos = letras + digitos + simbolos;

        var caracteres = new char[12];
        caracteres[0] = letras[RandomNumberGenerator.GetInt32(letras.Length)];
        caracteres[1] = digitos[RandomNumberGenerator.GetInt32(digitos.Length)];
        caracteres[2] = simbolos[RandomNumberGenerator.GetInt32(simbolos.Length)];
        for (var i = 3; i < caracteres.Length; i++)
            caracteres[i] = todos[RandomNumberGenerator.GetInt32(todos.Length)];

        // Revolver (Fisher–Yates) para que la letra, el número y el símbolo no queden siempre al inicio.
        for (var i = caracteres.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (caracteres[i], caracteres[j]) = (caracteres[j], caracteres[i]);
        }
        return new string(caracteres);
    }

    private static string? Recortar(string? texto, int maximo)
    {
        texto = GestorBd.Limpiar(texto);
        return texto is { Length: > 0 } && texto.Length > maximo ? texto[..maximo] : texto;
    }

    /// <summary>Regla del panel SA que no se cumple (el mensaje es para la persona).</summary>
    private sealed class ReglaSAException : Exception
    {
        public ReglaSAException(string mensaje) : base(mensaje) { }
    }
}
