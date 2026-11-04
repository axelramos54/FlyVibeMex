// ███ ESTE ARCHIVO VA EN EL PROYECTO DE FLYVIBEMEX ███  (no en GoldStay)
// Ruta: Services/PeticionEmbebida.cs

namespace FlyVibeMex.Services;

/// <summary>
/// ¿Esta petición viene de un &lt;iframe&gt; (el widget incrustado en GoldStay) o de alguien navegando el sitio
/// normal? Lo usa Views/_ViewStart.cshtml para elegir el layout: el normal con header, menú y footer, o
/// _LayoutWidget, que es solo el contenido.
///
/// Por qué así y no con vistas aparte: duplicar Catalogo/Detalle y las de Reservas en una versión "widget"
/// significa mantener dos copias de lo mismo y que una se quede atrás. La vista es la misma; lo único que
/// cambia es el marco.
///
/// Cómo se detecta, en orden:
///   1. Cabecera "Sec-Fetch-Dest: iframe". La manda el navegador, NO la página: Chrome, Firefox y Safari
///      actuales la incluyen y el JavaScript de un sitio no puede falsificarla (es una "forbidden header").
///      Es el método bueno porque no requiere nada de GoldStay ni ensucia las URL.
///   2. "?widget=1" en la dirección. Respaldo para un navegador viejo que no manda Sec-Fetch-Dest, y para
///      poder PROBAR el layout limpio abriendo la página directo, sin montar el iframe.
///
/// Que alguien fuerce el layout limpio con ?widget=1 no es un problema de seguridad: ve el mismo contenido,
/// solo sin el menú. La autorización no depende de esto en ningún caso.
/// </summary>
public static class PeticionEmbebida
{
    /// <summary>Nombre del parámetro de respaldo para forzar el layout del widget.</summary>
    public const string ParametroWidget = "widget";

    /// <summary>¿Hay que usar el layout limpio (_LayoutWidget) en esta petición?</summary>
    public static bool EsEmbebida(this HttpContext? contexto)
    {
        if (contexto is null)
        {
            return false;
        }

        // 1) Lo que dice el navegador sobre el destino de la petición.
        //    "iframe" = documento dentro de un iframe; "frame" existe por los <frame> antiguos.
        var destino = contexto.Request.Headers["Sec-Fetch-Dest"].ToString();

        if (string.Equals(destino, "iframe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(destino, "frame", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 2) Respaldo explícito: ?widget=1
        var valor = contexto.Request.Query[ParametroWidget].ToString();

        return valor is "1" or "true" or "True";
    }
}
