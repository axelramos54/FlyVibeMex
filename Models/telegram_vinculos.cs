namespace FlyVibeMex.Models
{
    /// <summary>
    /// Vínculo entre una cuenta de CLIENTE y su chat privado con el bot de Telegram (tabla <c>telegram_vinculos</c>,
    /// docs/06). Lo crea el bot con el código de un solo uso que el cliente genera en Mi cuenta › Telegram.
    /// Solo puede haber un vínculo ACTIVO por cuenta y uno por chat (índices únicos filtrados en el script 01).
    /// </summary>
    public class TelegramVinculos
    {
        public ulong id {get; set;}

        /// <summary>Cuenta del cliente (usuarios.id).</summary>
        public ulong id_usuarioglobal {get; set;}

        /// <summary>
        /// Id del chat privado con el bot (en un chat privado coincide con el id del usuario de Telegram).
        /// Es <c>long</c> (con signo) porque así lo define Telegram; los grupos tienen id negativo y aquí no se aceptan.
        /// </summary>
        public long chat_id {get; set;}

        /// <summary>false = se desvinculó (desde la web, desde el bot o al vincular otra cuenta / otro chat).</summary>
        public bool activo {get; set;}

        public DateTime vinculado_en {get; set;}

        public DateTime? desvinculado_en {get; set;}
    }
}
