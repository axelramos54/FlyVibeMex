namespace FlyVibeMex.Models
{
    /// <summary>
    /// Catálogo de estados válidos de una transacción de pago
    /// </summary>
    public class EstadosPago
    {
        /// <summary>Llave primaria (identificador único autoincremental).</summary>
        public ushort id {get; set;}

        /// <summary>Nombre único del estado.</summary>
        public string nombre {get; set;} = string.Empty;

        /// <summary>Descripción del estado.</summary>
        public string descripcion {get; set;} = string.Empty;

        /// <summary>Indica si el estado concluye el flujo del pago.</summary>
        // NOTA: declarado como byte; en EstadosReservacion el mismo concepto es bool. Unificar a bool. // Solucionado 21-10-2026 Por Yamil
        public bool es_final {get; set;}
    }
}