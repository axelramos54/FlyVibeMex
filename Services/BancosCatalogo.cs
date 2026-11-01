using FlyVibeMex.Data;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace FlyVibeMex.Services;

/// <summary>
/// Bancos (instituciones_financieras) que el cliente puede elegir, opcionalmente, al guardar una tarjeta
/// (Mis tarjetas y pago con tarjeta nueva). Los administra el SA (T4 fase D).
/// </summary>
public static class BancosCatalogo
{
    /// <summary>Bancos ACTIVOS para un &lt;select&gt;, con <paramref name="elegido"/> marcado.</summary>
    public static async Task<List<SelectListItem>> OpcionesAsync(FlyVibeMexDbContext db, ulong? elegido, CancellationToken cancellationToken)
    {
        var bancos = await db.InstitucionesFinancieras.AsNoTracking()
            .Where(b => b.activo)
            .OrderBy(b => b.nombre)
            .Select(b => new { b.id, b.nombre })
            .ToListAsync(cancellationToken);
        return bancos.Select(b => new SelectListItem(b.nombre, b.id.ToString(), b.id == elegido)).ToList();
    }

    /// <summary>null (sin banco) es válido; si se indica uno, debe existir y estar activo.</summary>
    public static async Task<bool> EsValidoAsync(FlyVibeMexDbContext db, ulong? idInstitucion, CancellationToken cancellationToken) =>
        idInstitucion is not ulong id
        || await db.InstitucionesFinancieras.AsNoTracking().AnyAsync(b => b.id == id && b.activo, cancellationToken);

    /// <summary>Nombre de cada banco (activos e inactivos) para mostrar en la lista de tarjetas.</summary>
    public static Task<Dictionary<ulong, string>> NombresAsync(FlyVibeMexDbContext db, CancellationToken cancellationToken) =>
        db.InstitucionesFinancieras.AsNoTracking().ToDictionaryAsync(b => b.id, b => b.nombre, cancellationToken);
}
