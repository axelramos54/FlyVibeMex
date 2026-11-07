// Controllers/ResenasController.cs  — crear reseña (CLIENTE)
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using FlyVibeMex.Models;
using FlyVibeMex.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlyVibeMex.Controllers;

[Authorize(Roles = Roles.Cliente)]
public class ResenasController : Controller
{
    private readonly IComunidadService _comunidad;
    public ResenasController(IComunidadService comunidad) => _comunidad = comunidad;

    public class ResenaForm
    {
        public ulong id_servicio { get; set; }
        [Range(1, 5, ErrorMessage = "Elige de 1 a 5 estrellas.")] public int calificacion { get; set; }
        [StringLength(100)] public string? titulo { get; set; }
        [Required(ErrorMessage = "Escribe un comentario."), StringLength(1000)] public string comentario { get; set; } = string.Empty;
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Crear(ResenaForm f, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = ModelState.Values.SelectMany(v => v.Errors).First().ErrorMessage;
            return RedirectToAction("Detalle", "Catalogo", new { id = f.id_servicio });
        }
        try
        {
            var id = ulong.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await _comunidad.CrearResenaAsync(id, f.id_servicio, f.calificacion, f.titulo, f.comentario, ct);
            TempData["Exito"] = "¡Gracias por tu reseña!";
        }
        catch (ReglaProcedimientoException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction("Detalle", "Catalogo", new { id = f.id_servicio });
    }
}