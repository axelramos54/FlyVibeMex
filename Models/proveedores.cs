namespace FlyVibeMex.Models
{
    /// <summary>
    /// Empresa que suministra servicios turísticos: aerolíneas, hoteles, rentadoras de vehículos u otros.
    /// Los proveedores NO son usuarios del sistema; cada <c>Servicios</c> pertenece a un proveedor.
    /// FlyVibe Mex puede registrarse como proveedor interno para los paquetes que arma la agencia.
    /// </summary>
    public class Proveedores
    {
        /// <summary>Llave primaria (identificador único autoincremental).</summary>
        public ulong id {get; set;}

        /// <summary>Llave foránea → <c>Ubicaciones.id</c>. Ubicación principal o fiscal.</summary>
        // NOTA: en el diseño es opcional.
        public ulong? id_ubicacion {get; set;}

        /// <summary>Tipo de proveedor: AEROLINEA, HOTEL, RENTADORA u OTRO.</summary>
        public string tipo_proveedor {get; set;} = string.Empty;
        public string nombre {get; set;} = string.Empty;

        /// <summary>Teléfono de contacto (único).</summary>
        public string telefono {get; set;} = string.Empty;

        /// <summary>Correo de contacto (único).</summary>
        public string correo {get; set;} = string.Empty;

        /// <summary>Borrado lógico <c>false</c> oculta el registro sin eliminarlo.</summary>
        public bool activo {get; set;}

        /// <summary>Fecha y hora de alta del registro (campo de control).</summary>
        public DateTime creado_en {get; set;}
        
        /// <summary>Fecha y hora de la última actualización (campo de control).</summary>
        public DateTime actualizado_en {get; set;}
    }
}