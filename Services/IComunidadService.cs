// Services/IComunidadService.cs
namespace FlyVibeMex.Services;

public record ResenaFila(ulong id, int calificacion, string? titulo, string comentario, string estado, DateTime creada_en, string autor);

public interface IComunidadService
{
    Task<bool> AlternarFavoritoAsync(ulong idUsuario, ulong idServicio, CancellationToken ct = default);
    Task<HashSet<ulong>> IdsFavoritosAsync(ulong idUsuario, CancellationToken ct = default);
    Task<ulong> CrearResenaAsync(ulong idUsuario, ulong idServicio, int calificacion, string? titulo, string comentario, CancellationToken ct = default);
    Task<(List<ResenaFila> resenas, decimal promedio, int total)> ResenasAsync(ulong idServicio, bool incluirOcultas, CancellationToken ct = default);
    Task ModerarResenaAsync(ulong idUsuario, ulong idResena, bool ocultar, CancellationToken ct = default);
}