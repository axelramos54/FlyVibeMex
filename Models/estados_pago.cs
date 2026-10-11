namespace fvm.models
{
    public class EstadosPago
    {
        public ushort id {get; set;}

        public string nombre {get; set;} = string.Empty;
        public string descripcion {get; set;} = string.Empty;
        public byte es_final {get; set;}
    }
}