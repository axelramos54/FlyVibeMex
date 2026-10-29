using System.Security.Claims;
using FlyVibeMex.Models;
using FlyVibeMex.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlyVibeMex.Controllers;

/// <summary>
/// Área del cliente: <c>/Miusuario</c> (perfil), <c>/Miusuario/Misreservaciones</c>, <c>/Miusuario/Reservacion/{codigo}</c>,
/// <c>/Miusuario/Miscompras</c> y <c>/Miusuario/Mistarjetas</c>. Las vistas están en <c>Views/Miusuario/</c>.
/// </summary>
/// <remarks>
/// - Requiere sesión. El id de la cuenta SIEMPRE sale de la cookie; nunca del formulario.
/// - Internos (SOPORTE / AGENTE / ADMINISTRADOR / SA): solo lectura. Con <c>?cliente=ID</c> ("Ver como cliente") ven la
///   cuenta de un cliente para comprobar que lo que añadieron / modificaron / eliminaron ya aparece. Todas las
///   acciones de cliente (cancelar, tarjetas) llevan además [Authorize(Roles = CLIENTE)]: aunque alguien arme el
///   POST a mano, el servidor responde "Acceso denegado". Su propio perfil y contraseña sí los pueden cambiar.
/// - NOTA: el archivo se llama Miusuariocontroller.cs pero la clase es MiusuarioController (la ruta es /Miusuario).
/// </remarks>
[Authorize]
public class MiusuarioController : Controller
{
    private readonly IMiCuentaService _cuenta;
    private readonly IMisViajesService _viajes;

    public MiusuarioController(IMiCuentaService cuenta, IMisViajesService viajes)
    {
        _cuenta = cuenta;
        _viajes = viajes;
    }

    // ============================== PERFIL ==============================

    // GET /Miusuario            (internos: /Miusuario?cliente=5)
    [HttpGet]
    public async Task<IActionResult> Index(ulong? cliente, CancellationToken cancellationToken)
    {
        var contexto = await _cuenta.ContextoAsync(User, cliente, cancellationToken);
        if (contexto is null) return Challenge();

        var vm = await _cuenta.PerfilAsync(contexto, cancellationToken);
        if (vm is null) return Challenge();
        return View(vm);
    }

    // POST /Miusuario/Perfil   → siempre la cuenta PROPIA
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Perfil([Bind(Prefix = "Perfil")] PerfilFormViewModel perfil, CancellationToken cancellationToken)
    {
        var contexto = await _cuenta.ContextoAsync(User, null, cancellationToken);
        if (contexto is null) return Challenge();

        if (ModelState.IsValid)
        {
            var resultado = await _cuenta.ActualizarPerfilAsync(contexto.IdUsuario, perfil, cancellationToken);
            if (resultado.Exitoso)
            {
                await RenovarNombreEnCookieAsync(perfil.nombre.Trim());
                TempData["Exito"] = resultado.Mensaje;
                return RedirectToAction(nameof(Index));
            }
            AgregarErrores(resultado, "Perfil");
        }

        var vm = await _cuenta.PerfilAsync(contexto, cancellationToken);
        if (vm is null) return Challenge();
        vm.Perfil = perfil;
        return View(nameof(Index), vm);
    }

    // POST /Miusuario/CambiarPassword   → siempre la cuenta PROPIA
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CambiarPassword([Bind(Prefix = "Password")] CambioPasswordViewModel password, CancellationToken cancellationToken)
    {
        var contexto = await _cuenta.ContextoAsync(User, null, cancellationToken);
        if (contexto is null) return Challenge();

        if (ModelState.IsValid)
        {
            var resultado = await _cuenta.CambiarPasswordAsync(contexto.IdUsuario, password, cancellationToken);
            if (resultado.Exitoso)
            {
                TempData["Exito"] = resultado.Mensaje;
                return RedirectToAction(nameof(Index));
            }
            AgregarErrores(resultado, "Password");
        }

        var vm = await _cuenta.PerfilAsync(contexto, cancellationToken);
        if (vm is null) return Challenge();
        // Las contraseñas nunca se regresan al navegador.
        vm.Password = new CambioPasswordViewModel();
        return View(nameof(Index), vm);
    }

