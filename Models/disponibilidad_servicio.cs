namespace FlyVibeMex.Models
{
    /// <summary>
    /// Representa la disponibilidad de un servicio para una fecha específica.
    /// Cada fila indica cuántas unidades del servicio están disponibles y a qué precio.
    /// </summary>
    /// <remarks>
    /// Reglas: una sola fila por (servicio, fecha); <c>cantidad_disponible</c> nunca menor que 0 ni mayor que
    /// <c>cantidad_total</c>; al reservar se descuenta dentro de una transacción y al cancelar se devuelve.
    /// El <c>precio_fecha</c> vigente es el que se copia como snapshot en <c>DetallesReservacion</c>.
    /// </remarks>
    public class DisponibilidadServicio
    {
        /// <summary>Llave primaria (identificador único autoincremental).</summary>
        public ulong id {get; set;}

        /// <summary>Llave foránea → <c>Servicios.id</c>. Servicio cuyo inventario se controla.</summary>
        public ulong id_servicio {get; set;}

        /// <summary>Día al que aplica este inventario y precio.</summary>
        public DateOnly fecha {get; set;}

        /// <summary>Capacidad total para esa fecha.</summary>
        public int cantidad_total {get; set;}

        /// <summary>Capacidad que aún puede reservarse.</summary>
        public int cantidad_disponible {get; set;}

        /// <summary>Precio vigente para esa fecha.</summary>
        public decimal precio_fecha {get; set;}

        /// <summary>Fecha y hora de la última actualización.</summary>
        public DateTime actualizado_en {get; set;}
    }
}