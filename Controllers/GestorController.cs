using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using FlyVibeMex.Models;
using FlyVibeMex.Services;

namespace FlyVibeMex.Controllers;

/// <summary>
/// Panel del gestor de ventas: <c>/Admin/GestorVentas</c>. Las vistas están en <c>Views/Admin/GestorVentas/</c>.
/// Los datos los maneja <see cref="IGestorCatalogoService"/> (EF Core + bitácora).
/// </summary>
/// <remarks>
/// Reglas de seguridad que sigue este controlador:
/// - Solo ADMINISTRADOR y SA (<see cref="Roles.PanelGestor"/>).
/// - Los GET solo consultan; todo lo que modifica datos es POST.
/// - [AutoValidateAntiforgeryToken]: todo POST exige el token que agrega el &lt;form&gt; tag helper.
/// - Los formularios se reciben en ViewModels (solo los campos permitidos) y se validan en el servidor.
/// - El id del proveedor / servicio siempre sale de la URL y se busca en la BD; nunca se confía en un campo oculto.
/// </remarks>
[Route("Admin/GestorVentas")]
[AutoValidateAntiforgeryToken]
[Authorize(Roles = Roles.PanelGestor)]   // el atributo aplica también a GestorController.ProveedoresServicios.cs
// NOTA: es "partial" porque el listado de proveedores, los servicios y la disponibilidad están en
//       Controllers/GestorController.ProveedoresServicios.cs (misma clase, otro archivo).
public partial class GestorController : Controller
{
    private const string Carpeta = "~/Views/Admin/GestorVentas/";

    private readonly IGestorCatalogoService _gestor;

    // NOTA: el constructor va SOLO en este archivo (la otra parte de la clase "partial" no debe declarar otro).
    public GestorController(IGestorCatalogoService gestor)
    {
        _gestor = gestor;
    }

    private IActionResult Vista(string nombre, object? modelo = null) => View(Carpeta + nombre + ".cshtml", modelo);

    // ============================== INICIO ==============================

    // GET /Admin/GestorVentas
    [HttpGet("")]
    public IActionResult Index() => Vista("Index");

    // ============================== ALTA DE PROVEEDOR ==============================

    // GET /Admin/GestorVentas/Proveedores/Alta
    [HttpGet("Proveedores/Alta")]
    public async Task<IActionResult> AltaProveedor(CancellationToken cancellationToken)
    {
        await CargarUbicacionesAsync(cancellationToken);
        return Vista("AltaProveedor", new ProveedorFormViewModel());
    }

    // POST /Admin/GestorVentas/Proveedores/Alta
    [HttpPost("Proveedores/Alta")]
    public async Task<IActionResult> AltaProveedor(
        [Bind("id_ubicacion,tipo_proveedor,nombre,telefono,correo")] ProveedorFormViewModel datos, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var resultado = await _gestor.GuardarProveedorAsync(null, datos, IdUsuario(), cancellationToken);
            if (resultado.Exitoso)
            {
                TempData["Exito"] = resultado.Mensaje;
                return RedirectToAction(nameof(ListaProveedores), new { nombre = datos.nombre.Trim() });
            }
            AgregarErrores(resultado);
        }

