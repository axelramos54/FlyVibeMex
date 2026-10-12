namespace fvm
{
    public class Reservaciones
    {
        public ulong id {get; set;}
        public ulong id_usuario {get; set;}
        public ushort id_estado_reservacion {get; set;}

        public string codigo_reserva {get; set;} = string.Empty;
        public string moneda {get; set;} = string.Empty;
        public decimal subtotal {get; set;}
        public decimal descuento_total {get; set;}
        public decimal total {get; set;}
        public DateTime creada_en {get; set;}
    }
}