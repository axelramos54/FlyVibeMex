namespace FlyVibeMex.Models
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
        // NOTA: identificador redundante (ver nota de la clase). // Solucionado 21-10-2026 Por Yamil
        // public ulong id {get; set;}

        /// <summary>Llave foránea → <c>Servicios.id</c>. Servicio (tipo PAQUETE) al que corresponde este paquete.</summary>
        public ulong id_servicio {get; set;}

        /// <summary>Duración estimada del paquete en días.</summary>
        public ushort duracion_dias {get; set;}

        /// <summary>Mínimo de personas para contratar el paquete.</summary>
        public ushort min_personas {get; set;}

        /// <summary>Máximo de personas, si aplica.</summary>
        // NOTA: opcional en el diseño.
        public ushort max_personas {get; set;}

        // NOTA: la política de cancelación ya no está aquí; es común a todos los servicios
        //            (Servicios.permite_cancelacion, horas_reembolso_total, porcentaje_reembolso_tardio, politica_cancelacion).
        
        /// <summary>Ruta pública de la imagen (opcional), p. ej. <c>/uploads/paquetes/&lt;guid&gt;.jpg</c>.</summary>
        public string? imagen_ruta {get; set;}
    }
}