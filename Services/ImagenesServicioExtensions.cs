using FlyVibeMex.Data;
using Microsoft.EntityFrameworkCore;

namespace FlyVibeMex.Services;

/// <summary>
/// Borrado seguro de la imagen de un servicio.
/// </summary>
public static class ImagenesServicioExtensions
{
    /// <summary>
    /// Borra del disco la imagen de <paramref name="rutaPublica"/> <b>solo si ningún servicio la sigue usando</b>.
    /// </summary>
    /// <remarks>
    /// Existe por el alta de vuelo con «repetir», que crea hasta 120 servicios —una salida por fecha— y les pone
    /// la MISMA <c>imagen_ruta</c>: es el mismo vuelo, la misma foto. Si después se cambia la imagen de una sola
    /// salida, el borrado directo del archivo anterior dejaría a las otras 119 sin foto, con la BD apuntando a un
    /// archivo que ya no existe. Esa falla no aparece en ninguna prueba manual rápida.
    /// La comprobación también protege cualquier otro caso de ruta compartida que aparezca en el futuro.
    /// </remarks>
    public static async Task BorrarImagenSiNadieLaUsaAsync(this FlyVibeMexDbContext db, IAlmacenImagenes imagenes,
        string? rutaPublica, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rutaPublica)) return;

        var sigueEnUso = await db.Servicios.AsNoTracking()
            .AnyAsync(srv => srv.imagen_ruta == rutaPublica, cancellationToken);

        if (!sigueEnUso) imagenes.Eliminar(rutaPublica);
    }
}
