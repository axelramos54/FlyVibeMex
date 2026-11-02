using System.Collections.Concurrent;
using FlyVibeMex.Services;

namespace FlyVibeMex.Bot;

/// <summary>
/// Fotos de las fichas del bot (singleton). La imagen se toma del mismo archivo que usa la web
/// (<c>imagen_ruta</c> = <c>/uploads/&lt;carpeta&gt;/&lt;guid&gt;.jpg</c>) y se SUBE a Telegram, así que funciona aunque
/// el sitio corra en localhost. Después de la primera subida se recuerda el <c>file_id</c> que devuelve Telegram
/// para no volver a mandar el archivo; al cambiar la imagen en el panel cambia su ruta (guid nuevo), así que el
/// <c>file_id</c> recordado nunca muestra una foto vieja.
/// </summary>
public sealed class FotosServicio
{
    /// <summary>Telegram acepta fotos de hasta 10 MB (el panel ya limita las imágenes a 2 MB).</summary>
    private const long TamanoMaximoBytes = 10 * 1024 * 1024;
    private const int MaximoRecordadas = 2000;

    private readonly string _raizUploads;
    private readonly ConcurrentDictionary<string, string> _idsTelegram = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _rechazadas = new(StringComparer.Ordinal);

    public FotosServicio(IWebHostEnvironment entorno) => _raizUploads = AlmacenImagenes.ObtenerRaizUploads(entorno);

    /// <summary>¿Hay una foto que mostrar para esta ruta? (existe el archivo o ya se subió, y Telegram no la rechazó).</summary>
    public bool Disponible(string? rutaPublica) =>
        !string.IsNullOrWhiteSpace(rutaPublica)
        && !_rechazadas.ContainsKey(rutaPublica)
        && (_idsTelegram.ContainsKey(rutaPublica) || RutaFisica(rutaPublica) is not null);

    public string? IdTelegram(string rutaPublica) => _idsTelegram.TryGetValue(rutaPublica, out var id) ? id : null;

    public void Recordar(string rutaPublica, string idTelegram)
    {
        if (_idsTelegram.Count < MaximoRecordadas) _idsTelegram[rutaPublica] = idTelegram;
    }

    /// <summary>Telegram no aceptó la imagen (formato o tamaño): la ficha se muestra en texto hasta reiniciar la app.</summary>
    public void Rechazar(string rutaPublica)
    {
        _idsTelegram.TryRemove(rutaPublica, out _);
        _rechazadas[rutaPublica] = 0;
    }

    /// <summary>
    /// Archivo físico de una ruta pública, o null si no existe. Seguridad: igual que <see cref="AlmacenImagenes.Eliminar"/>,
    /// nunca sale de <c>wwwroot/uploads</c> (una ruta como <c>/uploads/../appsettings.json</c> se rechaza).
    /// </summary>
    public string? RutaFisica(string? rutaPublica)
    {
        if (string.IsNullOrWhiteSpace(rutaPublica) || !rutaPublica.StartsWith(AlmacenImagenes.PrefijoPublico, StringComparison.Ordinal))
            return null;

        string completa;
        try
        {
            var relativa = rutaPublica[AlmacenImagenes.PrefijoPublico.Length..].Replace('/', Path.DirectorySeparatorChar);
            completa = Path.GetFullPath(Path.Combine(_raizUploads, relativa));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        if (!completa.StartsWith(_raizUploads + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;

        var archivo = new FileInfo(completa);
        return archivo.Exists && archivo.Length is > 0 and <= TamanoMaximoBytes ? completa : null;
    }
}
