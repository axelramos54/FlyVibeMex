using System.Diagnostics;
using FlyVibeMex.Data;
using FlyVibeMex.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FlyVibeMex.Controllers;

/// <summary>
/// Controlador principal de FlyVibe Mex.
/// Cumple dos funciones:
/// 1) Sirve el <b>shell</b> de la aplicación (<c>Index</c>):  el documento HTML completo con la barra de
///    navegación y el contenedor <c>main#contenido_main</c>.
/// 2) Sirve las <b>vistas parciales</b> (todas las vistas con <c>Layout = null</c>) que <c>wwwroot/assets/js/script.js</c>
///    solicita con <c>fetch</c> e inyecta con <c>innerHTML</c> dentro del shell (navegación tipo SPA casera).
/// </summary>
/// <remarks>
/// Ruta base: <c>/Home</c> (ruta por defecto <c>{controller=Home}/{action=Index}/{id?}</c> en Program.cs).
/// </remarks>
public class HomeController : Controller
{
    /// <summary>
    /// GET <c>/Home/Index</c>. Devuelve la <b>vista completa</b> <c>Index.cshtml</c> (shell de la aplicación).
    /// </summary>
    public IActionResult Index()
    {
        return View();
    }

    /// <summary>
    /// GET <c>/Home/Default</c>. Devuelve la <b>vista completa</b> <c>Default.cshtml</c> (pantalla de Inicio).
    /// </summary>
    public IActionResult Default()
    {
        return View();
    }

    /// <summary>
    /// GET <c>/Home/Hoteles</c>. Devuelve la <b>vista parcial</b> <c>Hoteles.cshtml</c>
    /// (módulo Explorar y reservar › Hospedaje). Estado: buscador sin funcionalidad y 3 tarjetas estáticas.
    /// </summary>
    public IActionResult Hoteles()
    {
        return View();
    }

    /// <summary>
    /// GET <c>/Home/Paquetes</c>. Devuelve la <b>vista parcial</b> <c>Paquetes.cshtml</c>
    /// (módulo Explorar y reservar › Paquetes). Estado: buscador sin funcionalidad y 3 tarjetas estáticas.
    /// </summary>
    public IActionResult Paquetes()
    {
        return View();
    }

    /// <summary>
    /// GET <c>/Home/Tours</c>. Devuelve la <b>vista parcial</b> <c>Tours.cshtml</c>.
    /// Estado: buscador sin funcionalidad y 3 tarjetas estáticas.
    /// </summary>

    public IActionResult Tours()
    {
        return View();
    }

    /// <summary>
    /// GET <c>/Home/Viajes</c>. Devuelve la <b>vista parcial</b> <c>Viajes.cshtml</c>
    /// (módulo Explorar y reservar › Vuelos). Estado: buscador sin funcionalidad y 3 tarjetas estáticas.
    /// </summary>
    public IActionResult Viajes()
    {
        return View();
    }

    /// <summary>
    /// GET <c>/Home/sesion</c>. Devuelve la <b>vista parcial</b> <c>sesion.cshtml</c> (módulo Cuenta › Iniciar sesión).
    /// Estado: formulario visual sin envío; no existe acción POST ni autenticación configurada.
    /// </summary>

    public IActionResult sesion()
    {
        return View();
    }

    /// <summary>
    /// GET <c>/Home/card</c>. Devuelve la <b>vista parcial</b> <c>card.cshtml</c>: maqueta de prueba con 12 tarjetas
    /// (hoteles, paquetes, tours y vuelos) que sirve de modelo para los resultados de búsqueda.
    /// </summary>
    // NOTA: quitar el enlace "VER TARJETA test" del menú de Index.cshtml antes de la entrega.
    public IActionResult card()
    {
        return View();
    }



