using Microsoft.AspNetCore.Mvc;
using FlyVibeMex.Models;

namespace FlyVibeMex.Controllers;

/// <summary>
/// Cuarta parte de <see cref="GestorController"/> (<c>/Admin/GestorVentas/Promociones</c>): alta, edición, listado y
/// activación de promociones. Vistas: Promociones y PromocionForm.
/// </summary>
/// <remarks>
/// Las promociones no se anuncian en el sitio: el cliente escribe el código en el resumen de compra.
/// Nunca se borran (las compras guardan qué promoción usaron): se desactivan.
/// </remarks>
public partial class GestorController
{
    // GET /Admin/GestorVentas/Promociones?estado=vigentes&codigo=VERANO
    [HttpGet("Promociones")]
    public async Task<IActionResult> GestionarPromociones(string? estado, string? codigo, CancellationToken cancellationToken)
    {
        var vm = await _promociones.ListaAsync(Recortar(codigo, 30), ListaPromocionesViewModel.NormalizarEstado(estado), cancellationToken);
        return Vista("Promociones", vm);
    }

    // GET /Admin/GestorVentas/Promociones/Crear
    // (el nombre "Promociones" se conserva porque así lo enlazan el menú y el inicio del Gestor)
    [HttpGet("Promociones/Crear")]
    public async Task<IActionResult> Promociones(CancellationToken cancellationToken) =>
        Vista("PromocionForm", await _promociones.NuevaAsync(cancellationToken));

    // POST /Admin/GestorVentas/Promociones/Crear
    [HttpPost("Promociones/Crear")]
    public async Task<IActionResult> GuardarPromocion(PromocionFormViewModel datos, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var resultado = await _promociones.GuardarAsync(null, datos, IdUsuario(), cancellationToken);
            if (resultado.Exitoso)
            {
                TempData["Exito"] = resultado.Mensaje;
                return RedirectToAction(nameof(GestionarPromociones), new { estado = "todas", codigo = datos.codigo });
            }
            AgregarErrores(resultado);
        }

        await _promociones.PrepararAsync(datos, cancellationToken);
        return Vista("PromocionForm", datos);
    }

    // GET /Admin/GestorVentas/Promociones/5/Actualizar
    [HttpGet("Promociones/{id}/Actualizar")]
    public async Task<IActionResult> ActualizarPromocion(ulong id, CancellationToken cancellationToken)
    {
        var vm = await _promociones.FormularioAsync(id, cancellationToken);
        if (vm is null) return NotFound();
        return Vista("PromocionForm", vm);
    }

    // POST /Admin/GestorVentas/Promociones/5/Actualizar
    [HttpPost("Promociones/{id}/Actualizar")]
    public async Task<IActionResult> GuardarCambiosPromocion(ulong id, PromocionFormViewModel datos, CancellationToken cancellationToken)
    {
        var actual = await _promociones.FormularioAsync(id, cancellationToken);
        if (actual is null) return NotFound();
        datos.id = id;                    // el id sale de la URL
        datos.codigo = actual.codigo;     // el código no se cambia al editar

        if (ModelState.IsValid)
        {
            var resultado = await _promociones.GuardarAsync(id, datos, IdUsuario(), cancellationToken);
            if (resultado.Exitoso)
            {
                TempData["Exito"] = resultado.Mensaje;
                return RedirectToAction(nameof(GestionarPromociones), new { estado = "todas", codigo = datos.codigo });
            }
            AgregarErrores(resultado);
        }

        await _promociones.PrepararAsync(datos, cancellationToken);
        return Vista("PromocionForm", datos);
    }

    // POST /Admin/GestorVentas/Promociones/5/Estado   (activo=true | false)
    [HttpPost("Promociones/{id}/Estado")]
    public async Task<IActionResult> CambiarEstadoPromocion(ulong id, bool activo, string? estado, CancellationToken cancellationToken)
    {
        var resultado = await _promociones.CambiarEstadoAsync(id, activo, IdUsuario(), cancellationToken);
        TempData[resultado.Exitoso ? "Exito" : "Aviso"] = resultado.Mensaje;
        return RedirectToAction(nameof(GestionarPromociones), new { estado = ListaPromocionesViewModel.NormalizarEstado(estado) });
    }
}
