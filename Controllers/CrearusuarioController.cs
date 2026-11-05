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
    /// <summary>Consultas de "¿este correo ya existe?" permitidas por IP en <see cref="VentanaConsultasCorreo"/>.</summary>
    private const int MaximoConsultasCorreo = 20;
    private static readonly TimeSpan VentanaConsultasCorreo = TimeSpan.FromMinutes(1);

    private readonly Lazy<IAuthService> _auth;
    private readonly ConfiguracionBaseDatos _configuracionBd;
    private readonly ILimitadorIntentos _limitador;

    public CrearusuarioController(Lazy<IAuthService> auth, ConfiguracionBaseDatos configuracionBd, ILimitadorIntentos limitador)
    {
        _auth = auth;
        _configuracionBd = configuracionBd;
        _limitador = limitador;
    }

    // GET /Crearusuario?returnUrl=…   returnUrl: a dónde volver al terminar (solo direcciones de este sitio).
    [HttpGet]
    public IActionResult Index(string? returnUrl) =>
        View(new CrearUsuarioViewModel { returnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null });

    // POST /Crearusuario
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(
        [Bind("nombre,apellido_paterno,apellido_materno,correo,telefono,password,confirmar_password,acepto_terminos,returnUrl")]
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

        // Auto-login (Módulo 1): se entra con las credenciales recién guardadas y por el MISMO camino que el
        // formulario de inicio de sesión, así el rol sale de roles_usuarios (lo que revisa ValidacionSesion).
        // Si este paso falla NO es un error de registro: la cuenta ya existe, así que se manda a Creado para
        // entrar a mano.
        var acceso = await _auth.Value.ValidarCredencialesAsync(datos.correo, datos.password, cancellationToken);
        if (acceso.Exitoso && acceso.Usuario is not null)
        {
            await HttpContext.IniciarSesionAsync(acceso.Usuario, persistente: false);

            var nombre = datos.nombre.Trim();

            // Solo se regresa a direcciones de este mismo sitio (evita "open redirect").
            if (!string.IsNullOrEmpty(datos.returnUrl) && Url.IsLocalUrl(datos.returnUrl))
            {
                TempData["Exito"] = $"¡Bienvenido, {nombre}! Tu cuenta quedó creada y ya iniciaste sesión.";
                return LocalRedirect(datos.returnUrl);
            }

            TempData["Exito"] = "Tu cuenta quedó creada y ya iniciaste sesión.";
            TempData["NombreNuevo"] = nombre;
            return RedirectToAction(nameof(Creado));
        }

        TempData["Exito"] = resultado.Mensaje;
        TempData["NombreNuevo"] = datos.nombre.Trim();
        return RedirectToAction(nameof(Creado));
    }

    // GET /Crearusuario/Creado  → confirmación después de registrarse
    [HttpGet]
    public IActionResult Creado() => View();

    // GET /Crearusuario/CorreoDisponible?correo=…  → lo llama el [Remote] del campo correo (aviso en vivo).
    // Respuesta en el formato de jquery-validation: true = válido; un texto = el mensaje de error.
    // Falla ABIERTO (responde "disponible") en tres casos: sin BD configurada, pasado el límite por IP y ante un
    // error de BD (eso lo resuelve AuthService.CorreoDisponibleAsync). Nunca bloquea el registro: la comprobación
    // que manda está en AuthService.RegistrarClienteAsync.
    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> CorreoDisponible(string? correo, CancellationToken cancellationToken)
    {
        if (!_configuracionBd.EstaConfigurada)
            return Json(true);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "sin-ip";
        if (!_limitador.Permitir($"registro-correo:{ip}", MaximoConsultasCorreo, VentanaConsultasCorreo))
            return Json(true);

        return await _auth.Value.CorreoDisponibleAsync(correo, cancellationToken)
            ? Json(true)
            : Json("Ya existe una cuenta con ese correo. Inicia sesión.");
    }

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
