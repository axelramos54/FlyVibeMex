using FlyVibeMex.Data;
using FlyVibeMex.Models;
using FlyVibeMex.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews(opciones =>
{
    // T6.1: mensajes de validación en español (model binding y DataAnnotations sin ErrorMessage).
    MensajesValidacionEspanol.Configurar(opciones);
    // T6.3: red de seguridad: un error de BD que se escape en un POST regresa al formulario con un aviso.
    opciones.Filters.Add<ErroresBaseDatosFilter>();
});

// Caché en memoria: el contenido del sitio que edita el SA (ContenidoSitioService) se lee de la BD cada 10 minutos.
builder.Services.AddMemoryCache();

// =============================================================================================
// Base de datos (Entity Framework Core + SQL Server)
// ---------------------------------------------------------------------------------------------
// "DatabaseMode" decide qué cadena se usa:  "Test" -> ConnectionStrings:FlyVibeMex_Test
//                                           "Real" -> ConnectionStrings:FlyVibeMex_Real
// Las cadenas reales NO están en appsettings.json (solo marcadores): cada integrante las registra
// con `dotnet user-secrets` y en el servidor se pasan por variables de entorno.
// Ver docs/05_CONEXION_EFCORE_Y_SEGURIDAD.md
// =============================================================================================
var configuracionBd = ConfiguracionBaseDatos.Desde(builder.Configuration);
builder.Services.AddSingleton(configuracionBd);

builder.Services.AddDbContext<FlyVibeMexDbContext>(opciones =>
{
    // Se valida aquí (al crear el DbContext) y no al arrancar, para que quien solo trabaja en el
    // frontend pueda ejecutar la app aunque todavía no tenga SQL Server configurado.
    if (!configuracionBd.EstaConfigurada)
    {
        throw new InvalidOperationException(configuracionBd.MensajeSinConfigurar());
    }

    opciones.UseSqlServer(configuracionBd.CadenaConexion!, sql =>
    {
        // Resiliencia: reintenta automáticamente ante fallos transitorios de red/SQL Server.
        // IMPORTANTE: con reintentos activos, las transacciones manuales (BeginTransaction) deben
        // envolverse en `db.Database.CreateExecutionStrategy().ExecuteAsync(...)`.
        sql.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorNumbersToAdd: null);
        sql.CommandTimeout(30);
    });

    // NUNCA registrar en los logs los valores de los parámetros SQL (correos, hashes, datos personales).
    opciones.EnableSensitiveDataLogging(false);

    if (builder.Environment.IsDevelopment())
    {
        // Mensajes de error de EF más claros en desarrollo (no incluye valores de los parámetros).
        opciones.EnableDetailedErrors();
    }
});

// =============================================================================================
// Servicios de la aplicación
// =============================================================================================
// Hash de contraseñas de ASP.NET Core Identity (PBKDF2-HMAC-SHA512). Es seguro como singleton.
builder.Services.AddSingleton<IPasswordHasher<UsuariosGlobales>, PasswordHasher<UsuariosGlobales>>();
builder.Services.AddScoped<IAuthService, AuthService>();

// T8.1: cualquier servicio se puede pedir como Lazy<T> en un constructor; se crea al leer .Value (no antes).
// Lo usan HomeController y CrearusuarioController (Lazy<IAuthService>) y DiagnosticoBdService (Lazy<DbContext>)
// para que Inicio, Iniciar sesión, Crear cuenta y TestDb abran aunque falte la cadena de conexión ("modo sin BD").
builder.Services.AddTransient(typeof(Lazy<>), typeof(ServicioDiferido<>));

// Diagnóstico de la conexión para GET /Home/TestDb (solo Development).
builder.Services.AddScoped<IDiagnosticoBdService, DiagnosticoBdService>();

// Imágenes subidas: se guardan en wwwroot/uploads/ y en la BD solo va la ruta (imagen_ruta).
builder.Services.AddSingleton<IAlmacenImagenes, AlmacenImagenes>();

// Catálogo público: búsqueda por sección, detalle y cotización.
builder.Services.AddScoped<ICatalogoService, CatalogoService>();

// Compra: resumen, pago simulado en una transacción y confirmación.
builder.Services.AddScoped<IReservacionService, ReservacionService>();

