// NOTA: [NAMESPACE] Esta clase está en el namespace `fvm`; el estándar acordado es `FlyVibeMex.Models`.
// Views/_ViewImports.cshtml solo importa FlyVibeMex.Models, así que esta clase no es visible en vistas ni
// controladores sin un `using fvm;` adicional. No se cambia aquí para no alterar la compilación actual.
namespace fvm
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