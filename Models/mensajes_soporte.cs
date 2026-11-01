namespace FlyVibeMex.Models
{
    /// <summary>
    /// Un mensaje de la conversación de un ticket (tabla <c>mensajes_soporte</c>, T4 fase C).
    /// El primer mensaje del ticket es <see cref="SolicitudesSoporte.mensaje"/>; aquí van las respuestas en orden.
    /// </summary>
    public class MensajesSoporte
    {
        public ulong id {get; set;}

        /// <summary>Ticket al que pertenece (solicitudes_soporte.id).</summary>
        public ulong id_solicitud {get; set;}

        /// <summary>Quién lo escribió (el cliente dueño del ticket o el agente que lo atiende).</summary>
        public ulong id_usuario {get; set;}

        /// <summary>true = lo escribió personal de FlyVibeMex; false = el cliente.</summary>
        public bool es_personal {get; set;}

        public string mensaje {get; set;} = string.Empty;

        public DateTime creado_en {get; set;}
    }
}
