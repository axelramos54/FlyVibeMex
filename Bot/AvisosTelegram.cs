using System.Text;
using System.Threading.Channels;
using FlyVibeMex.Services;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace FlyVibeMex.Bot;

public enum TipoAvisoTelegram { TicketCreado, RespuestaDelPersonal, TicketCerrado, CuentaDesvinculada, TicketActualizado, ClienteRespondio }

/// <param name="IdTicket">Ticket del aviso (0 en <see cref="TipoAvisoTelegram.CuentaDesvinculada"/>).</param>
/// <param name="ChatId">Chat destino, solo para <see cref="TipoAvisoTelegram.CuentaDesvinculada"/> (el vínculo ya no existe).</param>
public sealed record AvisoTelegram(TipoAvisoTelegram Tipo, ulong IdTicket, long ChatId = 0);

/// <summary>
/// Implementación de <see cref="IAvisosTelegram"/> cuando el bot está activo: solo forma el aviso en una cola en
/// memoria (no espera a Telegram). <see cref="DespachadorAvisosTelegram"/> los manda en segundo plano. g
/// </summary>
public sealed class ColaAvisosTelegram : IAvisosTelegram
{
    private readonly Channel<AvisoTelegram> _canal = Channel.CreateBounded<AvisoTelegram>(
        new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public ChannelReader<AvisoTelegram> Lector => _canal.Reader;

    public void TicketCreado(ulong idTicket) => _canal.Writer.TryWrite(new AvisoTelegram(TipoAvisoTelegram.TicketCreado, idTicket));

    public void RespuestaDelPersonal(ulong idTicket) => _canal.Writer.TryWrite(new AvisoTelegram(TipoAvisoTelegram.RespuestaDelPersonal, idTicket));

    public void TicketCerrado(ulong idTicket) => _canal.Writer.TryWrite(new AvisoTelegram(TipoAvisoTelegram.TicketCerrado, idTicket));

    public void CuentaDesvinculada(long chatId) => _canal.Writer.TryWrite(new AvisoTelegram(TipoAvisoTelegram.CuentaDesvinculada, 0, chatId));

    public void TicketActualizado(ulong idTicket) => _canal.Writer.TryWrite(new AvisoTelegram(TipoAvisoTelegram.TicketActualizado, idTicket));

    public void ClienteRespondio(ulong idTicket) => _canal.Writer.TryWrite(new AvisoTelegram(TipoAvisoTelegram.ClienteRespondio, idTicket));
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
    /// <summary>"💬 El cliente respondió" suena como máximo una vez cada 10 minutos por ticket (anti ruido en el grupo).</summary>
    private static readonly TimeSpan PausaAvisoRespuesta = TimeSpan.FromMinutes(10);

    /// <summary>Último aviso "el cliente respondió" por ticket (solo lo usa este servicio: un lector a la vez).</summary>
    private readonly Dictionary<ulong, DateTime> _ultimoAvisoRespuesta = new();

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

        // Primero el grupo (su mensaje del ticket queda al día) y después el cliente: si el cliente bloqueó al bot
        // (403), el grupo ya se actualizó.
        switch (aviso.Tipo)
        {
            case TipoAvisoTelegram.TicketCreado:
                await TicketCreadoAsync(consultas, aviso.IdTicket, ct);
                break;
            case TipoAvisoTelegram.RespuestaDelPersonal:
                await ActualizarGrupoAsync(consultas, aviso.IdTicket, ct);
                await RespuestaAsync(consultas, vinculos, aviso.IdTicket, ct);
                break;
            case TipoAvisoTelegram.TicketCerrado:
                await ActualizarGrupoAsync(consultas, aviso.IdTicket, ct);
                await CerradoAsync(consultas, vinculos, aviso.IdTicket, ct);
                break;
            case TipoAvisoTelegram.TicketActualizado:
                await ActualizarGrupoAsync(consultas, aviso.IdTicket, ct);
                break;
            case TipoAvisoTelegram.ClienteRespondio:
                await ClienteRespondioAsync(consultas, aviso.IdTicket, ct);
                break;
            case TipoAvisoTelegram.CuentaDesvinculada:
                await DesvinculadaAsync(aviso.ChatId, ct);
                break;
        }
    }

    private async Task TicketCreadoAsync(IConsultasTelegramService consultas, ulong idTicket, CancellationToken ct)
    {
        if (_opciones.ChatIdAgentes is not long grupo) return;   // sin grupo configurado no se avisa
        var t = await consultas.TicketParaGrupoAsync(idTicket, ct);
        if (t is null) return;

        // El botón abre ESTE ticket (/Admin/Soporte/Ticket/{id}); antes llevaba a "Atender nuevo ticket", que abre el
        // más antiguo de la fila. Se guarda el mensaje para reescribirlo cada vez que el ticket cambie.
        var (html, teclado) = MensajesGrupoSoporte.Ticket(t, DateTime.Now, _opciones.UrlSitioWeb);
        var enviado = await EnviarAsync(grupo, html, teclado, ct);
        await consultas.GuardarMensajeGrupoAsync(t.Id, t.IdCliente, grupo, enviado.Id, ct);
    }

