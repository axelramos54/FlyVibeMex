using System.Text;

namespace FlyVibeMex.Bot;

/// <summary>
/// Arma un archivo de calendario <c>.ics</c> (RFC 5545) con un viaje, para "📅 Agregar al calendario" en el bot.
/// Lo abren Google Calendar, Outlook y el calendario del iPhone o Android.
/// </summary>
/// <remarks>
/// Las fechas de la BD son hora local del servidor (la de México en esta app); en el archivo van en UTC (sufijo Z),
/// así cada calendario las muestra en la zona horaria de quien lo abre. Solo lleva código, servicio y fechas:
/// nada de pagos ni datos personales.
/// </remarks>
public static class CalendarioIcs
{
    private const int MaximoOctetosPorLinea = 75;

    public static byte[] Crear(string codigoReserva, string servicio, string? descripcion, DateTime inicio, DateTime fin)
    {
        if (fin <= inicio) fin = inicio.AddHours(1);

        var lineas = new List<string>
        {
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//FlyVibe Mex//Bot de Telegram//ES",
            "CALSCALE:GREGORIAN",
            "METHOD:PUBLISH",
            "BEGIN:VEVENT",
            $"UID:{Texto(codigoReserva)}@flyvibemex",
            $"DTSTAMP:{Utc(DateTime.Now)}",
            $"DTSTART:{Utc(inicio)}",
            $"DTEND:{Utc(fin)}",
            $"SUMMARY:{Texto($"{servicio} ({codigoReserva})")}",
            $"DESCRIPTION:{Texto($"Reservación {codigoReserva} de FlyVibe Mex.\n{descripcion}".Trim())}",
            // Recordatorio del propio calendario un día antes.
            "BEGIN:VALARM",
            "TRIGGER:-P1D",
            "ACTION:DISPLAY",
            $"DESCRIPTION:{Texto($"Mañana: {servicio}")}",
            "END:VALARM",
            "END:VEVENT",
            "END:VCALENDAR"
        };

        var sb = new StringBuilder();
        foreach (var linea in lineas) Doblar(sb, linea);
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string Utc(DateTime local) =>
        DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'");

    /// <summary>Escapa un texto para el .ics: \ ; , y saltos de línea.</summary>
    private static string Texto(string? texto) =>
        (texto ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace(";", "\\;")
            .Replace(",", "\\,")
            .Replace("\r\n", "\\n")
            .Replace("\n", "\\n")
            .Replace("\r", "\\n");

    /// <summary>
    /// Cada línea del .ics termina en CRLF y mide máximo 75 octetos; las largas se "doblan" (CRLF + espacio) sin partir
    /// un carácter de varios bytes (acentos, emojis).
    /// </summary>
    private static void Doblar(StringBuilder sb, string linea)
    {
        var octetos = 0;
        foreach (var runa in linea.EnumerateRunes())
        {
            var tamano = runa.Utf8SequenceLength;
            if (octetos + tamano > MaximoOctetosPorLinea)
            {
                sb.Append("\r\n ");
                octetos = 1;   // el espacio inicial cuenta
            }
            sb.Append(runa.ToString());
            octetos += tamano;
        }
        sb.Append("\r\n");
    }
}
