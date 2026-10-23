using Microsoft.AspNetCore.Mvc;

namespace FlyVibeMex.Controllers;

public class Miusuariocontroller : Controller
{
    // GET /Admin
    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Miscompras()
    {
        return View("Miscompras/index");
    }

    public IActionResult Misreservaciones()
    {
        return View("Misreservaciones/index");
    }

    public IActionResult Mistarjetas()
    {
        return View("Mistarjetas/index");
    }
}
