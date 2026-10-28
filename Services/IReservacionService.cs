using FlyVibeMex.Models;

namespace FlyVibeMex.Services;

/// <summary>
/// Compra de un servicio del catálogo: resumen (precio recalculado + promoción + tarjetas del cliente), pago
/// simulado en una sola transacción y confirmación. Una reservación = un servicio.
/// </summary>
public interface IReservacionService
{
    /// <summary>
    /// Arma la pantalla de resumen para el cliente <paramref name="idUsuario"/>. <c>null</c> si el servicio no existe
    /// o no está a la venta. Si no hay cupo, <c>Cotizacion.Disponible</c> es <c>false</c> y no se muestra el pago.
    /// </summary>
    Task<ResumenReservaViewModel?> ArmarResumenAsync(ulong idUsuario, PagoFormViewModel solicitud, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recalcula todo, valida la tarjeta, simula el cobro y, si se aprueba, crea en UNA transacción:
    /// descuento de cupo, reservación, detalle (snapshot), pago e historiales. Si algo falla no queda nada guardado.
    /// </summary>
    Task<ResultadoPago> PagarAsync(ulong idUsuario, PagoFormViewModel pago, CancellationToken cancellationToken = default);

    /// <summary>Datos del comprobante. Solo el dueño de la reservación puede verlo (<c>null</c> en otro caso).</summary>
    Task<ConfirmacionReservaViewModel?> ConfirmacionAsync(ulong idUsuario, string codigoReserva, CancellationToken cancellationToken = default);
}
