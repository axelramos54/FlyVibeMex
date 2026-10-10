namespace fvm
{
    public class Hospedajes
    {
        public ulong id { get; set; }
        public ulong id_ubicacion {get; set;}

        public string tipo_ubicacion {get; set;} = string.Empty;
        public string categoria {get; set; } = string.Empty;
        public int capacidad_huespedes {get; set;}
        public TimeOnly hora_entrada {get; set;};
        public TimeOnly hora_salida {get; set;};
        public byte[]? imagen {get; set;} //imagen
    }
}