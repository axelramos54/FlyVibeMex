using FlyVibeMex.Models;
using FlyVibeMex.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlyVibeMex.Controllers;

/// <summary>
/// Soporte del cliente: <c>/Solicitar</c>. Formulario para abrir un ticket (asesoría o problema, opcionalmente ligado
/// a una de sus reservaciones) y lista de sus tickets con estado y agente. Vista: <c>Views/Solicitar/Index.cshtml</c>.
/// <c>/Solicitar/Ticket/{id}</c>: la conversación del ticket; el cliente responde mientras no esté cerrado
/// (vista <c>Views/Solicitar/Ticket.cshtml</c>).
/// </summary>
/// <remarks>
/// Requiere sesión. Los internos lo ven en solo lectura (con <c>?cliente=ID</c> ven los tickets de ese cliente);
/// crear un ticket es solo para CLIENTE.
/// </remarks>
[Authorize]
public class SolicitarController : Controller
{
    private readonly IMiCuentaService _cuenta;
    private readonly ISoporteClienteService _soporte;

    public SolicitarController(IMiCuentaService cuenta, ISoporteClienteService soporte)
    {
        _cuenta = cuenta;
        _soporte = soporte;
    }

    // GET /Solicitar   (?reservacion=ID deja elegida esa reservación, p. ej. desde un reembolso rechazado)
    [HttpGet]
    public async Task<IActionResult> Index(ulong? cliente, ulong? reservacion, CancellationToken cancellationToken)
    {
        var contexto = await _cuenta.ContextoAsync(User, cliente, cancellationToken);
        if (contexto is null) return Challenge();

        var vm = await _soporte.SolicitudesAsync(contexto, cancellationToken);
        if (reservacion is ulong idReservacion && vm.Reservaciones.Any(r => r.Value == idReservacion.ToString()))
        {
            vm.Nueva.id_reservacion = idReservacion;   // solo si es una de SUS reservaciones
            vm.Nueva.tipo = "PROBLEMA";
        }
        return View(vm);
    }

    // GET /Solicitar/Ticket/5
    [HttpGet("Solicitar/Ticket/{id}")]
    public async Task<IActionResult> Ticket(ulong id, ulong? cliente, CancellationToken cancellationToken)
    {
        var contexto = await _cuenta.ContextoAsync(User, cliente, cancellationToken);
        if (contexto is null) return Challenge();

        var vm = await _soporte.TicketAsync(contexto, id, cancellationToken);
        return vm is null ? NotFound() : View(vm);
    }

    // POST /Solicitar/Ticket/5
    [HttpPost("Solicitar/Ticket/{id}")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.Cliente)]
    public async Task<IActionResult> Ticket(ulong id, [Bind(Prefix = "Respuesta")] RespuestaTicketForm respuesta, CancellationToken cancellationToken)
    {
        var contexto = await _cuenta.ContextoAsync(User, null, cancellationToken);
        if (contexto is null) return Challenge();

        if (ModelState.IsValid)
        {
            var resultado = await _soporte.ResponderAsync(contexto.IdUsuario, id, respuesta.mensaje, cancellationToken);
            if (resultado.Exitoso)
            {
                TempData["Exito"] = resultado.Mensaje;
                return RedirectToAction(nameof(Ticket), new { id });
            }

            if (resultado.Errores is { Count: > 0 } errores)
                foreach (var (campo, mensaje) in errores)
                    ModelState.AddModelError($"Respuesta.{campo}", mensaje);
            else
                TempData["Error"] = resultado.Mensaje;
        }

        var vm = await _soporte.TicketAsync(contexto, id, cancellationToken);
        if (vm is null) return NotFound();
        vm.Respuesta = respuesta;
        return View(vm);
    }

    // POST /Solicitar
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.Cliente)]
    public async Task<IActionResult> Index([Bind(Prefix = "Nueva")] NuevaSolicitudViewModel nueva, CancellationToken cancellationToken)
    {
        var contexto = await _cuenta.ContextoAsync(User, null, cancellationToken);
        if (contexto is null) return Challenge();

        if (ModelState.IsValid)
        {
            var resultado = await _soporte.CrearAsync(contexto.IdUsuario, nueva, cancellationToken);
            if (resultado.Exitoso)
            {
                TempData["Exito"] = resultado.Mensaje;
                return RedirectToAction(nameof(Index));
            }

            foreach (var (campo, mensaje) in resultado.Errores ?? new())
                ModelState.AddModelError($"Nueva.{campo}", mensaje);
        }

        var vm = await _soporte.SolicitudesAsync(contexto, cancellationToken);
        vm.Nueva = nueva;
        return View(vm);
    }
}
