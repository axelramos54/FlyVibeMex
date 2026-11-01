using FlyVibeMex.Models;

namespace FlyVibeMex.Services;

/// <summary>Qué pasa con el dinero al cancelar.</summary>
public enum ModoReembolso
{
    /// <summary>Solo se cancela (no se devuelve nada).</summary>
    Ninguno,
    /// <summary>Lo pide el cliente: el reembolso queda SOLICITADO y lo resuelve el personal.</summary>
    Solicitar,
    /// <summary>Lo hace el personal: el reembolso se procesa en el mismo paso (PROCESADO + pago REEMBOLSADO).</summary>
    Procesar
}

/// <summary>Resultado técnico de <see cref="ICancelacionesService.EjecutarAsync"/> (cada pantalla pone su mensaje).</summary>
public enum ResultadoCancelacion
{
    Ok,
    /// <summary>La reservación (o su pago) cambió mientras se cancelaba: no se hizo nada.</summary>
    Cambio,
    /// <summary>Error de la base de datos: no se hizo nada.</summary>
    ErrorBd
}

/// <summary>Todo lo que necesita una cancelación ya validada.</summary>
/// <param name="IdReservacion">Reservación a cancelar.</param>
/// <param name="IdUsuario">Quién cancela (queda en los historiales).</param>
/// <param name="IdDueno">Si se indica, la reservación debe ser de esa cuenta (cliente: evita IDOR).</param>
/// <param name="PorPersonal">true = la cancela personal de FlyVibeMex a nombre del cliente (se escribe en la bitácora).</param>
/// <param name="Motivo">Motivo (historial de la reservación y del reembolso).</param>
/// <param name="Reembolso">Qué pasa con el dinero.</param>
/// <param name="MontoReembolso">Monto ya calculado (política o cortesía).</param>
/// <param name="DetalleReembolso">Texto del historial del reembolso ("Solicitado por el cliente: 100 %…").</param>
public sealed record OrdenCancelacion(
    ulong IdReservacion,
    ulong IdUsuario,
    ulong? IdDueno,
    bool PorPersonal,
    string? Motivo,
    ModoReembolso Reembolso,
    decimal MontoReembolso,
    string? DetalleReembolso);

/// <summary>
/// Cancelaciones y reembolsos en UN solo lugar (T4 fase C). Lo usan "Mis reservaciones" del cliente
/// (<see cref="MisViajesService"/>) y el panel de Reservaciones del personal.
/// </summary>
public interface ICancelacionesService
{
    /// <summary>
    /// Opciones de cancelación de una reservación (de cualquier cliente) con la política GUARDADA en la compra
    /// (<see cref="ReglasCancelacion"/>); <c>null</c> si no existe.
    /// </summary>
    Task<OpcionesCancelacion?> OpcionesAsync(ulong idReservacion, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ejecuta una cancelación ya validada en UNA transacción: estado CANCELADA (UPDATE condicional) + historial →
    /// devolver el cupo → reembolso (SOLICITADO o PROCESADO + pago REEMBOLSADO) con sus historiales → bitácora.
    /// </summary>
    Task<ResultadoCancelacion> EjecutarAsync(OrdenCancelacion orden, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancelación del personal a nombre del cliente: mismas reglas que el cliente (cuándo se puede cancelar) y
    /// <paramref name="modo"/> = SIN (sin reembolso), POLITICA (lo que da la política) o CORTESIA (100 % del pago).
    /// El reembolso se procesa en el mismo paso. Motivo obligatorio (10 a 250).
    /// </summary>
    Task<ResultadoOperacion> CancelarComoPersonalAsync(ulong idReservacion, ulong idUsuario, string? modo, string? motivo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resuelve un reembolso SOLICITADO. Aprobar: PROCESADO (devolución simulada) + pago REEMBOLSADO. Rechazar: motivo
    /// obligatorio (10 a 250), que verá el cliente. El monto no se edita. UPDATE condicional: nadie lo resuelve dos veces.
    /// </summary>
    Task<ResultadoOperacion> ResolverReembolsoAsync(ulong idReembolso, bool aprobar, string? motivo, ulong idUsuario,
        CancellationToken cancellationToken = default);
}
