using FlyVibeMex.Models;
using FlyVibeMex.Services;
using Microsoft.AspNetCore.Mvc;

namespace FlyVibeMex.Controllers;

/// <summary>
/// Detalle de un servicio del catálogo: <c>GET /Catalogo/Detalle/{id}?desde=&amp;hasta=&amp;personas=&amp;habitaciones=</c>.
/// Las secciones con búsqueda y tarjetas están en <see cref="HomeController"/>.
/// </summary>
/// <remarks>
/// Es público (sin [Authorize]): cualquiera puede ver precios y condiciones. Reservar sí exigirá sesión.
/// </remarks>
public class CatalogoController : Controller
{
    private readonly ICatalogoService _catalogo;

    public CatalogoController(ICatalogoService catalogo)
    {
        _catalogo = catalogo;
    }

    // GET /Catalogo/Detalle/5?desde=2026-11-02&hasta=2026-11-05&personas=2&habitaciones=1
    [HttpGet]
    public async Task<IActionResult> Detalle(ulong id, BusquedaCatalogoViewModel busqueda, CancellationToken cancellationToken)
    {
        var tipo = await _catalogo.TipoDeServicioAsync(id, cancellationToken);
        if (tipo is null) return NotFound();   // no existe, está dado de baja o no se vende en el sitio

        busqueda.tipo = tipo;
        foreach (var (campo, mensaje) in busqueda.Validar(DateOnly.FromDateTime(DateTime.Today)))
            ModelState.AddModelError(campo, mensaje);

        var vm = await _catalogo.DetalleAsync(id, busqueda, cotizar: ModelState.IsValid, cancellationToken);
        if (vm is null) return NotFound();

        return View(vm);
    }
}
