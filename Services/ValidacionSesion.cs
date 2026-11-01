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
/// T8.1: es una clase de eventos de la cookie con inyección por constructor (antes era estática y pedía el DbContext
/// con <c>HttpContext.RequestServices</c>). Se conecta en Program.cs con
/// <c>opciones.EventsType = typeof(ValidacionSesion)</c> + <c>AddScoped&lt;ValidacionSesion&gt;()</c>: ASP.NET crea una
/// instancia por petición.
/// El DbContext llega como <c>Lazy&lt;FlyVibeMexDbContext&gt;</c>: solo se crea si hay alguien con sesión. Es una
/// consulta pequeña por petición (activo + nombre del rol). Si la base de datos no responde (o no está configurada),
/// NO se cierra la sesión: la pantalla fallará por su cuenta y el error queda en el log.
/// </remarks>
public sealed class ValidacionSesion : CookieAuthenticationEvents
{
    private readonly Lazy<FlyVibeMexDbContext> _db;
    private readonly ILogger<ValidacionSesion> _logger;

    public ValidacionSesion(Lazy<FlyVibeMexDbContext> db, ILogger<ValidacionSesion> logger)
    {
        _db = db;
        _logger = logger;
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext contexto)
    {
        var principal = contexto.Principal;
        if (principal?.Identity?.IsAuthenticated != true) return;

        if (!ulong.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var idUsuario))
        {
            await RechazarAsync(contexto);
            return;
        }
        var rolCookie = principal.FindFirstValue(ClaimTypes.Role);

        try
        {
            var db = _db.Value;
            var actual = await (
                    from u in db.Usuarios.AsNoTracking()
                    join r in db.RolesUsuarios.AsNoTracking() on u.id_rol equals r.id
                    where u.id == idUsuario
                    select new { u.activo, rol = r.nombre })
                .FirstOrDefaultAsync(contexto.HttpContext.RequestAborted);

            if (actual is null || !actual.activo || actual.rol != rolCookie)
            {
                _logger.LogInformation("Sesión del usuario {IdUsuario} cerrada: la cuenta se desactivó o cambió de rol.", idUsuario);
                await RechazarAsync(contexto);
            }
        }
        catch (OperationCanceledException)
        {
            // El navegador canceló la petición: no hay nada que hacer.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo validar la sesión del usuario {IdUsuario}; se conserva.", idUsuario);
        }
    }

    private static async Task RechazarAsync(CookieValidatePrincipalContext contexto)
    {
        contexto.RejectPrincipal();
        await contexto.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
