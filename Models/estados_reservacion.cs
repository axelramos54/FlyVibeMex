namespace FlyVibeMex.Models
{
    /// <summary>
    /// Catálogo de estados válidos de una reservación 
    /// Un estado con <c>es_final = true</c> ya no admite transiciones (p. ej. cancelada o completada).
    /// </summary>
    public class EstadosReservacion
    {
        /// <summary>Llave primaria (identificador único autoincremental).</summary>
        public ushort id {get; set;}

        /// <summary>Nombre único del estado.</summary>
        /// 
        public string nombre {get; set;} = string.Empty;
        
        /// <summary>Descripción del estado.</summary>
        public string descripcion {get; set;} = string.Empty;

        /// <summary>Indica si ya no se esperan transiciones desde este estado.</summary>
        public bool es_final {get; set;}
    }
}