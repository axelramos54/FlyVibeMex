using System.Security.Claims;
using System.Text;
using FlyVibeMex.Data;
using FlyVibeMex.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FlyVibeMex.Services;

/// <summary>
/// Implementación de <see cref="ISsoIntegracionService"/>: valida un JWT firmado con HMAC-SHA256 por GoldStay
/// y lo traduce a un usuario local.
///
/// ============================  QUÉ SE VERIFICA Y POR QUÉ  ============================
/// La validación la hace <see cref="JsonWebTokenHandler"/> (biblioteca de Microsoft), no código propio: un
/// verificador de JWT escrito a mano es la fuente clásica de agujeros (aceptar "alg": "none", comparar firmas
/// con != en lugar de tiempo constante, olvidar exp…).
///
///   · FIRMA con la llave compartida → nadie puede fabricar un token sin la llave.
///   · ALGORITMO fijo en HS256 → se cierra la "confusión de algoritmos": sin esta lista, un atacante puede
///     mandar un token con "alg": "none" (sin firma) o firmado con otro esquema y pasar la validación.
///   · iss y aud → el token tiene que venir de GoldStay y estar emitido PARA FlyVibeMex; un token que GoldStay
///     emita para otro proveedor no sirve aquí.
///   · exp obligatorio y vida corta → un token filtrado (historial del navegador, cabecera Referer, logs del
///     servidor: el token viaja en la URL) deja de servir en segundos.
///   · jti obligatorio y de UN SOLO USO → sin esto, quien vea la URL una vez puede repetirla y entrar como ese
///     cliente ("replay"). El identificador se guarda en memoria hasta que el token expira y el segundo intento
///     se rechaza.
///   · ROL: el usuario local tiene que ser CLIENTE. El SSO nunca da acceso al panel interno: si el correo del
///     token resulta ser de un AGENTE, ADMINISTRADOR o SA, se rechaza y esa persona entra por el formulario
///     normal con su contraseña. Así, un token robado —o un GoldStay comprometido— nunca abre la administración.
///   · El usuario DEBE existir y estar activo en la tabla usuarios: el token no crea cuentas. GoldStay no decide
///     quién tiene cuenta en FlyVibeMex, solo prueba quién es.
///
/// La caché de jti es de ESTE proceso: si algún día la app corre en varios servidores con balanceador, hay que
/// pasar esa lista a una caché compartida (o a una tabla), porque si no el mismo token se podría reusar una vez
/// en cada servidor.
/// ======================================================================================
/// </summary>
public sealed class SsoIntegracionService : ISsoIntegracionService
{
    /// <summary>Un JWT razonable no se acerca a esto; corta entradas enormes antes de intentar validarlas.</summary>
    private const int LongitudMaximaToken = 4096;

    private const string PrefijoCacheJti = "sso-jti:";

    private readonly FlyVibeMexDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly OpcionesIntegracion _opciones;
    private readonly IAuthService _auth;
    private readonly ILogger<SsoIntegracionService> _logger;

