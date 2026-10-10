namespace fvm
{
    public class Ubicaciones
    {
        public ulong id { get; set; }

        public string pais { get; set; } = string.Empty;
        public string estado { get; set; } = string.Empty;
        public string municipio_ciudad { get; set; } = string.Empty;
        public string codigo_postal { get; set; } = string.Empty;
        public string correo { get; set; } = string.Empty;
        public bool activo {get; set;}
        public string creado {get; set;} = string.Empty;
        public string actualizado { get; set; } = string.Empty;
    }
}