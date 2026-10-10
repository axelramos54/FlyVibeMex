namespace fvm
{
    public class DetallesReservacion
    {
        public ulong id {get; set;}
        public ulong id_reservacion {get; set;}
        public ulong id_servicio {get; set;}
        public ulong id_promocion {get; set;}

        public decimal cantidad {get; set;}
        public DateTime fehca_inicio {get; set;}
        public DateTime fecha_fin {get; set;}
        public decimal precio_unitario {get; set;}
        public decimal descuento_aplicado {get; set;}
        public decimal subtotal {get; set;}
        public string descripcion_snapshot {get; set;} = string.Empty;
    }
}