using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using FlyVibeMex.Models;

namespace FlyVibeMex.Controllers;

/// <summary>
/// Segunda parte de <see cref="GestorController"/> (<c>/Admin/GestorVentas</c>):
/// listado de proveedores, alta / actualización / baja de servicios y su disponibilidad por fecha.
/// </summary>
/// <remarks>
/// Hereda de la otra parte de la clase el atributo [Route("Admin/GestorVentas")], [AutoValidateAntiforgeryToken],
/// [Authorize] y los ayudantes <c>Vista</c>, <c>IdUsuario</c>, <c>AgregarErrores</c> y <c>CargarUbicacionesAsync</c>.
/// Un servicio se da de alta con UN formulario por tipo (vuelo, hospedaje, vehículo, evento) que guarda el servicio,
/// su subtipo y su disponibilidad inicial juntos. El paquete tiene su propia pantalla.
/// Vistas: Proveedores, Servicios, AltaServicio, ActualizarServicio, Disponibilidad, DarDeBajaServicio y los parciales
/// _FormServicio, _ServicioCampos, _FormVuelo, _FormHospedaje, _FormVehiculo, _InventarioInicialCampos.
/// </remarks>
public partial class GestorController
{
    // ============================== LISTADO DE PROVEEDORES ==============================

    // GET /Admin/GestorVentas/Proveedores/Lista?nombre=&tipo=&incluirInactivos=true
    [HttpGet("Proveedores/Lista")]
    public async Task<IActionResult> ListaProveedores(string? nombre, string? tipo, bool incluirInactivos = false,
        CancellationToken cancellationToken = default)
    {
        var vm = new ListaProveedoresViewModel
        {
            Nombre = Recortar(nombre, 120),
            Tipo = ListaProveedoresViewModel.NormalizarTipo(tipo),
            IncluirInactivos = incluirInactivos
        };

        vm.Resultados = await _gestor.ProveedoresAsync(vm.Nombre, vm.Tipo, vm.IncluirInactivos, cancellationToken: cancellationToken);
        return Vista("Proveedores", vm);
    }

    // POST /Admin/GestorVentas/Proveedores/5/Reactivar   → deshace una baja lógica
    [HttpPost("Proveedores/{id}/Reactivar")]
    public async Task<IActionResult> ReactivarProveedor(ulong id, CancellationToken cancellationToken)
    {
        var resultado = await _gestor.CambiarEstadoProveedorAsync(id, true, IdUsuario(), cancellationToken);
        TempData[resultado.Exitoso ? "Exito" : "Aviso"] = resultado.Mensaje;
        return RedirectToAction(nameof(ListaProveedores), new { incluirInactivos = true });
    }

    // ============================== LISTADO DE SERVICIOS ==============================

    // GET /Admin/GestorVentas/Servicios?nombre=&tipo=&idProveedor=&incluirInactivos=true
    [HttpGet("Servicios")]
    public async Task<IActionResult> ListaServicios(string? nombre, string? tipo, ulong? idProveedor, bool incluirInactivos = false,
        CancellationToken cancellationToken = default)
    {
        var filtroNombre = Recortar(nombre, 150);
        var filtroTipo = ServicioFormViewModel.NormalizarTipo(tipo);
        var filtroProveedor = idProveedor is > 0 ? idProveedor : null;

        var vm = await _gestor.ListaServiciosAsync(filtroNombre, filtroTipo, filtroProveedor, incluirInactivos, cancellationToken);
        vm.Nombre = filtroNombre;
        vm.Tipo = filtroTipo;
        vm.IdProveedor = filtroProveedor;
        vm.IncluirInactivos = incluirInactivos;

        await CargarProveedoresAsync(soloActivos: false, cancellationToken);
        return Vista("Servicios", vm);
    }

    // ============================== ALTA DE SERVICIO ==============================

