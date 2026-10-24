using Microsoft.AspNetCore.Mvc;

namespace FlyVibeMex.Controllers;

// NOTA: antes se llamaba "Miusuariocontroller" (c minúscula). MVC solo quita el sufijo "Controller" exacto,
//       así que la ruta era /Miusuariocontroller y los enlaces asp-controller="Miusuario" salían vacíos.
public class MiusuarioController : Controller
{
    // GET /Admin
    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Miscompras()
    {
        return View("Miscompras/index");
    }

    public IActionResult Misreservaciones()
    {
        return View("Misreservaciones/index");
    }

    public IActionResult Mistarjetas()
    {
        return View("Mistarjetas/index");
    }

    // ============================== CANCELAR / REEMBOLSAR ==============================
    // Los envían los modales de Views/Miusuario/_ModalesCancelacion.cshtml (Mis reservaciones y Mis compras).
    // La doble confirmación (clic + clic sostenido) solo evita accidentes en el navegador:
    // aquí se vuelve a validar todo y, al conectar la BD, se debe comprobar que la reservación sea del usuario.

    private const int MotivoMinimo = 10;
    private const int MotivoMaximo = 250;   // reembolsos.motivo es varchar(250)

    // POST /Miusuario/CancelarReservacion
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult CancelarReservacion(ulong id, string? returnUrl)
    {
        if (id == 0) return BadRequest();

        // TODO BD (en una transacción):
        //   1) Buscar la reservación con id = @id Y id_usuario = usuario autenticado
        //      (User.FindFirstValue(ClaimTypes.NameIdentifier)); si no es suya → NotFound().
        //   2) Si su estado actual tiene es_final = 1 (ya cancelada/terminada) → error, no se puede cancelar (R6).
        //   3) UPDATE reservaciones SET id_estado_reservacion = (id del estado 'CANCELADA')
        //   4) INSERT historial_estado_reservacion (id_reservacion, id_estado_reservacion, id_usuario_cambio, motivo)
        //   5) Devolver el cupo: cantidad_disponible += cantidad en disponibilidad_servicio por cada detalle (R3)
        TempData["CancelacionExito"] = "Tu solicitud de cancelación fue recibida. Se aplicará cuando se conecte la base de datos.";
        return Volver(returnUrl);
    }

    // POST /Miusuario/SolicitarReembolso
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SolicitarReembolso(ulong id, string? motivo, string? returnUrl)
    {
        if (id == 0) return BadRequest();

        motivo = motivo?.Trim();
        if (string.IsNullOrEmpty(motivo) || motivo.Length < MotivoMinimo || motivo.Length > MotivoMaximo)
        {
            TempData["CancelacionError"] = $"El motivo del reembolso debe tener entre {MotivoMinimo} y {MotivoMaximo} caracteres.";
            return Volver(returnUrl);
        }

        // TODO BD (en una transacción, además de los pasos 1-5 de CancelarReservacion):
        //   6) Buscar el pago aprobado de la reservación (el reembolso deriva del PAGO, regla R8).
        //   7) INSERT reembolsos (id_pago, monto, motivo, estado = 'SOLICITADO')   ← motivo va como parámetro, nunca concatenado
        //   8) INSERT historial_estado_reembolso (id_reembolso, id_usuario_cambio, estado = 'SOLICITADO', detalle)
        //   NOTA: en el script el CHECK de reembolsos dice 'APROVADO' (sin B); usar ese valor o corregir el script.
        TempData["CancelacionExito"] = "Tu solicitud de cancelación y reembolso fue recibida. Un agente la revisará. " +
                                       "(Se guardará cuando se conecte la base de datos.)";
        return Volver(returnUrl);
    }

    /// <summary>Regresa a la pantalla de donde vino (solo direcciones de este sitio, evita "open redirect").</summary>
    private IActionResult Volver(string? returnUrl)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return LocalRedirect(returnUrl);
        return RedirectToAction(nameof(Misreservaciones));
    }
}
