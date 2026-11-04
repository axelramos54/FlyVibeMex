using FlyVibeMex.Models;

namespace FlyVibeMex.Services;

/// <summary>
/// "Mis reservaciones" (próximas, pasadas y canceladas), detalle con historial, "Mis compras" (pagos y reembolsos)
/// y la cancelación de una reservación con o sin reembolso.
/// </summary>
public interface IMisViajesService
{
    /// <summary>
    /// "Mis reservaciones" = los <b>viajes</b>: agrupados en Próximos, Pasados y Cancelados, con sus opciones de
    /// cancelación ya calculadas con la política GUARDADA en la compra (R4).
    /// </summary>
    Task<MisReservacionesViewModel> ReservacionesAsync(ContextoCuenta contexto, CancellationToken cancellationToken = default);

    /// <summary>Detalle de una reservación de la cuenta del contexto; <c>null</c> si no existe o no es suya.</summary>
    Task<DetalleReservacionViewModel?> DetalleAsync(ContextoCuenta contexto, string codigoReserva, CancellationToken cancellationToken = default);

    /// <summary>
    /// "Mis compras" = el <b>dinero</b>: los pagos y los reembolsos de la cuenta. Es la contraparte de
    /// "Mis reservaciones", que muestra los viajes.
    /// </summary>
    Task<MisComprasViewModel> ComprasAsync(ContextoCuenta contexto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancela una reservación del cliente <paramref name="idUsuario"/> en una sola transacción: estado CANCELADA +
    /// historial + devolución del cupo y, si <paramref name="reembolsar"/>, reembolso SOLICITADO + su historial.
    /// Vuelve a calcular todo con <see cref="ReglasCancelacion"/> (no confía en lo que mostró el navegador).
    /// </summary>
    Task<ResultadoOperacion> CancelarAsync(ulong idUsuario, ulong idReservacion, bool reembolsar, string? motivo,
        CancellationToken cancellationToken = default);
}
