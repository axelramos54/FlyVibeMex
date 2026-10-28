using System.Security.Claims;
using FlyVibeMex.Models;
using FlyVibeMex.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlyVibeMex.Controllers;

/// <summary>
/// Compra: <c>/Reservas/Resumen</c> (desglose + promoción + pago), <c>POST /Reservas/Pagar</c> y
/// <c>/Reservas/Confirmacion/{codigo}</c>. Las vistas están en <c>Views/Reservas/</c>.
/// </summary>
/// <remarks>
/// Solo compran cuentas CLIENTE: sin sesión, ASP.NET manda al login (y regresa aquí con ReturnUrl);
/// un agente o administrador ve "Acceso denegado". El id del cliente SIEMPRE sale de la cookie.
/// El controlador es delgado: toda la lógica está en <see cref="IReservacionService"/>.
/// </remarks>
[Authorize(Roles = Roles.Cliente)]
public class ReservasController : Controller
{
    private readonly IReservacionService _reservas;
    private readonly ICatalogoService _catalogo;

    public ReservasController(IReservacionService reservas, ICatalogoService catalogo)
    {
        _reservas = reservas;
        _catalogo = catalogo;
    }

    // GET /Reservas/Resumen?id=5&desde=2026-11-02&hasta=2026-11-05&personas=2&habitaciones=1&codigo=BIENVENIDA10
    [HttpGet]
    public async Task<IActionResult> Resumen(PagoFormViewModel solicitud, CancellationToken cancellationToken)
    {
        if (!TryObtenerUsuario(out var idUsuario)) return Challenge();

        // Fechas o personas no válidas: se regresa al detalle, que muestra los errores junto a cada campo.
        if (!await BusquedaValidaAsync(solicitud, cancellationToken))
            return RedirectToAction("Detalle", "Catalogo", RutaDetalle(solicitud));

        var vm = await _reservas.ArmarResumenAsync(idUsuario, solicitud, cancellationToken);
        if (vm is null) return NotFound();
        return View(vm);
    }

    // POST /Reservas/Pagar   (campos ocultos del resumen + método + tarjeta)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pagar(PagoFormViewModel pago, CancellationToken cancellationToken)
    {
        if (!TryObtenerUsuario(out var idUsuario)) return Challenge();

        if (!await BusquedaValidaAsync(pago, cancellationToken))
            return RedirectToAction("Detalle", "Catalogo", RutaDetalle(pago));

        if (ModelState.IsValid)
        {
            var resultado = await _reservas.PagarAsync(idUsuario, pago, cancellationToken);
            if (resultado.Exitoso)
            {
                // Post/Redirect/Get: recargar la confirmación no vuelve a cobrar.
                return RedirectToAction(nameof(Confirmacion), new { id = resultado.CodigoReserva });
            }

            foreach (var (campo, mensaje) in resultado.Errores)
                ModelState.AddModelError(campo, mensaje);
        }

        // Se vuelve a mostrar el resumen (recalculado) con los errores. El número de tarjeta y el CVV no se regresan.
        pago.LimpiarDatosSensibles();
        var vm = await _reservas.ArmarResumenAsync(idUsuario, pago, cancellationToken);
        if (vm is null) return NotFound();
        return View(nameof(Resumen), vm);
    }

    // GET /Reservas/Confirmacion/FV-261028-AB3K
    [HttpGet]
    public async Task<IActionResult> Confirmacion(string id, CancellationToken cancellationToken)
    {
        if (!TryObtenerUsuario(out var idUsuario)) return Challenge();

        var vm = await _reservas.ConfirmacionAsync(idUsuario, id, cancellationToken);
        if (vm is null) return NotFound();   // no existe o es de otro cliente
        return View(vm);
    }

    // ============================== Ayudantes ==============================

    private bool TryObtenerUsuario(out ulong idUsuario) =>
        ulong.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out idUsuario);

    /// <summary>Mismas reglas de fechas que el catálogo (no en el pasado, rango máximo, etc.).</summary>
    private async Task<bool> BusquedaValidaAsync(PagoFormViewModel solicitud, CancellationToken cancellationToken)
    {
        var tipo = await _catalogo.TipoDeServicioAsync(solicitud.id, cancellationToken);
        if (tipo is null) return false;

        var busqueda = solicitud.ComoBusqueda();
        busqueda.tipo = tipo;
        var hayErrores = busqueda.Validar(DateOnly.FromDateTime(DateTime.Today)).Any();
        var faltaFecha = tipo != TipoCatalogo.Vuelo && solicitud.desde is null;
        var personasInvalidas = ModelState[nameof(solicitud.personas)]?.Errors.Count > 0
                                || ModelState[nameof(solicitud.habitaciones)]?.Errors.Count > 0;
        return !hayErrores && !faltaFecha && !personasInvalidas;
    }

    private static Dictionary<string, string?> RutaDetalle(PagoFormViewModel s) => new()
    {
        ["id"] = s.id.ToString(),
        ["desde"] = s.desde?.ToString("yyyy-MM-dd"),
        ["hasta"] = s.hasta?.ToString("yyyy-MM-dd"),
        ["personas"] = s.personas.ToString(),
        ["habitaciones"] = s.habitaciones.ToString()
    };
}
