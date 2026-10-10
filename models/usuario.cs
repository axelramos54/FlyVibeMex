namespace fvm
{
    public class Usuarios
    {
        public ulong id {get; set;}
        public ulong id_rol {get; set;}
        
        public string nombre {get; set;} = string.Empty;
        public string apellido_paterno {get; set;} = string.Empty;
        public string apellido_materno {get; set;} = string.Empty;
        public string correo {get; set;} = string.Empty;
        public string telefono {get; set;} = string.Empty;
        public string password_hash {get; set;} = string.Empty;
        public bool activo {get; set;}
        public DateTime creado_en {get; set;};
        public byte[]? imagen {get; set;} //imagen
    }
}