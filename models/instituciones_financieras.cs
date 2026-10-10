namespace fvm.models
{
    public class InstitucionesFinancieras
    {
        public ulong id {get; set;}

        public string nombre {get; set;} = string.Empty;
        public string clave_institucion {get; set;} = string.Empty;
        public string codigo_internacional {get; set;} = string.Empty;
        public string telefono {get; set;} = string.Empty;
        public bool activo {get; set;}

    }
}