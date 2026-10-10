namespace fvm
{
    public class Paquetes
    {
        public ulong id {get; set;}
        public ulong id_servicio {get; set;}

        
        public ushort duracion_dias {get; set;}
        public ushort min_personas {get; set;}
        public ushort max_personas {get; set;}
        public string politica_cancelacion {get; set;} = string.Empty;
        public byte[]? imagen {get; set;} //imagen
    }
}