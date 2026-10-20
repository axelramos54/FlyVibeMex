namespace FlyVibeMex.Models
{
    /// <summary>
    /// Catálogo de formas de pago aceptadas (tarjeta de crédito, débito, transferencia…). Se separa de los bancos
    /// (<c>InstitucionesFinancieras</c>) porque un método de pago no equivale a una institución financiera.
    /// </summary>
    public class MetodosPago
    {
        /// <summary>Llave primaria (identificador único autoincremental).</summary>
        public ushort id {get; set;}

        /// <summary>Nombre único del método.</summary>
        public string nombre {get; set;} = string.Empty;

        /// <summary>Descripción del método.</summary>

        public string descripcion {get; set;} = string.Empty;
        
        /// <summary>Borrado lógico <c>false</c> oculta el registro sin eliminarlo.</summary>
        public bool activo {get; set;}
    }
}