namespace FlyVibeMex.Services;

/// <summary>
/// Implementación de <see cref="IAlmacenImagenes"/> sobre el disco del servidor (<c>wwwroot/uploads/</c>).
/// Se registra como singleton en Program.cs (no guarda estado por petición).
/// </summary>
/// <remarks>
/// Los archivos subidos en ejecución NO los sirve <c>MapStaticAssets()</c> (solo conoce los archivos que existían al
/// compilar); por eso Program.cs agrega <c>UseStaticFiles</c> exclusivamente para <c>/uploads</c>.
/// La carpeta <c>wwwroot/uploads/</c> está en .gitignore: cada integrante tiene sus propias imágenes de prueba.
/// </remarks>
public sealed class AlmacenImagenes : IAlmacenImagenes
{
    /// <summary>Tamaño máximo permitido: 2 MB.</summary>
    public const long TamanoMaximoBytes = 2 * 1024 * 1024;

    /// <summary>Prefijo de todas las rutas públicas que genera este servicio.</summary>
    public const string PrefijoPublico = "/uploads/";

    private readonly string _raizUploads;
    private readonly ILogger<AlmacenImagenes> _logger;

    public AlmacenImagenes(IWebHostEnvironment entorno, ILogger<AlmacenImagenes> logger)
    {
        _raizUploads = ObtenerRaizUploads(entorno);
        _logger = logger;
    }

    /// <summary>Ruta física de <c>wwwroot/uploads</c> (la usa también Program.cs).</summary>
    public static string ObtenerRaizUploads(IWebHostEnvironment entorno)
    {
        var wwwroot = entorno.WebRootPath ?? Path.Combine(entorno.ContentRootPath, "wwwroot");
        return Path.GetFullPath(Path.Combine(wwwroot, "uploads"));
    }

    public string? Validar(IFormFile? archivo)
    {
        if (archivo is null || archivo.Length == 0) return null;   // la imagen es opcional

        if (archivo.Length > TamanoMaximoBytes)
            return "La imagen no debe pesar más de 2 MB.";

        return DetectarExtension(archivo) is null
            ? "Formato no permitido. Usa una imagen JPG, PNG o WEBP."
            : null;
    }

    public async Task<string> GuardarAsync(IFormFile archivo, CarpetaImagen carpeta, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archivo);

        var error = archivo.Length == 0 ? "La imagen está vacía." : Validar(archivo);
        if (error is not null) throw new InvalidOperationException(error);

        var extension = DetectarExtension(archivo)!;                 // ya validado: .jpg | .png | .webp
        var nombreCarpeta = carpeta.ToString().ToLowerInvariant();  // hospedajes | vehiculos | paquetes | usuarios
        var directorio = Path.Combine(_raizUploads, nombreCarpeta);
        Directory.CreateDirectory(directorio);

        // Nombre generado por el servidor: evita rutas maliciosas ("../../"), choques y caracteres raros.
        var nombreArchivo = Guid.NewGuid().ToString("N") + extension;

        await using (var destino = new FileStream(Path.Combine(directorio, nombreArchivo),
                         FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true))
        {
            await archivo.CopyToAsync(destino, cancellationToken);
        }

        return PrefijoPublico + nombreCarpeta + "/" + nombreArchivo;
    }

    public void Eliminar(string? rutaPublica)
    {
        if (string.IsNullOrWhiteSpace(rutaPublica) || !rutaPublica.StartsWith(PrefijoPublico, StringComparison.Ordinal))
            return;

        var relativa = rutaPublica[PrefijoPublico.Length..].Replace('/', Path.DirectorySeparatorChar);
        var completa = Path.GetFullPath(Path.Combine(_raizUploads, relativa));

        // Seguridad: nunca borrar nada fuera de wwwroot/uploads (p. ej. si alguien guardó "/uploads/../appsettings.json").
        if (!completa.StartsWith(_raizUploads + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Se intentó borrar una imagen fuera de wwwroot/uploads: {Ruta}", rutaPublica);
            return;
        }

        try
        {
            if (File.Exists(completa)) File.Delete(completa);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "No se pudo borrar la imagen {Ruta}.", rutaPublica);
        }
    }

    /// <summary>Lee los primeros 12 bytes y devuelve la extensión real (.jpg, .png, .webp) o <c>null</c>.</summary>
    private static string? DetectarExtension(IFormFile archivo)
    {
        Span<byte> c = stackalloc byte[12];
        int leidos;
        using (var stream = archivo.OpenReadStream())
        {
            leidos = stream.ReadAtLeast(c, c.Length, throwOnEndOfStream: false);
        }

        if (leidos >= 3 && c[0] == 0xFF && c[1] == 0xD8 && c[2] == 0xFF)
            return ".jpg";
        if (leidos >= 8 && c[0] == 0x89 && c[1] == 0x50 && c[2] == 0x4E && c[3] == 0x47
                        && c[4] == 0x0D && c[5] == 0x0A && c[6] == 0x1A && c[7] == 0x0A)
            return ".png";
        if (leidos >= 12 && c[0] == (byte)'R' && c[1] == (byte)'I' && c[2] == (byte)'F' && c[3] == (byte)'F'
                         && c[8] == (byte)'W' && c[9] == (byte)'E' && c[10] == (byte)'B' && c[11] == (byte)'P')
            return ".webp";
        return null;
    }
}
