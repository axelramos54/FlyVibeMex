using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlyVibeMex.Controllers;

/// <summary>Soporte del cliente: <c>/Solicitar</c>. Requiere sesión (el ticket se liga al usuario autenticado).</summary>
[Authorize]
public class SolicitarController : Controller
{
    // GET /Solicitar
    public IActionResult Index()
    {
        return View();
    }
}
