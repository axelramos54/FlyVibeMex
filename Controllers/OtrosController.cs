using Microsoft.AspNetCore.Mvc;

namespace FlyVibeMex.Controllers;

public class OtrosController : Controller
{
    public IActionResult Index()
    {
        return View();
    }
    public IActionResult Politicas()
    {
        return View("Politicas/Index");
    }
}