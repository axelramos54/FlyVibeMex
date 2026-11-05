using FlyVibeMex.Data;
using FlyVibeMex.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FlyVibeMex.Services;

/// <summary>
/// Implementación de <see cref="IAuthService"/> con EF Core y <see cref="PasswordHasher{TUser}"/>.
/// </summary>
/// <remarks>
/// <para><b>Por qué no se puede vulnerar con SQL Injection:</b> la consulta es LINQ puro. EF Core envía el correo como
/// parámetro (<c>WHERE [u].[correo] = @__correo_0 AND [u].[activo] = 1</c>), así que un texto como
/// <c>' OR 1=1 --</c> se busca literalmente como correo y simplemente no encuentra a nadie.</para>
/// <para><b>Por qué no se filtran credenciales:</b></para>
/// <list type="bullet">
/// <item>La contraseña NUNCA viaja a SQL Server: la consulta solo filtra por correo y <c>activo</c>; el hash se
/// verifica en memoria, en el servidor web, con <see cref="IPasswordHasher{TUser}"/>.</item>
/// <item>El <c>password_hash</c> se proyecta a una variable local y se descarta; a los controladores solo sale
/// <see cref="UsuarioSesion"/> (id, nombre, correo, id_rol).</item>
/// <item>Respuesta genérica "Credenciales inválidas." tanto si el correo no existe como si la contraseña es
/// incorrecta o el usuario está inactivo, y tiempo de respuesta similar en los tres casos (enumeración de usuarios).</item>
/// <item>Los errores de SQL Server se registran con <see cref="ILogger"/> en el servidor y al navegador solo llega un
/// mensaje genérico.</item>
/// </list>
/// <para>PROHIBIDO en este servicio y en cualquier otro: <c>FromSqlRaw</c>/<c>ExecuteSqlRaw</c> con interpolación o
/// concatenación de strings. Si se requiere SQL manual usar <c>FromSqlInterpolated</c> o <c>SqlParameter</c>.</para>
/// </remarks>
public sealed class AuthService : IAuthService
{
    /// <summary>Longitudes máximas aceptadas (correo = columna varchar(150)). Evita abusos con entradas enormes.</summary>
    private const int LongitudMaximaCorreo = 150;
    private const int LongitudMaximaPassword = 128;

    private readonly FlyVibeMexDbContext _db;
    private readonly IPasswordHasher<UsuariosGlobales> _passwordHasher;
    private readonly ILogger<AuthService> _logger;

    /// <summary>
    /// Hash ficticio que se verifica cuando el correo no existe, para que esa respuesta tarde lo mismo que una
    /// contraseña incorrecta y no se pueda adivinar qué correos están registrados midiendo el tiempo.
    /// </summary>
    private readonly Lazy<string> _hashFicticio;

    public AuthService(
        FlyVibeMexDbContext db,
        IPasswordHasher<UsuariosGlobales> passwordHasher,
        ILogger<AuthService> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _logger = logger;
        _hashFicticio = new Lazy<string>(() => _passwordHasher.HashPassword(new UsuariosGlobales(), Guid.NewGuid().ToString("N")));
    }

    /// <inheritdoc />
    public string NormalizarCorreo(string correo) => correo.Trim().ToLowerInvariant();

