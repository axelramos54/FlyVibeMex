using FlyVibeMex.Models;
using Microsoft.AspNetCore.Mvc;

namespace FlyVibeMex.Controllers;

/// <summary>Segunda parte de <see cref="SAController"/>: catálogos base (ubicaciones, métodos de pago y bancos).</summary>
public partial class SAController
{
    // ============================== UBICACIONES ==============================

    // GET /Admin/SA/Ubicaciones?texto=&incluirInactivas=true
    [HttpGet("Ubicaciones")]
    public async Task<IActionResult> Ubicaciones(string? texto, bool incluirInactivas = false, CancellationToken cancellationToken = default) =>
        Vista("Ubicaciones", await _catalogos.UbicacionesAsync(texto, incluirInactivas, cancellationToken));

    // GET /Admin/SA/Ubicaciones/Alta
    [HttpGet("Ubicaciones/Alta")]
    public IActionResult AltaUbicacion() => Vista("UbicacionForm", new UbicacionFormViewModel());

    // POST /Admin/SA/Ubicaciones/Alta
    [HttpPost("Ubicaciones/Alta")]
    public async Task<IActionResult> GuardarUbicacion(UbicacionFormViewModel datos, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var resultado = await _catalogos.GuardarUbicacionAsync(null, datos, IdUsuario(), cancellationToken);
            if (resultado.Exitoso)
            {
                TempData["Exito"] = resultado.Mensaje;
                return RedirectToAction(nameof(Ubicaciones), new { texto = datos.municipio_ciudad.Trim() });
            }
            AgregarErrores(resultado);
        }
        return Vista("UbicacionForm", datos);
    }

    // GET /Admin/SA/Ubicaciones/5/Editar
    [HttpGet("Ubicaciones/{id}/Editar")]
    public async Task<IActionResult> EditarUbicacion(ulong id, CancellationToken cancellationToken)
    {
        var vm = await _catalogos.UbicacionAsync(id, cancellationToken);
        return vm is null ? NotFound() : Vista("UbicacionForm", vm);
    }

    // POST /Admin/SA/Ubicaciones/5/Editar
    [HttpPost("Ubicaciones/{id}/Editar")]
    public async Task<IActionResult> GuardarCambiosUbicacion(ulong id, UbicacionFormViewModel datos, CancellationToken cancellationToken)
    {
        datos.id = id;
        if (ModelState.IsValid)
        {
            var resultado = await _catalogos.GuardarUbicacionAsync(id, datos, IdUsuario(), cancellationToken);
            if (resultado.Exitoso)
            {
                TempData["Exito"] = resultado.Mensaje;
                return RedirectToAction(nameof(Ubicaciones), new { texto = datos.municipio_ciudad.Trim(), incluirInactivas = true });
            }
            AgregarErrores(resultado);
        }
        return Vista("UbicacionForm", datos);
    }

    // GET /Admin/SA/Ubicaciones/5/Baja  → confirmación con lo que dejará de aparecer en el catálogo
    [HttpGet("Ubicaciones/{id}/Baja")]
    public async Task<IActionResult> BajaUbicacion(ulong id, CancellationToken cancellationToken)
    {
        var vm = await _catalogos.ImpactoBajaUbicacionAsync(id, cancellationToken);
        return vm is null ? NotFound() : Vista("UbicacionBaja", vm);
    }

    // POST /Admin/SA/Ubicaciones/5/Estado   (activo = true | false)
    [HttpPost("Ubicaciones/{id}/Estado")]
    public async Task<IActionResult> CambiarEstadoUbicacion(ulong id, bool activo, CancellationToken cancellationToken)
    {
        var resultado = await _catalogos.CambiarEstadoUbicacionAsync(id, activo, IdUsuario(), cancellationToken);
        TempData[resultado.Exitoso ? "Exito" : "Aviso"] = resultado.Mensaje;
        return RedirectToAction(nameof(Ubicaciones), new { incluirInactivas = true });
    }

    // ============================== MÉTODOS DE PAGO ==============================

    // GET /Admin/SA/MetodosPago
    [HttpGet("MetodosPago")]
    public async Task<IActionResult> MetodosPago(CancellationToken cancellationToken) =>
        Vista("MetodosPago", await _catalogos.MetodosPagoAsync(cancellationToken));

    // GET /Admin/SA/MetodosPago/1/Editar
    [HttpGet("MetodosPago/{id}/Editar")]
    public async Task<IActionResult> EditarMetodoPago(ushort id, CancellationToken cancellationToken)
    {
        var vm = await _catalogos.MetodoPagoAsync(id, cancellationToken);
        return vm is null ? NotFound() : Vista("MetodoPagoForm", vm);
    }

    // POST /Admin/SA/MetodosPago/1/Editar
    [HttpPost("MetodosPago/{id}/Editar")]
    public async Task<IActionResult> GuardarMetodoPago(ushort id, MetodoPagoFormViewModel datos, CancellationToken cancellationToken)
    {
        datos.id = id;
        if (ModelState.IsValid)
        {
            var resultado = await _catalogos.GuardarMetodoPagoAsync(id, datos, IdUsuario(), cancellationToken);
            if (resultado.Exitoso)
            {
                TempData["Exito"] = resultado.Mensaje;
                return RedirectToAction(nameof(MetodosPago));
            }
            AgregarErrores(resultado);
        }
        return Vista("MetodoPagoForm", datos);
    }

    // POST /Admin/SA/MetodosPago/1/Estado
    [HttpPost("MetodosPago/{id}/Estado")]
    public async Task<IActionResult> CambiarEstadoMetodoPago(ushort id, bool activo, CancellationToken cancellationToken)
    {
        var resultado = await _catalogos.CambiarEstadoMetodoPagoAsync(id, activo, IdUsuario(), cancellationToken);
        TempData[resultado.Exitoso ? "Exito" : "Aviso"] = resultado.Mensaje;
        return RedirectToAction(nameof(MetodosPago));
    }

    // ============================== BANCOS ==============================

    // GET /Admin/SA/Bancos
    [HttpGet("Bancos")]
    public async Task<IActionResult> Bancos(CancellationToken cancellationToken) =>
        Vista("Bancos", await _catalogos.BancosAsync(cancellationToken));

    // GET /Admin/SA/Bancos/Alta
    [HttpGet("Bancos/Alta")]
    public IActionResult AltaBanco() => Vista("BancoForm", new InstitucionFormViewModel());

    // POST /Admin/SA/Bancos/Alta
    [HttpPost("Bancos/Alta")]
    public async Task<IActionResult> GuardarBanco(InstitucionFormViewModel datos, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var resultado = await _catalogos.GuardarBancoAsync(null, datos, IdUsuario(), cancellationToken);
            if (resultado.Exitoso)
            {
                TempData["Exito"] = resultado.Mensaje;
                return RedirectToAction(nameof(Bancos));
            }
            AgregarErrores(resultado);
        }
        return Vista("BancoForm", datos);
    }

    // GET /Admin/SA/Bancos/5/Editar
    [HttpGet("Bancos/{id}/Editar")]
    public async Task<IActionResult> EditarBanco(ulong id, CancellationToken cancellationToken)
    {
        var vm = await _catalogos.BancoAsync(id, cancellationToken);
        return vm is null ? NotFound() : Vista("BancoForm", vm);
    }

    // POST /Admin/SA/Bancos/5/Editar
    [HttpPost("Bancos/{id}/Editar")]
    public async Task<IActionResult> GuardarCambiosBanco(ulong id, InstitucionFormViewModel datos, CancellationToken cancellationToken)
    {
        datos.id = id;
        if (ModelState.IsValid)
        {
            var resultado = await _catalogos.GuardarBancoAsync(id, datos, IdUsuario(), cancellationToken);
            if (resultado.Exitoso)
            {
                TempData["Exito"] = resultado.Mensaje;
                return RedirectToAction(nameof(Bancos));
            }
            AgregarErrores(resultado);
        }
        return Vista("BancoForm", datos);
    }

    // POST /Admin/SA/Bancos/5/Estado
    [HttpPost("Bancos/{id}/Estado")]
    public async Task<IActionResult> CambiarEstadoBanco(ulong id, bool activo, CancellationToken cancellationToken)
    {
        var resultado = await _catalogos.CambiarEstadoBancoAsync(id, activo, IdUsuario(), cancellationToken);
        TempData[resultado.Exitoso ? "Exito" : "Aviso"] = resultado.Mensaje;
        return RedirectToAction(nameof(Bancos));
    }
}
