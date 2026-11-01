using System.Diagnostics;
using FlyVibeMex.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FlyVibeMex.Controllers;

/// <summary>
/// Páginas de error propias (T6.2): <c>/Estado/{codigo}</c>. Program.cs la usa en
/// <c>UseStatusCodePagesWithReExecute("/Estado/{0}")</c>, <c>UseExceptionHandler("/Estado/500")</c> y como
/// <c>AccessDeniedPath</c> de la cookie (<c>/Estado/403</c>). Vistas en <c>Views/Estado/</c>.
/// </summary>
/// <remarks>
/// - Acepta cualquier método (GET o POST): el middleware vuelve a ejecutar la petición original con su método.
/// - Responde con el MISMO código HTTP (no 200), así los buscadores y el navegador saben que es un error.
/// - Nunca muestra detalles técnicos: el 500 solo muestra una referencia; el detalle queda en el log del servidor.
/// - Zona: si la dirección original empieza con /Admin y quien navega es personal, usa el layout del panel.
/// </remarks>
[AllowAnonymous]
[Route("Estado")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class EstadoController : Controller
{
    private readonly ILogger<EstadoController> _logger;

    public EstadoController(ILogger<EstadoController> logger)
    {
        _logger = logger;
    }

    // /Estado/404, /Estado/403, /Estado/400, /Estado/500 …
    [Route("{codigo:int}")]
    public IActionResult Index(int codigo, string? returnUrl)
    {
        var reejecutada = HttpContext.Features.Get<IStatusCodeReExecuteFeature>();
        var excepcion = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        // Origen: la petición que falló (re-ejecución) o, en el 403 de la cookie, su ReturnUrl.
        var ruta = reejecutada?.OriginalPath ?? excepcion?.Path ?? returnUrl ?? string.Empty;

        var vm = new EstadoViewModel
        {
            Codigo = codigo is 400 or 403 or 404 ? codigo : 500,
            RutaOriginal = ruta,
            EsPanel = ruta.StartsWith("/Admin", StringComparison.OrdinalIgnoreCase) && Roles.EsInterno(User)
        };

        if (vm.Codigo == 500)
        {
            vm.Referencia = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
            // El middleware ya registró la excepción; aquí se deja la referencia para encontrarla.
            _logger.LogError(excepcion?.Error, "Página de error 500 mostrada (referencia {Referencia}, ruta {Ruta}).", vm.Referencia, ruta);
        }
        else if (vm.Codigo == 400)
        {
            _logger.LogWarning("Solicitud inválida (400) en {Ruta}: formulario vencido o datos no válidos.", ruta);
        }

        Response.StatusCode = vm.Codigo;
        var vista = vm.Codigo switch
        {
            404 => "NoEncontrado",
            403 => "AccesoDenegado",
            400 => "SolicitudInvalida",
            _ => "Error"
        };
        return View($"~/Views/Estado/{vista}.cshtml", vm);
    }
}
