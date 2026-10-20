namespace FlyVibeMex.Models
{
    /// <summary>
    /// Catálogo opcional de bancos o instituciones emisoras asociadas a las tarjetas tokenizadas.
    /// </summary>
    public class InstitucionesFinancieras
    {
        /// <summary>Llave primaria (identificador único autoincremental).</summary>
        public ulong id {get; set;}

        /// <summary>Nombre de la institución.</summary>
        public string nombre {get; set;} = string.Empty;

        /// <summary>Clave nacional, cuando aplique (única).</summary>
        // NOTA: `clave_institucion`, `codigo_internacional` y `telefono` son opcionales en el diseño.
        public string clave_institucion {get; set;} = string.Empty;

        /// <summary>Código internacional, cuando aplique (único).</summary>
        public string codigo_internacional {get; set;} = string.Empty;

        /// <summary>Teléfono de referencia.</summary>
        public string telefono {get; set;} = string.Empty;

        /// <summary>Borrado lógico <c>false</c> oculta el registro sin eliminarlo.</summary>
        public bool activo {get; set;}

    }
}