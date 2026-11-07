// Controllers/AdminResenasController.cs  — moderación (SOPORTE, ADMINISTRADOR, SA)
using System.Security.Claims;
using FlyVibeMex.Models;
using FlyVibeMex.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlyVibeMex.Controllers;

[Authorize(Roles = Roles.PanelResenas)]
[Route("Admin/Resenas")]
public class AdminResenasController : Controller
{
    private readonly IComunidadService _comunidad;
    public AdminResenasController(IComunidadService comunidad) => _comunidad = comunidad;

    [HttpGet("{idServicio}")]
    public async Task<IActionResult> Index(ulong idServicio, CancellationToken ct)
    {
        var (resenas, promedio, total) = await _comunidad.ResenasAsync(idServicio, incluirOcultas: true, ct);
        ViewBag.IdServicio = idServicio; ViewBag.Promedio = promedio; ViewBag.Total = total;
        return View("~/Views/Admin/Resenas/Index.cshtml", resenas);
    }

    [HttpPost("Moderar"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Moderar(ulong idServicio, ulong idResena, bool ocultar, CancellationToken ct)
    {
        try { await _comunidad.ModerarResenaAsync(ulong.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), idResena, ocultar, ct); }
        catch (ReglaProcedimientoException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index), new { idServicio });
    }
}