using System.Text;
using System.Threading.Channels;
using FlyVibeMex.Services;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace FlyVibeMex.Bot;

public enum TipoAvisoTelegram { TicketCreado, RespuestaDelPersonal, TicketCerrado }

public sealed record AvisoTelegram(TipoAvisoTelegram Tipo, ulong IdTicket);

/// <summary>
/// Implementación de <see cref="IAvisosTelegram"/> cuando el bot está activo: solo forma el aviso en una cola en
/// memoria (no espera a Telegram). <see cref="DespachadorAvisosTelegram"/> los manda en segundo plano.
/// </summary>
public sealed class ColaAvisosTelegram : IAvisosTelegram
{
    private readonly Channel<AvisoTelegram> _canal = Channel.CreateBounded<AvisoTelegram>(
        new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public ChannelReader<AvisoTelegram> Lector => _canal.Reader;

    public void TicketCreado(ulong idTicket) => _canal.Writer.TryWrite(new AvisoTelegram(TipoAvisoTelegram.TicketCreado, idTicket));

    public void RespuestaDelPersonal(ulong idTicket) => _canal.Writer.TryWrite(new AvisoTelegram(TipoAvisoTelegram.RespuestaDelPersonal, idTicket));

    public void TicketCerrado(ulong idTicket) => _canal.Writer.TryWrite(new AvisoTelegram(TipoAvisoTelegram.TicketCerrado, idTicket));
}

/// <summary>
/// Manda los avisos de la cola: tickets nuevos al grupo privado de agentes (<c>TelegramBot:ChatIdAgentes</c>) y
/// respuestas / cierres al cliente que vinculó su cuenta. Un error con Telegram solo se registra en el log.
/// </summary>
/// <remarks>
/// Privacidad: al grupo NO se manda el nombre, correo ni teléfono del cliente; solo folio, tipo, asunto y la
/// reservación. Los agentes ven el resto en el panel (/Admin/Soporte), con su sesión.
/// </remarks>
public sealed class DespachadorAvisosTelegram : BackgroundService
{
    /// <summary>Un PROBLEMA con viaje dentro de estas horas sale marcado como URGENTE.</summary>
    private static readonly TimeSpan HorasUrgente = TimeSpan.FromHours(72);

    private readonly ColaAvisosTelegram _cola;
    private readonly ITelegramBotClient _bot;
    private readonly IServiceScopeFactory _scopes;
    private readonly TelegramBotOpciones _opciones;
    private readonly ILogger<DespachadorAvisosTelegram> _logger;

    public DespachadorAvisosTelegram(ColaAvisosTelegram cola, ITelegramBotClient bot, IServiceScopeFactory scopes,
        IOptions<TelegramBotOpciones> opciones, ILogger<DespachadorAvisosTelegram> logger)
    {
        _cola = cola;
        _bot = bot;
        _scopes = scopes;
        _opciones = opciones.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken detener)
    {
        try
        {
            await foreach (var aviso in _cola.Lector.ReadAllAsync(detener))
            {
                try
                {
                    await MandarAsync(aviso, detener);
                }
                catch (OperationCanceledException) when (detener.IsCancellationRequested)
                {
                    break;
                }
                catch (ApiRequestException ex) when (ex.ErrorCode == 403)
                {
                    // El cliente bloqueó al bot o el bot ya no está en el grupo.
                    _logger.LogInformation("Telegram: no se pudo entregar el aviso {Tipo} del ticket {IdTicket} (403).", aviso.Tipo, aviso.IdTicket);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Telegram: falló el aviso {Tipo} del ticket {IdTicket}.", aviso.Tipo, aviso.IdTicket);
                }
            }
        }
        catch (OperationCanceledException) when (detener.IsCancellationRequested)
        {
            // la app se está deteniendo
        }
    }

    private async Task MandarAsync(AvisoTelegram aviso, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var consultas = scope.ServiceProvider.GetRequiredService<IConsultasTelegramService>();
        var vinculos = scope.ServiceProvider.GetRequiredService<IVinculacionTelegramService>();

        switch (aviso.Tipo)
        {
            case TipoAvisoTelegram.TicketCreado:
                await TicketCreadoAsync(consultas, aviso.IdTicket, ct);
                break;
            case TipoAvisoTelegram.RespuestaDelPersonal:
                await RespuestaAsync(consultas, vinculos, aviso.IdTicket, ct);
                break;
            case TipoAvisoTelegram.TicketCerrado:
                await CerradoAsync(consultas, vinculos, aviso.IdTicket, ct);
                break;
        }
    }

