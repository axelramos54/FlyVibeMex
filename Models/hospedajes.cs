namespace FlyVibeMex.Models
{
    /// <summary>
    /// <b>Subtipo HOSPEDAJE</b> de <c>Servicios</c> datos específicos de una oferta de hospedaje;
    /// cada registro representa una modalidad de habitación asociada a un servicio.
    /// </summary>
    // NOTA: según el diseño, esta entidad se identifica por `id_servicio` (llave primaria y foránea hacia
    // Servicios, relación 1:1). Aquí tiene un `id` propio y no hay propiedad que la ligue a su servicio,
    // así que no se puede saber a qué servicio/proveedor/precio pertenece un hospedaje.
    public class Hospedajes
    {
        /// <summary>Llave primaria (identificador único autoincremental).</summary>
        // NOTA: debería ser `id_servicio` (ver nota de la clase).
        public ulong id { get; set; }

        /// <summary>Llave foránea → <c>Ubicaciones.id</c>. Ubicación del hospedaje.</summary>
        public ulong id_ubicacion {get; set;}

        // NOTA: en el diseño este campo es `tipo_habitacion` (tipo de habitación u oferta).
        public string tipo_ubicacion {get; set;} = string.Empty;

        /// <summary>Categoría comercial del alojamiento.</summary>

        public string categoria {get; set; } = string.Empty;

        /// <summary>Capacidad máxima de huéspedes de la oferta.</summary>
        public int capacidad_huespedes {get; set;}

        /// <summary>Hora estándar de check-in.</summary>
        public TimeOnly hora_entrada {get; set;}

        /// <summary>Hora estándar de check-out.</summary>
        public TimeOnly hora_salida {get; set;}

        /// <summary>Imagen del hospedaje (opcional; agregada por el equipo, no está en el diccionario del PDF).</summary>
        public byte[]? imagen {get; set;}
    }
}