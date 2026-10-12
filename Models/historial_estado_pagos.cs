namespace fvm.models
{
    public class HistorialEstadoPago
    {
        public ulong id {get; set;}
        public ulong id_pago {get; set;}
        public ushort id_estado_pago {get; set;}

        public DateTime fecha_cambio {get; set;}
        public string detalle {get; set;} = string.Empty;
    }
}