    public SsoIntegracionService(
        FlyVibeMexDbContext db,
        IMemoryCache cache,
        IOptions<OpcionesIntegracion> opciones,
        IAuthService auth,
        ILogger<SsoIntegracionService> logger)
    {
        _db = db;
        _cache = cache;
        _opciones = opciones.Value;
        _auth = auth;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ResultadoAutenticacion> ValidarTokenAsync(
        string? token,
        CancellationToken cancellationToken = default)
    {
        if (!_opciones.EstaSsoActivo)
        {
            _logger.LogWarning("Se recibió un token de SSO pero la integración no está activa o la llave es inválida.");
            return ResultadoAutenticacion.Invalido();
        }

        if (string.IsNullOrWhiteSpace(token) || token.Length > LongitudMaximaToken)
        {
            return ResultadoAutenticacion.Invalido();
        }

        var parametros = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _opciones.EmisorSso,
            ValidateAudience = true,
            ValidAudience = _opciones.AudienciaSso,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opciones.LlaveFirmaSso!)),
            // Lista blanca de algoritmos: sin esto se aceptarían tokens sin firma o firmados de otra forma.
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(Math.Clamp(_opciones.ToleranciaRelojSegundos, 0, 300))
        };

        var manejador = new JsonWebTokenHandler();
        TokenValidationResult validacion;

        try
        {
            validacion = await manejador.ValidateTokenAsync(token, parametros);
        }
        catch (Exception ex)
        {
            // Token con formato irreconocible: no es un error del servidor, es una entrada inválida.
            _logger.LogWarning(ex, "Token de SSO con formato inválido.");
            return ResultadoAutenticacion.Invalido();
        }

        if (!validacion.IsValid)
        {
            // El detalle (firma incorrecta, expirado, otro emisor…) queda SOLO en el log.
            _logger.LogWarning(validacion.Exception, "Token de SSO rechazado.");
            return ResultadoAutenticacion.Invalido();
        }

        // --- Un solo uso (anti-replay) -----------------------------------------------------------
        var jti = validacion.Claims.TryGetValue(JwtRegisteredClaimNames.Jti, out var valorJti)
            ? valorJti?.ToString()
            : null;

        if (string.IsNullOrWhiteSpace(jti))
        {
            _logger.LogWarning("Token de SSO sin jti: se rechaza (no se puede garantizar un solo uso).");
            return ResultadoAutenticacion.Invalido();
        }

        var claveCache = PrefijoCacheJti + jti;

        if (_cache.TryGetValue(claveCache, out _))
        {
            _logger.LogWarning("Token de SSO reutilizado (jti ya consumido). Posible intento de replay.");
            return ResultadoAutenticacion.Invalido();
        }

        // Se guarda hasta un poco después de la expiración del propio token: pasada esa fecha, la validación
        // de lifetime ya lo rechaza por su cuenta y no hace falta seguir recordándolo.
        var vencimiento = validacion.SecurityToken.ValidTo == default
            ? DateTime.UtcNow.AddSeconds(_opciones.VidaTokenSegundos)
            : validacion.SecurityToken.ValidTo;

        _cache.Set(
            claveCache,
            true,
            vencimiento.AddSeconds(Math.Clamp(_opciones.ToleranciaRelojSegundos, 0, 300) + 5));

        // --- Correo del token -> usuario local ---------------------------------------------------
        var correo = ObtenerPrimerClaim(validacion.Claims, JwtRegisteredClaimNames.Email, ClaimTypes.Email, "correo");

        if (string.IsNullOrWhiteSpace(correo))
        {
            _logger.LogWarning("Token de SSO sin correo: no se puede identificar al usuario.");
            return ResultadoAutenticacion.Invalido();
        }

        var correoNormalizado = _auth.NormalizarCorreo(correo);

        try
        {
            // Misma consulta parametrizada por LINQ que el inicio de sesión normal, sin tocar password_hash.
            var usuario = await (
                from u in _db.Usuarios.AsNoTracking()
                join r in _db.RolesUsuarios.AsNoTracking() on u.id_rol equals r.id
                where u.correo == correoNormalizado && u.activo
                select new UsuarioSesion(u.id, u.nombre, u.correo, u.id_rol, r.nombre))
                .FirstOrDefaultAsync(cancellationToken);

            if (usuario is null)
            {
                // El token es legítimo, pero ese correo no tiene cuenta activa aquí. El SSO NO crea cuentas:
                // el cliente tiene que registrarse una vez en FlyVibeMex.
                _logger.LogInformation("SSO: el correo del token no corresponde a un usuario activo.");
                return ResultadoAutenticacion.Invalido();
            }

            if (!string.Equals(usuario.rol, Roles.Cliente, StringComparison.OrdinalIgnoreCase))
            {
                // Ver la nota de la clase: el SSO jamás abre el panel interno.
                _logger.LogWarning(
                    "SSO rechazado: el usuario {IdUsuario} tiene el rol interno {Rol}.", usuario.id, usuario.rol);
                return ResultadoAutenticacion.Invalido();
            }

            _logger.LogInformation("SSO correcto para el usuario {IdUsuario}.", usuario.id);
            return ResultadoAutenticacion.Correcto(usuario);
        }
        catch (OperationCanceledException)
        {
            throw; // La petición la canceló el cliente: no es un error de BD.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error de base de datos al resolver el usuario de un token de SSO.");
            return ResultadoAutenticacion.FalloServicio();
        }
    }

    /// <summary>Primer claim no vacío de entre varios nombres posibles (el emisor puede usar uno u otro).</summary>
    private static string? ObtenerPrimerClaim(IDictionary<string, object> claims, params string[] nombres)
    {
        foreach (var nombre in nombres)
        {
            if (claims.TryGetValue(nombre, out var valor))
            {
                var texto = valor?.ToString();
                if (!string.IsNullOrWhiteSpace(texto))
                {
                    return texto;
                }
            }
        }

        return null;
    }
}
