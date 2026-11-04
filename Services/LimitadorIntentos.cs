using System.Collections.Concurrent;

namespace FlyVibeMex.Services;

/// <summary>
/// Límite de acciones por llave en una ventana de tiempo. Lo usan <b>dos</b> partes del sistema: el bot de
/// Telegram (anti spam de mensajes y anti fuerza bruta de <c>/vincular</c>) y el sitio web (intentos de inicio
/// de sesión, y la consulta de disponibilidad de correo del registro).
/// </summary>
/// <remarks>
/// <para>
/// Vivía en <c>Bot/</c> hasta el 04/11/2026. Se movió aquí porque su registro estaba <b>dentro</b> de
/// <c>ConfiguracionBotTelegram.AgregarBotTelegram</c>, debajo del <c>return</c> que sale cuando no hay token de
/// Telegram configurado. Es decir: <b>sin token, esta interfaz no se registraba</b>, y cualquier controlador web
/// que la inyectara habría tronado en la primera petición. Ese es justo el escenario de una máquina recién
/// clonada o de la laptop de una presentación.
/// </para>
/// <para>
/// <b>Se registra en Program.cs, sin condiciones.</b> No volver a registrarlo en la configuración del bot:
/// limitar intentos de inicio de sesión no tiene nada que ver con Telegram, y si algún día se quita el bot, el
/// login se quedaría sin su protección sin que nadie lo note.
/// </para>
/// </remarks>
public interface ILimitadorIntentos
{
    /// <summary>Registra un intento y devuelve <c>false</c> si ya se pasó de <paramref name="maximo"/> en <paramref name="ventana"/>.</summary>
    bool Permitir(string llave, int maximo, TimeSpan ventana);
}

/// <summary>Implementación en memoria con ventana deslizante (singleton).</summary>
public sealed class LimitadorIntentosEnMemoria : ILimitadorIntentos
{
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _intentos = new();

    public bool Permitir(string llave, int maximo, TimeSpan ventana)
    {
        var ahora = DateTimeOffset.UtcNow;
        var cola = _intentos.GetOrAdd(llave, _ => new Queue<DateTimeOffset>());
        lock (cola)
        {
            while (cola.Count > 0 && ahora - cola.Peek() > ventana) cola.Dequeue();
            if (cola.Count >= maximo) return false;
            cola.Enqueue(ahora);
        }

        if (_intentos.Count > 5000) Limpiar(ahora, ventana);
        return true;
    }

    private void Limpiar(DateTimeOffset ahora, TimeSpan ventana)
    {
        foreach (var (llave, cola) in _intentos)
        {
            lock (cola)
            {
                if (cola.Count == 0 || ahora - cola.Peek() > ventana) _intentos.TryRemove(llave, out _);
            }
        }
    }
}
