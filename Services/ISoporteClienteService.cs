using FlyVibeMex.Models;

namespace FlyVibeMex.Services;

/// <summary>Soporte del lado del cliente: crear tickets (asesoría o problema) y ver su estado.</summary>
public interface ISoporteClienteService
{
    /// <summary>Tickets de la cuenta del contexto + sus reservaciones (para ligar un ticket a una de ellas).</summary>
    Task<MisSolicitudesViewModel> SolicitudesAsync(ContextoCuenta contexto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Crea un ticket ABIERTA del cliente <paramref name="idUsuario"/>. Si se indica una reservación, debe ser suya.
    /// </summary>
    Task<ResultadoOperacion> CrearAsync(ulong idUsuario, NuevaSolicitudViewModel nueva, CancellationToken cancellationToken = default);
}