    // GET /Miusuario/VerCliente?correo=ana.lopez@flyvibemex.test   (solo internos)
    [HttpGet]
    [Authorize(Roles = Roles.PanelInterno)]
    public async Task<IActionResult> VerCliente(string? correo, CancellationToken cancellationToken)
    {
        var idCliente = await _cuenta.BuscarClientePorCorreoAsync(correo, cancellationToken);
        if (idCliente is null)
        {
            TempData["Error"] = "No encontramos un cliente con ese correo.";
            return RedirectToAction(nameof(Index));
        }
        return RedirectToAction(nameof(Misreservaciones), new { cliente = idCliente });
    }

    // ============================== RESERVACIONES ==============================

    // GET /Miusuario/Misreservaciones
    [HttpGet]
    public async Task<IActionResult> Misreservaciones(ulong? cliente, CancellationToken cancellationToken)
    {
        var contexto = await _cuenta.ContextoAsync(User, cliente, cancellationToken);
        if (contexto is null) return Challenge();
        return View("Misreservaciones/index", await _viajes.ReservacionesAsync(contexto, cancellationToken));
    }

    // GET /Miusuario/Reservacion/FV-261028-AB3K
    [HttpGet]
    public async Task<IActionResult> Reservacion(string id, ulong? cliente, CancellationToken cancellationToken)
    {
        var contexto = await _cuenta.ContextoAsync(User, cliente, cancellationToken);
        if (contexto is null) return Challenge();

        var vm = await _viajes.DetalleAsync(contexto, id, cancellationToken);
        if (vm is null) return NotFound();   // no existe o es de otra cuenta
        return View("Reservacion/index", vm);
    }

    // GET /Miusuario/Miscompras
    [HttpGet]
    public async Task<IActionResult> Miscompras(ulong? cliente, CancellationToken cancellationToken)
    {
        var contexto = await _cuenta.ContextoAsync(User, cliente, cancellationToken);
        if (contexto is null) return Challenge();
        return View("Miscompras/index", await _viajes.ComprasAsync(contexto, cancellationToken));
    }

    // ============================== CANCELAR / REEMBOLSAR (solo CLIENTE) ==============================
    // Los envían los modales de Views/Miusuario/_ModalesCancelacion.cshtml. La doble confirmación (clic + clic
    // sostenido) solo evita accidentes; aquí se vuelve a validar todo (dueño, estado, fechas y política).