// Área del cliente: perfil, tarjetas, reservaciones, cancelaciones y tickets de soporte.
builder.Services.AddScoped<IMiCuentaService, MiCuentaService>();
builder.Services.AddScoped<IMisViajesService, MisViajesService>();
builder.Services.AddScoped<ISoporteClienteService, SoporteClienteService>();

// Cancelaciones y reembolsos (una sola regla para el cliente y el panel) + panel de atención (T4 fase C).
builder.Services.AddScoped<ICancelacionesService, CancelacionesService>();
builder.Services.AddScoped<IReservacionesPanelService, ReservacionesPanelService>();
builder.Services.AddScoped<ISoportePanelService, SoportePanelService>();

// Panel SA (T4 fase D): usuarios y roles, catálogos base, bitácora y contenido del sitio.
builder.Services.AddScoped<ISAUsuariosService, SAUsuariosService>();
builder.Services.AddScoped<ISACatalogosService, SACatalogosService>();
builder.Services.AddScoped<ISABitacoraService, SABitacoraService>();
builder.Services.AddScoped<IContenidoSitioService, ContenidoSitioService>();

// Panel: Gestor de ventas (proveedores, servicios con su subtipo y disponibilidad). Escribe en la bitácora.
builder.Services.AddScoped<IGestorCatalogoService, GestorCatalogoService>();
builder.Services.AddScoped<IGestorPaquetesService, GestorPaquetesService>();
builder.Services.AddScoped<IGestorPromocionesService, GestorPromocionesService>();

// Sesión de usuario con cookie (la crea HomeController.IniciarSesion y la borra HomeController.CerrarSesion).
// La cookie solo guarda id, nombre, correo, id_rol y el nombre del rol (ClaimTypes.Role, para [Authorize(Roles = ...)]);
// nunca la contraseña ni el hash.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(opciones =>
    {
        opciones.LoginPath = "/Home/IniciarSesion";
        opciones.AccessDeniedPath = "/Estado/403";   // T6.2: página 403 propia (según la zona: sitio o panel)
        opciones.Cookie.Name = "FlyVibeMex.Sesion";
        opciones.Cookie.HttpOnly = true;                              // JavaScript no puede leerla
        // En producción la cookie solo viaja por HTTPS; en desarrollo también funciona con el perfil http.
        opciones.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        opciones.Cookie.SameSite = SameSiteMode.Lax;
        opciones.ExpireTimeSpan = TimeSpan.FromHours(8);
        opciones.SlidingExpiration = true;
        // En cada petición: la cuenta debe seguir activa y con el mismo rol (si el SA la cambió, se cierra la sesión).
        // T8.1: clase de eventos con inyección por constructor (Services/ValidacionSesion.cs), una por petición.
        opciones.EventsType = typeof(ValidacionSesion);
    });
builder.Services.AddScoped<ValidacionSesion>();

var app = builder.Build();

app.Logger.LogInformation(
    "Base de datos: modo {Modo} (cadena \"{Cadena}\", {Estado}).",
    configuracionBd.Modo,
    configuracionBd.NombreCadena,
    configuracionBd.EstaConfigurada ? "configurada" : "SIN CONFIGURAR: ver docs/05_CONEXION_EFCORE_Y_SEGURIDAD.md");

// Configure the HTTP request pipeline.
// T6.2 · Páginas de error propias (EstadoController): 400, 403, 404 y 500 sin detalles técnicos.
// En Development se sigue viendo la página de detalle de .NET para las excepciones.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Estado/500");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

// Respuestas de error SIN contenido (404 de una dirección inexistente, 400 del antiforgery, NotFound(), Forbid()…):
// se vuelve a ejecutar la petición en /Estado/{código}, que muestra la página propia con el mismo código HTTP.
app.UseStatusCodePagesWithReExecute("/Estado/{0}");

// Imágenes subidas mientras la app corre. MapStaticAssets() solo sirve los archivos que existían al compilar,
// así que /uploads se sirve aparte con UseStaticFiles, limitado a esa carpeta.
var carpetaUploads = AlmacenImagenes.ObtenerRaizUploads(app.Environment);
Directory.CreateDirectory(carpetaUploads);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(carpetaUploads),
    RequestPath = "/uploads",
    // El navegador no debe "adivinar" el tipo: un archivo .jpg siempre se trata como imagen.
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.XContentTypeOptions = "nosniff"
});
app.UseRouting();

app.UseAuthentication();   // debe ir antes de UseAuthorization
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
