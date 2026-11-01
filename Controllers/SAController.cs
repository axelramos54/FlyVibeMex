using System.Security.Claims;
using FlyVibeMex.Models;
using FlyVibeMex.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlyVibeMex.Controllers;

/// <summary>
/// Panel del SA: <c>/Admin/SA</c> (solo rol SA). Vistas en <c>Views/Admin/SA/</c>.
/// </summary>
/// <remarks>
/// Controlador partial (T4 fase D):
/// <list type="bullet">
/// <item>SAController.cs: inicio, usuarios y roles.</item>
/// <item>SAController.Catalogos.cs: ubicaciones, métodos de pago y bancos.</item>
/// <item>SAController.Contenido.cs: bitácora y contenido del sitio.</item>
/// </list>
/// El SA sale SIEMPRE de la cookie (nunca del formulario). Todo cambio queda en la bitácora.
/// </remarks>
[Route("Admin/SA")]
[AutoValidateAntiforgeryToken]
[Authorize(Roles = Roles.PanelSA)]
public partial class SAController : Controller
{
    private const string Carpeta = "~/Views/Admin/SA/";

    private readonly ISAUsuariosService _usuarios;
    private readonly ISACatalogosService _catalogos;
    private readonly ISABitacoraService _bitacora;
    private readonly IContenidoSitioService _contenido;

    public SAController(ISAUsuariosService usuarios, ISACatalogosService catalogos, ISABitacoraService bitacora,
        IContenidoSitioService contenido)
    {
        _usuarios = usuarios;
        _catalogos = catalogos;
        _bitacora = bitacora;
        _contenido = contenido;
    }

    // GET /Admin/SA
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        Vista("Index", await _bitacora.InicioAsync(cancellationToken));

    // ============================== USUARIOS ==============================

    // GET /Admin/SA/Usuarios?texto=&rol=&estado=activos&pagina=1
    [HttpGet("Usuarios")]
    public async Task<IActionResult> Usuarios([FromQuery] FiltroUsuarios filtro, CancellationToken cancellationToken) =>
        Vista("Usuarios", await _usuarios.ListaAsync(filtro, cancellationToken));

    // GET /Admin/SA/Usuarios/Alta
    [HttpGet("Usuarios/Alta")]
    public async Task<IActionResult> AltaUsuario(CancellationToken cancellationToken)
    {
        var vm = new UsuarioAltaViewModel();
        await CargarRolesInternosAsync(vm.rol, cancellationToken);
        return Vista("UsuarioAlta", vm);
    }

    // POST /Admin/SA/Usuarios/Alta  → muestra la contraseña temporal UNA vez (no se redirige: no se guarda en ningún lado)
    [HttpPost("Usuarios/Alta")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GuardarUsuario(UsuarioAltaViewModel datos, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var (resultado, temporal) = await _usuarios.CrearInternoAsync(datos, IdUsuario(), cancellationToken);
            if (resultado.Exitoso && temporal is not null) return Vista("ContrasenaTemporal", temporal);
            AgregarErrores(resultado);
        }

        await CargarRolesInternosAsync(datos.rol, cancellationToken);
        return Vista("UsuarioAlta", datos);
    }

    // GET /Admin/SA/Usuarios/5
    [HttpGet("Usuarios/{id}")]
    public async Task<IActionResult> Usuario(ulong id, CancellationToken cancellationToken)
    {
        var vm = await _usuarios.EdicionAsync(id, IdUsuario(), cancellationToken);
        return vm is null ? NotFound() : Vista("Usuario", vm);
    }

    // POST /Admin/SA/Usuarios/5/Datos
    [HttpPost("Usuarios/{id}/Datos")]
    public async Task<IActionResult> GuardarDatosUsuario(ulong id, [Bind(Prefix = "Datos")] UsuarioDatosForm datos, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var resultado = await _usuarios.ActualizarDatosAsync(id, datos, IdUsuario(), cancellationToken);
            if (resultado.Exitoso)
            {
                TempData["Exito"] = resultado.Mensaje;
                return RedirectToAction(nameof(Usuario), new { id });
            }
            AgregarErrores(resultado, "Datos");
        }

