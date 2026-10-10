namespace fvm
{
    public class MetodosPago
    {
        public ushort id {get; set;}

        public string nombre {get; set;} = string.Empty;
        public string descripcion {get; set;} = string.Empty;
        public bool activo {get; set;}
    }
}