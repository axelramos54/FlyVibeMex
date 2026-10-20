namespace FlyVibeMex.Models
{
    /// <summary>
    /// Tabla puente (muchos a muchos) entre <c>Promociones</c> y <c>Servicios</c>: indica a qué servicios
    /// aplica cada promoción. La clave es la combinación (id_promocion, id_servicio).
    /// </summary>
    public class PromocionServicio
    {
        /// <summary>Llave foránea → <c>Promociones.id</c>. Parte de la clave compuesta.</summary>
        public ulong id_promocion {get; set;}

        /// <summary>Llave foránea → <c>Servicios.id</c>. Parte de la clave compuesta.</summary>
        public ulong id_servicio {get; set;}
    }
}