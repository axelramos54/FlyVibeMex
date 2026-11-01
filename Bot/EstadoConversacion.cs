using System.Collections.Concurrent;

namespace FlyVibeMex.Bot;

/// <summary>Paso actual de un chat dentro de un flujo de varios mensajes (máquina de estados sencilla).</summary>
/// <param name="Flujo">Ver <see cref="Flujos"/>.</param>
/// <param name="Paso">Ver <see cref="Pasos"/>.</param>
/// <param name="Datos">Lo que el usuario ya contestó (asunto, id de la reservación…).</param>
public sealed record EstadoConversacion(string Flujo, string Paso, IReadOnlyDictionary<string, string> Datos, DateTimeOffset ExpiraEn)
{
    public string? Dato(string clave) => Datos.TryGetValue(clave, out var valor) ? valor : null;
}

/// <summary>Nombres de los flujos de varios pasos.</summary>
public static class Flujos
{
    public const string Vincular = "VINCULAR";
    public const string Ticket = "TICKET";
    public const string Respuesta = "RESPUESTA";
}

/// <summary>Pasos dentro de un flujo.</summary>
public static class Pasos
{
    public const string Codigo = "CODIGO";
    public const string Asunto = "ASUNTO";
    public const string Mensaje = "MENSAJE";
    public const string Confirmar = "CONFIRMAR";
}

/// <summary>Dónde se guarda en qué paso va cada chat.</summary>
public interface IEstadoConversacionStore
{
    EstadoConversacion? Obtener(long chatId);
    void Guardar(long chatId, string flujo, string paso, IReadOnlyDictionary<string, string> datos);
    void Limpiar(long chatId);
}

/// <summary>
/// Implementación en memoria (singleton). Suficiente para un solo servidor: si la app se reinicia, el usuario
/// solo repite el flujo (nada se guarda en la BD hasta el último paso). Cada estado vence a los 10 minutos.
/// </summary>
public sealed class EstadoConversacionEnMemoria : IEstadoConversacionStore
{
    private static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<long, EstadoConversacion> _estados = new();

    public EstadoConversacion? Obtener(long chatId)
    {
        if (!_estados.TryGetValue(chatId, out var estado)) return null;
        if (estado.ExpiraEn > DateTimeOffset.UtcNow) return estado;
        _estados.TryRemove(chatId, out _);
        return null;
    }

    public void Guardar(long chatId, string flujo, string paso, IReadOnlyDictionary<string, string> datos)
    {
        _estados[chatId] = new EstadoConversacion(flujo, paso, new Dictionary<string, string>(datos), DateTimeOffset.UtcNow + Vigencia);

        // Limpieza ocasional de estados vencidos (chats que abandonaron un flujo).
        if (_estados.Count > 500)
        {
            var ahora = DateTimeOffset.UtcNow;
            foreach (var (id, e) in _estados)
                if (e.ExpiraEn <= ahora) _estados.TryRemove(id, out _);
        }
    }

    public void Limpiar(long chatId) => _estados.TryRemove(chatId, out _);
}
