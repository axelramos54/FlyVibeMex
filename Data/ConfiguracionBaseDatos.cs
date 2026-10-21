using Microsoft.Data.SqlClient;

namespace FlyVibeMex.Data;

/// <summary>
/// Selector de base de datos <b>Test</b> / <b>Real</b>. Lee la clave <c>DatabaseMode</c> y la cadena de conexión
/// correspondiente (<c>ConnectionStrings:FlyVibeMex_Test</c> o <c>ConnectionStrings:FlyVibeMex_Real</c>).
/// </summary>
/// <remarks>
/// Orden en que ASP.NET Core combina la configuración (el último gana):
/// <c>appsettings.json</c> → <c>appsettings.Development.json</c> → <b>user-secrets</b> (solo en Development) → variables de entorno.
/// Por eso en <c>appsettings.json</c> solo hay marcadores (<c>__SERVIDOR__</c>) y la cadena real de cada integrante
/// vive en su <c>dotnet user-secrets</c>, fuera de Git.
/// <para>SEGURIDAD: esta clase NUNCA debe serializarse a JSON ni enviarse a una vista; <see cref="CadenaConexion"/>
/// puede contener usuario y contraseña de SQL Server.</para>
/// </remarks>
public sealed class ConfiguracionBaseDatos
{
    /// <summary>Texto que marca una cadena de conexión de ejemplo (sin configurar) en appsettings.json.</summary>
    public const string MarcadorSinConfigurar = "__SERVIDOR__";

    public const string ModoTest = "Test";
    public const string ModoReal = "Real";

    /// <summary>Modo activo normalizado: <c>"Test"</c> o <c>"Real"</c>.</summary>
    public string Modo { get; }

    /// <summary>Nombre de la entrada en <c>ConnectionStrings</c> que se está usando.</summary>
    public string NombreCadena { get; }

    /// <summary>Cadena de conexión completa. Uso exclusivo de <c>Program.cs</c> al registrar el DbContext.</summary>
    internal string? CadenaConexion { get; }

    /// <summary><c>true</c> si la cadena existe y no es el marcador de ejemplo de appsettings.json.</summary>
    public bool EstaConfigurada =>
        !string.IsNullOrWhiteSpace(CadenaConexion) &&
        !CadenaConexion.Contains(MarcadorSinConfigurar, StringComparison.Ordinal);

    private ConfiguracionBaseDatos(string modo, string nombreCadena, string? cadenaConexion)
    {
        Modo = modo;
        NombreCadena = nombreCadena;
        CadenaConexion = cadenaConexion;
    }

    /// <summary>
    /// Construye la configuración a partir de <see cref="IConfiguration"/>.
    /// Lanza <see cref="InvalidOperationException"/> si <c>DatabaseMode</c> tiene un valor distinto de Test/Real
    /// (es un error de configuración y conviene que la app falle al arrancar, no a mitad de una petición).
    /// </summary>
    public static ConfiguracionBaseDatos Desde(IConfiguration configuracion)
    {
        var modoLeido = (configuracion["DatabaseMode"] ?? ModoTest).Trim();

        string modo;
        if (modoLeido.Equals(ModoTest, StringComparison.OrdinalIgnoreCase))
        {
            modo = ModoTest;
        }
        else if (modoLeido.Equals(ModoReal, StringComparison.OrdinalIgnoreCase))
        {
            modo = ModoReal;
        }
        else
        {
            throw new InvalidOperationException(
                $"Valor de \"DatabaseMode\" no válido: \"{modoLeido}\". Usa \"Test\" o \"Real\".");
        }

        var nombreCadena = modo == ModoReal ? "FlyVibeMex_Real" : "FlyVibeMex_Test";
        return new ConfiguracionBaseDatos(modo, nombreCadena, configuracion.GetConnectionString(nombreCadena));
    }

    /// <summary>
    /// Nombre de la base de datos (Initial Catalog) de la cadena activa, para diagnósticos.
    /// No expone servidor, usuario ni contraseña.
    /// </summary>
    public string? NombreBaseDeDatos()
    {
        if (!EstaConfigurada)
        {
            return null;
        }

        try
        {
            return new SqlConnectionStringBuilder(CadenaConexion!).InitialCatalog;
        }
        catch (ArgumentException)
        {
            // Cadena mal formada: no se repite su contenido para no filtrar credenciales.
            return null;
        }
    }

    /// <summary>Mensaje de ayuda (sin datos sensibles) cuando falta la cadena de conexión.</summary>
    public string MensajeSinConfigurar() =>
        $"La cadena de conexión \"{NombreCadena}\" no está configurada en este equipo. " +
        $"Regístrala con: dotnet user-secrets set \"ConnectionStrings:{NombreCadena}\" \"<tu cadena>\" " +
        "(ver docs/05_CONEXION_EFCORE_Y_SEGURIDAD.md).";
}
