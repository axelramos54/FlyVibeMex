using FlyVibeMex.Models;
using Microsoft.AspNetCore.Mvc;

namespace FlyVibeMex.Controllers;

/// <summary>
/// Cuarta parte de <see cref="SAController"/>: panel de estadísticas de ventas (solo lectura).
/// </summary>
/// <remarks>
/// Vive bajo <c>/Admin/SA</c>, igual que el resto del panel: no es una ruta embebible, así que la integración
/// con GoldStay (iframes, SSO y cabeceras CSP) no la alcanza ni cambia nada aquí.
/// </remarks>
public partial class SAController
{
    // ============================== ESTADÍSTICAS DE VENTAS ==============================

    // GET /Admin/SA/Estadisticas?desde=&hasta=
    [HttpGet("Estadisticas")]
    public async Task<IActionResult> Estadisticas([FromQuery] FiltroEstadisticas filtro, CancellationToken cancellationToken) =>
        Vista("Estadisticas", await _estadisticas.VentasAsync(filtro, cancellationToken));
}
