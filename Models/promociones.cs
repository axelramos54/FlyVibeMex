namespace fvm
{
    public class Promociones
    {
        public ulong id {get; set;}

        public string codigo {get; set;} = string.Empty;
        public string descripcion {get; set;} = string.Empty;
        public string tipo_descuento {get; set;} = string.Empty;
        public decimal valor {get; set;}
        public DateTime fecha_inicio {get; set;}
        public DateTime fecha_fin {get; set;}
        public bool activo {get; set;}
    }
}