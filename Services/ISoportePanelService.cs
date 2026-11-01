using FlyVibeMex.Models;

namespace FlyVibeMex.Services;

/// <summary>
/// Panel de Soporte (<c>/Admin/Soporte</c>, roles SOPORTE, ADMINISTRADOR y SA): tomar el siguiente ticket de la fila,
/// conversar con el cliente, cerrar y, los supervisores (ADMINISTRADOR y SA), ver todos y reasignar.
/// </summary>
/// <remarks>
/// Regla: solo el agente ASIGNADO responde y cierra (un supervisor que quiera intervenir se reasigna el ticket).
/// </remarks>
public interface ISoportePanelService
{
    Task<ResumenSoporteViewModel> ResumenAsync(ulong idAgente, bool esSupervisor, CancellationToken cancellationToken = default);

    /// <summary>El ticket ABIERTO sin agente más antiguo (solo para leerlo antes de tomarlo); <c>null</c> si no hay.</summary>
    Task<TicketDetalleViewModel?> SiguienteAsync(ulong idAgente, bool esSupervisor, CancellationToken cancellationToken = default);

    /// <summary>Asigna el ticket a <paramref name="idAgente"/> con un UPDATE condicional (nadie toma el mismo dos veces).</summary>
    Task<ResultadoOperacion> TomarAsync(ulong idTicket, ulong idAgente, CancellationToken cancellationToken = default);

    /// <summary>Tickets del agente: en curso (los que esperan respuesta primero) o cerrados.</summary>
    Task<List<TicketPanelItem>> MisTicketsAsync(ulong idAgente, bool cerrados, CancellationToken cancellationToken = default);

    /// <summary>
    /// Detalle con conversación y resumen de la reservación. Lo ve quien lo atiende, cualquiera si está ABIERTO sin
    /// agente (para decidir si lo toma) y los supervisores; si no, <c>null</c>.
    /// </summary>
    Task<TicketDetalleViewModel?> DetalleAsync(ulong idTicket, ulong idUsuario, bool esSupervisor, CancellationToken cancellationToken = default);

    Task<ResultadoOperacion> ResponderAsync(ulong idTicket, ulong idAgente, string? mensaje, CancellationToken cancellationToken = default);

    Task<ResultadoOperacion> CerrarAsync(ulong idTicket, ulong idAgente, CancellationToken cancellationToken = default);

    /// <summary>Todos los tickets con filtros (supervisores).</summary>
    Task<ListaTicketsViewModel> TodosAsync(string? estado, ulong? idAgente, string? texto, int pagina, CancellationToken cancellationToken = default);

    /// <summary>Pasa un ticket no cerrado a otra cuenta activa de SOPORTE, ADMINISTRADOR o SA (supervisores).</summary>
    Task<ResultadoOperacion> ReasignarAsync(ulong idTicket, ulong idAgenteNuevo, ulong idSupervisor, CancellationToken cancellationToken = default);
}
