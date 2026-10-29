using FlyVibeMex.Data;
using FlyVibeMex.Models;

namespace FlyVibeMex.Services;

/// <summary>Ayudante para escribir en la bitácora del panel (tabla <c>bitacora</c>).</summary>
public static class BitacoraExtensions
{
    /// <summary>
    /// Agrega un registro a la bitácora. NO guarda: se guarda con el siguiente <c>SaveChangesAsync</c>, así el registro
    /// queda en la misma transacción que el cambio (si el cambio falla, tampoco queda el registro).
    /// </summary>
    public static void RegistrarBitacora(this FlyVibeMexDbContext db, ulong? idUsuario, string accion, string entidad,
        ulong? idEntidad, string? detalle)
    {
        if (detalle is { Length: > 500 }) detalle = detalle[..497] + "...";
        db.Bitacora.Add(new Bitacora
        {
            fecha = DateTime.Now,
            id_usuario = idUsuario,
            accion = accion,
            entidad = entidad,
            id_entidad = idEntidad,
            detalle = detalle
        });
    }
}
