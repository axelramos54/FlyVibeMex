// NOTA: [NAMESPACE] Esta clase está en el namespace `fvm`; el estándar acordado es `FlyVibeMex.Models`.
// Views/_ViewImports.cshtml solo importa FlyVibeMex.Models, así que esta clase no es visible en vistas ni
// controladores sin un `using fvm;` adicional. No se cambia aquí para no alterar la compilación actual.
namespace fvm
{
    /// <summary>
    /// <b>Subtipo PAQUETE</b> de <c>Servicios</c> un paquete es un servicio compuesto por otros
    /// servicios (vuelo, hospedaje, vehículo…) a través de <c>PaqueteServicio</c>.
    /// La política de cancelación debe mostrarse al cliente ANTES de pagar.
    /// </summary>
    // NOTA: en el diseño la identidad del paquete es solo `id_servicio` (PK y FK hacia Servicios);
    // la propiedad `id` adicional sobra y permitiría varios paquetes por servicio.
    public class Paquetes
    {
        // NOTA: identificador redundante (ver nota de la clase).
        public ulong id {get; set;}

        /// <summary>Llave foránea → <c>Servicios.id</c>. Servicio (tipo PAQUETE) al que corresponde este paquete.</summary>
        public ulong id_servicio {get; set;}

        /// <summary>Duración estimada del paquete en días.</summary>
        public ushort duracion_dias {get; set;}

        /// <summary>Mínimo de personas para contratar el paquete.</summary>
        public ushort min_personas {get; set;}

        /// <summary>Máximo de personas, si aplica.</summary>
        // NOTA: opcional en el diseño.
        public ushort max_personas {get; set;}

        /// <summary>Condiciones de cancelación.</summary>
        // NOTA: opcional en el diseño.
        public string politica_cancelacion {get; set;} = string.Empty;
        
        /// <summary>Imagen del paquete (opcional; agregada por el equipo).</summary>
        public byte[]? imagen {get; set;} //imagen
    }
}