namespace fvm
{
    public class DisponibilidadServicio
    {
        public ulong id {get; set;}
        public ulong id_servicio {get; set;}

        public DateOnly fecha {get; set;}
        public int cantidad_total {get; set;}
        public int cantidad_disponible {get; set;}
        public decimal precio_fecha {get; set;}
        public DateTime actualizado_en {get; set;}
    }
}