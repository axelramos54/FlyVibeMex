namespace FlyVibeMex.Services;

/// <summary>
/// Imágenes de identidad del sitio (T7.3), que el equipo deja en <c>wwwroot/assets/img/</c>:
/// <list type="bullet">
/// <item><b>logo.png</b> — logotipo de la barra superior. Mientras no exista, la barra muestra solo el texto
/// "FlyVibeMex" (sin ícono roto).</item>
/// <item><b>favicon.ico</b> — ícono de la pestaña del navegador. Mientras no exista, no se declara favicon.</item>
/// </list>
/// Son DOS archivos independientes: puede haber favicon sin logotipo y al revés.
/// </summary>
/// <remarks>
/// <para>Lo usan <c>Views/Shared/_Marca.cshtml</c> (logotipo) y <c>Views/Shared/_Favicon.cshtml</c> (ícono).
/// Revisar si un archivo existe es una consulta al disco muy barata (el proveedor de archivos de wwwroot),
/// así que se hace en cada página sin caché: en cuanto copies el archivo, aparece sin reiniciar la
/// aplicación.</para>
/// <para>CUIDADO AL PUBLICAR: <c>MapStaticAssets()</c> arma su manifiesto con los archivos que EXISTÍAN al
/// compilar. Un archivo que se deje en wwwroot directamente en el servidor, después de publicar, puede
/// responder 404 aunque esta clase diga que existe. El orden correcto es copiar la imagen, compilar y
/// después publicar.</para>
/// </remarks>
public static class LogoSitio
{
    /// <summary>Ruta del logotipo dentro de wwwroot (sin "/" inicial).</summary>
    public const string Ruta = "assets/img/logo.png";

    /// <summary>Ruta del ícono de la pestaña dentro de wwwroot (sin "/" inicial).</summary>
    public const string RutaFavicon = "assets/img/favicon.ico";

    /// <summary>¿Está el logotipo de la barra superior?</summary>
    public static bool Existe(IWebHostEnvironment entorno) => ExisteArchivo(entorno, Ruta);

    /// <summary>¿Está el ícono de la pestaña?</summary>
    public static bool ExisteFavicon(IWebHostEnvironment entorno) => ExisteArchivo(entorno, RutaFavicon);

    private static bool ExisteArchivo(IWebHostEnvironment entorno, string ruta) =>
        entorno.WebRootFileProvider.GetFileInfo(ruta).Exists;
}
