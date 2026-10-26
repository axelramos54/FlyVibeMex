using System.Security.Claims;
using FlyVibeMex.Data;
using FlyVibeMex.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FlyVibeMex.Controllers;

/// <summary>
/// Área del cliente: <c>/Miusuario</c> (perfil, compras, reservaciones, tarjetas, cancelar y reembolsar).
/// Requiere sesión iniciada: sin cookie, ASP.NET redirige a <c>/Home/IniciarSesion?ReturnUrl=...</c>.
/// </summary>
/// <remarks>
/// Regla de seguridad: Toda acción que reciba el id de una reservación comprueba que pertenezca al
/// usuario autenticado. El id del usuario SIEMPRE sale de la cookie, nunca del formulario.
/// </remarks>
[Authorize]
public class MiusuarioController : Controller
{
    private const int MotivoMinimo = 10;
    private const int MotivoMaximo = 250;   // reembolsos.motivo es nvarchar(250)

    private readonly FlyVibeMexDbContext _db;

    public MiusuarioController(FlyVibeMexDbContext db)
    {
        _db = db;
    }

    // GET /Miusuario
    public IActionResult Index()
    {
        return View();
    }

    // GET /Miusuario/Miscompras
    public IActionResult Miscompras()
    {
        return View("Miscompras/index");
    }

    // GET /Miusuario/Misreservaciones
    public IActionResult Misreservaciones()
    {
        return View("Misreservaciones/index");
    }

    // GET /Miusuario/Mistarjetas
    public IActionResult Mistarjetas()
    {
        return View("Mistarjetas/index");
    }

    // ============================== CANCELAR / REEMBOLSAR ==============================
    // Los envían los modales de Views/Miusuario/_ModalesCancelacion.cshtml (Mis reservaciones y Mis compras).
    // La doble confirmación (clic + clic sostenido) solo evita accidentes en el navegador:
    // aquí se vuelve a validar todo en el servidor.

    // POST /Miusuario/CancelarReservacion
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelarReservacion(ulong id, string? returnUrl, CancellationToken cancellationToken)
    {
        if (id == 0) return BadRequest();

        var error = await ValidarReservacionPropiaAsync(id, cancellationToken);
        if (error is not null)
        {
            TempData["CancelacionError"] = error;
            return Volver(returnUrl);
        }

        // TODO BD (tarea T3.5, en una transacción con _db.Database.CreateExecutionStrategy()):
        //   1) UPDATE reservaciones SET id_estado_reservacion = (id del estado 'CANCELADA')
        //   2) INSERT historial_estado_reservacion (id_reservacion, id_estado_reservacion, id_usuario_cambio, motivo)
        //   3) Devolver el cupo: cantidad_disponible += cantidad en disponibilidad_servicio por cada detalle (R3)
        TempData["CancelacionExito"] = "Tu solicitud de cancelación fue recibida.";
        return Volver(returnUrl);
    }

    // POST /Miusuario/SolicitarReembolso
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SolicitarReembolso(ulong id, string? motivo, string? returnUrl, CancellationToken cancellationToken)
    {
        if (id == 0) return BadRequest();

        motivo = motivo?.Trim();
        if (string.IsNullOrEmpty(motivo) || motivo.Length < MotivoMinimo || motivo.Length > MotivoMaximo)
        {
            TempData["CancelacionError"] = $"El motivo del reembolso debe tener entre {MotivoMinimo} y {MotivoMaximo} caracteres.";
            return Volver(returnUrl);
        }

        var error = await ValidarReservacionPropiaAsync(id, cancellationToken);
        if (error is not null)
        {
            TempData["CancelacionError"] = error;
            return Volver(returnUrl);
        }

        // TODO BD (tarea T3.5, en la misma transacción que la cancelación):
        //   4) Buscar el pago APROBADO de la reservación (el reembolso deriva del PAGO, regla R8).
        //   5) INSERT reembolsos (id_pago, monto, motivo, estado = 'SOLICITADO')   <- motivo como parámetro (LINQ)
        //   6) INSERT historial_estado_reembolso (id_reembolso, id_usuario_cambio, estado = 'SOLICITADO', detalle)
        TempData["CancelacionExito"] = "Tu solicitud de cancelación y reembolso fue recibida. Un agente la revisará.";
        return Volver(returnUrl);
    }

    // ============================== AYUDANTES ==============================

    /// <summary>
    /// Comprueba que la reservación exista, sea del usuario autenticado y no esté en un estado final
    /// (cancelada / completada, regla R6). Devuelve <c>null</c> si todo está bien o el mensaje de error.
    /// </summary>
    /// <remarks>
    /// Si la reservación es de otra persona se responde igual que si no existiera: así no se revela
    /// qué ids existen (evita IDOR y enumeración).
    /// </remarks>
    private async Task<string?> ValidarReservacionPropiaAsync(ulong idReservacion, CancellationToken cancellationToken)
    {
        if (!ulong.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var idUsuario))
        {
            return "Tu sesión no es válida. Vuelve a iniciar sesión.";
        }

        var reservacion = await (
                from r in _db.Reservaciones.AsNoTracking()
                join e in _db.EstadosReservacion.AsNoTracking() on r.id_estado_reservacion equals e.id
                where r.id == idReservacion && r.id_usuarioglobal == idUsuario
                select new { e.es_final })
            .FirstOrDefaultAsync(cancellationToken);

        if (reservacion is null)
        {
            return "No encontramos esa reservación en tu cuenta.";
        }

        if (reservacion.es_final)
        {
            return "Esta reservación ya está cancelada o terminada; no se puede modificar.";
        }

        return null;
    }

    /// <summary>Regresa a la pantalla de donde vino (solo direcciones de este sitio, evita "open redirect").</summary>
    private IActionResult Volver(string? returnUrl)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return LocalRedirect(returnUrl);
        return RedirectToAction(nameof(Misreservaciones));
    }
}
