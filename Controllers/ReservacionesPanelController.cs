using System.Security.Claims;
using FlyVibeMex.Models;
using FlyVibeMex.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlyVibeMex.Controllers;

/// <summary>
/// Panel de Reservaciones y reembolsos: <c>/Admin/Reservaciones</c> (roles AGENTE, ADMINISTRADOR y SA).
/// Vistas en <c>Views/Admin/Reservaciones/</c>.
/// </summary>
/// <remarks>
/// - Lista con filtros y detalle con historiales, pagos, reembolsos y tickets ligados.
/// - Cancelar a nombre del cliente: mismas reglas que el cliente (política guardada en la compra); sin reembolso,
///   con el de la política o con 100 % de cortesía. El reembolso se procesa en el mismo paso.
/// - Reembolsos SOLICITADO: aprobar (PROCESADO + pago REEMBOLSADO) o rechazar con motivo. El monto no se edita.
/// Las acciones están en <see cref="ICancelacionesService"/> (la misma que usa el cliente).
/// </remarks>
[Route("Admin/Reservaciones")]
[AutoValidateAntiforgeryToken]
[Authorize(Roles = Roles.PanelReservaciones)]
public class ReservacionesPanelController : Controller
{
    private const string Carpeta = "~/Views/Admin/Reservaciones/";

    private readonly IReservacionesPanelService _reservaciones;
    private readonly ICancelacionesService _cancelaciones;

    public ReservacionesPanelController(IReservacionesPanelService reservaciones, ICancelacionesService cancelaciones)
    {
        _reservaciones = reservaciones;
        _cancelaciones = cancelaciones;
    }

    // GET /Admin/Reservaciones?codigo=&correo=&estado=&desde=&hasta=&pagina=1
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] FiltroReservacionesPanel filtro, CancellationToken cancellationToken) =>
        Vista("Index", await _reservaciones.ListaAsync(filtro, cancellationToken));

    // GET /Admin/Reservaciones/Reembolsos?vista=pendientes|todos
    [HttpGet("Reembolsos")]
    public async Task<IActionResult> Reembolsos(string? vista, CancellationToken cancellationToken) =>
        Vista("Reembolsos", await _reservaciones.ReembolsosAsync(vista, cancellationToken));

    // POST /Admin/Reservaciones/Reembolsos/5/Aprobar   (volver = código de la reservación para regresar a su detalle)
    [HttpPost("Reembolsos/{id}/Aprobar")]
    public async Task<IActionResult> AprobarReembolso(ulong id, string? volver, CancellationToken cancellationToken)
    {
        var resultado = await _cancelaciones.ResolverReembolsoAsync(id, aprobar: true, motivo: null, IdUsuario(), cancellationToken);
        TempData[resultado.Exitoso ? "Exito" : "Aviso"] = resultado.Mensaje;
        return Volver(volver);
    }

    // POST /Admin/Reservaciones/Reembolsos/5/Rechazar
    [HttpPost("Reembolsos/{id}/Rechazar")]
    public async Task<IActionResult> RechazarReembolso(ulong id, string? motivo, string? volver, CancellationToken cancellationToken)
    {
        var resultado = await _cancelaciones.ResolverReembolsoAsync(id, aprobar: false, motivo, IdUsuario(), cancellationToken);
        TempData[resultado.Exitoso ? "Exito" : "Aviso"] = resultado.Mensaje;
        return Volver(volver);
    }

    // GET /Admin/Reservaciones/FV-261031-AB12
    [HttpGet("{codigo}")]
    public async Task<IActionResult> Detalle(string codigo, CancellationToken cancellationToken)
    {
        var vm = await _reservaciones.DetalleAsync(codigo, cancellationToken);
        return vm is null ? NotFound() : Vista("Detalle", vm);
    }

    // POST /Admin/Reservaciones/FV-261031-AB12/Cancelar   (modo = SIN | POLITICA | CORTESIA)
    [HttpPost("{codigo}/Cancelar")]
    public async Task<IActionResult> Cancelar(string codigo, string? modo, string? motivo, CancellationToken cancellationToken)
    {
        var id = await _reservaciones.IdPorCodigoAsync(codigo, cancellationToken);
        if (id is not ulong idReservacion) return NotFound();

        var resultado = await _cancelaciones.CancelarComoPersonalAsync(idReservacion, IdUsuario(), modo, motivo, cancellationToken);
        TempData[resultado.Exitoso ? "Exito" : "Aviso"] = resultado.Mensaje;
        return RedirectToAction(nameof(Detalle), new { codigo });
    }

    // ============================== Ayudantes ==============================

    private IActionResult Vista(string nombre, object? modelo = null) => View(Carpeta + nombre + ".cshtml", modelo);

    private ulong IdUsuario() => ulong.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Regresa al detalle de la reservación (si se indicó su código) o a la lista de reembolsos.</summary>
    /// <remarks>El código va como valor de ruta (nunca como URL), así no sirve para redirigir a otro sitio.</remarks>
    private IActionResult Volver(string? codigo) =>
        string.IsNullOrWhiteSpace(codigo) || codigo.Length > 30
            ? RedirectToAction(nameof(Reembolsos))
            : RedirectToAction(nameof(Detalle), new { codigo });
}
