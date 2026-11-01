using FlyVibeMex.Services;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

namespace FlyVibeMex.Bot;

/// <summary>
/// Servicio en segundo plano que conecta el bot con Telegram por <b>long polling</b> (docs/06, sección 2.8):
/// la app le pregunta a Telegram "¿hay mensajes nuevos?", así que funciona en localhost:5106 / 7045 sin dominio
/// público ni túnel HTTPS. Corre dentro del mismo proceso que la web (mismo Program.cs, misma BD y servicios).
/// </summary>
/// <remarks>
/// - Un token solo admite UN proceso leyendo mensajes: si dos integrantes corren la app con el mismo token, Telegram
///   responde 409 Conflict. Cada quien crea su bot de prueba en @BotFather (ver docs/06).
/// - Si se pierde la conexión, reintenta con espera creciente (5 s, 10 s… hasta 1 min). Con un token inválido (401)
///   se detiene y lo dice en el log; la web sigue funcionando.
/// </remarks>
public sealed class TelegramBotWorker : BackgroundService
{
    private readonly ITelegramBotClient _bot;
    private readonly ReceptorActualizaciones _receptor;
    private readonly EstadoBotTelegram _estado;
    private readonly ILogger<TelegramBotWorker> _logger;

    public TelegramBotWorker(ITelegramBotClient bot, ReceptorActualizaciones receptor, EstadoBotTelegram estado,
        ILogger<TelegramBotWorker> logger)
    {
        _bot = bot;
        _receptor = receptor;
        _estado = estado;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken detener)
    {
        var espera = TimeSpan.FromSeconds(5);
        while (!detener.IsCancellationRequested)
        {
            try
            {
                var yo = await _bot.GetMe(detener);

                // Long polling y webhook no pueden convivir: se borra cualquier webhook anterior.
                await _bot.DeleteWebhook(dropPendingUpdates: true, cancellationToken: detener);
                await _bot.SetMyCommands(ComandosBot.Menu, cancellationToken: detener);

                _estado.MarcarConectado(yo.Username);
                _logger.LogInformation("Bot de Telegram @{Usuario} conectado (long polling).", yo.Username);
                espera = TimeSpan.FromSeconds(5);

                await _bot.ReceiveAsync(_receptor, new ReceiverOptions
                {
                    AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery },
                    DropPendingUpdates = true   // no se procesan mensajes viejos (de cuando la app estaba apagada)
                }, detener);

                // ReceiveAsync también puede terminar sin excepción (p. ej. un timeout de red interno).
                if (!detener.IsCancellationRequested)
                    _logger.LogWarning("Bot de Telegram: el ciclo de polling terminó. Se reintenta en {Segundos} s.", espera.TotalSeconds);
            }
            catch (OperationCanceledException) when (detener.IsCancellationRequested)
            {
                break;
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 401)
            {
                _estado.MarcarDesconectado();
                _logger.LogCritical("Bot de Telegram detenido: Telegram rechazó el token (401). Revisa TelegramBot:Token en dotnet user-secrets.");
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Bot de Telegram: se perdió la conexión. Se reintenta en {Segundos} s.", espera.TotalSeconds);
            }

            _estado.MarcarDesconectado();
            try
            {
                await Task.Delay(espera, detener);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            espera = TimeSpan.FromSeconds(Math.Min(espera.TotalSeconds * 2, 60));
        }

        _estado.MarcarDesconectado();
        _logger.LogInformation("Bot de Telegram detenido.");
    }
}
