using FlyVibeMex.Services;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace FlyVibeMex.Bot;

/// <summary>
/// Servicio en segundo plano del grupo de agentes (solo si hay <c>TelegramBot:ChatIdAgentes</c>). Cada minuto:
/// <list type="bullet">
/// <item>Re-aviso, UNA vez, de cada PROBLEMA con viaje en menos de 72 h que sigue sin agente 30 minutos después de
/// abrirse (como respuesta al mensaje del ticket, para que se vea de cuál es).</item>
/// <item>Resumen diario a la hora de <c>TelegramBot:HoraResumen</c> (09:00), de lunes a viernes salvo que
/// <c>ResumenFinesDeSemana</c> sea true. Si la app estaba apagada a esa hora, sale al arrancar, ese mismo día.</item>
/// </list>
/// Los dos se apartan en <c>telegram_avisos</c> antes de mandarse (no se repiten aunque la app se reinicie).
/// </summary>
public sealed class ResumenSoporteDiario : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan EsperaInicial = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EsperaUrgente = TimeSpan.FromMinutes(30);
    private static readonly LinkPreviewOptions SinVistaPrevia = new() { IsDisabled = true };

    private readonly ITelegramBotClient _bot;
    private readonly IServiceScopeFactory _scopes;
    private readonly EstadoBotTelegram _estado;
    private readonly TelegramBotOpciones _opciones;
    private readonly ILogger<ResumenSoporteDiario> _logger;

    /// <summary>Día del último resumen ya resuelto (evita consultar la BD cada minuto el resto del día).</summary>
    private DateOnly? _ultimoResumen;

    public ResumenSoporteDiario(ITelegramBotClient bot, IServiceScopeFactory scopes, EstadoBotTelegram estado,
        IOptions<TelegramBotOpciones> opciones, ILogger<ResumenSoporteDiario> logger)
    {
        _bot = bot;
        _scopes = scopes;
        _estado = estado;
        _opciones = opciones.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken detener)
    {
        if (_opciones.ChatIdAgentes is not long grupo) return;   // sin grupo de agentes no hay nada que hacer

        try
        {
            await Task.Delay(EsperaInicial, detener);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var reloj = new PeriodicTimer(Intervalo);
        do
        {
            if (!_estado.Conectado) continue;
            try
            {
                await RevisarAsync(grupo, detener);
            }
            catch (OperationCanceledException) when (detener.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Telegram: falló la revisión del grupo de soporte; se reintenta en 1 minuto.");
            }
        }
        while (await SiguienteAsync(reloj, detener));
    }

    private static async Task<bool> SiguienteAsync(PeriodicTimer reloj, CancellationToken detener)
    {
        try
        {
            return await reloj.WaitForNextTickAsync(detener);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task RevisarAsync(long grupo, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var consultas = scope.ServiceProvider.GetRequiredService<IConsultasTelegramService>();
        var ahora = DateTime.Now;

        // 1) Urgentes sin agente (una vez por ticket).
        foreach (var t in await consultas.UrgentesSinAgenteAsync(ahora, EsperaUrgente, ct))
        {
            if (!await consultas.ApartarAvisoAsync(t.IdCliente, TiposAvisoTelegram.UrgenteSinAgente, t.Id, ct)) continue;
            var mensaje = await consultas.MensajeGrupoAsync(t.Id, ct);
            var (html, teclado) = MensajesGrupoSoporte.Urgente(t, ahora, _opciones.UrlSitioWeb);
            var respondeA = mensaje is not null && mensaje.ChatId == grupo ? mensaje.IdMensaje : (int?)null;
            if (!await EntregarAsync(grupo, html, teclado, respondeA, ct))
                await consultas.LiberarAvisoAsync(TiposAvisoTelegram.UrgenteSinAgente, t.Id, ct);
        }

        // 2) Resumen diario.
        var hoy = DateOnly.FromDateTime(ahora);
        if (_ultimoResumen == hoy || _opciones.HoraResumenValida is not TimeOnly hora) return;
        var esFinDeSemana = ahora.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        if (esFinDeSemana && !_opciones.ResumenFinesDeSemana)
        {
            _ultimoResumen = hoy;
            return;
        }
        if (TimeOnly.FromDateTime(ahora) < hora) return;

        var referencia = (ulong)(ahora.Year * 10000 + ahora.Month * 100 + ahora.Day);   // AAAAMMDD
        if (!await consultas.ApartarAvisoAsync(null, TiposAvisoTelegram.ResumenDiario, referencia, ct))
        {
            _ultimoResumen = hoy;   // ya salió hoy (p. ej. antes de un reinicio)
            return;
        }

        var resumen = await consultas.ResumenSoporteAsync(ahora, ct);
        var (texto, botones) = MensajesGrupoSoporte.Resumen(resumen, ahora, _opciones.UrlSitioWeb, diario: true);
        if (await EntregarAsync(grupo, texto, botones, null, ct))
            _ultimoResumen = hoy;
        else
            await consultas.LiberarAvisoAsync(TiposAvisoTelegram.ResumenDiario, referencia, ct);
    }

    /// <summary>true = entregado o imposible (400/403: el bot ya no está en el grupo); false = falla pasajera.</summary>
    private async Task<bool> EntregarAsync(long grupo, string html, Teclado teclado, int? respondeA, CancellationToken ct)
    {
        try
        {
            await _bot.SendMessage(grupo, FormatoBot.AjustarLargo(html), parseMode: ParseMode.Html,
                replyParameters: respondeA is int id ? new ReplyParameters { MessageId = id, AllowSendingWithoutReply = true } : null,
                replyMarkup: teclado.Crear(), linkPreviewOptions: SinVistaPrevia, cancellationToken: ct);
            return true;
        }
        catch (ApiRequestException ex) when (ex.ErrorCode is 400 or 403)
        {
            _logger.LogWarning("Telegram: no se pudo escribir en el grupo de agentes ({Codigo}). Revisa que el bot siga en el grupo.", ex.ErrorCode);
            return true;
        }
        catch (RequestException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Telegram: no se pudo escribir en el grupo de agentes (red o límite); se reintenta en 1 minuto.");
            return false;
        }
    }
}
