using FlyVibeMex.Models;

namespace FlyVibeMex.Services;

/// <summary>Soporte del lado del cliente: crear tickets (asesoría o problema), ver su estado y conversar con el agente.</summary>
public interface ISoporteClienteService
{
    /// <summary>Tickets de la cuenta del contexto + sus reservaciones (para ligar un ticket a una de ellas).</summary>
    Task<MisSolicitudesViewModel> SolicitudesAsync(ContextoCuenta contexto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Crea un ticket ABIERTA del cliente <paramref name="idUsuario"/>. Si se indica una reservación, debe ser suya.
    /// </summary>
    Task<ResultadoOperacion> CrearAsync(ulong idUsuario, NuevaSolicitudViewModel nueva, CancellationToken cancellationToken = default);

    /// <summary>Un ticket de la cuenta del contexto con su conversación; <c>null</c> si no existe o es de otra persona.</summary>
    Task<TicketClienteViewModel?> TicketAsync(ContextoCuenta contexto, ulong idTicket, CancellationToken cancellationToken = default);

    /// <summary>
    /// El cliente <paramref name="idUsuario"/> responde en su ticket mientras no esté CERRADO (UPDATE condicional:
    /// si se cerró mientras escribía, no se guarda y se le pide abrir uno nuevo).
    /// </summary>
    Task<ResultadoOperacion> ResponderAsync(ulong idUsuario, ulong idTicket, string? mensaje, CancellationToken cancellationToken = default);
}
