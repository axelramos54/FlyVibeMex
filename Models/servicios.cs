namespace fvm
{
    public class Servicios
    {
        public ulong id {get; set;}
        public ulong id_proveedor {get; set;}

        public string tipo_servicio {get; set;} = string.Empty;
        public string nombre {get; set;} = string.Empty;
        public string descripcion {get; set;} = string.Empty;
        public decimal precio_base {get; set;}
        public string moneda {get; set;} = string.Empty;
        public bool activo {get; set;}
        public DateTime creado_en {get; set;}
        public DateTime actualizado_en {get; set;}
    }
}