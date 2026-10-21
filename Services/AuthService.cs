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
    private readonly IPasswordHasher<Usuarios> _passwordHasher;
    private readonly ILogger<AuthService> _logger;

    /// <summary>
    /// Hash ficticio que se verifica cuando el correo no existe, para que esa respuesta tarde lo mismo que una
    /// contraseña incorrecta y no se pueda adivinar qué correos están registrados midiendo el tiempo.
    /// </summary>
    private readonly Lazy<string> _hashFicticio;

    public AuthService(
        FlyVibeMexDbContext db,
        IPasswordHasher<Usuarios> passwordHasher,
        ILogger<AuthService> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _logger = logger;
        _hashFicticio = new Lazy<string>(() => _passwordHasher.HashPassword(new Usuarios(), Guid.NewGuid().ToString("N")));
    }

    /// <inheritdoc />
    public string NormalizarCorreo(string correo) => correo.Trim().ToLowerInvariant();

    /// <inheritdoc />
    public string GenerarHash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        return _passwordHasher.HashPassword(new Usuarios(), password);
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
            var candidato = await _db.Usuarios
                .AsNoTracking()
                .Where(u => u.correo == correoNormalizado && u.activo)
                .Select(u => new
                {
                    u.id,
                    u.nombre,
                    u.correo,
                    u.id_rol,
                    u.password_hash
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (candidato is null)
            {
                // Mismo costo que una verificación real (ver _hashFicticio).
                _passwordHasher.VerifyHashedPassword(new Usuarios(), _hashFicticio.Value, password);
                _logger.LogInformation("Inicio de sesión rechazado: credenciales inválidas.");
                return ResultadoAutenticacion.Invalido();
            }

            // 3) Verificación del hash EN MEMORIA (la contraseña nunca se compara dentro de SQL).
            PasswordVerificationResult verificacion;
            try
            {
                verificacion = _passwordHasher.VerifyHashedPassword(
                    new Usuarios { id = candidato.id },
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
                new UsuarioSesion(candidato.id, candidato.nombre, candidato.correo, candidato.id_rol));
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

    /// <summary>Re-hashea la contraseña con los parámetros actuales. Un fallo aquí no impide iniciar sesión.</summary>
    private async Task ActualizarHashAsync(ulong idUsuario, string password, CancellationToken cancellationToken)
    {
        try
        {
            var nuevoHash = _passwordHasher.HashPassword(new Usuarios { id = idUsuario }, password);

            // ExecuteUpdate también es parametrizado: UPDATE usuarios SET password_hash = @p WHERE id = @id
            await _db.Usuarios
                .Where(u => u.id == idUsuario)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.password_hash, nuevoHash), cancellationToken);
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
