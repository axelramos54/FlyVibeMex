namespace fvm
{
    public class Proveedores
    {
        public ulong id {get; set;}
        public ulong id_ubicacion {get; set;}

        public string tipo_proveedor {get; set;} = string.Empty;
        public string nombre {get; set;} = string.Empty;
        public string telefono {get; set;} = string.Empty;
        public string correo {get; set;} = string.Empty;
        public bool activo {get; set;}
        public DateTime creado_en {get; set;}
        public DateTime actualizado_en {get; set;}
    }
}