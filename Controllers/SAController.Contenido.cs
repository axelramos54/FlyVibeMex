using FlyVibeMex.Models;
using Microsoft.AspNetCore.Mvc;

namespace FlyVibeMex.Controllers;

/// <summary>Tercera parte de <see cref="SAController"/>: bitácora (solo lectura) y contenido del sitio.</summary>
public partial class SAController
{
    // ============================== BITÁCORA ==============================

    // GET /Admin/SA/Bitacora?desde=&hasta=&usuario=&entidad=&accion=&texto=&pagina=1
    [HttpGet("Bitacora")]
    public async Task<IActionResult> Bitacora([FromQuery] FiltroBitacora filtro, CancellationToken cancellationToken) =>
        Vista("Bitacora", await _bitacora.ListaAsync(filtro, cancellationToken));

    // ============================== CONTENIDO DEL SITIO ==============================

    // GET /Admin/SA/Contenido?seccion=inicio|politicas|pie
    [HttpGet("Contenido")]
    public async Task<IActionResult> Contenido(string? seccion, CancellationToken cancellationToken) =>
        Vista("Contenido", await _contenido.SeccionAsync(seccion, cancellationToken));

    // POST /Admin/SA/Contenido/Texto
    [HttpPost("Contenido/Texto")]
    public async Task<IActionResult> GuardarTextoContenido(string? clave, string? texto, string? seccion, CancellationToken cancellationToken)
    {
        var resultado = await _contenido.GuardarTextoAsync(clave, texto, IdUsuario(), cancellationToken);
        TempData[resultado.Exitoso ? "Exito" : "Aviso"] = resultado.Mensaje;
        return VolverAContenido(seccion, clave);
    }

    // POST /Admin/SA/Contenido/Imagen
    [HttpPost("Contenido/Imagen")]
    [RequestSizeLimit(3_000_000)]
    public async Task<IActionResult> GuardarImagenContenido(string? clave, IFormFile? imagen, string? seccion, CancellationToken cancellationToken)
    {
        var resultado = await _contenido.GuardarImagenAsync(clave, imagen, IdUsuario(), cancellationToken);
        TempData[resultado.Exitoso ? "Exito" : "Aviso"] = resultado.Mensaje;
        return VolverAContenido(seccion, clave);
    }

    // POST /Admin/SA/Contenido/Restaurar
    [HttpPost("Contenido/Restaurar")]
    public async Task<IActionResult> RestaurarContenido(string? clave, string? seccion, CancellationToken cancellationToken)
    {
        var resultado = await _contenido.RestaurarAsync(clave, IdUsuario(), cancellationToken);
        TempData[resultado.Exitoso ? "Exito" : "Aviso"] = resultado.Mensaje;
        return VolverAContenido(seccion, clave);
    }

    /// <summary>Regresa a la pestaña de la sección, en el campo editado (ancla #campo_...).</summary>
    private IActionResult VolverAContenido(string? seccion, string? clave)
    {
        var definicion = ContenidoSitioClaves.Buscar(clave);
        var url = Url.Action(nameof(Contenido), new { seccion = definicion?.Seccion ?? seccion }) ?? "/Admin/SA/Contenido";
        return LocalRedirect(definicion is null ? url : $"{url}#campo_{definicion.Clave.Replace('.', '_')}");
    }
}
