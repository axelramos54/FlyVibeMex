using Microsoft.AspNetCore.Mvc;
using FlyVibeMex.Models;

namespace FlyVibeMex.Controllers;

/// <summary>
/// Panel de soporte: <c>/Admin/Soporte</c>. Las vistas están en <c>Views/Admin/Soporte/</c>.
/// </summary>
[Route("Admin/Soporte")]
[AutoValidateAntiforgeryToken]   // todo POST de este controlador exige el token antiforgery
// TODO (cuando exista el login): [Authorize(Roles = "Soporte,SA")]
public class SoporteController : Controller
{
    private const string Carpeta = "~/Views/Admin/Soporte/";

    private IActionResult Vista(string nombre, object? modelo = null) => View(Carpeta + nombre + ".cshtml", modelo);

    // GET /Admin/Soporte  → información general del panel
    [HttpGet("")]
    public IActionResult Index() => Vista("Index");

    // GET /Admin/Soporte/NuevoTicket  → busca el siguiente ticket sin agente asignado
    // Solo CONSULTA. Tomar el ticket (asignárselo al agente) modifica datos, por eso es un POST aparte.
    [HttpGet("NuevoTicket")]
    public IActionResult NuevoTicket()
    {
        SolicitudesSoporte? ticket = BuscarSiguienteTicketDisponible();
        return Vista("NuevoTicket", ticket);
    }

    // POST /Admin/Soporte/Tomar/5  → asigna el ticket al agente actual
    [HttpPost("Tomar/{id}")]
    public IActionResult Tomar(ulong id)
    {
        // TODO BD: UPDATE solicitudes_soporte SET id_agente_asignado = @agente, estado = 'EN PROCESO'
        //          WHERE id = @id AND id_agente_asignado IS NULL   ← evita que dos agentes tomen el mismo ticket
        // El id del agente debe salir del usuario autenticado, nunca del formulario.
        TempData["Aviso"] = "Tomar tickets se activará cuando se conecte la base de datos.";
        return RedirectToAction(nameof(SeguirAtendiendo));
    }

    // GET /Admin/Soporte/SeguirAtendiendo  → tickets que el agente tiene en proceso
    [HttpGet("SeguirAtendiendo")]
    public IActionResult SeguirAtendiendo()
    {
        List<SolicitudesSoporte> tickets = ObtenerTicketsEnCurso();
        return Vista("SeguirAtendiendo", tickets);
    }

    // GET /Admin/Soporte/Historial  → tickets que el agente ya cerró
    [HttpGet("Historial")]
    public IActionResult Historial()
    {
        List<SolicitudesSoporte> tickets = ObtenerHistorial();
        return Vista("Historial", tickets);
    }

    // ===================== Acceso a datos (pendiente de conectar a la BD) =====================
    // Mientras no haya conexión, devuelven "nada" y las vistas muestran el mensaje de que no hay tickets.
    // Cuando conectes la BD solo cambias estos métodos; las vistas ya están listas para mostrar datos.
    // El agente se obtendrá de: User.FindFirstValue(ClaimTypes.NameIdentifier)

    private SolicitudesSoporte? BuscarSiguienteTicketDisponible()
    {
        // TODO BD: SELECT TOP 1 ... FROM solicitudes_soporte
        //          WHERE id_agente_asignado IS NULL AND estado = 'ABIERTA' ORDER BY creada_en
        ViewData["SinBD"] = true;
        return null;
    }

    private List<SolicitudesSoporte> ObtenerTicketsEnCurso()
    {
        // TODO BD: WHERE id_agente_asignado = @agente AND estado <> 'CERRADA'
        ViewData["SinBD"] = true;
        return new List<SolicitudesSoporte>();
    }

    private List<SolicitudesSoporte> ObtenerHistorial()
    {
        // TODO BD: WHERE id_agente_asignado = @agente AND estado = 'CERRADA' ORDER BY actualizada_en DESC
        ViewData["SinBD"] = true;
        return new List<SolicitudesSoporte>();
    }
}
