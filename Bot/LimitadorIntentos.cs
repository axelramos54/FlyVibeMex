using System.Collections.Concurrent;

namespace FlyVibeMex.Bot;

/// <summary>Límite de acciones por llave en una ventana de tiempo (anti spam y anti fuerza bruta de /vincular).</summary>
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
