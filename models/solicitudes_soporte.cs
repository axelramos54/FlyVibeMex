namespace fvm.models
{
    public class SolicitudesSoporte
    {
        public ulong id {get; set;}
        public ulong id_usuario {get; set;}
        public ulong id_reservacion {get; set;}
        public ulong id_agente_asignado {get; set;}

        public string tipo {get; set;} = string.Empty;
        public string asunto {get; set;} = string.Empty;
        public string mensaje {get; set;} = string.Empty;
        public string estado {get; set;} = string.Empty;
        public DateTime creada_en {get; set;}
        public DateTime actualizada_en {get; set;}
    }
}