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
        // NOTA: debería ser ulong?.
        public ulong id_promocion {get; set;}

        /// <summary>Unidades, personas o noches, según la lógica del servicio.</summary>
        public decimal cantidad {get; set;}

        /// <summary>Fecha de inicio del servicio reservado.</summary>
        // NOTA: error en el nombre: `fehca_inicio` debería ser `fecha_inicio`. No se renombra aquí para no romper la compilación.
        public DateTime fehca_inicio {get; set;}

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
    }
}