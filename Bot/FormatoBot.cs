using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FlyVibeMex.Bot;

/// <summary>
/// Formato de los mensajes del bot (HTML de Telegram: solo &lt;b&gt;, &lt;i&gt;, &lt;code&gt;, &lt;a&gt;…).
/// REGLA: todo texto que venga de la BD o del usuario pasa por <see cref="Html"/> antes de ir en un mensaje.
/// </summary>
public static partial class FormatoBot
{
    public static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-MX");

    /// <summary>Telegram acepta hasta 4096 caracteres por mensaje; se deja margen.</summary>
    public const int LargoMaximoMensaje = 3900;

    /// <summary>El pie de una foto admite 1024 caracteres; se deja margen.</summary>
    public const int LargoMaximoPie = 1000;

    /// <summary>Escapa &amp;, &lt;, &gt; y comillas para el modo HTML de Telegram.</summary>
    public static string Html(string? texto) =>
        string.IsNullOrEmpty(texto)
            ? string.Empty
            : texto.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    /// <summary>
    /// Primera línea de una pantalla: dónde está el usuario ("🔎 Explorar › 🏨 Hoteles › 📍 Cancún").
    /// Las partes ya deben venir escapadas con <see cref="Html"/> si traen datos de la BD.
    /// </summary>
    public static string Ruta(params string[] partes) => $"<i>{string.Join(" › ", partes)}</i>";

    /// <summary>Calificación con estrellas: 4 → "⭐⭐⭐⭐☆".</summary>
    public static string Estrellas(int cuantas)
    {
        cuantas = Math.Clamp(cuantas, 0, 5);
        return string.Concat(Enumerable.Repeat("⭐", cuantas)) + new string('☆', 5 - cuantas);
    }

    /// <summary>"Paso 2 de 3 · Detalle" (flujos de varios pasos).</summary>
    public static string Progreso(int paso, int total, string nombre) => $"<i>Paso {paso} de {total} · {nombre}</i>";

    /// <summary>Minúsculas y sin acentos ("Cancún" → "cancun"), para buscar sin que importe cómo se escribió.</summary>
    public static string SinAcentos(string? texto)
    {
        if (string.IsNullOrEmpty(texto)) return string.Empty;
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Recorta (antes de escapar) y agrega "…".</summary>
    public static string Recortar(string? texto, int maximo)
    {
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;
        var limpio = texto.Trim();
        return limpio.Length <= maximo ? limpio : limpio[..Math.Max(1, maximo - 1)].TrimEnd() + "…";
    }

    /// <summary>Recorta y escapa en un paso.</summary>
    public static string HtmlRecortado(string? texto, int maximo) => Html(Recortar(texto, maximo));

    public static string Dinero(decimal monto, string? moneda) => $"${monto.ToString("N2", Es)} {moneda ?? "MXN"}";

    public static string Fecha(DateTime fecha) => fecha.ToString("dd/MM/yyyy", Es);

    public static string FechaHora(DateTime fecha) => fecha.ToString("dd/MM/yyyy HH:mm", Es);

    public static string DiaCorto(DateOnly fecha) => fecha.ToString("ddd dd/MM", Es);

    public static string EstadoTicket(string estado) => estado switch
    {
        "ABIERTA" => "Abierta",
        "EN_PROCESO" => "En proceso",
        "CERRADA" => "Cerrada",
        _ => estado
    };

    public static string TipoTicket(string tipo) => tipo switch
    {
        "ASESORIA" => "Asesoría",
        "PROBLEMA" => "Problema",
        _ => tipo
    };

    /// <summary>"CONFIRMADA" → "Confirmada", "EN_CURSO" → "En curso".</summary>
    public static string Estado(string estado)
    {
        if (string.IsNullOrWhiteSpace(estado)) return string.Empty;
        var texto = estado.Replace('_', ' ').ToLower(Es);
        return char.ToUpper(texto[0], Es) + texto[1..];
    }

    /// <summary>
    /// Une la dirección del sitio con una ruta ("/Catalogo/Detalle/5"). Devuelve <c>null</c> si la dirección no es válida.
    /// </summary>
    public static string? UrlWeb(string urlSitio, string ruta)
    {
        if (!Uri.TryCreate(urlSitio.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri)) return null;
        return Uri.TryCreate(baseUri, ruta.TrimStart('/'), out var completa) ? completa.ToString() : null;
    }

    /// <summary>
    /// Telegram solo acepta botones con enlaces públicos: con <c>localhost</c> o una IP local el botón se rechaza.
    /// En ese caso el enlace se escribe como texto en el mensaje (sirve para la demo en la misma PC).
    /// </summary>
    public static bool EsUrlPublica(string? url) =>
        url is not null
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && !uri.IsLoopback
        && uri.Host.Contains('.')
        && !uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
        && !EsIpPrivada(uri.Host);

    private static bool EsIpPrivada(string host) =>
        System.Net.IPAddress.TryParse(host, out var ip)
        && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
        && ip.GetAddressBytes() is var b
        && (b[0] == 10 || b[0] == 127 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168));

    [GeneratedRegex(@"(?<!\d)(?:\d[ -]?){12,18}\d(?!\d)")]
    private static partial Regex PosibleTarjeta();

    /// <summary>
    /// ¿El texto trae algo con forma de número de tarjeta (13 a 19 dígitos que pasan Luhn)? Regla R5: el bot no lo
    /// guarda ni lo registra, y le pide al usuario que no lo mande. (Un teléfono de 10 dígitos no cuenta.)
    /// </summary>
    public static bool PareceTarjeta(string? texto)
    {
        if (string.IsNullOrEmpty(texto)) return false;
        foreach (Match coincidencia in PosibleTarjeta().Matches(texto))
        {
            var digitos = new string(coincidencia.Value.Where(char.IsDigit).ToArray());
            if (digitos.Length is >= 13 and <= 19 && PasaLuhn(digitos)) return true;
        }
        return false;
    }

    private static bool PasaLuhn(string digitos)
    {
        var suma = 0;
        var doble = false;
        for (var i = digitos.Length - 1; i >= 0; i--)
        {
            var d = digitos[i] - '0';
            if (doble) { d *= 2; if (d > 9) d -= 9; }
            suma += d;
            doble = !doble;
        }
        return suma % 10 == 0;
    }

    /// <summary>
    /// Asegura el largo máximo cortando en un salto de línea (cada línea de los mensajes del bot tiene sus etiquetas
    /// HTML completas, así que cortar entre líneas nunca deja una etiqueta abierta).
    /// </summary>
    /// <param name="maximo"><see cref="LargoMaximoMensaje"/> para un mensaje; <see cref="LargoMaximoPie"/> para el pie de una foto.</param>
    public static string AjustarLargo(string html, int maximo = LargoMaximoMensaje)
    {
        if (html.Length <= maximo) return html;
        var corte = html.LastIndexOf('\n', maximo - 40);
        if (corte <= 0) corte = maximo - 40;
        return html[..corte] + "\n…";
    }

    /// <summary>Ayudante para armar mensajes línea por línea.</summary>
    public static StringBuilder Linea(this StringBuilder sb, string html = "") => sb.Append(html).Append('\n');
}