    private async Task TicketCreadoAsync(IConsultasTelegramService consultas, ulong idTicket, CancellationToken ct)
    {
        if (_opciones.ChatIdAgentes is not long grupo) return;   // sin grupo configurado no se avisa
        var t = await consultas.TicketParaAvisoAsync(idTicket, ct);
        if (t is null) return;

        var sb = new StringBuilder()
            .Linea($"🆕 <b>Ticket #{t.Id}</b> · {FormatoBot.TipoTicket(t.Tipo)}")
            .Linea($"<b>Asunto:</b> {FormatoBot.HtmlRecortado(t.Asunto, 120)}");
        if (t.CodigoReserva is not null)
            sb.Linea($"<b>Reservación:</b> <code>{FormatoBot.Html(t.CodigoReserva)}</code>");

        var ahora = DateTime.Now;
        if (t.Tipo == "PROBLEMA" && t.InicioViaje is DateTime inicio && inicio > ahora && inicio - ahora <= HorasUrgente)
            sb.Linea($"⚠️ <b>URGENTE</b>: el viaje empieza el {FormatoBot.FechaHora(inicio)} (en menos de 72 h).");

        sb.Linea().Linea("Atiéndelo en el panel: Soporte › Atender nuevo ticket.");

        var teclado = new Teclado();
        var url = FormatoBot.UrlWeb(_opciones.UrlSitioWeb, "/Admin/Soporte/NuevoTicket");
        if (!teclado.Enlace("Abrir el panel de soporte", url) && url is not null)
            sb.Linea(FormatoBot.Html(url));

        await EnviarAsync(grupo, sb.ToString(), teclado, ct);
    }

    private async Task RespuestaAsync(IConsultasTelegramService consultas, IVinculacionTelegramService vinculos, ulong idTicket,
        CancellationToken ct)
    {
        var r = await consultas.UltimaRespuestaDelPersonalAsync(idTicket, ct);
        if (r is null) return;
        var chat = await vinculos.ChatDeClienteAsync(r.IdCliente, ct);
        if (chat is null) return;   // el cliente no vinculó su cuenta

        var sb = new StringBuilder()
            .Linea($"💬 <b>Te respondieron en tu ticket #{r.IdTicket}</b>")
            .Linea($"«{FormatoBot.HtmlRecortado(r.Asunto, 120)}»")
            .Linea()
            .Linea(FormatoBot.HtmlRecortado(r.Mensaje, 1500));

        var teclado = new Teclado()
            .Fila(("✍️ Responder", Teclado.DeAviso($"rsp:{r.IdTicket}")), ("Ver la conversación", Teclado.DeAviso($"tkt:{r.IdTicket}")));
        await EnviarAsync(chat.Value, sb.ToString(), teclado, ct);
    }

    private async Task CerradoAsync(IConsultasTelegramService consultas, IVinculacionTelegramService vinculos, ulong idTicket,
        CancellationToken ct)
    {
        var t = await consultas.TicketParaAvisoAsync(idTicket, ct);
        if (t is null) return;
        var chat = await vinculos.ChatDeClienteAsync(t.IdCliente, ct);
        if (chat is null) return;

        var sb = new StringBuilder()
            .Linea($"✅ <b>Tu ticket #{t.Id} quedó cerrado</b>")
            .Linea($"«{FormatoBot.HtmlRecortado(t.Asunto, 120)}»")
            .Linea()
            .Linea("Si necesitas algo más, abre otro con /asesoria o /problema.");

        // Botones "de aviso": abren una pantalla nueva y el aviso se queda en el chat.
        var teclado = new Teclado().Fila(("Ver el ticket", Teclado.DeAviso($"tkt:{t.Id}")), ("Soporte", Teclado.DeAviso("sop")));
        await EnviarAsync(chat.Value, sb.ToString(), teclado, ct);
    }

    private Task EnviarAsync(long chatId, string html, Teclado teclado, CancellationToken ct) =>
        _bot.SendMessage(chatId, FormatoBot.AjustarLargo(html), parseMode: ParseMode.Html,
            replyMarkup: teclado.Crear(), linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
            cancellationToken: ct);
}
