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

        /// <summary>Cliente que recibió el aviso (usuarios.id).</summary>
        public ulong id_usuarioglobal {get; set;}

        /// <summary>RESERVACION, REEMBOLSO, RECORDATORIO_72 o RECORDATORIO_24 (ver <c>Services.TiposAvisoTelegram</c>).</summary>
        public string tipo {get; set;} = string.Empty;

        /// <summary>
        /// historial_estado_reservacion.id (RESERVACION), historial_estado_reembolso.id (REEMBOLSO)
        /// o reservaciones.id (recordatorios).
        /// </summary>
        public ulong id_referencia {get; set;}

        public DateTime enviado_en {get; set;}
    }
}
