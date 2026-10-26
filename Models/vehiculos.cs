namespace FlyVibeMex.Models
{
    /// <summary>
    /// <b>Subtipo VEHICULO</b> de <c>Servicios</c> datos específicos del alquiler de vehículos.
    /// </summary>
    // NOTA: igual que Hospedajes, debería identificarse por `id_servicio` (PK y FK hacia Servicios, 1:1)
    // en lugar de un `id` propio.
    // NOTA: falta `transmision`, que aparece en el diagrama entidad-relación.
    public class Vehiculos
    {
        /// <summary>Llave primaria (identificador único autoincremental).</summary>
        // NOTA: debería ser `id_servicio` (ver nota de la clase). // Solucionado 21-10-2026 Por Yamil
        public ulong id_servicio {get; set;}

        /// <summary>Llave foránea → <c>Ubicaciones.id</c>. Punto o ciudad de retiro del vehículo.</summary>
        public ulong id_ubicacion_retiro {get; set;}

        /// <summary>Categoría del vehículo (p. ej. compacto, SUV).</summary>
        public string categoria {get; set;} = string.Empty;

        /// <summary>Marca y modelo del vehículo.</summary>
        public string marca {get; set;} = string.Empty;
        public string modelo {get; set;} = string.Empty;

        /// <summary>Capacidad de pasajeros.</summary>
        public byte capacidad_pasajeros {get; set;}

        /// <summary>Ruta pública de la imagen (opcional), p. ej. <c>/uploads/vehiculos/&lt;guid&gt;.jpg</c>.</summary>
        public string? imagen_ruta {get; set;}
    }
}