namespace fvm
{
    public class PaqueteServicio
    {
        public ulong id {get; set;}
        public ulong id_servicio {get; set;}
        public ulong id_servicio_incluido {get; set;}

        public decimal cantidad {get; set;}
        public ushort orden {get; set;}
    }
}