using FlyVibeMex.Models;
using Microsoft.Extensions.Options;

namespace FlyVibeMex.Services;

/// <summary>
/// Middleware que decide, en cada respuesta, si el navegador puede mostrar esa página dentro de un
/// &lt;iframe&gt; de otro sitio.
///
/// Las dos cabeceras:
///   · Content-Security-Policy: frame-ancestors …  → la que usan los navegadores actuales. Admite una LISTA
///     de orígenes, así que es la única que sirve para "solo GoldStay".
///   · X-Frame-Options: DENY → la cabecera vieja. Solo entiende DENY o SAMEORIGIN: no sabe de listas, y si se
///     manda junto con frame-ancestors en una página embebible, algunos navegadores hacen caso a la MÁS
///     restrictiva y el widget sale en blanco. Por eso aquí se manda SOLO en las rutas NO embebibles,
///     donde las dos dicen lo mismo.
///
/// Reglas:
///   · Ruta embebible + integración activa → frame-ancestors 'self' + los orígenes de GoldStay, sin X-Frame-Options.
///   · Todo lo demás (panel interno, iniciar sesión, Mi usuario, Soporte…) → frame-ancestors 'none' + DENY.
///     Es decir, habilitar la integración NO abre el sitio entero: solo las rutas de la lista.
///
/// Si ya existe una cabecera Content-Security-Policy puesta por otra capa, no se sobreescribe: se le agrega la
/// directiva frame-ancestors solo si no la trae.
///
/// Dónde va en la tubería: ANTES de UseStaticFiles/MapStaticAssets y de los controladores, para que la cabecera
/// viaje también en las páginas de error regeneradas por UseStatusCodePagesWithReExecute.
/// </summary>
public sealed class CabecerasIntegracionMiddleware
{
    private readonly RequestDelegate _siguiente;
    private readonly OpcionesIntegracion _opciones;

    public CabecerasIntegracionMiddleware(RequestDelegate siguiente, IOptions<OpcionesIntegracion> opciones)
    {
        _siguiente = siguiente;
        _opciones = opciones.Value;
    }

    public Task InvokeAsync(HttpContext contexto)
    {
        // Las cabeceras se fijan ANTES de que empiece a escribirse la respuesta: después ya no se puede.
        var embebible = _opciones.EsRutaEmbebible(contexto.Request.Path);

        var directiva = embebible
            ? _opciones.DirectivaFrameAncestors
            : "frame-ancestors 'none'";

        var politicaActual = contexto.Response.Headers.ContentSecurityPolicy.ToString();

        if (string.IsNullOrWhiteSpace(politicaActual))
        {
            contexto.Response.Headers.ContentSecurityPolicy = directiva;
        }
        else if (!politicaActual.Contains("frame-ancestors", StringComparison.OrdinalIgnoreCase))
        {
            contexto.Response.Headers.ContentSecurityPolicy =
                politicaActual.TrimEnd(';', ' ') + "; " + directiva;
        }

        if (!embebible)
        {
            contexto.Response.Headers.XFrameOptions = "DENY";
        }

        return _siguiente(contexto);
    }
}

/// <summary>Extensión para dejar la tubería de Program.cs legible: app.UseCabecerasIntegracion();</summary>
public static class CabecerasIntegracionExtensions
{
    public static IApplicationBuilder UseCabecerasIntegracion(this IApplicationBuilder app) =>
        app.UseMiddleware<CabecerasIntegracionMiddleware>();
}