    /// <summary>Reescribe el mensaje del ticket en el grupo con su estado actual (si se guardó al crearlo).</summary>
    private async Task ActualizarGrupoAsync(IConsultasTelegramService consultas, ulong idTicket, CancellationToken ct)
    {
        if (await consultas.MensajeGrupoAsync(idTicket, ct) is not { } mensaje) return;   // ticket anterior a esta función
        var t = await consultas.TicketParaGrupoAsync(idTicket, ct);
        if (t is null) return;

        var (html, teclado) = MensajesGrupoSoporte.Ticket(t, DateTime.Now, _opciones.UrlSitioWeb);
        try
        {
            await _bot.EditMessageText(mensaje.ChatId, mensaje.IdMensaje, FormatoBot.AjustarLargo(html), parseMode: ParseMode.Html,
                replyMarkup: teclado.Crear(), linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true }, cancellationToken: ct);
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 400)
        {
            // "message is not modified" (nada cambió) o el mensaje ya se borró del grupo: no es un error.
            if (!ex.Message.Contains("not modified", StringComparison.OrdinalIgnoreCase))
                _logger.LogInformation("Telegram: no se pudo actualizar el mensaje del ticket {IdTicket} en el grupo.", idTicket);
        }
    }

    /// <summary>El cliente respondió: se actualiza el mensaje del ticket y, si ya lo atiende un agente, sale un aviso que suena.</summary>
    private async Task ClienteRespondioAsync(IConsultasTelegramService consultas, ulong idTicket, CancellationToken ct)
    {
        await ActualizarGrupoAsync(consultas, idTicket, ct);
        if (_opciones.ChatIdAgentes is not long grupo) return;

        var t = await consultas.TicketParaGrupoAsync(idTicket, ct);
        if (t is not { Estado: "EN_PROCESO" }) return;   // si está en la fila, el mensaje del ticket ya lo dice

        var ahora = DateTime.Now;
        if (_ultimoAvisoRespuesta.TryGetValue(idTicket, out var ultimo) && ahora - ultimo < PausaAvisoRespuesta) return;
        _ultimoAvisoRespuesta[idTicket] = ahora;
        if (_ultimoAvisoRespuesta.Count > 1000) _ultimoAvisoRespuesta.Clear();

        var mensaje = await consultas.MensajeGrupoAsync(idTicket, ct);
        var respondeA = mensaje is not null && mensaje.ChatId == grupo ? mensaje.IdMensaje : (int?)null;
        await _bot.SendMessage(grupo, MensajesGrupoSoporte.ClienteRespondio(t), parseMode: ParseMode.Html,
            replyParameters: respondeA is int id ? new ReplyParameters { MessageId = id, AllowSendingWithoutReply = true } : null,
            cancellationToken: ct);
    }

    private async Task RespuestaAsync(IConsultasTelegramService consultas, IVinculacionTelegramService vinculos, ulong idTicket,
        CancellationToken ct)
    {
        var r = await consultas.UltimaRespuestaDelPersonalAsync(idTicket, ct);
        if (r is null) return;
        var chat = await vinculos.ChatDeClienteAsync(r.IdCliente, PreferenciaAviso.Soporte, ct);
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
        var chat = await vinculos.ChatDeClienteAsync(t.IdCliente, PreferenciaAviso.Soporte, ct);
        if (chat is null) return;

        var sb = new StringBuilder()
            .Linea($"✅ <b>Tu ticket #{t.Id} quedó cerrado</b>")
            .Linea($"«{FormatoBot.HtmlRecortado(t.Asunto, 120)}»")
            .Linea()
            .Linea("¿Cómo te atendimos? Toca de 1 a 5 estrellas.")
            .Linea("Si necesitas algo más, abre otro desde Soporte.");

        // Las estrellas SÍ reescriben este aviso ("¡Gracias!…"); los demás botones son "de aviso" (pantalla nueva).
        var teclado = new Teclado()
            .Fila(Enumerable.Range(1, 5).Select(n => ($"{n}⭐", $"cal:{t.Id}:{n}")).ToArray())
            .Fila(("Ver el ticket", Teclado.DeAviso($"tkt:{t.Id}")), ("Soporte", Teclado.DeAviso("sop")));
        await EnviarAsync(chat.Value, sb.ToString(), teclado, ct);
    }

    /// <summary>Aviso de seguridad: el cliente desvinculó Telegram desde la web (siempre se manda; no se puede apagar).</summary>
    private async Task DesvinculadaAsync(long chatId, CancellationToken ct)
    {
        if (chatId <= 0) return;
        var sb = new StringBuilder()
            .Linea("🔓 <b>Tu cuenta se desvinculó desde la web</b>")
            .Linea("Este chat ya no está vinculado a tu cuenta de FlyVibeMex: aquí ya no verás tus reservaciones ni recibirás avisos.")
            .Linea()
            .Linea("Si no fuiste tú, entra a la web y cambia tu contraseña.");
        var teclado = new Teclado().Boton("🔗 Vincular de nuevo", Teclado.DeAviso("cta"));
        await EnviarAsync(chatId, sb.ToString(), teclado, ct);
    }

    private Task<Message> EnviarAsync(long chatId, string html, Teclado teclado, CancellationToken ct) =>
        _bot.SendMessage(chatId, FormatoBot.AjustarLargo(html), parseMode: ParseMode.Html,
            replyMarkup: teclado.Crear(), linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
            cancellationToken: ct);
}
