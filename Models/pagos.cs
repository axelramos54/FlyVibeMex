namespace FlyVibeMex.Models
{
    public class Pagos
    {
        public ulong id {get; set;}
        public ulong id_reservacion {get; set;}
        public ushort id_metodo_pago {get; set;}
        public ulong id_tarjeta {get; set;}
        public ushort id_estado_pago {get; set;}

        public decimal monto {get; set;}
        public string moneda {get; set;} = string.Empty;
        public string referencia_externa {get; set;} = string.Empty;

        public DateTime fecha_pago {get; set;}
        public DateTime creado_en {get; set;}
    }
}