namespace FlyVibeMex.Models
{
    /// <summary>
    /// Aviso automático que el bot de Telegram ya mandó (tabla <c>telegram_avisos</c>): confirmación de compra,
    /// cancelación, cambio de reembolso o recordatorio 72 h / 24 h antes del viaje. El índice único
    /// (tipo, id_referencia) del script 01 impide mandar el mismo aviso dos veces, aunque la app se reinicie.
    /// Solo guarda metadatos, nunca el texto del mensaje.
    /// </summary>
    public class TelegramAvisos
    {
        public ulong id {get; set;}

        /// <summary>Cliente que recibió el aviso (usuarios.id). NULL en avisos al grupo de agentes que no son de un cliente.</summary>
        public ulong? id_usuarioglobal {get; set;}

        /// <summary>Ver <c>Services.TiposAvisoTelegram</c> (RESERVACION, REEMBOLSO, RECORDATORIO_72/24, TICKET_GRUPO…).</summary>
        public string tipo {get; set;} = string.Empty;

        /// <summary>
        /// historial_estado_reservacion.id (RESERVACION), historial_estado_reembolso.id (REEMBOLSO), reservaciones.id
        /// (recordatorios), solicitudes_soporte.id (TICKET_GRUPO, URGENTE_SIN_AGENTE) o la fecha AAAAMMDD (RESUMEN_DIARIO).
        /// </summary>
        public ulong id_referencia {get; set;}

        /// <summary>Solo TICKET_GRUPO: chat del grupo donde quedó el mensaje del ticket (para editarlo).</summary>
        public long? chat_id {get; set;}

        /// <summary>Solo TICKET_GRUPO: id del mensaje del ticket en ese grupo.</summary>
        public int? id_mensaje {get; set;}

        public DateTime enviado_en {get; set;}
    }
}
