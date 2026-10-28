using System.Diagnostics;
using System.Security.Claims;
using FlyVibeMex.Data;
using FlyVibeMex.Models;
using FlyVibeMex.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FlyVibeMex.Controllers;

/// <summary>
/// Controlador principal de FlyVibe Mex: Inicio, secciones públicas (Vuelos, Hoteles, Vehículos, Tours, Paquetes),
/// inicio / cierre de sesión y diagnóstico de la BD.
/// Desde el 26-10-2026 cada sección es una <b>página completa</b> con <c>Views/Shared/_Layout.cshtml</c> (antes era una
/// "SPA casera" con <c>cargarPagina()</c>, sin URL propia ni botón Atrás).
/// </summary>
/// <remarks>
/// Ruta base: <c>/Home</c> (ruta por defecto <c>{controller=Home}/{action=Index}/{id?}</c> en Program.cs).
/// </remarks>
public class HomeController : Controller
{
    /// <summary>
    /// GET <c>/</c> y <c>/Home/Index</c>. Página de Inicio (<c>Views/Home/Index.cshtml</c>).
    /// </summary>
    public IActionResult Index()
    {
        return View();
    }

    /// <summary>
    /// GET <c>/Home/Default</c>. Ruta antigua de Inicio: redirige a <c>/</c> (por si quedó algún enlace viejo).
    /// </summary>
    public IActionResult Default()
    {
        return RedirectToAction(nameof(Index));
    }

    public IActionResult AccesoDenegado()
    {
        return View();
    }

    /// <summary>
    /// GET <c>/Home/Hoteles</c>. Devuelve la <b>página</b> <c>Hoteles.cshtml</c>
    /// (módulo Explorar y reservar › Hospedaje). Buscador + tarjetas con datos de la BD.
    /// </summary>
    public Task<IActionResult> Hoteles(BusquedaCatalogoViewModel busqueda,
        [FromServices] ICatalogoService catalogo, CancellationToken cancellationToken)
        => SeccionCatalogoAsync(TipoCatalogo.Hospedaje, busqueda, catalogo, cancellationToken);

    /// <summary>
    /// GET <c>/Home/Paquetes</c>. Devuelve la <b>página</b> <c>Paquetes.cshtml</c>
    /// (módulo Explorar y reservar › Paquetes). Buscador + tarjetas con datos de la BD.
    /// </summary>
    public Task<IActionResult> Paquetes(BusquedaCatalogoViewModel busqueda,
        [FromServices] ICatalogoService catalogo, CancellationToken cancellationToken)
        => SeccionCatalogoAsync(TipoCatalogo.Paquete, busqueda, catalogo, cancellationToken);

    /// <summary>
    /// GET <c>/Home/Tours</c>. Devuelve la <b>página</b> <c>Tours.cshtml</c>.
    /// Buscador + tarjetas con datos de la BD.
    /// </summary>

    public Task<IActionResult> Tours(BusquedaCatalogoViewModel busqueda,
        [FromServices] ICatalogoService catalogo, CancellationToken cancellationToken)
        => SeccionCatalogoAsync(TipoCatalogo.Evento, busqueda, catalogo, cancellationToken);

    /// <summary>
    /// GET <c>/Home/Viajes</c>. Devuelve la <b>página</b> <c>Viajes.cshtml</c>
    /// (módulo Explorar y reservar › Vuelos). Buscador + tarjetas con datos de la BD.
    /// </summary>
    public Task<IActionResult> Viajes(BusquedaCatalogoViewModel busqueda,
        [FromServices] ICatalogoService catalogo, CancellationToken cancellationToken)
        => SeccionCatalogoAsync(TipoCatalogo.Vuelo, busqueda, catalogo, cancellationToken);

    /// <summary>
    /// GET <c>/Home/sesion</c>. Ruta antigua del login (fragmento de la SPA casera): redirige a la página completa
    /// <c>/Home/IniciarSesion</c>. La vista <c>sesion.cshtml</c> ya no se usa.
    /// </summary>
    public IActionResult sesion()
    {
        return RedirectToAction(nameof(IniciarSesion));
    }

    // ============================== INICIAR / CERRAR SESIÓN ==============================

    /// <summary>
    /// GET <c>/Home/IniciarSesion</c>. Versión de <b>página completa</b> del inicio de sesión
    /// (<c>Views/Home/IniciarSesion.cshtml</c>). Se usa cuando el login falla (para mostrar el error)
    /// y es la <c>LoginPath</c> de la cookie: a futuro, una página con [Authorize] manda aquí con <c>?ReturnUrl=</c>.
    /// </summary>
    [HttpGet]
    public IActionResult IniciarSesion(string? returnUrl)
    {
        return View(new LoginViewModel { returnUrl = returnUrl });
    }

