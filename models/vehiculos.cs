namespace fvm
{
    public class Vehiculos
    {
        public ulong id {get; set;}
        public ulong id_ubicacion_retiro {get; set;}

        public string categoria {get; set;} = string.Empty;
        public string marca {get; set;} = string.Empty;
        public string modelo {get; set;} = string.Empty;
        public byte capacidad_pasajeros {get; set;}
        public byte[]? imagen {get; set;} //imagen
    }
}