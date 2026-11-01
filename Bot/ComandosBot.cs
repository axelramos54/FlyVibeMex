using Telegram.Bot.Types;

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
}
