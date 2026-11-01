using FlyVibeMex.Services;

namespace FlyVibeMex.Models
{
    /// <summary>Pantalla Mi cuenta › Telegram (/Miusuario/Telegram): vincular la cuenta con el bot (docs/06).</summary>
    public class TelegramCuentaViewModel
    {
        public ContextoCuenta Contexto { get; set; } = new();

        /// <summary>El bot está configurado en este servidor (TelegramBot:Habilitado + token).</summary>
        public bool BotConfigurado { get; set; }

        /// <summary>El bot está conectado a Telegram en este momento.</summary>
        public bool BotConectado { get; set; }

        /// <summary>Usuario del bot sin @ (null si aún no se conoce).</summary>
        public string? NombreUsuarioBot { get; set; }

        /// <summary>Enlace al chat del bot (https://t.me/usuario).</summary>
        public string? EnlaceBot { get; set; }

        /// <summary>Fecha en que se vinculó (null = no vinculada).</summary>
        public DateTime? VinculadoDesde { get; set; }
        public bool Vinculado => VinculadoDesde is not null;

        /// <summary>Código recién generado (solo en la respuesta del POST; nunca se vuelve a mostrar).</summary>
        public CodigoVinculacionTelegram? Codigo { get; set; }

        /// <summary>Enlace que abre el bot y le manda el código solo: https://t.me/usuario?start=v_CODIGO</summary>
        public string? EnlaceConCodigo { get; set; }

        /// <summary>Minutos que dura un código.</summary>
        public int MinutosVigencia => (int)VinculacionTelegramService.VigenciaCodigo.TotalMinutes;
    }
}
