using System.Security.Claims;
using FlyVibeMex.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace FlyVibeMex.Services;

/// <summary>
/// Revisa en CADA petición que la cuenta de la cookie siga activa y con el mismo rol (T4 fase D). Si el SA la
/// desactivó o le cambió el rol, la sesión se cierra en ese momento y la persona vuelve al inicio de sesión.
/// </summary>
/// <remarks>
/// Se conecta en Program.cs: <c>opciones.Events.OnValidatePrincipal = ValidacionSesion.ValidarAsync</c>.
/// Es una consulta pequeña por petición (id + activo + nombre del rol). Si la base de datos no responde, NO se cierra
/// la sesión (la pantalla fallará por su cuenta y el error queda en el log).
/// </remarks>
public static class ValidacionSesion
{
    public static async Task ValidarAsync(CookieValidatePrincipalContext contexto)
    {
        var principal = contexto.Principal;
        if (principal?.Identity?.IsAuthenticated != true) return;

        var http = contexto.HttpContext;
        var logger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ValidacionSesion));

        if (!ulong.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var idUsuario))
        {
            await RechazarAsync(contexto);
            return;
        }
        var rolCookie = principal.FindFirstValue(ClaimTypes.Role);

        try
        {
            var db = http.RequestServices.GetRequiredService<FlyVibeMexDbContext>();
            var actual = await (
                    from u in db.Usuarios.AsNoTracking()
                    join r in db.RolesUsuarios.AsNoTracking() on u.id_rol equals r.id
                    where u.id == idUsuario
                    select new { u.activo, rol = r.nombre })
                .FirstOrDefaultAsync(http.RequestAborted);

            if (actual is null || !actual.activo || actual.rol != rolCookie)
            {
                logger.LogInformation("Sesión del usuario {IdUsuario} cerrada: la cuenta se desactivó o cambió de rol.", idUsuario);
                await RechazarAsync(contexto);
            }
        }
        catch (OperationCanceledException)
        {
            // El navegador canceló la petición: no hay nada que hacer.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo validar la sesión del usuario {IdUsuario}; se conserva.", idUsuario);
        }
    }

    private static async Task RechazarAsync(CookieValidatePrincipalContext contexto)
    {
        contexto.RejectPrincipal();
        await contexto.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
