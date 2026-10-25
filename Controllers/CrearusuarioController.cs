using FlyVibeMex.Data;
using FlyVibeMex.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FlyVibeMex.Controllers;

/// <summary>
/// Registro de clientes: <c>/Crearusuario</c>. Las vistas están en <c>Views/Crearusuario/</c>.
/// Se llega desde el botón "Crear nueva cuenta" de <c>Views/Home/sesion.cshtml</c>.
/// </summary>
/// <remarks>
/// Reglas de seguridad:
/// - El formulario se recibe en <see cref="CrearUsuarioViewModel"/>: nadie puede mandar id_rol, activo o password_hash.
/// - La contraseña se convierte en hash con <see cref="IPasswordHasher{TUser}"/> (el mismo que usa AuthService para
///   validar el login) y nunca se guarda en texto plano (regla R10).
/// - El rol siempre es el de cliente; se busca por nombre en roles_usuarios ("Usuario" en el script del equipo,
///   "CLIENTE" en sql/00_db_prueba_conexion.sql), nunca se toma del formulario.
/// - El DbContext se pide con RequestServices (como HomeController.TestDb) para que la pantalla abra
///   aunque SQL Server todavía no esté configurado.
/// </remarks>
public class CrearusuarioController : Controller
{
    /// <summary>Nombres aceptados para el rol de cliente, en orden de preferencia.</summary>
    private static readonly string[] RolesCliente = { "Usuario", "CLIENTE", "Cliente" };

    private readonly IPasswordHasher<UsuariosGlobales> _passwordHasher;
    private readonly ConfiguracionBaseDatos _configuracionBd;
    private readonly ILogger<CrearusuarioController> _logger;

    public CrearusuarioController(
        IPasswordHasher<UsuariosGlobales> passwordHasher,
        ConfiguracionBaseDatos configuracionBd,
        ILogger<CrearusuarioController> logger)
    {
        _passwordHasher = passwordHasher;
        _configuracionBd = configuracionBd;
        _logger = logger;
    }

    // GET /Crearusuario
    [HttpGet]
    public IActionResult Index() => View(new CrearUsuarioViewModel());

    // POST /Crearusuario
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(
        [Bind("nombre,apellido_paterno,apellido_materno,correo,telefono,password,confirmar_password,acepto_terminos")]
        CrearUsuarioViewModel datos,
        CancellationToken cancellationToken)
    {
        // Normalización (igual que AuthService.NormalizarCorreo: el login busca el correo en minúsculas).
        var correo = (datos.correo ?? string.Empty).Trim().ToLowerInvariant();
        var telefono = (datos.telefono ?? string.Empty).Trim();

        if (!ModelState.IsValid)
        {
            return VistaFormulario(datos);
        }

        // Sin BD configurada: la pantalla se puede probar, pero no se guarda nada.
        if (!_configuracionBd.EstaConfigurada)
        {
            TempData["Aviso"] = "Modo prueba: tus datos son válidos, pero la cuenta se guardará cuando se conecte la base de datos.";
            TempData["NombreNuevo"] = datos.nombre.Trim();
            return RedirectToAction(nameof(Creado));
        }

        try
        {
            var db = HttpContext.RequestServices.GetRequiredService<FlyVibeMexDbContext>();

            // Correo y teléfono son UNIQUE en la tabla: se revisa antes para dar un mensaje claro.
            if (await db.Usuarios.AsNoTracking().AnyAsync(u => u.correo == correo, cancellationToken))
                ModelState.AddModelError(nameof(datos.correo), "Ya existe una cuenta con ese correo. Inicia sesión.");

            if (await db.Usuarios.AsNoTracking().AnyAsync(u => u.telefono == telefono, cancellationToken))
                ModelState.AddModelError(nameof(datos.telefono), "Ese teléfono ya está registrado en otra cuenta.");

            if (!ModelState.IsValid)
            {
                return VistaFormulario(datos);
            }

            var roles = await db.RolesUsuarios
                .AsNoTracking()
                // Comparaciones explícitas (no Contains de una lista) para que funcione en cualquier versión de SQL Server.
                .Where(r => r.activo && (r.nombre == "Usuario" || r.nombre == "CLIENTE" || r.nombre == "Cliente"))
                .Select(r => new { r.id, r.nombre })
                .ToListAsync(cancellationToken);

            var rol = RolesCliente
                .Select(nombre => roles.FirstOrDefault(r => r.nombre == nombre))
                .FirstOrDefault(r => r is not null);

            if (rol is null)
            {
                _logger.LogError("Registro: no existe un rol de cliente activo (Usuario/CLIENTE) en roles_usuarios.");
                ModelState.AddModelError(string.Empty, "No es posible crear cuentas en este momento. Intenta más tarde.");
                return VistaFormulario(datos);
            }

            var usuario = new UsuariosGlobales
            {
                id_rol = rol.id,
                nombre = datos.nombre.Trim(),
                apellido_paterno = datos.apellido_paterno.Trim(),
                apellido_materno = datos.apellido_materno.Trim(),
                correo = correo,
                telefono = telefono,
                password_hash = _passwordHasher.HashPassword(new UsuariosGlobales(), datos.password),
                activo = true,
                creado_en = DateTime.Now,
                imagen = null
            };

            db.Usuarios.Add(usuario);
            await db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Registro: se creó el usuario {IdUsuario}.", usuario.id);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DbUpdateException ex)
        {
            // Lo más común: otra persona registró el mismo correo/teléfono justo al mismo tiempo (UNIQUE).
            _logger.LogWarning(ex, "Registro: no se pudo guardar el usuario.");
            ModelState.AddModelError(string.Empty, "No se pudo crear la cuenta. Revisa que el correo y el teléfono no estén ya registrados.");
            return VistaFormulario(datos);
        }
        catch (Exception ex) when (ex is SqlException or RetryLimitExceededException or InvalidOperationException or TimeoutException)
        {
            // El detalle técnico solo queda en el log del servidor.
            _logger.LogError(ex, "Registro: error de base de datos.");
            ModelState.AddModelError(string.Empty, "No es posible crear cuentas en este momento. Intenta más tarde.");
            return VistaFormulario(datos);
        }

        TempData["Exito"] = "¡Tu cuenta se creó correctamente! Ya puedes iniciar sesión.";
        TempData["NombreNuevo"] = datos.nombre.Trim();
        return RedirectToAction(nameof(Creado));
    }

    // GET /Crearusuario/Creado  → confirmación después de registrarse
    [HttpGet]
    public IActionResult Creado() => View();

    /// <summary>
    /// Vuelve a mostrar el formulario. Las contraseñas no se regresan al navegador: los &lt;input type="password"&gt;
    /// del tag helper nunca escriben su valor, y además se vacían aquí.
    /// </summary>
    private IActionResult VistaFormulario(CrearUsuarioViewModel datos)
    {
        datos.password = string.Empty;
        datos.confirmar_password = string.Empty;
        return View(nameof(Index), datos);
    }
}
