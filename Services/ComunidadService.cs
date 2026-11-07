// Services/ComunidadService.cs
using System.Data;
using FlyVibeMex.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using static FlyVibeMex.Services.ProcedimientosBd;

namespace FlyVibeMex.Services;

/// <summary>Favoritos y reseñas. Escribe con procedimientos; los errores 514xx suben como ReglaProcedimientoException.</summary>
public class ComunidadService : IComunidadService
{
    private readonly FlyVibeMexDbContext _db;
    public ComunidadService(FlyVibeMexDbContext db) => _db = db;

    public async Task<bool> AlternarFavoritoAsync(ulong idUsuario, ulong idServicio, CancellationToken ct = default)
    {
        var salida = Salida("@es_favorito", SqlDbType.Bit);
        await EjecutarAsync(_db, "dbo.pa_alternar_favorito",
            new[] { Par("@id_usuario", (long)idUsuario), Par("@id_servicio", (long)idServicio), salida }, ct);
        return salida.Value is bool b && b;
    }

    // Una sola consulta por página para pintar el corazón lleno.
    public async Task<HashSet<ulong>> IdsFavoritosAsync(ulong idUsuario, CancellationToken ct = default) =>
        (await _db.Favoritos.AsNoTracking().Where(f => f.id_usuarioglobal == idUsuario)
            .Select(f => f.id_servicio).ToListAsync(ct)).ToHashSet();

    public async Task<ulong> CrearResenaAsync(ulong idUsuario, ulong idServicio, int calificacion, string? titulo, string comentario, CancellationToken ct = default)
    {
        var salida = Salida("@id_resena", SqlDbType.BigInt);
        await EjecutarAsync(_db, "dbo.pa_crear_resena", new[]
        {
            Par("@id_usuario", (long)idUsuario), Par("@id_servicio", (long)idServicio),
            Par("@calificacion", (byte)calificacion), Par("@titulo", titulo), Par("@comentario", comentario), salida
        }, ct);
        return salida.Value is long id ? (ulong)id : 0;
    }

    public async Task<(List<ResenaFila>, decimal, int)> ResenasAsync(ulong idServicio, bool incluirOcultas, CancellationToken ct = default)
    {
        var sets = await LeerVariosAsync<object>(_db, "dbo.pa_listar_resenas",
            new[] { Par("@id_servicio", (long)idServicio), Par("@incluir_ocultas", incluirOcultas) },
            new Func<SqlDataReader, object>[]
            {
                r => new ResenaFila((ulong)r.GetInt64(0), r.GetByte(1), r.IsDBNull(2) ? null : r.GetString(2),
                                    r.GetString(3), r.GetString(4), r.GetDateTime(5), r.GetString(6)),
                r => (r.GetDecimal(0), r.GetInt32(1))
            }, ct);
        var resenas = sets[0].Cast<ResenaFila>().ToList();
        var (prom, total) = sets.Count > 1 && sets[1].Count > 0 ? ((decimal, int))sets[1][0] : (0m, 0);
        return (resenas, prom, total);
    }

    public Task ModerarResenaAsync(ulong idUsuario, ulong idResena, bool ocultar, CancellationToken ct = default) =>
        EjecutarAsync(_db, "dbo.pa_moderar_resena", new[]
            { Par("@id_usuario", (long)idUsuario), Par("@id_resena", (long)idResena), Par("@estado", ocultar ? "OCULTA" : "VISIBLE") }, ct);
}