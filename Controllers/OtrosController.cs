using Microsoft.AspNetCore.Mvc;

namespace FlyVibeMex.Controllers;

/// <summary>
/// Páginas informativas del sitio público: <c>/Otros/Politicas</c> (términos, aviso de privacidad y cancelaciones).
/// Los textos los edita el SA en Contenido del sitio › Políticas. Vista: <c>Views/Otros/Politicas/Index.cshtml</c>.
/// </summary>
/// <remarks>
/// T8.3: se quitó la acción <c>Index</c> (/Otros), que solo mostraba el título "Otros" y no la enlazaba ninguna página.
/// </remarks>
public class OtrosController : Controller
{
    // GET /Otros/Politicas  (los enlaces del footer usan #terminos, #privacidad y #cancelaciones)
    public IActionResult Politicas()
    {
        return View("Politicas/Index");
    }
}
