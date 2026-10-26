using FlyVibeMex.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlyVibeMex.Controllers;

/// <summary>
/// Panel del SA: <c>/Admin/SA</c>. Las vistas están en <c>Views/Admin/SA/</c>.
/// Por ahora solo muestra un mensaje general; las funciones del SA aún no están definidas.
/// </summary>
[Route("Admin/SA")]
[AutoValidateAntiforgeryToken]
[Authorize(Roles = Roles.Administrador)]
public class SAController : Controller
{
    // GET /Admin/SA
    [HttpGet("")]
    public IActionResult Index() => View("~/Views/Admin/SA/Index.cshtml");
}
