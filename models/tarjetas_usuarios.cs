namespace fvm.models
{
    public class TarjetasUsuarios
    {
        public ulong id {get; set;}
        public ulong id_usuario {get; set;}
        public ulong id_institucion {get; set;}

        public string token_referencia {get; set;} = string.Empty;
        public string marca {get; set;} = string.Empty;
        public string ultimos4 {get; set;} = string.Empty;
        public byte mes_expiracion {get; set;}
        public ushort anio_expiracion {get; set;}
        public string alias {get; set;} = string.Empty;
        public bool activo {get; set;}
        public DateTime creado_en {get; set;}
    }
}