// Controllers/FavoritosController.cs
using System.Security.Claims;
using FlyVibeMex.Models;
using FlyVibeMex.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlyVibeMex.Controllers;

[Authorize(Roles = Roles.Cliente)]
public class FavoritosController : Controller
{
    private readonly IComunidadService _comunidad;
    public FavoritosController(IComunidadService comunidad) => _comunidad = comunidad;

    // POST /Favoritos/Alternar  (lo llama fetch desde el corazón; devuelve JSON)
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Alternar(ulong id, CancellationToken ct)
    {
        var idUsuario = ulong.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        try { return Json(new { ok = true, favorito = await _comunidad.AlternarFavoritoAsync(idUsuario, id, ct) }); }
        catch (ReglaProcedimientoException ex) { return BadRequest(new { ok = false, mensaje = ex.Message }); }
    }
}