        await CargarUbicacionesAsync(cancellationToken);
        return Vista("AltaProveedor", datos);
    }

    // ============================== GESTIONAR PROVEEDORES ==============================

    // GET /Admin/GestorVentas/Proveedores?nombre=xxx
    [HttpGet("Proveedores")]
    public async Task<IActionResult> GestionarProveedores(string? nombre, CancellationToken cancellationToken)
    {
        var vm = new GestionarProveedoresViewModel { Nombre = nombre?.Trim() };

        if (!string.IsNullOrEmpty(vm.Nombre))
        {
            if (vm.Nombre.Length > 120)
            {
                ModelState.AddModelError("nombre", "El nombre no puede tener más de 120 caracteres.");
            }
            else
            {
                vm.Buscado = true;
                vm.Resultados = await _gestor.ProveedoresAsync(vm.Nombre, null, incluirInactivos: true, maximo: 20, cancellationToken);
            }
        }

        return Vista("GestionarProveedores", vm);
    }

    // ----- Actualizar -----

    // GET /Admin/GestorVentas/Proveedores/5/Actualizar
    [HttpGet("Proveedores/{id}/Actualizar")]
    public async Task<IActionResult> Actualizar(ulong id, CancellationToken cancellationToken)
    {
        var proveedor = await _gestor.ProveedorAsync(id, cancellationToken);
        if (proveedor is null) return NotFound();

        await CargarUbicacionesAsync(cancellationToken);
        return Vista("Actualizar", ProveedorFormViewModel.Desde(proveedor));
    }

    // POST /Admin/GestorVentas/Proveedores/5/Actualizar
    [HttpPost("Proveedores/{id}/Actualizar")]
    public async Task<IActionResult> Actualizar(ulong id,
        [Bind("id_ubicacion,tipo_proveedor,nombre,telefono,correo,activo")] ProveedorFormViewModel datos, CancellationToken cancellationToken)
    {
        if (await _gestor.ProveedorAsync(id, cancellationToken) is null) return NotFound();
        datos.id = id; // el id sale de la URL, no del formulario

        if (ModelState.IsValid)
        {
            var resultado = await _gestor.GuardarProveedorAsync(id, datos, IdUsuario(), cancellationToken);
            if (resultado.Exitoso)
            {
                TempData["Exito"] = resultado.Mensaje;
                return RedirectToAction(nameof(ListaProveedores), new { nombre = datos.nombre.Trim(), incluirInactivos = !datos.activo });
            }
            AgregarErrores(resultado);
        }

        await CargarUbicacionesAsync(cancellationToken);
        return Vista("Actualizar", datos);
    }

    // ----- Dar de baja (doble confirmación) -----

    // GET /Admin/GestorVentas/Proveedores/5/Baja          → confirmación 1
    // GET /Admin/GestorVentas/Proveedores/5/Baja?paso=2   → confirmación 2 (formulario POST)
    [HttpGet("Proveedores/{id}/Baja")]
    public async Task<IActionResult> DarDeBaja(ulong id, int paso = 1, CancellationToken cancellationToken = default)
    {
        var proveedor = await _gestor.ProveedorAsync(id, cancellationToken);
        if (proveedor is null) return NotFound();

        ViewData["Paso"] = paso == 2 ? 2 : 1;
        return Vista("DarDeBaja", proveedor);
    }

    // POST /Admin/GestorVentas/Proveedores/5/Baja   → ejecuta la baja
    [HttpPost("Proveedores/{id}/Baja")]
    public async Task<IActionResult> ConfirmarBaja(ulong id, bool confirmo, CancellationToken cancellationToken)
    {
        var proveedor = await _gestor.ProveedorAsync(id, cancellationToken);
        if (proveedor is null) return NotFound();

        // La segunda confirmación se valida en el servidor (no basta con el "required" del navegador).
        if (!confirmo)
        {
            ModelState.AddModelError("confirmo", "Marca la casilla para confirmar la baja.");
            ViewData["Paso"] = 2;
            return Vista("DarDeBaja", proveedor);
        }

        // Baja LÓGICA, nunca DELETE (la tabla servicios hace referencia al proveedor).
        var resultado = await _gestor.CambiarEstadoProveedorAsync(id, false, IdUsuario(), cancellationToken);
        TempData[resultado.Exitoso ? "Exito" : "Aviso"] = resultado.Mensaje;
        return RedirectToAction(nameof(ListaProveedores), new { incluirInactivos = true });
    }

    // ----- Asociar = dar de alta un servicio con este proveedor ya elegido -----

    // GET /Admin/GestorVentas/Proveedores/5/Asociar?tipo=hospedaje
    [HttpGet("Proveedores/{id}/Asociar")]
    public IActionResult Asociar(ulong id, string? tipo) =>
        RedirectToAction(nameof(AltaServicio), new { tipo = TiposGestor.Normalizar(tipo), idProveedor = id });

    // ============================== PAQUETES Y PROMOCIONES ==============================
    // Pantallas pendientes: por ahora muestran un aviso de "en construcción".

    [HttpGet("Paquetes/Crear")]
    public IActionResult CrearPaquete() => EnConstruccion("Crear paquete");

    [HttpGet("Paquetes")]
    public IActionResult GestionarPaquetes() => EnConstruccion("Gestionar paquetes");

    [HttpGet("Promociones/Crear")]
    public IActionResult Promociones() => EnConstruccion("Promociones");

    [HttpGet("Promociones")]
    public IActionResult GestionarPromociones() => EnConstruccion("Gestionar promociones");

    // ============================== AYUDANTES ==============================

    private IActionResult EnConstruccion(string titulo)
    {
        ViewData["Title"] = titulo;
        return Vista("EnConstruccion");
    }

    /// <summary>Id del usuario de la cookie (para la bitácora). [Authorize] garantiza que existe.</summary>
    private ulong IdUsuario() => ulong.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Pasa los errores del servicio a ModelState (con el prefijo del formulario, si lo tiene).</summary>
    private void AgregarErrores(ResultadoOperacion resultado, string prefijo = "")
    {
        if (resultado.Errores is { Count: > 0 } errores)
        {
            foreach (var (campo, mensaje) in errores)
                ModelState.AddModelError(prefijo.Length == 0 ? campo : $"{prefijo}.{campo}", mensaje);
        }
        else
        {
            ModelState.AddModelError(string.Empty, resultado.Mensaje);
        }
    }

    private async Task CargarUbicacionesAsync(CancellationToken cancellationToken)
    {
        ViewData["Ubicaciones"] = (await _gestor.UbicacionesAsync(cancellationToken))
            .Select(u => new SelectListItem($"{u.municipio_ciudad}, {u.estado}, {u.pais}", u.id.ToString()))
            .ToList();
    }
}
