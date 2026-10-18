using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using FlyVibeMex.Models;

namespace FlyVibeMex.Controllers;

/// <summary>
/// Controlador principal de FlyVibe Mex.
/// Cumple dos funciones:
/// 1) Sirve el <b>shell</b> de la aplicación (<c>Index</c>):  el documento HTML completo con la barra de
///    navegación y el contenedor <c>main#contenido_main</c>.
/// 2) Sirve las <b>vistas parciales</b> (todas las vistas con <c>Layout = null</c>) que <c>wwwroot/assets/js/script.js</c>
///    solicita con <c>fetch</c> e inyecta con <c>innerHTML</c> dentro del shell (navegación tipo SPA casera).
/// </summary>
/// <remarks>
/// Ruta base: <c>/Home</c> (ruta por defecto <c>{controller=Home}/{action=Index}/{id?}</c> en Program.cs).
/// </remarks>
public class HomeController : Controller
{
    /// <summary>
    /// GET <c>/Home/Index</c>. Devuelve la <b>vista completa</b> <c>Index.cshtml</c> (shell de la aplicación).
    /// </summary>
    public IActionResult Index()
    {
        return View();
    }

    /// <summary>
    /// GET <c>/Home/Default</c>. Devuelve la <b>vista completa</b> <c>Default.cshtml</c> (pantalla de Inicio).
    /// </summary>
    public IActionResult Default()
    {
        return View();
    }

    /// <summary>
    /// GET <c>/Home/Hoteles</c>. Devuelve la <b>vista parcial</b> <c>Hoteles.cshtml</c>
    /// (módulo Explorar y reservar › Hospedaje). Estado: buscador sin funcionalidad y 3 tarjetas estáticas.
    /// </summary>
    public IActionResult Hoteles()
    {
        return View();
    }

    /// <summary>
    /// GET <c>/Home/Paquetes</c>. Devuelve la <b>vista parcial</b> <c>Paquetes.cshtml</c>
    /// (módulo Explorar y reservar › Paquetes). Estado: buscador sin funcionalidad y 3 tarjetas estáticas.
    /// </summary>
    public IActionResult Paquetes()
    {
        return View();
    }

    /// <summary>
    /// GET <c>/Home/Tours</c>. Devuelve la <b>vista parcial</b> <c>Tours.cshtml</c>.
    /// Estado: buscador sin funcionalidad y 3 tarjetas estáticas.
    /// </summary>

    public IActionResult Tours()
    {
        return View();
    }

    /// <summary>
    /// GET <c>/Home/Viajes</c>. Devuelve la <b>vista parcial</b> <c>Viajes.cshtml</c>
    /// (módulo Explorar y reservar › Vuelos). Estado: buscador sin funcionalidad y 3 tarjetas estáticas.
    /// </summary>
    public IActionResult Viajes()
    {
        return View();
    }

    /// <summary>
    /// GET <c>/Home/sesion</c>. Devuelve la <b>vista parcial</b> <c>sesion.cshtml</c> (módulo Cuenta › Iniciar sesión).
    /// Estado: formulario visual sin envío; no existe acción POST ni autenticación configurada.
    /// </summary>

    public IActionResult sesion()
    {
        return View();
    }

    /// <summary>
    /// GET <c>/Home/card</c>. Devuelve la <b>vista parcial</b> <c>card.cshtml</c>: maqueta de prueba con 12 tarjetas
    /// (hoteles, paquetes, tours y vuelos) que sirve de modelo para los resultados de búsqueda.
    /// </summary>
    // NOTA: quitar el enlace "VER TARJETA test" del menú de Index.cshtml antes de la entrega.
    public IActionResult card()
    {
        return View();
    }



    /// <summary>
    /// GET <c>/Home/Error</c>. Devuelve <c>Views/Shared/Error.cshtml</c> (documento completo con <c>_Layout</c>)
    /// con el identificador de la petición. Program.cs la usa como manejador de excepciones fuera de Development
    /// (<c>UseExceptionHandler("/Home/Error")</c>). No se guarda en caché.
    /// </summary>
    /// NOTA: falta la acción Privacy(). Existe Views/Home/Privacy.cshtml y _Layout.cshtml la enlaza,
    // pero /Home/Privacy responde 404 porque este controlador no la define.
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
