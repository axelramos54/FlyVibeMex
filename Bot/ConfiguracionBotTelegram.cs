using FlyVibeMex.Services;
using Telegram.Bot;

namespace FlyVibeMex.Bot;

/// <summary>Registro del bot de Telegram en Program.cs: <c>var opcionesBot = builder.Services.AgregarBotTelegram(builder.Configuration);</c></summary>
public static class ConfiguracionBotTelegram
{
    /// <summary>
    /// Registra lo que la web necesita SIEMPRE (vinculación, estado del bot, consultas) y, solo si el bot está
    /// configurado (<see cref="TelegramBotOpciones.EstaConfigurado"/>), el cliente de Telegram y los servicios en
    /// segundo plano. Sin token, la app arranca igual que antes.
    /// </summary>
    public static TelegramBotOpciones AgregarBotTelegram(this IServiceCollection services, IConfiguration configuracion)
    {
        var seccion = configuracion.GetSection(TelegramBotOpciones.Seccion);
        services.Configure<TelegramBotOpciones>(seccion);
        var opciones = seccion.Get<TelegramBotOpciones>() ?? new TelegramBotOpciones();

        // Siempre: Mi cuenta › Telegram y los servicios de soporte los usan aunque el bot esté apagado.
        services.AddSingleton(new EstadoBotTelegram(opciones.EstaConfigurado, opciones.NombreUsuarioBot));
        services.AddScoped<IVinculacionTelegramService, VinculacionTelegramService>();
        services.AddScoped<IConsultasTelegramService, ConsultasTelegramService>();

        if (!opciones.EstaConfigurado)
        {
            services.AddSingleton<IAvisosTelegram, AvisosTelegramDesactivados>();
            return opciones;
        }

        // Cliente único de Telegram. Se crea SIN IHttpClientFactory a propósito: su log de nivel Information
        // escribiría la URL https://api.telegram.org/bot<TOKEN>/... en la consola.
        var token = opciones.Token!.Trim();
        services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(token));

        services.AddSingleton<IEstadoConversacionStore, EstadoConversacionEnMemoria>();
        services.AddSingleton<ILimitadorIntentos, LimitadorIntentosEnMemoria>();
        services.AddSingleton<FotosServicio>();

        services.AddSingleton<ColaAvisosTelegram>();
        services.AddSingleton<IAvisosTelegram>(sp => sp.GetRequiredService<ColaAvisosTelegram>());
        services.AddHostedService<DespachadorAvisosTelegram>();

        services.AddScoped<ManejadorActualizaciones>();   // uno por mensaje (scope nuevo, como una petición web)
        services.AddSingleton<ReceptorActualizaciones>();
        services.AddHostedService<TelegramBotWorker>();
        return opciones;
    }
}
