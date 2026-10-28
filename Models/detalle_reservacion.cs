namespace FlyVibeMex.Models
{
    /// <summary>
    /// Representa una línea de detalle de una reservación. Cada línea corresponde a un servicio reservado (vuelo, hotel, paquete, etc.).
    /// </summary>
    /// <remarks>
    /// Al crear la línea se copian: precio unitario vigente (de DisponibilidadServicio.precio_fecha),
    /// descuento aplicado, subtotal, fechas contratadas y una descripción textual del servicio.
    /// </remarks>
    // NOTA: la clase se llama DetallesReservacion (plural) y el archivo detalle_reservacion.cs.
    public class DetallesReservacion
    {
        /// <summary>Llave primaria (identificador único autoincremental).</summary>
        public ulong id {get; set;}

        /// <summary>Llave foránea → <c>Reservaciones.id</c>. Reserva a la que pertenece la línea.</summary>
        public ulong id_reservacion {get; set;}

        /// <summary>Llave foránea → <c>Servicios.id</c>. Servicio reservado.</summary>
        public ulong id_servicio {get; set;}

        /// <summary>Llave foránea → <c>Promociones.id</c>. Promoción aplicada, si hubo.</summary>
        // NOTA: debería ser ulong?. // Solucionado 21-10-2026 Por Yamil
        public ulong? id_promocion {get; set;}

        /// <summary>Unidades, personas o noches, según la lógica del servicio.</summary>
        public decimal cantidad {get; set;}

        /// <summary>Fecha de inicio del servicio reservado.</summary>
        // NOTA: error en el nombre: `fehca_inicio` debería ser `fecha_inicio`. No se renombra aquí para no romper la compilación. // Solucionado 21-10-2026 Por Yamil
        public DateTime fecha_inicio {get; set;}

        /// <summary>Fecha de fin del servicio reservado.</summary>
        public DateTime fecha_fin {get; set;}

        /// <summary>Precio unitario al momento de reservar.</summary>
        public decimal precio_unitario {get; set;}

        /// <summary>Descuento monetario aplicado.</summary>
        public decimal descuento_aplicado {get; set;}

        /// <summary>Subtotal de la línea.</summary>
        public decimal subtotal {get; set;}

        /// <summary>Descripción del concepto reservado.</summary>
        public string descripcion_snapshot {get; set;} = string.Empty;

        // ---------- Snapshot de la política de cancelación del servicio al momento de comprar ----------
        // Se copian de Servicios al crear la reservación y NUNCA se actualizan: el reembolso se calcula con estos valores.

        /// <summary><c>false</c> = el servicio no admite cancelación (tarifa no reembolsable).</summary>
        public bool permite_cancelacion {get; set;} = true;

        /// <summary>Horas de anticipación al inicio para obtener el 100 % de reembolso (0 = hasta el inicio).</summary>
        public int horas_reembolso_total {get; set;} = 48;

        /// <summary>Porcentaje (0-100) que se reembolsa si se cancela después de ese plazo y antes del inicio.</summary>
        public decimal porcentaje_reembolso_tardio {get; set;} = 50;

        /// <summary>Texto de la política que ve el cliente antes de pagar (opcional).</summary>
        public string? politica_cancelacion {get; set;}
    }
}