namespace fvm.models
{
    public class HistorialEstadoReembolso
    {
        public ulong id {get; set;}
        public ulong id_reembolso {get; set;}
        public ulong id_usuario_cambio {get; set;}

        public string estado {get; set;} = string.Empty;
        public DateTime fecha_cambio {get; set;}
        public string detalle {get; set;} = string.Empty;
    }
}