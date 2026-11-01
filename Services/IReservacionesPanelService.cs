using FlyVibeMex.Models;

namespace FlyVibeMex.Services;

/// <summary>
/// Consultas del panel de Reservaciones y reembolsos (<c>/Admin/Reservaciones</c>, roles AGENTE, ADMINISTRADOR y SA).
/// Las acciones (cancelar a nombre del cliente, aprobar o rechazar reembolsos) están en <see cref="ICancelacionesService"/>.
/// </summary>
public interface IReservacionesPanelService
{
    /// <summary>Reservaciones de todos los clientes con filtros (código, correo, estado, fechas de compra), de 50 en 50.</summary>
    Task<ListaReservacionesPanelViewModel> ListaAsync(FiltroReservacionesPanel filtro, CancellationToken cancellationToken = default);

    /// <summary>Detalle completo con historiales, pagos, reembolsos, tickets y opciones de cancelación; <c>null</c> si no existe.</summary>
    Task<DetalleReservacionPanelViewModel?> DetalleAsync(string codigoReserva, CancellationToken cancellationToken = default);

    /// <summary>Resumen de solo lectura (lo muestra el ticket de soporte); <c>null</c> si no existe.</summary>
    Task<ResumenReservacionViewModel?> ResumenAsync(ulong idReservacion, CancellationToken cancellationToken = default);

    /// <summary>Id de una reservación por su código; <c>null</c> si no existe.</summary>
    Task<ulong?> IdPorCodigoAsync(string codigoReserva, CancellationToken cancellationToken = default);

    /// <summary>Reembolsos pendientes (SOLICITADO) o todos.</summary>
    Task<ListaReembolsosPanelViewModel> ReembolsosAsync(string? vista, CancellationToken cancellationToken = default);
}
