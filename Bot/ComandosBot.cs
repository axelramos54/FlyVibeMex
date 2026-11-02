using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace FlyVibeMex.Bot;

/// <summary>Menú de comandos que Telegram muestra al escribir "/" (se registra al arrancar con SetMyCommands).</summary>
/// <remarks>
/// Solo se MUESTRAN 5; el resto sigue funcionando aunque no aparezca en la lista (mensajes viejos, la página
/// Mi cuenta › Telegram y la costumbre de quien ya usaba el bot): /cuenta, /tickets, /asesoria, /problema,
/// /vincular, /desvincular y /cancelar.
/// </remarks>
public static class ComandosBot
{
    /// <summary>Chats privados.</summary>
    public static readonly BotCommand[] Menu =
    {
        new() { Command = "menu", Description = "Menú principal" },
        new() { Command = "explorar", Description = "Vuelos, hoteles, vehículos, tours y paquetes" },
        new() { Command = "reservas", Description = "Mis reservaciones (cuenta vinculada)" },
        new() { Command = "soporte", Description = "Asesoría, reportar un problema y mis tickets" },
        new() { Command = "ayuda", Description = "Políticas, métodos de pago y contacto" },
    };

    /// <summary>Grupos: ahí el bot solo responde /idchat (para configurar el grupo de agentes).</summary>
    public static readonly BotCommand[] Grupo =
    {
        new() { Command = "idchat", Description = "Id de este chat (configurar el grupo de agentes)" },
    };

    // Teclado fijo (abajo del chat). Sus botones llegan como texto normal y se tratan como comandos.
    public const string AtajoExplorar = "🔎 Explorar";
    public const string AtajoReservas = "🧳 Mis reservaciones";
    public const string AtajoSoporte = "💬 Soporte";
    public const string AtajoMenu = "🏠 Menú";

    /// <summary>Comando de un botón del teclado fijo, o null si el texto no es uno de ellos.</summary>
    public static string? Atajo(string texto) => texto switch
    {
        AtajoExplorar => "explorar",
        AtajoReservas => "reservas",
        AtajoSoporte => "soporte",
        AtajoMenu => "menu",
        _ => null
    };

    /// <summary>
    /// Teclado de 2×2 que reemplaza al del celular (se oculta con el ícono del teclado). Telegram lo recuerda en el
    /// chat; se manda con el saludo de /start.
    /// </summary>
    public static ReplyKeyboardMarkup TecladoFijo() => new(new[]
    {
        new KeyboardButton[] { new(AtajoExplorar), new(AtajoReservas) },
        new KeyboardButton[] { new(AtajoSoporte), new(AtajoMenu) }
    })
    {
        IsPersistent = true,
        ResizeKeyboard = true,
        InputFieldPlaceholder = "Escribe un destino o usa los botones"
    };
}
