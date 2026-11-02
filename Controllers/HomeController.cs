using System.Security.Claims;
using FlyVibeMex.Data;
using FlyVibeMex.Models;
using FlyVibeMex.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

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
    private readonly Lazy<IAuthService> _auth;

    /// <param name="auth">Diferido (Services/ServicioDiferido.cs): se crea solo al iniciar sesión.</param>
    public HomeController(Lazy<IAuthService> auth)
    {
        _auth = auth;
    }

    /// <summary>
    /// GET <c>/</c> y <c>/Home/Index</c>. Página de Inicio (<c>Views/Home/Index.cshtml</c>).
    /// </summary>
    public IActionResult Index()
    {
        return View();
    }

    // Enlace anterior a la página 403: ahora la muestra EstadoController (T6.2).
    public IActionResult AccesoDenegado(string? returnUrl) =>
        RedirectToAction("Index", "Estado", new { codigo = 403, returnUrl });

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
    /// POST <c>/Home/IniciarSesion</c>. Lo envía el formulario de <c>_FormSesion.cshtml</c> (página IniciarSesion).
    /// Valida con <see cref="IAuthService"/> (consulta parametrizada + hash) y crea la cookie de sesión.
    /// </summary>
    /// <remarks>
    /// <see cref="IAuthService"/> llega por constructor como <c>Lazy&lt;IAuthService&gt;</c>: el servicio (y la BD) solo
    /// se crean aquí, al validar, así que Inicio y el formulario abren aunque SQL Server no esté configurado.
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

        var resultado = await _auth.Value.ValidarCredencialesAsync(datos.correo, datos.password, cancellationToken);

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
        if (Roles.EsInterno(usuario.rol))
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
    /// GET <c>/Home/Vehiculos</c>. Devuelve la <b>página</b> <c>Vehiculos.cshtml</c>.
    /// Buscador + tarjetas con datos de la BD. (La URL vieja /Home/vehiculos sigue funcionando: las rutas no
    /// distinguen mayúsculas.)
    /// </summary>
    public Task<IActionResult> Vehiculos(BusquedaCatalogoViewModel busqueda,
        [FromServices] ICatalogoService catalogo, CancellationToken cancellationToken)
        => SeccionCatalogoAsync(TipoCatalogo.Vehiculo, busqueda, catalogo, cancellationToken);

    /// <summary>
    /// Lógica común de las 5 secciones del catálogo: valida la búsqueda según el tipo y llena destinos y tarjetas.
    /// </summary>
    /// <remarks>
    /// <see cref="ICatalogoService"/> se pide con [FromServices] en cada acción (inyección en la acción, no en el
    /// constructor) para que Inicio, el login y TestDb sigan abriendo aunque un integrante aún no tenga SQL Server.
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
    /// La lógica vive en <see cref="IDiagnosticoBdService"/> (T8.1): modo activo (Test / Real), conexión, consulta de
    /// prueba y verificación de las 31 tablas. Nunca devuelve servidor, usuario, contraseña, hashes ni datos personales.
    /// </summary>
    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> TestDb(
        [FromServices] IWebHostEnvironment entorno,
        [FromServices] IDiagnosticoBdService diagnostico,
        CancellationToken cancellationToken)
    {
        if (!entorno.IsDevelopment())
        {
            return NotFound();
        }

        var resultado = await diagnostico.DiagnosticarAsync(cancellationToken);
        return StatusCode(resultado.CodigoHttp, resultado.Cuerpo);
    }

    /// <summary>
    /// GET <c>/Home/Error</c>: enlace anterior a la página de error. Desde T6.2 el manejador de excepciones es
    /// <c>/Estado/500</c> (EstadoController); esta acción solo redirige ahí.
    /// </summary>
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => RedirectToAction("Index", "Estado", new { codigo = 500 });
}
