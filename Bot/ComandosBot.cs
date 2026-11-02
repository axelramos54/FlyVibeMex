using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace FlyVibeMex.Bot;

/// <summary>Menú de comandos que Telegram muestra al escribir "/" (se registra al arrancar con SetMyCommands).</summary>
public static class ComandosBot
{
    public static readonly BotCommand[] Menu =
    {
        new() { Command = "menu", Description = "Menú principal" },
        new() { Command = "explorar", Description = "Vuelos, hoteles, vehículos, tours y paquetes" },
        new() { Command = "reservas", Description = "Mis próximas reservaciones (cuenta vinculada)" },
        new() { Command = "tickets", Description = "Mis tickets de soporte (cuenta vinculada)" },
        new() { Command = "asesoria", Description = "Pedir ayuda a un asesor" },
        new() { Command = "problema", Description = "Reportar un problema con una reservación" },
        new() { Command = "ayuda", Description = "Políticas, métodos de pago y contacto" },
        new() { Command = "vincular", Description = "Vincular mi cuenta de FlyVibeMex" },
        new() { Command = "desvincular", Description = "Desvincular mi cuenta" },
        new() { Command = "cancelar", Description = "Cancelar lo que estoy haciendo" },
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
