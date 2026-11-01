namespace FlyVibeMex.Services;

/// <summary>
/// Logo del sitio (T7.3). El archivo lo pone el equipo en <c>wwwroot/assets/img/logo.png</c>; mientras no exista,
/// la barra muestra solo el texto "FlyVibeMex" (sin ícono roto) y no se declara favicon.
/// </summary>
/// <remarks>
/// Lo usan <c>Views/Shared/_Marca.cshtml</c> y <c>Views/Shared/_Favicon.cshtml</c>. Revisar si el archivo existe es una
/// consulta al disco muy barata (el proveedor de archivos de wwwroot), así que se hace en cada página sin caché:
/// en cuanto copies el logo, aparece sin reiniciar la aplicación.
/// </remarks>
public static class LogoSitio
{
    /// <summary>Ruta del logo dentro de wwwroot (sin "/" inicial).</summary>
    public const string Ruta = "assets/img/logo.png";

    public static bool Existe(IWebHostEnvironment entorno) => entorno.WebRootFileProvider.GetFileInfo(Ruta).Exists;
}
