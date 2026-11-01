using System.Security.Claims;
using FlyVibeMex.Models;
using FlyVibeMex.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlyVibeMex.Controllers;

/// <summary>
/// Panel de soporte: <c>/Admin/Soporte</c> (roles SOPORTE, ADMINISTRADOR y SA). Vistas en <c>Views/Admin/Soporte/</c>.
/// </summary>
/// <remarks>
/// - Atender nuevo ticket: el ABIERTO sin agente más antiguo; "Tomar" lo asigna con un UPDATE condicional.
/// - Seguir atendiendo / Mi historial: los tickets propios (en curso / cerrados).
/// - Ticket/{id}: conversación con el cliente, resumen de solo lectura de su reservación, Responder y Cerrar
///   (solo quien lo atiende).
/// - Todos (ADMINISTRADOR y SA): todos los tickets con filtros y Reasignar.
/// La lógica está en <see cref="ISoportePanelService"/>; el agente sale SIEMPRE de la cookie, nunca del formulario.
/// </remarks>
[Route("Admin/Soporte")]
[AutoValidateAntiforgeryToken]   // todo POST de este controlador exige el token antiforgery
[Authorize(Roles = Roles.PanelSoporte)]
public class SoporteController : Controller
{
    private const string Carpeta = "~/Views/Admin/Soporte/";

    private readonly ISoportePanelService _soporte;

    public SoporteController(ISoportePanelService soporte)
    {
        _soporte = soporte;
    }

    // GET /Admin/Soporte  → resumen del panel (fila, mis tickets y los que esperan respuesta)
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        Vista("Index", await _soporte.ResumenAsync(IdUsuario(), EsSupervisor(), cancellationToken));

    // GET /Admin/Soporte/NuevoTicket  → muestra el siguiente ticket sin agente (solo CONSULTA; tomarlo es un POST)
    [HttpGet("NuevoTicket")]
    public async Task<IActionResult> NuevoTicket(CancellationToken cancellationToken) =>
        Vista("NuevoTicket", await _soporte.SiguienteAsync(IdUsuario(), EsSupervisor(), cancellationToken));

    // POST /Admin/Soporte/Tomar/5  → asigna el ticket al usuario de la cookie
    [HttpPost("Tomar/{id}")]
    public async Task<IActionResult> Tomar(ulong id, CancellationToken cancellationToken)
    {
        var resultado = await _soporte.TomarAsync(id, IdUsuario(), cancellationToken);
        TempData[resultado.Exitoso ? "Exito" : "Aviso"] = resultado.Mensaje;
        return resultado.Exitoso ? RedirectToAction(nameof(Ticket), new { id }) : RedirectToAction(nameof(NuevoTicket));
    }

    // GET /Admin/Soporte/SeguirAtendiendo  → tickets propios que no están cerrados
    [HttpGet("SeguirAtendiendo")]
    public async Task<IActionResult> SeguirAtendiendo(CancellationToken cancellationToken) =>
        Vista("SeguirAtendiendo", await _soporte.MisTicketsAsync(IdUsuario(), cerrados: false, cancellationToken));

    // GET /Admin/Soporte/Historial  → tickets propios ya cerrados
    [HttpGet("Historial")]
    public async Task<IActionResult> Historial(CancellationToken cancellationToken) =>
        Vista("Historial", await _soporte.MisTicketsAsync(IdUsuario(), cerrados: true, cancellationToken));

    // GET /Admin/Soporte/Ticket/5
    [HttpGet("Ticket/{id}")]
    public async Task<IActionResult> Ticket(ulong id, CancellationToken cancellationToken)
    {
        var vm = await _soporte.DetalleAsync(id, IdUsuario(), EsSupervisor(), cancellationToken);
        return vm is null ? NotFound() : Vista("Ticket", vm);
    }

    // POST /Admin/Soporte/Ticket/5/Responder
    [HttpPost("Ticket/{id}/Responder")]
    public async Task<IActionResult> Responder(ulong id, [Bind(Prefix = "Respuesta")] RespuestaTicketForm respuesta, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            // Se vuelve a mostrar el ticket con el texto escrito y el error.
            var vm = await _soporte.DetalleAsync(id, IdUsuario(), EsSupervisor(), cancellationToken);
            if (vm is null) return NotFound();
            vm.Respuesta = respuesta;
            return Vista("Ticket", vm);
        }

        var resultado = await _soporte.ResponderAsync(id, IdUsuario(), respuesta.mensaje, cancellationToken);
        TempData[resultado.Exitoso ? "Exito" : "Aviso"] = resultado.Mensaje;
        return RedirectToAction(nameof(Ticket), new { id });
    }

    // POST /Admin/Soporte/Ticket/5/Cerrar
    [HttpPost("Ticket/{id}/Cerrar")]
    public async Task<IActionResult> Cerrar(ulong id, CancellationToken cancellationToken)
    {
        var resultado = await _soporte.CerrarAsync(id, IdUsuario(), cancellationToken);
        TempData[resultado.Exitoso ? "Exito" : "Aviso"] = resultado.Mensaje;
        return resultado.Exitoso ? RedirectToAction(nameof(Historial)) : RedirectToAction(nameof(Ticket), new { id });
    }

    // GET /Admin/Soporte/Todos?estado=ABIERTA&agente=0&texto=&pagina=1   (ADMINISTRADOR y SA)
    [HttpGet("Todos")]
    [Authorize(Roles = Roles.SupervisionSoporte)]
    public async Task<IActionResult> Todos(string? estado, ulong? agente, string? texto, int pagina = 1, CancellationToken cancellationToken = default) =>
        Vista("Todos", await _soporte.TodosAsync(estado, agente, texto, pagina, cancellationToken));

    // POST /Admin/Soporte/Ticket/5/Reasignar   (ADMINISTRADOR y SA)
    [HttpPost("Ticket/{id}/Reasignar")]
    [Authorize(Roles = Roles.SupervisionSoporte)]
    public async Task<IActionResult> Reasignar(ulong id, ulong idAgente, CancellationToken cancellationToken)
    {
        var resultado = await _soporte.ReasignarAsync(id, idAgente, IdUsuario(), cancellationToken);
        TempData[resultado.Exitoso ? "Exito" : "Aviso"] = resultado.Mensaje;
        return RedirectToAction(nameof(Ticket), new { id });
    }

    // ============================== Ayudantes ==============================

    private IActionResult Vista(string nombre, object? modelo = null) => View(Carpeta + nombre + ".cshtml", modelo);

    /// <summary>Id del usuario de la cookie ([Authorize] garantiza que hay sesión).</summary>
    private ulong IdUsuario() => ulong.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private bool EsSupervisor() => Roles.EsSupervisorSoporte(User);
}
