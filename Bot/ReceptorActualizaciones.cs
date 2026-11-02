using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;

namespace FlyVibeMex.Bot;

/// <summary>
/// Recibe cada actualización de Telegram (mensaje o botón) y la pasa a <see cref="ManejadorActualizaciones"/> dentro
/// de un <b>scope de DI nuevo</b>, igual que una petición web: así cada mensaje tiene su propio
/// <c>FlyVibeMexDbContext</c> y sus servicios Scoped (catálogo, reservaciones, soporte).
/// </summary>
/// <remarks>
/// IMPORTANTE (Telegram.Bot 22): si este método deja escapar un <see cref="OperationCanceledException"/>, el ciclo de
/// polling termina. Por eso todo error (incluido un timeout de HTTP, que también es una cancelación) se atrapa aquí,
/// salvo cuando de verdad se está deteniendo la app.
/// </remarks>
public sealed class ReceptorActualizaciones : IUpdateHandler
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ReceptorActualizaciones> _logger;

    public ReceptorActualizaciones(IServiceScopeFactory scopes, ILogger<ReceptorActualizaciones> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var manejador = scope.ServiceProvider.GetRequiredService<ManejadorActualizaciones>();
            await manejador.ManejarAsync(update, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;   // la app se está deteniendo
        }
        catch (Exception ex)
        {
            // Solo metadatos: nunca el texto del mensaje (puede traer correos, teléfonos o algo peor).
            _logger.LogError(ex, "Bot de Telegram: error al procesar el update {IdUpdate} ({Tipo}).", update.Id, update.Type);
            await AvisarErrorAsync(botClient, update, cancellationToken);
        }
    }

    public async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, HandleErrorSource source,
        CancellationToken cancellationToken)
    {
        if (exception is ApiRequestException { ErrorCode: 409 })
            _logger.LogError("Bot de Telegram: otro proceso está usando el MISMO token (409 Conflict). " +
                             "Cada integrante debe usar su propio bot de prueba (docs/06).");
        else if (exception is RequestException and not ApiRequestException)
            // Sin internet, DNS caído, conexión cortada o timeout: es pasajero y el polling se recupera solo.
            // Una sola línea (sin la pila completa) para no llenar la consola; el mensaje nunca incluye el token.
            _logger.LogWarning("Bot de Telegram: sin conexión con Telegram ({Detalle}). Se reintenta en 5 s.",
                exception.InnerException?.Message ?? exception.Message);
        else
            _logger.LogWarning(exception, "Bot de Telegram: error de {Origen}.", source);

        // Sin esta pausa, un error de red haría que el ciclo de polling reintente sin parar.
        if (source == HandleErrorSource.PollingError)
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
    }

    private async Task AvisarErrorAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        var chatId = update.Message?.Chat.Id ?? update.CallbackQuery?.Message?.Chat.Id ?? update.CallbackQuery?.From.Id;
        if (chatId is null or <= 0) return;   // solo chats privados
        try
        {
            // Siempre con salida: "🔁 Reintentar" repite el MISMO botón (es seguro: "Enviar" de un ticket no lo duplica,
            // porque el flujo se borra antes de crearlo). Un texto escrito no se puede repetir (el bot no lo guarda).
            var teclado = new Teclado();
            if (update.CallbackQuery is { } boton)
            {
                // Normalmente el manejador ya contestó el botón; si se intenta otra vez Telegram responde 400, y eso
                // no debe impedir que llegue el mensaje de error.
                try
                {
                    await botClient.AnswerCallbackQuery(boton.Id, cancellationToken: cancellationToken);
                }
                catch (ApiRequestException)
                {
                }
                if (!string.IsNullOrEmpty(boton.Data)) teclado.Boton("🔁 Reintentar", boton.Data);
            }
            teclado.Navegacion();

            await botClient.SendMessage(chatId.Value,
                "Ups, algo salió mal de nuestro lado. Intenta de nuevo en un momento.",
                replyMarkup: teclado.Crear(), cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "Bot de Telegram: no se pudo avisar del error al chat.");
        }
    }
}
