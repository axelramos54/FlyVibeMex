namespace FlyVibeMex.Services;

/// <summary>Subcarpetas permitidas dentro de <c>wwwroot/uploads/</c> (una por tabla con imagen).</summary>
public enum CarpetaImagen
{
    Hospedajes,
    Vehiculos,
    Paquetes,
    Usuarios
}

/// <summary>
/// Guarda, valida y borra las imágenes que se suben desde la aplicación.
/// En la base de datos solo se guarda la <b>ruta pública</b> que devuelve <see cref="GuardarAsync"/>
/// (columna <c>imagen_ruta</c>), nunca los bytes.
/// </summary>
/// <remarks>
/// Uso típico en un POST:
/// <code>
/// var error = _imagenes.Validar(datos.imagen);
/// if (error is not null) ModelState.AddModelError(nameof(datos.imagen), error);
/// if (!ModelState.IsValid) return View(datos);
/// string? ruta = datos.imagen is { Length: > 0 }
///     ? await _imagenes.GuardarAsync(datos.imagen, CarpetaImagen.Hospedajes, ct)
///     : null;
/// try { /* INSERT con imagen_ruta = ruta */ }
/// catch { _imagenes.Eliminar(ruta); throw; }   // si falla la BD, no dejar archivos huérfanos
/// </code>
/// </remarks>
public interface IAlmacenImagenes
{
    /// <summary>
    /// Revisa tamaño (máx. 2 MB) y tipo REAL del archivo (JPG, PNG o WEBP por sus primeros bytes, no por la extensión).
    /// Devuelve <c>null</c> si es válido o si no se subió nada (la imagen es opcional); si no, el mensaje de error.
    /// </summary>
    string? Validar(IFormFile? archivo);

    /// <summary>
    /// Guarda el archivo como <c>wwwroot/uploads/&lt;carpeta&gt;/&lt;guid&gt;.&lt;ext&gt;</c> y devuelve su ruta pública
    /// (<c>/uploads/&lt;carpeta&gt;/&lt;guid&gt;.&lt;ext&gt;</c>). El nombre lo genera el servidor; el nombre original
    /// del usuario nunca se usa. Lanza <see cref="InvalidOperationException"/> si el archivo no pasa <see cref="Validar"/>.
    /// </summary>
    Task<string> GuardarAsync(IFormFile archivo, CarpetaImagen carpeta, CancellationToken cancellationToken = default);

    /// <summary>
    /// Borra el archivo de una ruta pública guardada antes (al reemplazar una imagen o si falló el INSERT).
    /// Ignora valores vacíos o rutas fuera de <c>/uploads/</c>; nunca lanza excepción.
    /// </summary>
    void Eliminar(string? rutaPublica);
}

/// <summary>Ayudante para las vistas: muestra una imagen genérica cuando <c>imagen_ruta</c> es NULL.</summary>
public static class Imagenes
{
    /// <summary>Imagen que se muestra cuando un registro no tiene foto.</summary>
    public const string Generica = "/assets/img/sin-imagen.svg";

    /// <summary>Uso en Razor: <c>&lt;img src="@Imagenes.UrlOGenerica(h.imagen_ruta)" alt="..."&gt;</c></summary>
    public static string UrlOGenerica(string? rutaPublica) =>
        string.IsNullOrWhiteSpace(rutaPublica) ? Generica : rutaPublica;
}
