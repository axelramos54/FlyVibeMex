namespace FlyVibeMex.Models
{
    /// <summary>
    /// Bitácora de transiciones de estado de cada pago. Solo se insertan registros: nunca se
    /// actualizan ni se eliminan, para conservar la historia completa.
    /// </summary>
    // NOTA: el archivo está en plural (historial_estado_pagos.cs) y la clase en singular.
    public class HistorialEstadoPago
    {
        /// <summary>Llave primaria (identificador único autoincremental).</summary>
        public ulong id {get; set;}

        /// <summary>Llave foránea → <c>Pagos.id</c>. Pago afectado.</summary>
        public ulong id_pago {get; set;}

        /// <summary>Llave foránea → <c>EstadosPago.id</c>. Estado registrado.</summary>
        public ushort id_estado_pago {get; set;}

        /// <summary>Momento del cambio.</summary>
        public DateTime fecha_cambio {get; set;}

        /// <summary>Referencia o motivo del cambio.</summary>
        public string detalle {get; set;} = string.Empty;
    }
}