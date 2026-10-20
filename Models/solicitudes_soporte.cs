// NOTA: [NAMESPACE] Esta clase está en el namespace `fvm.models`; el estándar acordado es `FlyVibeMex.Models`.
// Views/_ViewImports.cshtml solo importa FlyVibeMex.Models, así que esta clase no es visible en vistas ni
// controladores sin un `using fvm.models;` adicional. No se cambia aquí para no alterar la compilación actual.
namespace fvm.models
{
    /// <summary>
    /// Ticket de soporte unificado, cubre "Contactar a un asesor" (tipo ASESORIA) y
    /// "Reportar un problema" (tipo PROBLEMA) en una sola entidad, opcionalmente ligada a una reservación
    /// y asignada a un agente. Estados: ABIERTA → EN_PROCESO → CERRADA.
    /// </summary>
    // NOTA: punto de integración previsto para el bot (etapa 7 del cronograma): el bot crea solicitudes y el
    // escalamiento a humano se hace asignando id_agente_asignado y pasando a EN_PROCESO.
    public class SolicitudesSoporte
    {
        /// <summary>Llave primaria (identificador único autoincremental).</summary>
        public ulong id {get; set;}

        /// <summary>Llave foránea → <c>Usuarios.id</c>. Usuario que abre la solicitud.</summary>
        public ulong id_usuario {get; set;}

        /// <summary>Llave foránea → <c>Reservaciones.id</c>. Reserva relacionada, si aplica.</summary>
        // NOTA: opcional en el diseño.
        public ulong id_reservacion {get; set;}

        /// <summary>Llave foránea → <c>Usuarios.id</c>. Agente responsable (rol AGENTE).</summary>
        // NOTA: vacío hasta asignarse.
        public ulong id_agente_asignado {get; set;}

        /// <summary>ASESORIA o PROBLEMA.</summary>
        public string tipo {get; set;} = string.Empty;

        /// <summary>Asunto breve.</summary>
        public string asunto {get; set;} = string.Empty;

        /// <summary>Descripción de la solicitud.</summary>
        public string mensaje {get; set;} = string.Empty;

        /// <summary>ABIERTA, EN_PROCESO o CERRADA.</summary>
        public string estado {get; set;} = string.Empty;

        /// <summary>Fecha y hora de alta del registro (campo de control).</summary>
        public DateTime creada_en {get; set;}
        
        /// <summary>Fecha y hora de la última actualización (campo de control).</summary>
        public DateTime actualizada_en {get; set;}
    }
}