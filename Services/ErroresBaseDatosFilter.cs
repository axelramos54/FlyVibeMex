using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FlyVibeMex.Services;

/// <summary>
/// Red de seguridad (T6.3): si un error de base de datos se escapa de un servicio durante un <b>POST</b>
/// (BD caída, duplicado no previsto, reintentos agotados), en lugar de la página 500 se regresa a la página de donde
/// vino el formulario con el aviso "No se pudo guardar…". El detalle queda SOLO en el log del servidor.
/// </summary>
/// <remarks>
/// Se registra global en Program.cs (<c>opciones.Filters.Add&lt;ErroresBaseDatosFilter&gt;()</c>).
/// En un GET no hace nada: el error llega a <c>/Estado/500</c>. Solo atrapa errores de la acción (no de la vista).
/// Los servicios siguen atrapando sus propios errores con mensajes más precisos; esto es solo lo que se escapa.
/// </remarks>
public sealed class ErroresBaseDatosFilter : IAsyncExceptionFilter
{
    private const string Mensaje =
        "No se pudo guardar por un problema con la base de datos. No se hizo ningún cambio; intenta de nuevo en unos minutos.";

    private readonly ITempDataDictionaryFactory _tempData;
    private readonly ILogger<ErroresBaseDatosFilter> _logger;

    public ErroresBaseDatosFilter(ITempDataDictionaryFactory tempData, ILogger<ErroresBaseDatosFilter> logger)
    {
        _tempData = tempData;
        _logger = logger;
    }

    public Task OnExceptionAsync(ExceptionContext context)
    {
        var http = context.HttpContext;
        if (context.ExceptionHandled || !HttpMethods.IsPost(http.Request.Method) || !EsErrorDeBd(context.Exception))
            return Task.CompletedTask;

        _logger.LogError(context.Exception, "Error de base de datos en POST {Ruta} (referencia {Referencia}).",
            http.Request.Path, http.TraceIdentifier);

        // "Aviso" lo muestran _Layout (sitio) y _AdminLayout (panel).
        var tempData = _tempData.GetTempData(http);
        tempData["Aviso"] = Mensaje;

        context.Result = new RedirectResult(Regreso(http));
        context.ExceptionHandled = true;
        return Task.CompletedTask;
    }

    /// <summary>
    /// La página del formulario (Referer) si es de este mismo sitio; si no, el inicio. Solo se usa la ruta local
    /// (nunca una URL de otro sitio: evita redirecciones abiertas).
    /// </summary>
    private static string Regreso(HttpContext http)
    {
        var referer = http.Request.Headers.Referer.ToString();
        if (Uri.TryCreate(referer, UriKind.Absolute, out var origen)
            && string.Equals(origen.Host, http.Request.Host.Host, StringComparison.OrdinalIgnoreCase)
            && origen.PathAndQuery.StartsWith('/') && !origen.PathAndQuery.StartsWith("//"))
        {
            return origen.PathAndQuery;
        }
        return http.Request.Path.StartsWithSegments("/Admin") ? "/Admin" : "/";
    }

    private static bool EsErrorDeBd(Exception ex) =>
        ex is DbUpdateException or SqlException or RetryLimitExceededException
        || ex.InnerException is SqlException;
}