    // GET /Admin/GestorVentas/Servicios/Alta                          → elegir el tipo
    // GET /Admin/GestorVentas/Servicios/Alta?tipo=vuelo&idProveedor=5 → formulario de ese tipo (proveedor preseleccionado)
    [HttpGet("Servicios/Alta")]
    public async Task<IActionResult> AltaServicio(string? tipo, ulong? idProveedor, CancellationToken cancellationToken)
    {
        var formulario = TiposGestor.NuevoFormulario(tipo);
        if (formulario is null)
        {
            var proveedor = idProveedor is ulong id ? await _gestor.ProveedorAsync(id, cancellationToken) : null;
            return Vista("AltaServicio", new FormServicioViewModel { IdProveedor = proveedor?.id, NombreProveedor = proveedor?.nombre });
        }

        formulario.id_proveedor = idProveedor is > 0 ? idProveedor : null;

        // Valores iniciales que ahorran captura.
        switch (formulario)
        {
            case VueloFormViewModel v:
                v.fecha_salida = DateTime.Today.AddDays(1).AddHours(8);
                v.fecha_llegada = v.fecha_salida.Value.AddHours(2);
                break;
            case ServicioConInventarioFormBase inv:
                inv.inventario_desde = DateTime.Today;
                inv.inventario_hasta = DateTime.Today.AddDays(89);
                break;
        }

        return await VistaFormularioAsync(formulario, cancellationToken);
    }

    // POST /Admin/GestorVentas/Servicios/Alta/hospedaje
    // El tipo de la URL decide qué ViewModel se llena (vuelo, hospedaje, vehiculo o evento).
    [HttpPost("Servicios/Alta/{tipo}")]
    [RequestSizeLimit(5_000_000)]
    public async Task<IActionResult> GuardarAlta(string tipo, CancellationToken cancellationToken)
    {
        var datos = TiposGestor.NuevoFormulario(tipo);
        if (datos is null) return NotFound();

        // Llena y VALIDA el ViewModel concreto con lo que envió el formulario ([BindNever] protege el id).
        if (await TryUpdateModelAsync(datos, datos.GetType(), string.Empty))
        {
            var resultado = await _gestor.CrearServicioAsync(datos, IdUsuario(), cancellationToken);
            if (resultado.Exitoso)
            {
                TempData["Exito"] = resultado.Mensaje;
                return RedirectToAction(nameof(ListaServicios), new { idProveedor = datos.id_proveedor, tipo = datos.TipoServicio });
            }
            AgregarErrores(resultado);
        }

        return await VistaFormularioAsync(datos, cancellationToken);
    }

    // ============================== ACTUALIZAR SERVICIO ==============================

    // GET /Admin/GestorVentas/Servicios/5/Actualizar
    [HttpGet("Servicios/{id}/Actualizar")]
    public async Task<IActionResult> ActualizarServicio(ulong id, CancellationToken cancellationToken)
    {
        var formulario = await _gestor.FormularioAsync(id, cancellationToken);
        if (formulario is null) return await SinFormularioAsync(id, cancellationToken);
        return await VistaFormularioAsync(formulario, cancellationToken);
    }

    // POST /Admin/GestorVentas/Servicios/5/Actualizar
    [HttpPost("Servicios/{id}/Actualizar")]
    [RequestSizeLimit(5_000_000)]
    public async Task<IActionResult> GuardarActualizacion(ulong id, CancellationToken cancellationToken)
    {
        // El tipo sale de la BD (regla R1: no se cambia), nunca del formulario.
        var actual = await _gestor.FormularioAsync(id, cancellationToken);
        if (actual is null) return await SinFormularioAsync(id, cancellationToken);

        var datos = TiposGestor.NuevoFormulario(actual.Slug)!;
        datos.id = id;   // el id sale de la URL
        CopiarImagenActual(actual, datos);

        if (await TryUpdateModelAsync(datos, datos.GetType(), string.Empty))
        {
            var resultado = await _gestor.ActualizarServicioAsync(id, datos, IdUsuario(), cancellationToken);
            if (resultado.Exitoso)
            {
                TempData["Exito"] = resultado.Mensaje;
                return RedirectToAction(nameof(ListaServicios), new { nombre = datos.nombre.Trim(), incluirInactivos = !datos.activo });
            }
            AgregarErrores(resultado);
        }

        return await VistaFormularioAsync(datos, cancellationToken);
    }

