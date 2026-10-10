namespace fvm
{
    public class EstadosReservacion
    {
        public ushort id {get; set;}

        public string nombre {get; set;} = string.Empty;
        public string descripcion {get; set;} = string.Empty;
        public bool es_final {get; set;}

    }
}