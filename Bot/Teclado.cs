using System.Text;
using Telegram.Bot.Types.ReplyMarkups;

namespace FlyVibeMex.Bot;

/// <summary>
/// Construye los botones que van debajo de un mensaje (InlineKeyboardMarkup).
/// <c>callback_data</c> admite como máximo 64 bytes: los datos son cortos ("srv:15", "tkt:3") y nunca llevan datos
/// personales. Al recibir un botón, el servidor SIEMPRE vuelve a validar (que exista, que sea del usuario…).
/// </summary>
public sealed class Teclado
{
    private const int MaximoBytesCallback = 64;

    /// <summary>
    /// Sufijo de los botones que van en un AVISO (te respondieron, ticket cerrado): al tocarlos se abre una pantalla
    /// nueva en lugar de reescribir el aviso, para que siga en el historial del chat.
    /// </summary>
    public const string SufijoAviso = ":a";

    /// <summary>Datos de un botón de aviso: <c>DeAviso("tkt:5")</c> → <c>"tkt:5:a"</c>.</summary>
    public static string DeAviso(string datos) => datos + SufijoAviso;
    private readonly List<List<InlineKeyboardButton>> _filas = new();

    public bool Vacio => _filas.Count == 0;

    /// <summary>Un renglón con un botón.</summary>
    public Teclado Boton(string texto, string datos) => Fila((texto, datos));

    /// <summary>Un renglón con varios botones: Fila(("◀ Anterior", "cat:X:1"), ("Siguiente ▶", "cat:X:3")).</summary>
    public Teclado Fila(params (string texto, string datos)[] botones)
    {
        var fila = botones.Select(b => InlineKeyboardButton.WithCallbackData(b.texto, Validar(b.datos))).ToList();
        if (fila.Count > 0) _filas.Add(fila);
        return this;
    }

    /// <summary>
    /// Último renglón de casi todas las pantallas: "← (pantalla anterior)" y "🏠 Menú". Sin pantalla anterior, solo "🏠 Menú".
    /// </summary>
    public Teclado Navegacion(string? volverA = null, string? datosVolver = null) =>
        volverA is null || datosVolver is null
            ? Boton("🏠 Menú", "menu")
            : Fila(($"← {volverA}", datosVolver), ("🏠 Menú", "menu"));

    /// <summary>Botón que abre una dirección web (solo si es pública: ver <see cref="FormatoBot.EsUrlPublica"/>).</summary>
    public bool Enlace(string texto, string? url)
    {
        if (!FormatoBot.EsUrlPublica(url)) return false;
        _filas.Add(new List<InlineKeyboardButton> { InlineKeyboardButton.WithUrl(texto, url!) });
        return true;
    }

    public InlineKeyboardMarkup? Crear() => Vacio ? null : new InlineKeyboardMarkup(_filas);

    private static string Validar(string datos)
    {
        if (Encoding.UTF8.GetByteCount(datos) > MaximoBytesCallback)
            throw new ArgumentException($"callback_data de más de {MaximoBytesCallback} bytes: {datos}", nameof(datos));
        return datos;
    }
}
