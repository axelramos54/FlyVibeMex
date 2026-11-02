using System.Text;
using FlyVibeMex.Services;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace FlyVibeMex.Bot;

/// <summary>
/// Servicio en segundo plano que, cada minuto, avisa por Telegram a los clientes vinculados:
/// <list type="bullet">
/// <item>Reservación confirmada (comprobante de la compra) o cancelada.</item>
/// <item>Cambios del reembolso: solicitado, aprobado, rechazado o procesado.</item>
/// <item>Recordatorios 72 h y 24 h antes del inicio del viaje.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>Por qué revisa los historiales en lugar de que cada servicio avise: los cambios llegan por varios caminos
/// (compra en la web, cancelación del cliente o del personal, procedimientos almacenados corridos en SSMS) y todos
/// escriben en <c>historial_estado_reservacion</c> / <c>historial_estado_reembolso</c>. Así no se pierde ninguno,
/// ni siquiera si la app estuvo apagada un rato. Costo: hasta 1 minuto de retraso.</para>
/// <para>Nunca se manda dos veces: cada aviso se APARTA en <c>telegram_avisos</c> (índice único) antes de mandarlo;
/// si falla por algo pasajero (red, 429) se libera y se reintenta en el siguiente ciclo. Solo se toman cambios de
/// las últimas 2 horas, así que al instalarlo no llegan avisos de todo el historial.</para>
/// <para>Una cancelación con reembolso escribe varios renglones a la vez (cancelada + solicitado + aprobado…): se
/// agrupan por reservación y salen en UN solo mensaje.</para>
/// <para>Los recordatorios solo se mandan si la reservación ya existía antes de esa marca (quien reserva 30 h antes no
/// recibe el de 72 h junto con su confirmación).</para>
/// </remarks>
public sealed class VigilanteViajes : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan EsperaInicial = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan Ventana = TimeSpan.FromHours(2);
    private static readonly TimeSpan Antes72 = TimeSpan.FromHours(72);
    private static readonly TimeSpan Antes24 = TimeSpan.FromHours(24);
    private static readonly LinkPreviewOptions SinVistaPrevia = new() { IsDisabled = true };

    private readonly ITelegramBotClient _bot;
    private readonly IServiceScopeFactory _scopes;
    private readonly EstadoBotTelegram _estado;
    private readonly ILogger<VigilanteViajes> _logger;

    public VigilanteViajes(ITelegramBotClient bot, IServiceScopeFactory scopes, EstadoBotTelegram estado, ILogger<VigilanteViajes> logger)
    {
        _bot = bot;
        _scopes = scopes;
        _estado = estado;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken detener)
    {
        try
        {
            await Task.Delay(EsperaInicial, detener);   // que el bot termine de conectarse
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var reloj = new PeriodicTimer(Intervalo);
        do
        {
            if (!_estado.Conectado) continue;   // sin conexión con Telegram no tiene caso apartar avisos
            try
            {
                await RevisarAsync(detener);
            }
            catch (OperationCanceledException) when (detener.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Telegram: falló la revisión de avisos de viajes; se reintenta en 1 minuto.");
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

    private async Task RevisarAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var consultas = scope.ServiceProvider.GetRequiredService<IConsultasTelegramService>();
        var ahora = DateTime.Now;

        // 1) Reservaciones y reembolsos: un mensaje por reservación.
        var cambios = await consultas.CambiosParaAvisarAsync(ahora - Ventana, ct);
        foreach (var grupo in cambios.GroupBy(c => c.IdReservacion))
        {
            var apartados = new List<CambioParaAviso>();
            foreach (var cambio in grupo)
                if (await consultas.ApartarAvisoAsync(cambio.IdCliente, cambio.Tipo, cambio.IdHistorial, ct))
                    apartados.Add(cambio);
            if (apartados.Count == 0) continue;

            var (html, teclado) = MensajeCambios(apartados);
            if (html is null) continue;   // nada que contar (p. ej. solo un renglón intermedio)
            if (!await EntregarAsync(apartados[0].ChatId, html, teclado, ct))
                foreach (var cambio in apartados)
                    await consultas.LiberarAvisoAsync(cambio.Tipo, cambio.IdHistorial, ct);
        }

        // 2) Recordatorios 72 h y 24 h.
        foreach (var viaje in await consultas.ViajesProximosAsync(ahora, Antes72, ct))
        {
            var faltan = viaje.Viaje.Inicio - ahora;
            string? tipo = null;
            if (faltan <= Antes24)
            {
                if (!viaje.Enviado24 && viaje.CreadaEn <= viaje.Viaje.Inicio - Antes24) tipo = TiposAvisoTelegram.Recordatorio24;
            }
            else if (!viaje.Enviado72 && viaje.CreadaEn <= viaje.Viaje.Inicio - Antes72)
            {
                tipo = TiposAvisoTelegram.Recordatorio72;
            }
            if (tipo is null || !await consultas.ApartarAvisoAsync(viaje.IdCliente, tipo, viaje.IdReservacion, ct)) continue;

            var (html, teclado) = MensajeRecordatorio(viaje, ahora);
            if (!await EntregarAsync(viaje.ChatId, html, teclado, ct))
                await consultas.LiberarAvisoAsync(tipo, viaje.IdReservacion, ct);
        }
    }

    /// <summary>
    /// true = entregado, o imposible de entregar (403: bloqueó al bot; 400: el chat ya no existe) y no se reintenta.
    /// false = falla pasajera (sin red, 429): se libera y se reintenta en el siguiente ciclo.
    /// </summary>
    private async Task<bool> EntregarAsync(long chatId, string html, Teclado teclado, CancellationToken ct)
    {
        try
        {
            await _bot.SendMessage(chatId, FormatoBot.AjustarLargo(html), parseMode: ParseMode.Html,
                replyMarkup: teclado.Crear(), linkPreviewOptions: SinVistaPrevia, cancellationToken: ct);
            return true;
        }
        catch (ApiRequestException ex) when (ex.ErrorCode is 400 or 403)
        {
            _logger.LogInformation("Telegram: un aviso de viaje no se pudo entregar ({Codigo}); no se reintenta.", ex.ErrorCode);
            return true;
        }
        catch (RequestException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Telegram: no se pudo mandar un aviso de viaje (red o límite de Telegram); se reintenta en 1 minuto.");
            return false;
        }
    }

    // =============================================================================================
    // Mensajes
    // =============================================================================================

    private static (string? html, Teclado teclado) MensajeCambios(List<CambioParaAviso> cambios)
    {
        var primero = cambios[0];
        var reservacion = cambios.LastOrDefault(c => c.Tipo == TiposAvisoTelegram.Reservacion);
        var reembolso = cambios.LastOrDefault(c => c.Tipo == TiposAvisoTelegram.Reembolso);
        var codigo = primero.Codigo;
        var sb = new StringBuilder();
        var teclado = new Teclado();

        if (reservacion is { Estado: "CONFIRMADA" })
        {
            sb.Linea("✅ <b>Reservación confirmada</b>");
            Encabezado(sb, codigo, primero.Viaje);
            sb.Linea($"Total pagado: <b>{FormatoBot.Dinero(reservacion.Monto, reservacion.Moneda)}</b>")
              .Linea()
              .Linea("Guarda este mensaje como comprobante. Te escribiré unos días antes de tu viaje ⏰");
            teclado.Fila(("🧳 Ver reservación", Teclado.DeAviso($"res:{codigo}")), ("📅 Al calendario", Teclado.DeAviso($"ics:{codigo}")));
        }
        else if (reservacion is { Estado: "CANCELADA" })
        {
            sb.Linea("❌ <b>Reservación cancelada</b>");
            Encabezado(sb, codigo, primero.Viaje);
            if (!string.IsNullOrWhiteSpace(reservacion.Nota)) sb.Linea($"Motivo: {FormatoBot.HtmlRecortado(reservacion.Nota, 200)}");
            if (reembolso is not null)
                sb.Linea($"💸 Reembolso: <b>{EstadoReembolso(reembolso.Estado)}</b> · {FormatoBot.Dinero(reembolso.Monto, reembolso.Moneda)}");
            teclado.Fila(("🧳 Ver reservación", Teclado.DeAviso($"res:{codigo}")), ("💬 Soporte", Teclado.DeAviso("sop")));
        }
        else if (reembolso is not null)
        {
            sb.Linea($"💸 <b>Reembolso {EstadoReembolso(reembolso.Estado).ToLowerInvariant()}</b>");
            Encabezado(sb, codigo, primero.Viaje);
            sb.Linea($"Monto: <b>{FormatoBot.Dinero(reembolso.Monto, reembolso.Moneda)}</b>");
            switch (reembolso.Estado)
            {
                case "SOLICITADO":
                    sb.Linea().Linea("Lo revisa un agente de FlyVibeMex; te aviso aquí cuando cambie.");
                    break;
                case "APROBADO":
                    sb.Linea().Linea("En breve se procesa la devolución.");
                    break;
                case "PROCESADO":
                    sb.Linea().Linea("El dinero vuelve a tu método de pago original; puede tardar unos días hábiles en reflejarse.");
                    break;
                case "RECHAZADO":
                    if (!string.IsNullOrWhiteSpace(reembolso.Nota)) sb.Linea($"Motivo: {FormatoBot.HtmlRecortado(reembolso.Nota, 200)}");
                    sb.Linea().Linea("Si tienes dudas, escríbenos desde Soporte.");
                    break;
            }
            teclado.Fila(("🧳 Ver reservación", Teclado.DeAviso($"res:{codigo}")), ("💬 Soporte", Teclado.DeAviso("sop")));
        }
        else
        {
            return (null, teclado);
        }

        sb.Linea().Linea("<i>Puedes apagar estos avisos en 👤 Mi cuenta › 🔔 Avisos.</i>");
        return (sb.ToString(), teclado);
    }

    private static (string html, Teclado teclado) MensajeRecordatorio(ViajeParaRecordatorio viaje, DateTime ahora)
    {
        var inicio = viaje.Viaje.Inicio;
        var hora = inicio.ToString("HH:mm", FormatoBot.Es);
        var cuando = inicio.Date == ahora.Date ? $"hoy a las {hora}"
            : inicio.Date == ahora.Date.AddDays(1) ? $"mañana a las {hora}"
            : $"el {inicio.ToString("dddd dd/MM", FormatoBot.Es)} a las {hora}";

        var sb = new StringBuilder().Linea($"⏰ <b>Tu viaje es {cuando}</b>");
        Encabezado(sb, viaje.Codigo, viaje.Viaje);
        sb.Linea()
          .Linea("Ten a la mano tu código de reservación y una identificación oficial. ¡Buen viaje! ✈️")
          .Linea()
          .Linea("<i>Puedes apagar los recordatorios en 👤 Mi cuenta › 🔔 Avisos.</i>");

        var teclado = new Teclado()
            .Fila(("🧳 Ver reservación", Teclado.DeAviso($"res:{viaje.Codigo}")), ("📅 Al calendario", Teclado.DeAviso($"ics:{viaje.Codigo}")))
            .Boton("🆘 Tengo un problema", Teclado.DeAviso($"prb:{viaje.Codigo}"));
        return (sb.ToString(), teclado);
    }

    private static void Encabezado(StringBuilder sb, string codigo, ViajeResumen? viaje)
    {
        sb.Linea($"<code>{FormatoBot.Html(codigo)}</code>{(viaje is null ? "" : " · " + FormatoBot.HtmlRecortado(viaje.Servicio, 80))}");
        if (viaje is not null) sb.Linea($"Del {FormatoBot.FechaHora(viaje.Inicio)} al {FormatoBot.FechaHora(viaje.Fin)}");
    }

    private static string EstadoReembolso(string estado) => estado switch
    {
        "SOLICITADO" => "Solicitado",
        "APROBADO" => "Aprobado",
        "RECHAZADO" => "Rechazado",
        "PROCESADO" => "Procesado",
        _ => FormatoBot.Estado(estado)
    };
}
