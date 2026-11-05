using System.Security.Claims;
using FlyVibeMex.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace FlyVibeMex.Services;

/// <summary>
/// Crea la cookie de sesión de un usuario ya validado. Es el ÚNICO lugar de la aplicación que arma los claims de
/// una sesión nueva (Módulo 1): lo usan <c>HomeController.IniciarSesion</c> (formulario de inicio de sesión) y
/// <c>CrearusuarioController.Index</c> (auto-login al terminar el registro).
/// </summary>
/// <remarks>
/// <para><b>El rol tiene que salir de <see cref="UsuarioSesion.rol"/></b>, que <see cref="AuthService"/> llena con
/// <c>roles_usuarios.nombre</c>. <see cref="ValidacionSesion"/> compara ese claim contra la base de datos en CADA
/// petición: si se armara de otro lado, la sesión se crearía bien y la petición siguiente expulsaría al usuario sin
/// ningún mensaje.</para>
/// <para>Fuera de aquí quedan, a propósito:</para>
/// <list type="bullet">
/// <item><c>MiusuarioController.RenovarNombreEnCookieAsync</c>: no crea una sesión, reemite la existente cambiando
/// solo el nombre y conserva sus <see cref="AuthenticationProperties"/> ("mantener la sesión iniciada"). Unificarlo
/// aquí perdería eso.</item>
/// <item><c>IntegracionController.Entrar</c> (SSO de GoldStay): agrega el claim <c>origen_sesion</c> y nunca es
/// persistente. Se dejó como está porque no se puede probar sin GoldStay; si se cambian los 5 claims de aquí, hay
/// que cambiarlos también allá.</item>
/// </list>
/// </remarks>
public static class SesionCookie
{
    /// <summary>Inicia la sesión de <paramref name="usuario"/> con la cookie de autenticación del sitio.</summary>
    /// <param name="persistente"><c>true</c> = "Mantener la sesión iniciada" (la cookie sobrevive al cerrar el navegador).</param>
    public static Task IniciarSesionAsync(this HttpContext contexto, UsuarioSesion usuario, bool persistente)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(usuario);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, usuario.id.ToString()),
            new(ClaimTypes.Name, usuario.nombre),
            new(ClaimTypes.Email, usuario.correo),
            new(ClaimTypes.Role, usuario.rol),
            new("id_rol", usuario.id_rol.ToString())
        };

        var identidad = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        return contexto.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identidad),
            new AuthenticationProperties { IsPersistent = persistente });
    }
}
