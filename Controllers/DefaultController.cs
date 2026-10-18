using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using FlyVibeMex.Models;

namespace FlyVibeMex.Controllers;


// NOTA: Esta clase duplica las acciones de HomeController.
// ASP.NET Core SÍ la registra como controlador (hereda el atributo [Controller] de ControllerBase), con el nombre
// "Default", por lo que expone /Default/Index, /Default/Hoteles, /Default/Paquetes, /Default/Tours,
// /Default/Viajes, /Default/sesion y /Default/card. Como no existe la carpeta Views/Default/ (ni esas vistas en
// Views/Shared/), cada una de esas URL lanza InvalidOperationException ("The view was not found") → Error 500.
// Recomendación: eliminar este archivo; las acciones reales viven en HomeController.
public class Default : Controller
{
    public IActionResult Index()
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
}