    // ============================== DAR DE BAJA SERVICIO (doble confirmación) ==============================

    // GET /Admin/GestorVentas/Servicios/5/Baja          → confirmación 1
    // GET /Admin/GestorVentas/Servicios/5/Baja?paso=2   → confirmación 2 (formulario POST)
    [HttpGet("Servicios/{id}/Baja")]
    public async Task<IActionResult> DarDeBajaServicio(ulong id, int paso = 1, CancellationToken cancellationToken = default)
    {
        var detalle = await DetalleServicioAsync(id, cancellationToken);
        if (detalle is null) return NotFound();

        ViewData["Paso"] = paso == 2 ? 2 : 1;
        return Vista("DarDeBajaServicio", detalle);
    }

    // POST /Admin/GestorVentas/Servicios/5/Baja   → ejecuta la baja
    [HttpPost("Servicios/{id}/Baja")]
    public async Task<IActionResult> ConfirmarBajaServicio(ulong id, bool confirmo, CancellationToken cancellationToken)
    {
        var detalle = await DetalleServicioAsync(id, cancellationToken);
        if (detalle is null) return NotFound();

        if (!confirmo)
        {
            ModelState.AddModelError("confirmo", "Marca la casilla para confirmar la baja.");
            ViewData["Paso"] = 2;
            return Vista("DarDeBajaServicio", detalle);
        }

        // Baja LÓGICA, nunca DELETE (detalle_reservacion, paquete_servicio, promocion_servicio y
        // disponibilidad_servicio hacen referencia al servicio).
        var resultado = await _gestor.CambiarEstadoServicioAsync(id, false, IdUsuario(), cancellationToken);
        TempData[resultado.Exitoso ? "Exito" : "Aviso"] = resultado.Mensaje;
        return RedirectToAction(nameof(ListaServicios), new { incluirInactivos = true });
    }

    // POST /Admin/GestorVentas/Servicios/5/Reactivar
    [HttpPost("Servicios/{id}/Reactivar")]
    public async Task<IActionResult> ReactivarServicio(ulong id, CancellationToken cancellationToken)
    {
        var resultado = await _gestor.CambiarEstadoServicioAsync(id, true, IdUsuario(), cancellationToken);
        TempData[resultado.Exitoso ? "Exito" : "Aviso"] = resultado.Mensaje;
        return RedirectToAction(nameof(ListaServicios), new { incluirInactivos = true });
    }

    // ============================== DISPONIBILIDAD ==============================

    // GET /Admin/GestorVentas/Servicios/5/Disponibilidad?desde=2026-11-01
    [HttpGet("Servicios/{id}/Disponibilidad")]
    public async Task<IActionResult> Disponibilidad(ulong id, DateTime? desde, CancellationToken cancellationToken)
    {
        var vm = await _gestor.DisponibilidadAsync(id, desde is DateTime d ? DateOnly.FromDateTime(d) : null, cancellationToken);
        if (vm is null) return NotFound();
        return Vista("Disponibilidad", vm);
    }

    // POST /Admin/GestorVentas/Servicios/5/Disponibilidad
    [HttpPost("Servicios/{id}/Disponibilidad")]
    public async Task<IActionResult> GuardarDisponibilidad(ulong id,
        [Bind(Prefix = "Formulario")] DisponibilidadFormViewModel datos, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var resultado = await _gestor.GuardarDisponibilidadAsync(id, datos, IdUsuario(), cancellationToken);
            if (resultado.Exitoso)
            {
                TempData["Exito"] = resultado.Mensaje;
                return RedirectToAction(nameof(Disponibilidad), new { id, desde = datos.desde?.ToString("yyyy-MM-dd") });
            }
            AgregarErrores(resultado, "Formulario");
        }

