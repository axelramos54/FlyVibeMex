namespace fvm.models
{
    public class Reembolsos
    {
        public ulong id {get; set;}
        public ulong id_pago {get; set;}

        public decimal monto {get; set;}
        public string motivo {get; set;} = string.Empty;
        public string estado {get; set;} = string.Empty;
        public DateTime fecha_solicitud {get; set;}
        public DateTime fecha_resolucion {get; set;}
        public string referencia_externa {get; set;} = string.Empty:
    }
}