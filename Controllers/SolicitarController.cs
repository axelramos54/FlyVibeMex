using FlyVibeMex.Models;
using FlyVibeMex.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlyVibeMex.Controllers;

/// <summary>
/// Soporte del cliente: <c>/Solicitar</c>. Formulario para abrir un ticket (asesoría o problema, opcionalmente ligado
/// a una de sus reservaciones) y lista de sus tickets con estado y agente. Vista: <c>Views/Solicitar/index.cshtml</c>.
/// </summary>
/// <remarks>
/// Requiere sesión. Los internos lo ven en solo lectura (con <c>?cliente=ID</c> ven los tickets de ese cliente);
/// crear un ticket es solo para CLIENTE.
/// </remarks>
[Authorize]
public class SolicitarController : Controller
{
    private readonly IMiCuentaService _cuenta;
    private readonly ISoporteClienteService _soporte;

    public SolicitarController(IMiCuentaService cuenta, ISoporteClienteService soporte)
    {
        _cuenta = cuenta;
        _soporte = soporte;
    }

    // GET /Solicitar
    [HttpGet]
    public async Task<IActionResult> Index(ulong? cliente, CancellationToken cancellationToken)
    {
        var contexto = await _cuenta.ContextoAsync(User, cliente, cancellationToken);
        if (contexto is null) return Challenge();
        return View(await _soporte.SolicitudesAsync(contexto, cancellationToken));
    }

    // POST /Solicitar
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.Cliente)]
    public async Task<IActionResult> Index([Bind(Prefix = "Nueva")] NuevaSolicitudViewModel nueva, CancellationToken cancellationToken)
    {
        var contexto = await _cuenta.ContextoAsync(User, null, cancellationToken);
        if (contexto is null) return Challenge();

        if (ModelState.IsValid)
        {
            var resultado = await _soporte.CrearAsync(contexto.IdUsuario, nueva, cancellationToken);
            if (resultado.Exitoso)
            {
                TempData["Exito"] = resultado.Mensaje;
                return RedirectToAction(nameof(Index));
            }

            foreach (var (campo, mensaje) in resultado.Errores ?? new())
                ModelState.AddModelError($"Nueva.{campo}", mensaje);
        }

        var vm = await _soporte.SolicitudesAsync(contexto, cancellationToken);
        vm.Nueva = nueva;
        return View(vm);
    }
}
