namespace FlyVibeMex.Services;

/// <summary>
/// Estado del bot de Telegram que la web necesita conocer (singleton): si está configurado en este servidor y con qué
/// nombre de usuario (@...) contestó Telegram al arrancar. Lo actualiza <c>Bot/TelegramBotWorker</c>.
/// </summary>
/// <remarks>
/// Mi cuenta › Telegram lo usa para armar el enlace directo <c>https://t.me/&lt;usuario&gt;?start=v_CODIGO</c> y para
/// avisar si el bot no está activo. Si el bot está desactivado (sin token), la web funciona igual.
/// </remarks>
public sealed class EstadoBotTelegram
{
    private volatile string? _usuarioConectado;

    public EstadoBotTelegram(bool configurado, string? nombreUsuarioConfigurado)
    {
        Configurado = configurado;
        NombreUsuarioConfigurado = string.IsNullOrWhiteSpace(nombreUsuarioConfigurado)
            ? null
            : nombreUsuarioConfigurado.Trim().TrimStart('@');
    }

    /// <summary><c>TelegramBot:Habilitado</c> = true y hay token: el bot se inicia con la app.</summary>
    public bool Configurado { get; }

    /// <summary>Respaldo de <c>TelegramBot:NombreUsuarioBot</c> (por si Telegram aún no responde).</summary>
    public string? NombreUsuarioConfigurado { get; }

    /// <summary><c>true</c> mientras el bot está conectado a Telegram (recibiendo mensajes).</summary>
    public bool Conectado => _usuarioConectado is not null;

    /// <summary>Usuario del bot sin @ (el que respondió Telegram, o el configurado).</summary>
    public string? NombreUsuario => _usuarioConectado ?? NombreUsuarioConfigurado;

    public void MarcarConectado(string? nombreUsuario) => _usuarioConectado = string.IsNullOrWhiteSpace(nombreUsuario) ? "bot" : nombreUsuario;

    public void MarcarDesconectado() => _usuarioConectado = null;

    /// <summary>Enlace directo al bot; con <paramref name="parametroInicio"/> abre el chat y le manda /start PARAMETRO.</summary>
    public string? Enlace(string? parametroInicio = null) =>
        NombreUsuario is null
            ? null
            : $"https://t.me/{NombreUsuario}" + (parametroInicio is null ? "" : $"?start={Uri.EscapeDataString(parametroInicio)}");
}