        var vm = await _gestor.DisponibilidadAsync(id, datos.desde is DateTime d ? DateOnly.FromDateTime(d) : null, cancellationToken);
        if (vm is null) return NotFound();
        vm.Formulario = datos;
        return Vista("Disponibilidad", vm);
    }

    // ============================== AYUDANTES ==============================

    private static string? Recortar(string? texto, int maximo)
    {
        var t = texto?.Trim();
        if (string.IsNullOrEmpty(t)) return null;
        return t.Length > maximo ? t[..maximo] : t;
    }

    /// <summary>Pantalla del formulario (alta o actualización) con sus listas.</summary>
    private async Task<IActionResult> VistaFormularioAsync(ServicioFormBase datos, CancellationToken cancellationToken)
    {
        await CargarUbicacionesAsync(cancellationToken);
        // En el alta solo se ofrecen proveedores activos; al actualizar también aparece el actual aunque esté inactivo.
        await CargarProveedoresAsync(soloActivos: datos.EsAlta, cancellationToken);

        var vm = new FormServicioViewModel { Formulario = datos, IdProveedor = datos.id_proveedor };
        if (datos.EsAlta && datos.id_proveedor is ulong idProveedor)
        {
            vm.NombreProveedor = (await _gestor.ProveedorAsync(idProveedor, cancellationToken))?.nombre;
            vm.ServiciosDelProveedor = await _gestor.ServiciosDeProveedorAsync(idProveedor, datos.TipoServicio, cancellationToken);
        }
        return Vista(datos.EsAlta ? "AltaServicio" : "ActualizarServicio", vm);
    }

    /// <summary>Respuesta cuando un servicio no tiene formulario: no existe (404) o es un paquete (otra pantalla).</summary>
    private async Task<IActionResult> SinFormularioAsync(ulong id, CancellationToken cancellationToken)
    {
        var servicio = await _gestor.ServicioAsync(id, cancellationToken);
        if (servicio is null) return NotFound();

        TempData["Aviso"] = $"\"{servicio.nombre}\" es un paquete: se edita en Gestionar paquetes.";
        return RedirectToAction(nameof(ListaServicios), new { tipo = servicio.tipo_servicio });
    }

    private static void CopiarImagenActual(ServicioFormBase origen, ServicioFormBase destino)
    {
        if (origen is HospedajeFormViewModel h && destino is HospedajeFormViewModel h2) h2.imagen_actual = h.imagen_actual;
        if (origen is VehiculoFormViewModel v && destino is VehiculoFormViewModel v2) v2.imagen_actual = v.imagen_actual;
    }

    private async Task<ServicioDetalleViewModel?> DetalleServicioAsync(ulong id, CancellationToken cancellationToken)
    {
        var servicio = await _gestor.ServicioAsync(id, cancellationToken);
        if (servicio is null) return null;
        return new ServicioDetalleViewModel
        {
            Servicio = servicio,
            NombreProveedor = (await _gestor.ProveedorAsync(servicio.id_proveedor, cancellationToken))?.nombre
                              ?? $"Proveedor #{servicio.id_proveedor}"
        };
    }

    /// <summary>Lista de proveedores para el &lt;select&gt; del formulario y del filtro.</summary>
    private async Task CargarProveedoresAsync(bool soloActivos, CancellationToken cancellationToken)
    {
        ViewData["Proveedores"] = (await _gestor.ProveedoresAsync(null, null, incluirInactivos: !soloActivos, cancellationToken: cancellationToken))
            .Select(p => new SelectListItem(p.activo ? p.nombre : $"{p.nombre} (inactivo)", p.id.ToString()))
            .ToList();
    }
}