    // POST /Miusuario/CancelarReservacion
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.Cliente)]
    public async Task<IActionResult> CancelarReservacion(ulong id, string? motivo, string? returnUrl, CancellationToken cancellationToken)
    {
        if (id == 0) return BadRequest();
        if (!TryIdUsuario(out var idUsuario)) return Challenge();

        var resultado = await _viajes.CancelarAsync(idUsuario, id, reembolsar: false, motivo, cancellationToken);
        TempData[resultado.Exitoso ? "CancelacionExito" : "CancelacionError"] = resultado.Mensaje;
        return Volver(returnUrl);
    }

    // POST /Miusuario/SolicitarReembolso
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.Cliente)]
    public async Task<IActionResult> SolicitarReembolso(ulong id, string? motivo, string? returnUrl, CancellationToken cancellationToken)
    {
        if (id == 0) return BadRequest();
        if (!TryIdUsuario(out var idUsuario)) return Challenge();

        var resultado = await _viajes.CancelarAsync(idUsuario, id, reembolsar: true, motivo, cancellationToken);
        TempData[resultado.Exitoso ? "CancelacionExito" : "CancelacionError"] = resultado.Mensaje;
        return Volver(returnUrl);
    }

    // ============================== TARJETAS ==============================

    // GET /Miusuario/Mistarjetas
    [HttpGet]
    public async Task<IActionResult> Mistarjetas(ulong? cliente, CancellationToken cancellationToken)
    {
        var contexto = await _cuenta.ContextoAsync(User, cliente, cancellationToken);
        if (contexto is null) return Challenge();
        return View("Mistarjetas/index", await _cuenta.TarjetasAsync(contexto, cancellationToken));
    }

    // POST /Miusuario/AgregarTarjeta
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.Cliente)]
    public async Task<IActionResult> AgregarTarjeta([Bind(Prefix = "Nueva")] TarjetaNuevaViewModel nueva, CancellationToken cancellationToken)
    {
        var contexto = await _cuenta.ContextoAsync(User, null, cancellationToken);
        if (contexto is null) return Challenge();

        if (ModelState.IsValid)
        {
            var resultado = await _cuenta.AgregarTarjetaAsync(contexto.IdUsuario, nueva, cancellationToken);
            if (resultado.Exitoso)
            {
                TempData["Exito"] = resultado.Mensaje;
                return RedirectToAction(nameof(Mistarjetas));
            }
            AgregarErrores(resultado, "Nueva");
        }

        // Se vuelve a mostrar con los errores; el número y el CVV NO se regresan.
        var vm = await _cuenta.TarjetasAsync(contexto, cancellationToken);
        vm.Nueva = new TarjetaNuevaViewModel
        {
            mes_expiracion = nueva.mes_expiracion,
            anio_expiracion = nueva.anio_expiracion,
            alias = nueva.alias
        };
        ViewData["AbrirFormulario"] = true;
        return View("Mistarjetas/index", vm);
    }

    // POST /Miusuario/CambiarAlias
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.Cliente)]
    public async Task<IActionResult> CambiarAlias(ulong id, string? alias, CancellationToken cancellationToken)
    {
        if (!TryIdUsuario(out var idUsuario)) return Challenge();
        var resultado = await _cuenta.CambiarAliasAsync(idUsuario, id, alias, cancellationToken);
        TempData[resultado.Exitoso ? "Exito" : "Error"] = resultado.Mensaje;
        return RedirectToAction(nameof(Mistarjetas));
    }

    // POST /Miusuario/EliminarTarjeta
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.Cliente)]
    public async Task<IActionResult> EliminarTarjeta(ulong id, CancellationToken cancellationToken)
    {
        if (!TryIdUsuario(out var idUsuario)) return Challenge();
        var resultado = await _cuenta.EliminarTarjetaAsync(idUsuario, id, cancellationToken);
        TempData[resultado.Exitoso ? "Exito" : "Error"] = resultado.Mensaje;
        return RedirectToAction(nameof(Mistarjetas));
    }

    // ============================== AYUDANTES ==============================

    private bool TryIdUsuario(out ulong idUsuario) =>
        ulong.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out idUsuario);

    /// <summary>Pasa los errores del servicio a ModelState con el prefijo del formulario (p. ej. "Perfil.telefono").</summary>
    private void AgregarErrores(ResultadoOperacion resultado, string prefijo)
    {
        if (resultado.Errores is { Count: > 0 } errores)
        {
            foreach (var (campo, mensaje) in errores)
                ModelState.AddModelError($"{prefijo}.{campo}", mensaje);
        }
        else
        {
            ModelState.AddModelError(string.Empty, resultado.Mensaje);
        }
    }

    /// <summary>
    /// Vuelve a emitir la cookie con el nombre nuevo (el menú muestra el nombre guardado en la cookie).
    /// Conserva el resto de los datos de la sesión (id, correo, rol, "mantener sesión").
    /// </summary>
    private async Task RenovarNombreEnCookieAsync(string nombre)
    {
        var autenticacion = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!autenticacion.Succeeded || autenticacion.Principal is null) return;

        var claims = autenticacion.Principal.Claims
            .Where(c => c.Type != ClaimTypes.Name)
            .Append(new Claim(ClaimTypes.Name, nombre));
        var identidad = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identidad), autenticacion.Properties);
    }

    /// <summary>Regresa a la pantalla de donde vino (solo direcciones de este sitio, evita "open redirect").</summary>
    private IActionResult Volver(string? returnUrl)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return LocalRedirect(returnUrl);
        return RedirectToAction(nameof(Misreservaciones));
    }
}
