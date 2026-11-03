using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

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

    /// <summary>
    /// Ancho máximo que se guarda. Las imágenes más anchas se reducen a esta medida conservando la proporción.
    /// </summary>
    /// <remarks>
    /// L3b de <c>docs/PLAN_CALIDAD_Y_OPTIMIZACION.md</c>. Las tarjetas del catálogo pintan la imagen a unos
    /// 300 px de ancho y el detalle a unos 700 px, así que 1600 px deja margen para pantallas grandes y de alta
    /// densidad sin guardar una foto de 4000 px. Lo que se gana no son solo bytes de red: un equipo antiguo tiene
    /// que DECODIFICAR la imagen completa en memoria antes de dibujarla, y eso es lo que lo hace sufrir.
    /// </remarks>
    public const int AnchoMaximo = 1600;

    /// <summary>Calidad del JPG de salida: 82 es el punto donde dejar de ver diferencia y el archivo ya es pequeño.</summary>
    private const int CalidadJpg = 82;

    /// <summary>
    /// Límite de píxeles que se aceptan decodificar (50 megapíxeles).
    /// </summary>
    /// <remarks>
    /// Defensa contra una "bomba de descompresión": un JPG de 2 MB puede describir una imagen de 20000 × 20000,
    /// que al decodificarse ocuparía más de 1 GB de RAM y tumbaría el servidor. Las dimensiones se leen con
    /// <c>Image.Identify</c>, que NO decodifica los píxeles; si se pasa del límite, el archivo se guarda tal como
    /// llegó (ya está acotado a 2 MB) y nunca se decodifica.
    /// </remarks>
    private const long PixelesMaximos = 50L * 1000 * 1000;

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
            await GuardarRedimensionadaAsync(archivo, destino, extension, cancellationToken);
        }

        return PrefijoPublico + nombreCarpeta + "/" + nombreArchivo;
    }

    /// <summary>
    /// Escribe la imagen en <paramref name="destino"/>, reducida a <see cref="AnchoMaximo"/> si es más ancha.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Regla de oro: <b>subir una imagen nunca debe fallar por una optimización</b>. Si el redimensionado no se
    /// puede hacer —formato que la librería no entiende, archivo corrupto, imagen descomunal, falta de memoria—
    /// se guarda el archivo original tal como llegó, que es exactamente lo que hacía este método antes de L3b.
    /// </para>
    /// <para>
    /// El formato de salida es el mismo que el de entrada (un PNG sigue siendo PNG: si se convirtiera a JPG, la
    /// transparencia se volvería negra). El JPG se vuelve a codificar con calidad <see cref="CalidadJpg"/>.
    /// </para>
    /// </remarks>
    private async Task GuardarRedimensionadaAsync(IFormFile archivo, Stream destino, string extension,
        CancellationToken cancellationToken)
    {
        try
        {
            // 1) Dimensiones SIN decodificar los píxeles (defensa contra la bomba de descompresión).
            await using (var sonda = archivo.OpenReadStream())
            {
                var info = await Image.IdentifyAsync(sonda, cancellationToken);
                if ((long)info.Width * info.Height > PixelesMaximos)
                {
                    _logger.LogWarning(
                        "Imagen de {Ancho}×{Alto} px: se guarda sin redimensionar para no decodificarla.",
                        info.Width, info.Height);
                    await CopiarOriginalAsync(archivo, destino, cancellationToken);
                    return;
                }

                // Ya cabe: si además no es más ancha que el máximo, no hay nada que mejorar. Se copia tal cual y
                // se evita recodificar (recodificar un JPG siempre pierde algo de calidad).
                if (info.Width <= AnchoMaximo)
                {
                    await CopiarOriginalAsync(archivo, destino, cancellationToken);
                    return;
                }
            }

            // 2) Decodificar, reducir y escribir.
            await using var origen = archivo.OpenReadStream();
            using var imagen = await Image.LoadAsync(origen, cancellationToken);

            var nuevoAlto = Math.Max(1, (int)Math.Round(imagen.Height * (double)AnchoMaximo / imagen.Width));
            imagen.Mutate(x => x.Resize(AnchoMaximo, nuevoAlto));

            switch (extension)
            {
                case ".png":
                    await imagen.SaveAsPngAsync(destino, cancellationToken);
                    break;
                case ".webp":
                    await imagen.SaveAsWebpAsync(destino, cancellationToken);
                    break;
                default:
                    await imagen.SaveAsJpegAsync(destino, new JpegEncoder { Quality = CalidadJpg }, cancellationToken);
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Nunca se rompe la subida por esto: se deja el archivo original.
            _logger.LogWarning(ex, "No se pudo redimensionar la imagen; se guarda el archivo original.");
            destino.SetLength(0);
            destino.Position = 0;
            await CopiarOriginalAsync(archivo, destino, cancellationToken);
        }
    }

    /// <summary>Copia el archivo subido byte a byte, sin decodificarlo.</summary>
    private static async Task CopiarOriginalAsync(IFormFile archivo, Stream destino, CancellationToken cancellationToken)
    {
        await using var origen = archivo.OpenReadStream();
        await origen.CopyToAsync(destino, cancellationToken);
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
