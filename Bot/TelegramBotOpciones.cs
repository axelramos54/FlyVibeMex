using System.Text.RegularExpressions;

namespace FlyVibeMex.Bot;

/// <summary>
/// Configuración del bot de Telegram (sección <c>"TelegramBot"</c>). docs/06_ANALISIS_Y_DISENO_BOT_TELEGRAM.md
/// </summary>
/// <remarks>
/// <para><b>El token NUNCA va en appsettings.json ni en el código.</b> Cada integrante lo guarda en su PC con
/// <c>dotnet user-secrets set "TelegramBot:Token" "123456:ABC..."</c>; en un servidor, con la variable de entorno
/// <c>TelegramBot__Token</c>. Si alguien lo sube a GitHub por error: @BotFather › /revoke y generar otro.</para>
/// <para>Si <see cref="Habilitado"/> es false o falta el token, la web funciona igual y el bot simplemente no arranca.</para>
/// </remarks>
public sealed partial class TelegramBotOpciones
{
    public const string Seccion = "TelegramBot";

    /// <summary>Interruptor general (appsettings / user-secrets).</summary>
    public bool Habilitado { get; set; }

    /// <summary>Token de @BotFather. Solo por user-secrets o variable de entorno.</summary>
    public string? Token { get; set; }

    /// <summary>Id del grupo privado de agentes que recibe los tickets nuevos (negativo, p. ej. -1001234567890). Opcional.</summary>
    public long? ChatIdAgentes { get; set; }

    /// <summary>Dirección del sitio para los botones "Ver en la web" (sin / al final).</summary>
    public string UrlSitioWeb { get; set; } = "https://localhost:7045";

    /// <summary>Usuario del bot sin @ (respaldo para los enlaces t.me si Telegram aún no responde). Opcional.</summary>
    public string? NombreUsuarioBot { get; set; }

    /// <summary>Formato de un token de @BotFather: número del bot, dos puntos y la clave.</summary>
    [GeneratedRegex(@"^\d{5,}:[A-Za-z0-9_-]{30,}$")]
    private static partial Regex FormatoToken();

    /// <summary>Hay token con formato válido.</summary>
    public bool TokenValido => !string.IsNullOrWhiteSpace(Token) && FormatoToken().IsMatch(Token.Trim());

    /// <summary>El bot se inicia con la app.</summary>
    public bool EstaConfigurado => Habilitado && TokenValido;

    /// <summary>Texto para el log al arrancar (nunca incluye el token).</summary>
    public string DescripcionEstado() =>
        !Habilitado ? "desactivado (TelegramBot:Habilitado = false)"
        : string.IsNullOrWhiteSpace(Token) ? "desactivado: falta TelegramBot:Token (dotnet user-secrets, ver docs/06)"
        : !TokenValido ? "desactivado: TelegramBot:Token no tiene el formato de @BotFather"
        : "activo (long polling)";
}