        var vm = await _usuarios.EdicionAsync(id, IdUsuario(), cancellationToken);
        if (vm is null) return NotFound();
        vm.Datos = datos;
        return Vista("Usuario", vm);
    }

    // POST /Admin/SA/Usuarios/5/Rol
    [HttpPost("Usuarios/{id}/Rol")]
    public async Task<IActionResult> CambiarRolUsuario(ulong id, string? rol, CancellationToken cancellationToken)
    {
        var resultado = await _usuarios.CambiarRolAsync(id, rol, IdUsuario(), cancellationToken);
        TempData[resultado.Exitoso ? "Exito" : "Aviso"] = resultado.Mensaje;
        return RedirectToAction(nameof(Usuario), new { id });
    }

    // POST /Admin/SA/Usuarios/5/Estado   (activo = true | false)
    [HttpPost("Usuarios/{id}/Estado")]
    public async Task<IActionResult> CambiarEstadoUsuario(ulong id, bool activo, CancellationToken cancellationToken)
    {
        var resultado = await _usuarios.CambiarEstadoAsync(id, activo, IdUsuario(), cancellationToken);
        TempData[resultado.Exitoso ? "Exito" : "Aviso"] = resultado.Mensaje;
        return RedirectToAction(nameof(Usuario), new { id });
    }

    // POST /Admin/SA/Usuarios/5/Contrasena  → muestra la contraseña temporal nueva UNA vez
    [HttpPost("Usuarios/{id}/Contrasena")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> RestablecerContrasena(ulong id, CancellationToken cancellationToken)
    {
        var (resultado, temporal) = await _usuarios.RestablecerContrasenaAsync(id, IdUsuario(), cancellationToken);
        if (resultado.Exitoso && temporal is not null) return Vista("ContrasenaTemporal", temporal);

        TempData["Aviso"] = resultado.Mensaje;
        return RedirectToAction(nameof(Usuario), new { id });
    }

    // ============================== ROLES ==============================

    // GET /Admin/SA/Roles
    [HttpGet("Roles")]
    public async Task<IActionResult> ListaRoles(CancellationToken cancellationToken) =>
        Vista("Roles", await _usuarios.RolesAsync(cancellationToken));

    // POST /Admin/SA/Roles/2/Descripcion
    [HttpPost("Roles/{id}/Descripcion")]
    public async Task<IActionResult> GuardarDescripcionRol(ulong id, string? descripcion, CancellationToken cancellationToken)
    {
        var resultado = await _usuarios.ActualizarDescripcionRolAsync(id, descripcion, IdUsuario(), cancellationToken);
        TempData[resultado.Exitoso ? "Exito" : "Aviso"] = resultado.Mensaje;
        return RedirectToAction(nameof(ListaRoles));
    }

    // ============================== Ayudantes ==============================

    private IActionResult Vista(string nombre, object? modelo = null) => View(Carpeta + nombre + ".cshtml", modelo);

    private ulong IdUsuario() => ulong.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task CargarRolesInternosAsync(string? elegido, CancellationToken cancellationToken) =>
        ViewData["Roles"] = await _usuarios.OpcionesRolAsync(soloInternos: true, elegido, cancellationToken);

    /// <summary>Pasa los errores del servicio a ModelState (con el prefijo del formulario, si lo tiene).</summary>
    private void AgregarErrores(ResultadoOperacion resultado, string prefijo = "")
    {
        if (resultado.Errores is { Count: > 0 } errores)
        {
            foreach (var (campo, mensaje) in errores)
                ModelState.AddModelError(string.IsNullOrEmpty(prefijo) ? campo : $"{prefijo}.{campo}", mensaje);
        }
        else
        {
            ModelState.AddModelError(string.Empty, resultado.Mensaje);
        }
    }
}
