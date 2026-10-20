using Microsoft.AspNetCore.Mvc;

namespace FlyVibeMex.Controllers;

/// <summary>
/// Pantalla de inicio del panel administrativo: GET <c>/Admin</c> → <c>Views/Admin/Admin.cshtml</c>.
/// </summary>
/// <remarks>
/// Cada sección del panel tiene su propio controlador (para poder protegerla por rol por separado):
/// <list type="bullet">
/// <item><c>/Admin/Soporte</c>      → <see cref="SoporteController"/></item>
/// <item><c>/Admin/SA</c>           → <see cref="SAController"/></item>
/// <item><c>/Admin/GestorVentas</c> → <see cref="GestorController"/></item>
/// </list>
/// </remarks>
// TODO (cuando exista el login): [Authorize(Roles = "Admin,SA,Soporte,GestorVentas")]
public class AdminController : Controller
{
    // GET /Admin
    public IActionResult Index()
    {
        return View("Admin");
    }
}
