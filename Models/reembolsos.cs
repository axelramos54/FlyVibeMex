namespace FlyVibeMex.Models
{
    /// <summary>
    /// Devolución monetaria derivada de un pago previo (regla R8): siempre se liga a un pago, no a la reservación.
    /// Flujo de estados: SOLICITADO → APROBADO o RECHAZADO → PROCESADO. Cada cambio se registra en
    /// <c>HistorialEstadoReembolso</c> (R6). El monto no debe exceder el del pago de origen.
    /// </summary>
    public class Reembolsos
    {
        /// <summary>Llave primaria (identificador único autoincremental).</summary>
        public ulong id {get; set;}

        /// <summary>Llave foránea → <c>Pagos.id</c>. Pago de origen.</summary>
        public ulong id_pago {get; set;}

        /// <summary>Monto solicitado o reembolsado.</summary>
        public decimal monto {get; set;}

        /// <summary>Motivo del reembolso.</summary>
        public string motivo {get; set;} = string.Empty;

        /// <summary>Estado actual: SOLICITADO, APROBADO, RECHAZADO o PROCESADO.</summary>
        public string estado {get; set;} = string.Empty;

        /// <summary>Fecha de solicitud.</summary>
        public DateTime fecha_solicitud {get; set;}

        /// <summary>Fecha de resolución.</summary>
        // NOTA: vacía hasta resolverse. // Creo que se soluciono 21-10-2026 Por Yamil
        public DateTime? fecha_resolucion {get; set;}

        /// <summary>Referencia del reembolso en el proveedor de pagos.</summary>
        // NOTA: opcional en el diseño. // Solucionado 21-10-2026 Por Yamil
        public string? referencia_externa {get; set;}
    }
}