    /// <summary>
    /// GET <c>/Home/TestDb</c>. Diagnóstico de la conexión con SQL Server mediante EF Core.
    /// <b>Solo existe en Development</b>: en cualquier otro entorno responde 404.
    /// Devuelve un JSON con el modo activo (<c>Test</c> o <c>Real</c>), si hubo conexión y el resultado de una consulta
    /// LINQ de prueba (roles activos y conteo de usuarios). Nunca devuelve servidor, usuario, contraseña, hashes
    /// ni datos personales.
    /// </summary>
    /// <remarks>
    /// El DbContext se pide aquí con <c>RequestServices</c> (y no en el constructor) para que el resto de las páginas
    /// de este controlador sigan funcionando en equipos que aún no configuran SQL Server.
    /// </remarks>
    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> TestDb(
        [FromServices] IWebHostEnvironment entorno,
        [FromServices] ConfiguracionBaseDatos configuracionBd,
        [FromServices] ILogger<HomeController> logger,
        CancellationToken cancellationToken)
    {
        if (!entorno.IsDevelopment())
        {
            return NotFound();
        }

        var modo = configuracionBd.Modo;
        var cadena = configuracionBd.NombreCadena;
        var baseDeDatos = configuracionBd.NombreBaseDeDatos();

        if (!configuracionBd.EstaConfigurada)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                modo,
                cadena,
                configurada = false,
                conectado = false,
                mensaje = configuracionBd.MensajeSinConfigurar()
            });
        }

        var cronometro = Stopwatch.StartNew();
        try
        {
            var db = HttpContext.RequestServices.GetRequiredService<FlyVibeMexDbContext>();

            if (!await db.Database.CanConnectAsync(cancellationToken))
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    modo,
                    cadena,
                    baseDeDatos,
                    configurada = true,
                    conectado = false,
                    mensaje = $"No se pudo abrir la conexión. Revisa que SQL Server esté iniciado, el nombre del servidor " +
                              $"en tu user-secret y que exista la base \"{baseDeDatos}\" (sql/00_db_prueba_conexion.sql)."
                });
            }

            // Consulta LINQ de prueba (parametrizada, sin seguimiento de cambios, sin columnas sensibles).
            var rolesActivos = await db.RolesUsuarios
                .AsNoTracking()
                .Where(r => r.activo)
                .OrderBy(r => r.id)
                .Select(r => r.nombre)
                .ToListAsync(cancellationToken);

            var usuariosActivos = await db.Usuarios.AsNoTracking().CountAsync(u => u.activo, cancellationToken);
            var usuariosInactivos = await db.Usuarios.AsNoTracking().CountAsync(u => !u.activo, cancellationToken);

            cronometro.Stop();
            return Json(new
            {
                modo,
                cadena,
                baseDeDatos,
                configurada = true,
                conectado = true,
                tiempoMs = cronometro.ElapsedMilliseconds,
                consultaPrueba = new { rolesActivos, usuariosActivos, usuariosInactivos },
                mensaje = "Conexión correcta con Entity Framework Core."
            });
        }
        catch (Exception ex) when (ex is SqlException || ex.InnerException is SqlException)
        {
            var errorSql = ex as SqlException ?? (SqlException)ex.InnerException!;
            logger.LogError(ex, "TestDb: error de SQL Server (número {NumeroSql}).", errorSql.Number);

            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                modo,
                cadena,
                baseDeDatos,
                configurada = true,
                conectado = false,
                codigoSql = errorSql.Number,
                mensaje = SugerenciaPorCodigoSql(errorSql.Number)
            });
        }
    }

    /// <summary>Traduce los códigos de error de SQL Server más comunes a una sugerencia (sin datos sensibles).</summary>
    private static string SugerenciaPorCodigoSql(int numero) => numero switch
    {
        4060 => "La base de datos no existe o tu usuario no tiene acceso. Ejecuta sql/00_db_prueba_conexion.sql en SSMS.",
        18456 => "SQL Server rechazó el inicio de sesión. Revisa usuario/contraseña o usa Trusted_Connection=True.",
        208 => "Faltan tablas en la base de datos. Ejecuta sql/00_db_prueba_conexion.sql en SSMS.",
        2 or 53 or -1 or -2 => "No se encontró el servidor o tardó demasiado. Revisa el nombre (localhost, .\\SQLEXPRESS o (localdb)\\MSSQLLocalDB) y que el servicio de SQL Server esté iniciado.",
        _ => "Error de SQL Server. Revisa la consola de la aplicación para ver el detalle."
    };

    /// <summary>
    /// GET <c>/Home/Error</c>. Devuelve <c>Views/Shared/Error.cshtml</c> (documento completo con <c>_Layout</c>)
    /// con el identificador de la petición. Program.cs la usa como manejador de excepciones fuera de Development
    /// (<c>UseExceptionHandler("/Home/Error")</c>). No se guarda en caché.
    /// </summary>
    /// NOTA: falta la acción Privacy(). Existe Views/Home/Privacy.cshtml y _Layout.cshtml la enlaza,
    // pero /Home/Privacy responde 404 porque este controlador no la define.
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