    /// <summary>
    /// POST <c>/Home/IniciarSesion</c>. Lo envía el botón "Iniciar sesión" de <c>sesion.cshtml</c>.
    /// Valida con <see cref="IAuthService"/> (consulta parametrizada + hash) y crea la cookie de sesión.
    /// </summary>
    /// <remarks>
    /// El servicio se pide con <c>RequestServices</c> (no en el constructor) por la misma razón que <c>TestDb</c>:
    /// el resto de este controlador debe funcionar aunque el equipo aún no configure SQL Server.
    /// </remarks>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IniciarSesion(
        [Bind("correo,password,recordarme,returnUrl")] LoginViewModel datos,
        [FromServices] ConfiguracionBaseDatos configuracionBd,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(datos);
        }

        if (!configuracionBd.EstaConfigurada)
        {
            ModelState.AddModelError(string.Empty,
                "El inicio de sesión estará disponible cuando se conecte la base de datos.");
            return View(datos);
        }

        var auth = HttpContext.RequestServices.GetRequiredService<IAuthService>();
        var resultado = await auth.ValidarCredencialesAsync(datos.correo, datos.password, cancellationToken);

        if (!resultado.Exitoso)
        {
            // Mensaje genérico: no dice si el correo existe (evita la enumeración de usuarios).
            ModelState.AddModelError(string.Empty, resultado.Mensaje);
            return View(datos);
        }

        var usuario = resultado.Usuario!;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, usuario.id.ToString()),
            new(ClaimTypes.Name, usuario.nombre),
            new(ClaimTypes.Email, usuario.correo),
            new(ClaimTypes.Role, usuario.rol),
            new("id_rol", usuario.id_rol.ToString())
            
        };

        var identidad = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identidad),
            new AuthenticationProperties { IsPersistent = datos.recordarme });

        // Solo se regresa a direcciones de este mismo sitio (evita "open redirect").
        if (!string.IsNullOrEmpty(datos.returnUrl) && Url.IsLocalUrl(datos.returnUrl))
        {
            return LocalRedirect(datos.returnUrl);
        }
        if (usuario.rol is Roles.Agente or Roles.Soporte or Roles.Administrador)
        {
            return RedirectToAction("Index", "Admin");
        }

        return RedirectToAction("Index", "Miusuario");
    }

    /// <summary>
    /// POST <c>/Home/CerrarSesion</c>. Borra la cookie de sesión. Es POST (con token) para que un enlace
    /// o una imagen de otro sitio no puedan cerrar la sesión del usuario.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CerrarSesion()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// GET <c>/Home/card</c>. Devuelve la <b>página</b> <c>card.cshtml</c>: maqueta de prueba con 12 tarjetas
    /// (hoteles, paquetes, tours y vuelos) que sirve de modelo para los resultados de búsqueda.
    /// </summary>
    // NOTA: ya no está en el menú (26-10-2026); solo se abre escribiendo /Home/card. Borrar antes de la entrega.
    public IActionResult card()
    {
        return View();
    }

    public Task<IActionResult> vehiculos(BusquedaCatalogoViewModel busqueda,
        [FromServices] ICatalogoService catalogo, CancellationToken cancellationToken)
        => SeccionCatalogoAsync(TipoCatalogo.Vehiculo, busqueda, catalogo, cancellationToken);

    /// <summary>
    /// Lógica común de las 5 secciones del catálogo: valida la búsqueda según el tipo y llena destinos y tarjetas.
    /// </summary>
    /// <remarks>
    /// <see cref="ICatalogoService"/> se pide con [FromServices] en cada acción (no en el constructor) para que Inicio,
    /// el login y TestDb sigan abriendo aunque un integrante aún no tenga SQL Server configurado.
    /// Si la búsqueda tiene errores, se muestran junto al campo y la sección lista lo disponible sin filtrar por fechas.
    /// </remarks>
    private async Task<IActionResult> SeccionCatalogoAsync(string tipo, BusquedaCatalogoViewModel busqueda,
        ICatalogoService catalogo, CancellationToken cancellationToken)
    {
        busqueda.tipo = tipo;
        foreach (var (campo, mensaje) in busqueda.Validar(DateOnly.FromDateTime(DateTime.Today)))
            ModelState.AddModelError(campo, mensaje);

        await catalogo.LlenarSeccionAsync(busqueda, aplicarFechas: ModelState.IsValid, cancellationToken);
        return View(busqueda);
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

            // Verificación del esquema completo: lee 1 fila de cada tabla con TODAS sus columnas.
            // Si un nombre o tipo de columna no coincide con el modelo, aquí aparece la tabla y el motivo.
            var tablas = await VerificarTablasAsync(db, cancellationToken);
            var tablasConError = tablas.Count(t => !t.ok);

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
                esquemaCompleto = tablasConError == 0,
                tablas,
                mensaje = tablasConError == 0
                    ? "Conexión correcta con Entity Framework Core y las 26 tablas coinciden con los modelos."
                    : $"Conexión correcta, pero {tablasConError} tabla(s) no coinciden con los modelos. " +
                      "Ejecuta sql/01_esquema.sql (base de prueba) o sql/03_alinear_nombres_legado.sql (base creada con un script anterior)."
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

    /// <summary>Resultado de la verificación de una tabla en <see cref="TestDb"/>.</summary>
    private sealed record VerificacionTabla(string tabla, bool ok, int? filas, string? error);

    /// <summary>
    /// Lee una fila (todas las columnas) y cuenta las filas de cada tabla mapeada en <see cref="FlyVibeMexDbContext"/>.
    /// Solo se usa en Development desde <see cref="TestDb"/>; no devuelve contenido de las filas, solo el conteo
    /// y, si falla, el mensaje de SQL Server/EF (p. ej. <c>Invalid column name 'tipo_proveedor'</c>).
    /// </summary>
    private static async Task<List<VerificacionTabla>> VerificarTablasAsync(FlyVibeMexDbContext db, CancellationToken ct)
    {
        var pruebas = new (string Tabla, Func<Task<int>> Prueba)[]
        {
            ("roles_usuarios", Probar(db.RolesUsuarios)),
            ("usuarios", Probar(db.Usuarios)),
            ("ubicaciones", Probar(db.Ubicaciones)),
            ("proveedores", Probar(db.Proveedores)),
            ("servicios", Probar(db.Servicios)),
            ("vuelos", Probar(db.Vuelos)),
            ("hospedajes", Probar(db.Hospedajes)),
            ("vehiculos", Probar(db.Vehiculos)),
            ("paquete", Probar(db.Paquetes)),
            ("paquete_servicio", Probar(db.PaquetesServicios)),
            ("disponibilidad_servicio", Probar(db.DisponibilidadServicios)),
            ("promociones", Probar(db.Promociones)),
            ("promocion_servicio", Probar(db.PromocionesServicios)),
            ("estados_reservacion", Probar(db.EstadosReservacion)),
            ("reservaciones", Probar(db.Reservaciones)),
            ("detalle_reservacion", Probar(db.DetallesReservacion)),
            ("historial_estado_reservacion", Probar(db.HistorialEstadosReservacion)),
            ("metodos_pago", Probar(db.MetodosPago)),
            ("instituciones_financieras", Probar(db.InstitucionesFinancieras)),
            ("tarjetas_usuarios", Probar(db.TarjetasUsuarios)),
            ("estados_pago", Probar(db.EstadosPago)),
            ("pagos", Probar(db.Pagos)),
            ("historial_estado_pago", Probar(db.HistorialEstadosPago)),
            ("reembolsos", Probar(db.Reembolsos)),
            ("historial_estado_reembolso", Probar(db.HistorialEstadosReembolso)),
            ("solicitudes_soporte", Probar(db.SolicitudesSoporte)),
        };

        var resultado = new List<VerificacionTabla>(pruebas.Length);
        foreach (var (tabla, probar) in pruebas)
        {
            try
            {
                resultado.Add(new VerificacionTabla(tabla, true, await probar(), null));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Exception causa = ex.InnerException is SqlException interna ? interna : ex;
                resultado.Add(new VerificacionTabla(tabla, false, null, $"{causa.GetType().Name}: {causa.Message}"));
            }
        }
        return resultado;

        // Materializa una fila (así EF lee y convierte cada columna) y después cuenta.
        Func<Task<int>> Probar<T>(DbSet<T> conjunto) where T : class => async () =>
        {
            await conjunto.AsNoTracking().Take(1).ToListAsync(ct);
            return await conjunto.CountAsync(ct);
        };
    }

    /// <summary>Traduce los códigos de error de SQL Server más comunes a una sugerencia (sin datos sensibles).</summary>
    private static string SugerenciaPorCodigoSql(int numero) => numero switch
    {
        4060 => "La base de datos no existe o tu usuario no tiene acceso. Ejecuta sql/00_db_prueba_conexion.sql en SSMS.",
        18456 => "SQL Server rechazó el inicio de sesión. Revisa usuario/contraseña o usa Trusted_Connection=True.",
        208 => "Faltan tablas en la base de datos. Ejecuta en SSMS sql/00_db_prueba_conexion.sql y después sql/01_esquema.sql.",
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
