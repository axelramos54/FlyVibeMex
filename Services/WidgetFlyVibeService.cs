// ███ ESTE ARCHIVO VA EN EL PROYECTO DE FLYVIBEMEX ███  (no en GoldStay)
// Ruta: Services/WidgetFlyVibeService.cs

using System.Security.Claims;
using System.Text;
using FlyVibeMex.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FlyVibeMex.Services;

/// <summary>
/// Arma la URL del iframe de GoldStay que se incrusta en FlyVibeMex (FlyVibeMex como ANFITRIÓN).
/// </summary>
public interface IWidgetFlyVibeService
{
    /// <summary>
    /// URL para el iframe:
    ///   · Cliente con sesión → <c>/Autenticacion/LoginSSO?token=…</c> con un token recién firmado.
    ///   · Visitante sin sesión, o sin llave configurada → <c>/Widget/Reserva</c> (modo público).
    /// Devuelve null solo si no hay ni dirección de GoldStay configurada.
    /// </summary>
    string? ConstruirUrlWidget(ClaimsPrincipal? usuario);
}

/// <summary>
/// Implementación: firma un JWT HS256 con el correo del cliente en sesión y lo pega a la URL de GoldStay.
///
/// ============================  REGLAS QUE NO SE PUEDEN RELAJAR  ============================
///   · UN TOKEN POR CARGA DE PÁGINA. Se firma al pintar la vista y no se guarda: GoldStay marca cada jti como
///     consumido y el segundo intento se rechaza. Por lo mismo, la página que lleva el widget NO se puede
///     cachear: el HTML cacheado traería un token ya usado (o vencido).
///   · exp DE MINUTOS, NO DE HORAS. GoldStay acepta 5 minutos como máximo.
///   · jti NUEVO en cada token (un GUID). Es lo que permite el uso único del otro lado.
///   · EL PAYLOAD NO ESTÁ CIFRADO. Un JWT firmado es Base64 legible: cualquiera que vea la URL del iframe lee
///     su contenido. Por eso solo van el correo y el nombre —lo mínimo para identificar al cliente— y nada
///     más. Ni teléfono, ni id interno, ni rol, ni nada de pagos.
///   · HS256 y nada más: es el único algoritmo que GoldStay acepta.
///   · SOLO CLIENTES. No se firma token para el personal interno (AGENTE, ADMINISTRADOR, SA): su correo no
///     tiene por qué abrir sesión en el sistema de otra empresa. Esas sesiones ven el widget público.
/// ============================================================================================
/// </summary>
public sealed class WidgetFlyVibeService : IWidgetFlyVibeService
{
    private readonly OpcionesWidgetFlyVibe _opciones;
    private readonly ILogger<WidgetFlyVibeService> _logger;

    public WidgetFlyVibeService(IOptions<OpcionesWidgetFlyVibe> opciones, ILogger<WidgetFlyVibeService> logger)
    {
        _opciones = opciones.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public string? ConstruirUrlWidget(ClaimsPrincipal? usuario)
    {
        if (!_opciones.EstaConfigurado)
        {
            _logger.LogWarning("Widget de GoldStay sin configurar: falta WidgetFlyVibe:UrlBase.");
            return null;
        }

        var urlBase = _opciones.UrlBase.TrimEnd('/');
        var urlPublica = urlBase + _opciones.RutaWidgetPublico;

        // Visitante sin sesión: GoldStay le muestra habitaciones y le pedirá iniciar sesión al reservar.
        if (usuario?.Identity?.IsAuthenticated != true)
        {
            return urlPublica;
        }

        // El personal interno no transfiere sesión a otra empresa (ver la nota de la clase).
        if (Roles.EsInterno(usuario))
        {
            return urlPublica;
        }

        if (!_opciones.PuedeFirmar)
        {
            _logger.LogWarning(
                "Widget de GoldStay en modo visitante: falta WidgetFlyVibe:LlaveFirma o es menor a 32 caracteres.");
            return urlPublica;
        }

        var correo = usuario.FindFirst(ClaimTypes.Email)?.Value;

        if (string.IsNullOrWhiteSpace(correo))
        {
            _logger.LogWarning("La sesión no trae claim de correo: el widget de GoldStay va en modo visitante.");
            return urlPublica;
        }

        var nombre = usuario.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;
        var ahora = DateTime.UtcNow;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _opciones.Emisor,
            Audience = _opciones.Audiencia,
            NotBefore = ahora,
            IssuedAt = ahora,
            Expires = ahora.AddMinutes(Math.Clamp(_opciones.VidaTokenMinutos, 1, 5)),
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, correo),
                new Claim(JwtRegisteredClaimNames.Email, correo),
                new Claim(JwtRegisteredClaimNames.Name, nombre),
                // jti nuevo en CADA token: es la base del uso único del otro lado.
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            }),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opciones.LlaveFirma!)),
                SecurityAlgorithms.HmacSha256)
        };

        string token;

        try
        {
            token = new JsonWebTokenHandler().CreateToken(descriptor);
        }
        catch (Exception ex)
        {
            // Nunca se tumba la página por el widget de un tercero: se degrada al modo visitante.
            _logger.LogError(ex, "No se pudo firmar el token del widget de GoldStay.");
            return urlPublica;
        }

        return $"{urlBase}{_opciones.RutaLoginSso}?token={Uri.EscapeDataString(token)}";
    }
}
