using FlyVibeMex.Data;
using FlyVibeMex.Models;
using FlyVibeMex.Services;
using Microsoft.AspNetCore.Mvc;

namespace FlyVibeMex.Controllers;

/// <summary>
/// Registro de clientes: <c>/Crearusuario</c>. Las vistas están en <c>Views/Crearusuario/</c>.
/// Se llega desde el enlace "Crear nueva cuenta" de <c>Views/Home/_FormSesion.cshtml</c> (página Iniciar sesión).
/// </summary>
/// <remarks>
/// Controlador delgado (T8.1): valida el formulario → <see cref="IAuthService.RegistrarClienteAsync"/> → redirige.
/// Reglas de seguridad (las aplica el servicio):
/// - El formulario se recibe en <see cref="CrearUsuarioViewModel"/> con [Bind]: nadie puede mandar id_rol, activo o
///   password_hash. El rol siempre es el de cliente y se busca en roles_usuarios, nunca se toma del formulario.
/// - La contraseña se guarda solo como hash (regla R10), con el mismo PasswordHasher que valida el login.
/// - <c>Lazy&lt;IAuthService&gt;</c> (ver Services/ServicioDiferido.cs): el servicio y la BD solo se crean al guardar,
///   así la pantalla abre aunque SQL Server todavía no esté configurado ("modo sin BD").
/// </remarks>
public class CrearusuarioController : Controller
{
    private readonly Lazy<IAuthService> _auth;
    private readonly ConfiguracionBaseDatos _configuracionBd;

    public CrearusuarioController(Lazy<IAuthService> auth, ConfiguracionBaseDatos configuracionBd)
    {
        _auth = auth;
        _configuracionBd = configuracionBd;
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

        var resultado = await _auth.Value.RegistrarClienteAsync(datos, cancellationToken);
        if (!resultado.Exitoso)
        {
            // Errores por campo (correo / teléfono ya registrados) junto a su campo; si no, arriba del formulario.
            if (resultado.Errores is { Count: > 0 } errores)
            {
                foreach (var (campo, mensaje) in errores)
                    ModelState.AddModelError(campo, mensaje);
            }
            else
            {
                ModelState.AddModelError(string.Empty, resultado.Mensaje);
            }
            return VistaFormulario(datos);
        }

        TempData["Exito"] = resultado.Mensaje;
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
