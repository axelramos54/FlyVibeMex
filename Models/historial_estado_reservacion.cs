namespace FlyVibeMex.Models
{
    /// <summary>
    /// <b>Bitácora de estados de una reservación</b>: una fila por cada cambio de estado.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implementa la regla <b>R6</b>: cada transición de estado se ESCRIBE aquí además de actualizar
    /// <c>reservaciones.id_estado_reservacion</c>. La fila actual dice en qué estado está; esta tabla dice cómo
    /// llegó ahí, quién lo hizo y cuándo. Sin ella no se puede responder "¿quién canceló esta reserva?".
    /// </para>
    /// <para>
    /// Las otras dos máquinas de estado tienen su propia bitácora equivalente:
    /// <see cref="HistorialEstadoPago"/> y <see cref="HistorialEstadoReembolso"/>.
    /// </para>
    /// </remarks>
    public class HistorialEstadoReservacion
    {
        /// <summary>Llave primaria (identificador único autoincremental).</summary>
        public ulong id {get; set;}

        /// <summary>Llave foránea → <c>Reservaciones.id</c>. Reservación que cambió de estado.</summary>
        public ulong id_reservacion {get; set;}

        /// <summary>Llave foránea → <c>EstadosReservacion.id</c>. Estado al que PASÓ en este cambio.</summary>
        // ushort (no ulong): debe coincidir con EstadosReservacion.id (smallint) para poder ser llave foránea. // Corregido 25-10-2026 (Paso 0)
        public ushort id_estado_reservacion {get; set;}

        /// <summary>
        /// Quién hizo el cambio. <c>null</c> = lo hizo el SISTEMA, no una persona (R6).
        /// </summary>
        /// <remarks>
        /// Es nullable a propósito: cuando el pago se aprueba, la reservación pasa sola de PENDIENTE a
        /// CONFIRMADA sin que nadie pulse nada, y en esa fila <c>id_usuario_cambio</c> queda en <c>null</c>.
        /// Un cliente que cancela o un agente que cancela a su nombre sí quedan registrados.
        /// </remarks>
        public ulong? id_usuario_cambio {get; set;}

        /// <summary>Cuándo ocurrió el cambio.</summary>
        public DateTime fecha_cambio {get; set;}

        /// <summary>Motivo escrito por quien canceló, o la razón automática del sistema. Opcional.</summary>
        public string? motivo {get; set;}
    }
}
