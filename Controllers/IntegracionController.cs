using System.Security.Claims;
using FlyVibeMex.Models;
using FlyVibeMex.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FlyVibeMex.Controllers;

/// <summary>
/// Punto de entrada del widget que se incrusta en GoldStay (Fase 3: inicio de sesión transparente).
///
/// Flujo completo:
///   1. El cliente ya tiene sesión en GoldStay.
///   2. GoldStay arma el iframe con  https://fvm…/Integracion/Entrar?token=<JWT>&destino=/Catalogo/Detalle/12
///      El JWT lo firma GoldStay con la llave compartida y lleva el correo del cliente, jti y exp corto.
///   3. Esta acción valida el token (ISsoIntegracionService), crea la MISMA cookie de sesión que el formulario
///      normal y redirige al destino. De ahí en adelante el widget funciona igual que el sitio.
///
/// Decisiones que importan:
///   · Es GET y redirige de inmediato: así el token deja de estar en la barra de direcciones (y por lo tanto
///     fuera de la cabecera Referer que el iframe mandaría a las peticiones siguientes).
///   · Sin [ValidateAntiForgeryToken]: no hay formulario ni cookie previa; la prueba de identidad es la FIRMA.
///   · El destino se valida con Url.IsLocalUrl: sin eso, GoldStay (o cualquiera que armara la URL) podría
///     usar este endpoint como redirector abierto hacia un sitio de phishing, con el prestigio del dominio
///     de FlyVibeMex detrás.
///   · Si el token no sirve NO se muestra un error técnico: se manda al formulario de inicio de sesión, que es
///     lo útil para el cliente (y no le dice a un atacante en qué falló su token).
/// </summary>
[Route("Integracion")]
public sealed class IntegracionController : Controller
{
    /// <summary>Destino por omisión si GoldStay no manda uno.</summary>
    private const string DestinoPredeterminado = "/";

    private readonly ISsoIntegracionService _sso;
    private readonly OpcionesIntegracion _opciones;
    private readonly ILogger<IntegracionController> _logger;

    public IntegracionController(
        ISsoIntegracionService sso,
        IOptions<OpcionesIntegracion> opciones,
        ILogger<IntegracionController> logger)
    {
        _sso = sso;
        _opciones = opciones.Value;
        _logger = logger;
    }

    /// <summary>
    /// GET <c>/Integracion/Entrar?token=…&amp;destino=/Catalogo/Detalle/12</c>.
    /// Inicia la sesión del cliente a partir del token de GoldStay y redirige al destino dentro del sitio.
    /// </summary>
    [HttpGet("Entrar")]
    public async Task<IActionResult> Entrar(string? token, string? destino, CancellationToken cancellationToken)
    {
        // Con el SSO apagado este endpoint no existe para nadie: 404, no un error que confirme que está ahí.
        if (!_opciones.EstaSsoActivo)
        {
            return NotFound();
        }

        var destinoSeguro = !string.IsNullOrWhiteSpace(destino) && Url.IsLocalUrl(destino)
            ? destino
            : DestinoPredeterminado;

        if (!string.IsNullOrWhiteSpace(destino) && destinoSeguro == DestinoPredeterminado)
        {
            // Alguien mandó un destino externo: se ignora y queda constancia.
            _logger.LogWarning("SSO: se ignoró un destino que no es de este sitio.");
        }

        var resultado = await _sso.ValidarTokenAsync(token, cancellationToken);

        if (!resultado.Exitoso)
        {
            // El cliente puede iniciar sesión a mano dentro del widget; el motivo real está en el log.
            return RedirectToAction("IniciarSesion", "Home", new { returnUrl = destinoSeguro });
        }

        var usuario = resultado.Usuario!;

        // Exactamente los mismos claims que HomeController.IniciarSesion: para el resto de la aplicación
        // esta sesión es indistinguible de una normal (ValidacionSesion la sigue revisando en cada petición,
        // así que si el SA desactiva la cuenta, la sesión del widget también se cierra).
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, usuario.id.ToString()),
            new(ClaimTypes.Name, usuario.nombre),
            new(ClaimTypes.Email, usuario.correo),
            new(ClaimTypes.Role, usuario.rol),
            new("id_rol", usuario.id_rol.ToString()),
            // Marca de origen: útil en la bitácora y si algún día se quiere restringir alguna acción
            // (por ejemplo cambiar la contraseña) a las sesiones iniciadas directamente en el sitio.
            new("origen_sesion", "sso-goldstay")
        };

        var identidad = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        // IsPersistent = false: la sesión del widget dura lo que el navegador tenga abierto. Una cookie
        // persistente en un iframe de otro sitio es más de lo que el cliente pidió.
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identidad),
            new AuthenticationProperties { IsPersistent = false });

        return LocalRedirect(destinoSeguro);
    }
}