    /// <inheritdoc />
    public string GenerarHash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        return _passwordHasher.HashPassword(new UsuariosGlobales(), password);
    }

    /// <inheritdoc />
    public async Task<ResultadoAutenticacion> ValidarCredencialesAsync(
        string? correo,
        string? password,
        CancellationToken cancellationToken = default)
    {
        // 1) Validación de entrada (sin consultar la BD).
        if (string.IsNullOrWhiteSpace(correo) ||
            string.IsNullOrEmpty(password) ||
            correo.Length > LongitudMaximaCorreo ||
            password.Length > LongitudMaximaPassword)
        {
            return ResultadoAutenticacion.Invalido();
        }

        var correoNormalizado = NormalizarCorreo(correo);

        try
        {
            // 2) Consulta 100 % parametrizada con LINQ. Solo se leen las columnas necesarias.
            //    El password_hash se proyecta para verificarlo aquí y NO sale de este método.
            var candidato = await(
                from u in _db.Usuarios.AsNoTracking()
                join r in _db.RolesUsuarios.AsNoTracking() on u.id_rol equals r.id
                where u.correo == correoNormalizado && u.activo
                select new
                {
                    u.id,
                    u.nombre,
                    u.correo,
                    u.id_rol,
                    rol = r.nombre,
                    u.password_hash
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (candidato is null)
            {
                // Mismo costo que una verificación real (ver _hashFicticio).
                _passwordHasher.VerifyHashedPassword(new UsuariosGlobales(), _hashFicticio.Value, password);
                _logger.LogInformation("Inicio de sesión rechazado: credenciales inválidas.");
                return ResultadoAutenticacion.Invalido();
            }

            // 3) Verificación del hash EN MEMORIA (la contraseña nunca se compara dentro de SQL).
            PasswordVerificationResult verificacion;
            try
            {
                verificacion = _passwordHasher.VerifyHashedPassword(
                    new UsuariosGlobales { id = candidato.id },
                    candidato.password_hash,
                    password);
            }
            catch (FormatException)
            {
                // El valor guardado no es un hash válido (p. ej. alguien insertó texto plano a mano).
                _logger.LogWarning("El usuario {IdUsuario} tiene un password_hash con formato inválido.", candidato.id);
                return ResultadoAutenticacion.Invalido();
            }

            if (verificacion == PasswordVerificationResult.Failed)
            {
                _logger.LogInformation("Inicio de sesión rechazado: credenciales inválidas.");
                return ResultadoAutenticacion.Invalido();
            }

            if (verificacion == PasswordVerificationResult.SuccessRehashNeeded)
            {
                // El hash usa parámetros antiguos (p. ej. menos iteraciones): se actualiza sin afectar el login.
                await ActualizarHashAsync(candidato.id, password, cancellationToken);
            }

            // 4) Solo datos públicos de sesión hacia el controlador.
            return ResultadoAutenticacion.Correcto(
                new UsuarioSesion(candidato.id, candidato.nombre, candidato.correo, candidato.id_rol, candidato.rol));
        }
        catch (OperationCanceledException)
        {
            throw; // La petición fue cancelada por el cliente: no es un error de BD.
        }
        catch (Exception ex) when (EsErrorDeBaseDeDatos(ex))
        {
            // El detalle (servidor, número de error) queda SOLO en el log del servidor.
            _logger.LogError(ex, "Error de base de datos durante el inicio de sesión.");
            return ResultadoAutenticacion.FalloServicio();
        }
    }

    /// <summary>Nombres aceptados para el rol de cliente, en orden de preferencia ("Usuario" en el script del equipo,
    /// "CLIENTE" en sql/01_crear_base_datos.sql).</summary>
    private static readonly string[] RolesCliente = { "Usuario", "CLIENTE", "Cliente" };

    /// <inheritdoc />
    public async Task<ResultadoOperacion> RegistrarClienteAsync(CrearUsuarioViewModel datos, CancellationToken cancellationToken = default)
    {
        // Normalización (igual que el login: el correo se busca en minúsculas).
        var correo = NormalizarCorreo(datos.correo ?? string.Empty);
        var telefono = (datos.telefono ?? string.Empty).Trim();
        const string MensajeNoDisponible = "No es posible crear cuentas en este momento. Intenta más tarde.";

        try
        {
            // Correo y teléfono son UNIQUE en la tabla: se revisa antes para dar un mensaje claro junto al campo.
            var errores = new List<(string campo, string mensaje)>();
            if (await _db.Usuarios.AsNoTracking().AnyAsync(u => u.correo == correo, cancellationToken))
                errores.Add((nameof(CrearUsuarioViewModel.correo), "Ya existe una cuenta con ese correo. Inicia sesión."));
            if (await _db.Usuarios.AsNoTracking().AnyAsync(u => u.telefono == telefono, cancellationToken))
                errores.Add((nameof(CrearUsuarioViewModel.telefono), "Ese teléfono ya está registrado en otra cuenta."));
            if (errores.Count > 0)
                return new ResultadoOperacion(false, "Revisa los datos marcados.", errores);

            var roles = await _db.RolesUsuarios
                .AsNoTracking()
                // Comparaciones explícitas (no Contains de una lista) para que funcione en cualquier versión de SQL Server.
                .Where(r => r.activo && (r.nombre == "Usuario" || r.nombre == "CLIENTE" || r.nombre == "Cliente"))
                .Select(r => new { r.id, r.nombre })
                .ToListAsync(cancellationToken);

            var rol = RolesCliente
                .Select(nombre => roles.FirstOrDefault(r => r.nombre == nombre))
                .FirstOrDefault(r => r is not null);
            if (rol is null)
            {
                _logger.LogError("Registro: no existe un rol de cliente activo (Usuario/CLIENTE) en roles_usuarios.");
                return ResultadoOperacion.Falla(MensajeNoDisponible);
            }

            // Una sola lectura del reloj para las dos fechas de control: con dos DateTime.Now distintos la cuenta
            // nacería con `actualizado_en` unos microsegundos después de `creado_en`, y la interfaz mostraría una
            // cuenta recién creada como "modificada".
            var ahora = DateTime.Now;
            var usuario = new UsuariosGlobales
            {
                id_rol = rol.id,
                nombre = datos.nombre.Trim(),
                apellido_paterno = datos.apellido_paterno.Trim(),
                apellido_materno = datos.apellido_materno.Trim(),
                correo = correo,
                telefono = telefono,
                password_hash = GenerarHash(datos.password),
                activo = true,
                creado_en = ahora,
                actualizado_en = ahora,
                imagen_ruta = null
            };

            _db.Usuarios.Add(usuario);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Registro: se creó el usuario {IdUsuario}.", usuario.id);
            return ResultadoOperacion.Ok("¡Tu cuenta se creó correctamente! Ya puedes iniciar sesión.");
        }
        catch (DbUpdateException ex)
        {
            // Lo más común: otra persona registró el mismo correo/teléfono justo al mismo tiempo (UNIQUE).
            _logger.LogWarning(ex, "Registro: no se pudo guardar el usuario.");
            return ResultadoOperacion.Falla("No se pudo crear la cuenta. Revisa que el correo y el teléfono no estén ya registrados.");
        }
        catch (Exception ex) when (EsErrorDeBaseDeDatos(ex))
        {
            // El detalle técnico solo queda en el log del servidor.
            _logger.LogError(ex, "Registro: error de base de datos.");
            return ResultadoOperacion.Falla(MensajeNoDisponible);
        }
    }

    /// <inheritdoc />
    public async Task<bool> CorreoDisponibleAsync(string? correo, CancellationToken cancellationToken = default)
    {
        // El formato y la longitud los valida el propio formulario; aquí no se consulta la BD por algo inválido.
        if (string.IsNullOrWhiteSpace(correo) || correo.Length > LongitudMaximaCorreo)
            return true;

        // Misma normalización y misma condición que RegistrarClienteAsync (cuenta activa o no: el UNIQUE es del correo).
        var correoNormalizado = NormalizarCorreo(correo);
        try
        {
            return !await _db.Usuarios.AsNoTracking().AnyAsync(u => u.correo == correoNormalizado, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw; // La petición la canceló el navegador: no es un error de BD.
        }
        catch (Exception ex) when (EsErrorDeBaseDeDatos(ex))
        {
            _logger.LogWarning(ex, "Registro: no se pudo consultar si un correo está disponible; se responde como disponible.");
            return true;
        }
    }

    /// <summary>Re-hashea la contraseña con los parámetros actuales. Un fallo aquí no impide iniciar sesión.</summary>
    private async Task ActualizarHashAsync(ulong idUsuario, string password, CancellationToken cancellationToken)
    {
        try
        {
            var nuevoHash = _passwordHasher.HashPassword(new UsuariosGlobales { id = idUsuario }, password);

            // ExecuteUpdate también es parametrizado: UPDATE usuarios SET password_hash = @p WHERE id = @id
            // Se estampa `actualizado_en` aunque el re-hash sea automático: la columna significa "cuándo cambió
            // esta fila por última vez", y una excepción sería más difícil de justificar que la regla.
            var ahora = DateTime.Now;
            await _db.Usuarios
                .Where(u => u.id == idUsuario)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.password_hash, nuevoHash)
                    .SetProperty(u => u.actualizado_en, ahora), cancellationToken);
        }
        catch (Exception ex) when (EsErrorDeBaseDeDatos(ex))
        {
            _logger.LogWarning(ex, "No se pudo actualizar el hash de contraseña del usuario {IdUsuario}.", idUsuario);
        }
    }

    /// <summary>Errores esperables al hablar con SQL Server (incluye reintentos agotados y BD sin configurar).</summary>
    private static bool EsErrorDeBaseDeDatos(Exception ex) =>
        ex is SqlException
            or RetryLimitExceededException
            or DbUpdateException
            or TimeoutException
            or InvalidOperationException;
}
