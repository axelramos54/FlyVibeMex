using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using FlyVibeMex.Models;

namespace FlyVibeMex.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Default()
    {
        return View();
    }

    public IActionResult Hoteles()
    {
        return View();
    }

    public IActionResult Paquetes()
    {
        return View();
    }

    public IActionResult Tours()
    {
        return View();
    }

    public IActionResult Viajes()
    {
        return View();
    }

    public IActionResult sesion()
    {
        return View();
    }

    public IActionResult card()
    {
        return View();
    }



    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
