using Microsoft.AspNetCore.Mvc;
using FlyVibeMex.Models;

namespace FlyVibeMex.Controllers;

/// <summary>
/// Tercera parte de <see cref="GestorController"/> (<c>/Admin/GestorVentas/Paquetes</c>): alta, edición y listado de paquetes.
/// </summary>
/// <remarks>
/// Un paquete es un servicio más (tipo PAQUETE): su baja / reactivación y su disponibilidad usan las mismas pantallas
/// que los demás servicios (Servicios/{id}/Baja, Servicios/{id}/Disponibilidad). Vistas: Paquetes y PaqueteForm.
/// </remarks>
public partial class GestorController
{
    // GET /Admin/GestorVentas/Paquetes?nombre=&incluirInactivos=true
    [HttpGet("Paquetes")]
    public async Task<IActionResult> GestionarPaquetes(string? nombre, bool incluirInactivos = false, CancellationToken cancellationToken = default)
    {
        var vm = await _paquetes.ListaAsync(Recortar(nombre, 150), incluirInactivos, cancellationToken);
        return Vista("Paquetes", vm);
    }

    // GET /Admin/GestorVentas/Paquetes/Crear?idProveedor=5
    [HttpGet("Paquetes/Crear")]
    public async Task<IActionResult> CrearPaquete(ulong? idProveedor, CancellationToken cancellationToken)
    {
        var vm = await _paquetes.NuevoAsync(idProveedor, cancellationToken);
        return await VistaPaqueteAsync(vm, cancellationToken);
    }

    // POST /Admin/GestorVentas/Paquetes/Crear
    [HttpPost("Paquetes/Crear")]
    [RequestSizeLimit(5_000_000)]
    public async Task<IActionResult> GuardarPaquete(PaqueteFormViewModel datos, CancellationToken cancellationToken)
    {
        IgnorarNoIncluidos(datos);
        if (ModelState.IsValid)
        {
            var resultado = await _paquetes.CrearAsync(datos, IdUsuario(), cancellationToken);
            if (resultado.Exitoso)
            {
                TempData["Exito"] = resultado.Mensaje;
                return RedirectToAction(nameof(GestionarPaquetes), new { nombre = datos.nombre.Trim() });
            }
            AgregarErrores(resultado);
        }

        await _paquetes.PrepararAsync(datos, cancellationToken);
        return await VistaPaqueteAsync(datos, cancellationToken);
    }

    // GET /Admin/GestorVentas/Paquetes/5/Actualizar
    [HttpGet("Paquetes/{id}/Actualizar")]
    public async Task<IActionResult> ActualizarPaquete(ulong id, CancellationToken cancellationToken)
    {
        var vm = await _paquetes.FormularioAsync(id, cancellationToken);
        if (vm is null) return NotFound();
        return await VistaPaqueteAsync(vm, cancellationToken);
    }

    // POST /Admin/GestorVentas/Paquetes/5/Actualizar
    [HttpPost("Paquetes/{id}/Actualizar")]
    [RequestSizeLimit(5_000_000)]
    public async Task<IActionResult> GuardarCambiosPaquete(ulong id, PaqueteFormViewModel datos, CancellationToken cancellationToken)
    {
        var actual = await _paquetes.FormularioAsync(id, cancellationToken);
        if (actual is null) return NotFound();
        datos.id = id;                              // el id sale de la URL
        datos.imagen_actual = actual.imagen_actual;
        IgnorarNoIncluidos(datos);

        if (ModelState.IsValid)
        {
            var resultado = await _paquetes.ActualizarAsync(id, datos, IdUsuario(), cancellationToken);
            if (resultado.Exitoso)
            {
                TempData["Exito"] = resultado.Mensaje;
                return RedirectToAction(nameof(GestionarPaquetes), new { nombre = datos.nombre.Trim(), incluirInactivos = !datos.activo });
            }
            AgregarErrores(resultado);
        }

        await _paquetes.PrepararAsync(datos, cancellationToken);
        return await VistaPaqueteAsync(datos, cancellationToken);
    }

    /// <summary>
    /// La tabla manda una fila por cada servicio candidato; la cantidad u orden de una fila NO marcada no importa,
    /// así que sus errores de validación (p. ej. una cantidad vacía) no deben impedir guardar.
    /// </summary>
    private void IgnorarNoIncluidos(PaqueteFormViewModel datos)
    {
        for (var i = 0; i < datos.componentes.Count; i++)
        {
            if (datos.componentes[i].incluido) continue;
            var prefijo = $"componentes[{i}].";
            foreach (var clave in ModelState.Keys.Where(k => k.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase)).ToList())
                ModelState.Remove(clave);
        }
    }

    /// <summary>Formulario de paquete con las listas de proveedores y ubicaciones.</summary>
    private async Task<IActionResult> VistaPaqueteAsync(PaqueteFormViewModel vm, CancellationToken cancellationToken)
    {
        await CargarUbicacionesAsync(cancellationToken);
        await CargarProveedoresAsync(soloActivos: vm.EsAlta, cancellationToken);
        return Vista("PaqueteForm", vm);
    }
}
