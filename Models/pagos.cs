namespace FlyVibeMex.Models
{
    /// <summary>
    /// Intento o transacción de pago asociada a una reservación. Una reservación puede tener varios
    /// intentos. Cada cambio de <c>id_estado_pago</c> debe registrarse en <c>HistorialEstadoPago</c>.
    /// </summary>
    public class Pagos
    {
        /// <summary>Llave primaria (identificador único autoincremental).</summary>
        public ulong id {get; set;}

        /// <summary>Llave foránea → <c>Reservaciones.id</c>. Reserva que se paga.</summary>
        public ulong id_reservacion {get; set;}

        /// <summary>Llave foránea → <c>MetodosPago.id</c>. Método empleado.</summary>
        public ushort id_metodo_pago {get; set;}

        /// <summary>Llave foránea → <c>TarjetasUsuarios.id</c>. Tarjeta tokenizada, solo si el método la requiere.</summary>

        public ulong id_tarjeta {get; set;}

        /// <summary>Llave foránea → <c>EstadosPago.id</c>. Estado actual del pago.</summary>
        public ushort id_estado_pago {get; set;}

        /// <summary>Monto de la transacción.</summary>
        public decimal monto {get; set;}

        /// <summary>Moneda (ISO, por defecto MXN).</summary>
        public string moneda {get; set;} = string.Empty;
        
        /// <summary>Referencia del gateway o banco (única cuando existe).</summary>
        // NOTA: opcional en el diseño.
        public string referencia_externa {get; set;} = string.Empty;

        /// <summary>Fecha en que se confirmó el pago (vacía mientras no se confirme).</summary>
        // NOTA: opcional en el diseño.
        public DateTime fecha_pago {get; set;}
        
        /// <summary>Fecha y hora de alta del registro (campo de control).</summary>
        public DateTime creado_en {get; set;}
    }
}