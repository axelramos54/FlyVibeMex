using System.Text;
using FlyVibeMex.Models;
using FlyVibeMex.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace FlyVibeMex.Bot;

/// <summary>Quién escribe y si su cuenta está vinculada (se arma en cada mensaje o botón).</summary>
public sealed class ContextoBot
{
    public required long ChatId { get; init; }

    /// <summary>Nombre de pila en Telegram (solo para saludar si no está vinculado).</summary>
    public required string NombreTelegram { get; init; }

    /// <summary>Cliente de FlyVibeMex vinculado a este chat (null = sin vincular).</summary>
    public ClienteTelegram? Cliente { get; init; }

    /// <summary>
    /// Mensaje del bot cuyo botón se tocó: la primera pantalla que se muestre lo reescribe en lugar de mandar otro
    /// (así el chat no se llena de mensajes). Es null si el usuario escribió algo o si el botón venía de un aviso.
    /// </summary>
    public MensajeEditable? Editable { get; set; }

    public bool Vinculado => Cliente is not null;

    public string Nombre => Cliente?.Nombre ?? NombreTelegram;

    /// <summary>Contexto que piden MisViajesService / SoporteClienteService: siempre la cuenta PROPIA del cliente vinculado.</summary>
    public ContextoCuenta Cuenta => new()
    {
        IdUsuario = Cliente!.IdUsuario,
        IdCuenta = Cliente.IdUsuario,
        EsInterno = false
    };
}

/// <summary>Mensaje del bot que se puede reescribir; <paramref name="EsFoto"/> = tiene foto (se edita su pie, no su texto).</summary>
public sealed record MensajeEditable(int Id, bool EsFoto);

/// <summary>
/// Router del bot (docs/06): recibe cada mensaje o botón, decide qué comando o paso de un flujo es y responde.
/// Partes: Catálogo y ayuda (público) · Búsqueda por texto · Cuenta (vincular, reservaciones) · Soporte (tickets).
/// </summary>
/// <remarks>
/// <para>Reglas que se cumplen en todo el bot:</para>
/// <list type="bullet">
/// <item>Cero SQL: reutiliza los mismos servicios que la web (catálogo, Mis reservaciones, Soporte, contenido del sitio).</item>
/// <item>Solo chats privados (en grupos únicamente responde <c>/idchat</c>, para configurar el grupo de agentes).</item>
/// <item>Datos de cuenta solo con la cuenta vinculada, y siempre filtrados por ESA cuenta (los servicios lo hacen).</item>
/// <item>Nunca pide ni guarda tarjetas, CVV ni contraseñas; si alguien manda algo con forma de tarjeta, se borra y se advierte.</item>
/// <item>No cobra ni cancela: eso se hace en la web (los botones llevan allá).</item>
/// <item>El log nunca incluye el texto de los mensajes.</item>
/// </list>
/// <para>Pantallas: un botón reescribe el mensaje donde estaba (<see cref="MostrarAsync"/>); lo que el usuario escribe
/// recibe un mensaje nuevo; los avisos (te respondieron, ticket cerrado) nunca se reescriben.</para>
/// </remarks>
public sealed partial class ManejadorActualizaciones
{
    /// <summary>Dato de un flujo: id del mensaje del paso actual (para quitarle los botones al avanzar).</summary>
    private const string DatoMensajePaso = "msg";

    /// <summary>Acciones que pertenecen al flujo en curso y por eso NO lo interrumpen.</summary>
    private static readonly HashSet<string> AccionesDelFlujo = new(StringComparer.Ordinal) { "ok", "no", "atr", "eda", "edm", "rev" };

    private static readonly LinkPreviewOptions SinVistaPrevia = new() { IsDisabled = true };

    private readonly ITelegramBotClient _bot;
    private readonly IEstadoConversacionStore _estados;
    private readonly ILimitadorIntentos _limitador;
    private readonly IVinculacionTelegramService _vinculos;
    private readonly ICatalogoService _catalogo;
    private readonly IMisViajesService _viajes;
    private readonly ISoporteClienteService _soporte;
    private readonly IContenidoSitioService _contenido;
    private readonly IConsultasTelegramService _consultas;
    private readonly EstadoBotTelegram _estadoBot;
    private readonly FotosServicio _fotos;
    private readonly IMemoryCache _cache;
    private readonly TelegramBotOpciones _opciones;
    private readonly ILogger<ManejadorActualizaciones> _logger;

    public ManejadorActualizaciones(
        ITelegramBotClient bot,
        IEstadoConversacionStore estados,
        ILimitadorIntentos limitador,
        IVinculacionTelegramService vinculos,
        ICatalogoService catalogo,
        IMisViajesService viajes,
        ISoporteClienteService soporte,
        IContenidoSitioService contenido,
        IConsultasTelegramService consultas,
        EstadoBotTelegram estadoBot,
        FotosServicio fotos,
        IMemoryCache cache,
        IOptions<TelegramBotOpciones> opciones,
        ILogger<ManejadorActualizaciones> logger)
    {
        _bot = bot;
        _estados = estados;
        _limitador = limitador;
        _vinculos = vinculos;
        _catalogo = catalogo;
        _viajes = viajes;
        _soporte = soporte;
        _contenido = contenido;
        _consultas = consultas;
        _estadoBot = estadoBot;
        _fotos = fotos;
        _cache = cache;
        _opciones = opciones.Value;
        _logger = logger;
    }

    // =============================================================================================
    // Entrada
    // =============================================================================================

    public async Task ManejarAsync(Update update, CancellationToken ct)
    {
        if (update.Message is { } mensaje)
            await MensajeAsync(mensaje, ct);
        else if (update.CallbackQuery is { } boton)
            await BotonAsync(boton, ct);
    }

    private async Task MensajeAsync(Message mensaje, CancellationToken ct)
    {
        if (mensaje.From is { IsBot: true }) return;

        if (mensaje.Chat.Type != ChatType.Private)
        {
            await GrupoAsync(mensaje, ct);
            return;
        }

        var chatId = mensaje.Chat.Id;
        if (!_limitador.Permitir($"msg:{chatId}", 30, TimeSpan.FromMinutes(1))) return;   // anti spam

        await EscribiendoAsync(chatId, ct);   // "escribiendo…" mientras se consulta la BD
        var ctx = await ContextoAsync(chatId, mensaje.From, editable: null, ct);
        var texto = mensaje.Text?.Trim();
        if (string.IsNullOrEmpty(texto))
        {
            await MostrarAsync(ctx,
                "Por ahora solo entiendo mensajes de texto. Escríbeme un destino (por ejemplo «Cancún») o usa los botones.",
                TecladoMenu(ctx), ct);
            return;
        }

        if (texto.StartsWith('/'))
        {
            var (comando, argumentos, _) = Partir(texto);
            await ComandoAsync(ctx, comando, argumentos, ct);
            return;
        }

        // Botones del teclado fijo (llegan como texto): funcionan igual que un comando.
        if (ComandosBot.Atajo(texto) is { } atajo)
        {
            MarcarTecladoFijo(chatId);   // si lo tocó, ya lo tiene
            await ComandoAsync(ctx, atajo, string.Empty, ct);
            return;
        }

        // Regla R5: algo con forma de tarjeta nunca se guarda (ni dentro de un ticket).
        if (FormatoBot.PareceTarjeta(texto))
        {
            await AdvertirTarjetaAsync(ctx, mensaje.Id, ct);
            return;
        }

        if (_estados.Obtener(chatId) is { } estado)
        {
            await PasoAsync(ctx, estado, texto, ct);
            return;
        }

        await TextoLibreAsync(ctx, texto, ct);   // búsqueda en el catálogo o saludo
    }

    private async Task BotonAsync(CallbackQuery boton, CancellationToken ct)
    {
        string? avisoFlotante = null;   // texto que Telegram muestra unos segundos sobre el chat
        try
        {
            if (boton.Message is { } original && original.Chat.Type != ChatType.Private) return;
            var chatId = boton.Message?.Chat.Id ?? boton.From.Id;
            if (!_limitador.Permitir($"msg:{chatId}", 30, TimeSpan.FromMinutes(1)))
            {
                avisoFlotante = "Vas muy rápido 🙂 Espera un momento y vuelve a intentarlo.";
                return;
            }

            // Los botones de un aviso abren una pantalla NUEVA: el aviso se queda en el historial del chat.
            var datos = boton.Data ?? string.Empty;
            var deAviso = datos.EndsWith(Teclado.SufijoAviso, StringComparison.Ordinal);
            if (deAviso) datos = datos[..^Teclado.SufijoAviso.Length];

            var editable = !deAviso && boton.Message is { } m
                ? new MensajeEditable(m.Id, m.Photo is { Length: > 0 })
                : null;
            var ctx = await ContextoAsync(chatId, boton.From, editable, ct);
            await AccionAsync(ctx, datos, ct);
        }
        finally
        {
            // Quita el "relojito" del botón en Telegram (hay que contestar siempre, aunque no haya nada que decir).
            try
            {
                await _bot.AnswerCallbackQuery(boton.Id, avisoFlotante, cancellationToken: ct);
            }
            catch (ApiRequestException)
            {
                // El botón ya era muy viejo: no pasa nada.
            }
        }
    }

    private async Task<ContextoBot> ContextoAsync(long chatId, User? usuario, MensajeEditable? editable, CancellationToken ct) => new()
    {
        ChatId = chatId,
        NombreTelegram = string.IsNullOrWhiteSpace(usuario?.FirstName) ? "viajero" : usuario!.FirstName,
        Cliente = await _vinculos.ClientePorChatAsync(chatId, ct),
        Editable = editable
    };

    // =============================================================================================
    // Comandos (/algo) y botones (callback_data)
    // =============================================================================================

    private async Task ComandoAsync(ContextoBot ctx, string comando, string argumentos, CancellationToken ct)
    {
        // Un comando siempre interrumpe el flujo que estuviera a medias.
        await TerminarFlujoAsync(ctx, ct);

        switch (comando)
        {
            case "start":
                await InicioAsync(ctx, argumentos, ct);
                break;
            case "menu":
                await AsegurarTecladoFijoAsync(ctx, ct);
                await MenuAsync(ctx, ct);
                break;
            case "explorar":
                await CategoriasAsync(ctx, ct);
                break;
            case "ayuda":
                await AyudaAsync(ctx, ct);
                break;
            case "vincular":
                await VincularComandoAsync(ctx, argumentos, ct);
                break;
            case "desvincular":
                await DesvincularPreguntarAsync(ctx, ct);
                break;
            case "reservas":
            case "reservaciones":
                await ReservasAsync(ctx, ct);
                break;
            case "soporte":
                await SoporteMenuAsync(ctx, ct);
                break;
            case "cuenta":
                await MiCuentaAsync(ctx, ct);
                break;
            case "avisos":
                await AvisosPreferenciasAsync(ctx, ct);
                break;
            case "tickets":
                await TicketsAsync(ctx, ct);
                break;
            case "asesoria":
            case "asesoría":
                await IniciarTicketAsync(ctx, "ASESORIA", null, null, totalPasos: 3, ct);
                break;
            case "problema":
                await ProblemaElegirReservacionAsync(ctx, ct);
                break;
            case "cancelar":
                await MostrarAsync(ctx, "Listo, cancelé lo que estábamos haciendo.", TecladoMenu(ctx), ct);
                break;
            case "idchat":
                await MostrarAsync(ctx, "Ese comando es para el grupo de agentes: agrégame al grupo y escribe ahí /idchat.",
                    new Teclado().Navegacion(), ct);
                break;
            default:
                await MostrarAsync(ctx, "No conozco ese comando. Estas son las opciones:", TecladoMenu(ctx), ct);
                break;
        }
    }

    private async Task AccionAsync(ContextoBot ctx, string datos, CancellationToken ct)
    {
        var partes = datos.Split(':');
        var accion = partes[0];
        string? Parte(int i) => partes.Length > i ? partes[i] : null;

        // Cualquier botón que no sea del flujo en curso (Enviar, Cancelar, Atrás, Editar…) lo interrumpe.
        if (!AccionesDelFlujo.Contains(accion)) await TerminarFlujoAsync(ctx, ct);

        switch (accion)
        {
            case "menu": await MenuAsync(ctx, ct); break;

            // ---- Catálogo y ayuda (público). La ficha recibe id:destino:página para que "←" regrese a la misma lista ----
            case "exp": await CategoriasAsync(ctx, ct); break;
            case "cat": await ListaAsync(ctx, Parte(1), null, Entero(Parte(2)), ct); break;
            case "catd": await ListaAsync(ctx, Parte(1), Id(Parte(2)), Entero(Parte(3)), ct); break;
            case "dst": await DestinosAsync(ctx, Parte(1), ct); break;
            case "srv" when Id(Parte(1)) is ulong s1: await FichaAsync(ctx, s1, PestanaFicha.Resumen, Id(Parte(2)), Entero(Parte(3)), ct); break;
            case "srvd" when Id(Parte(1)) is ulong s2: await FichaAsync(ctx, s2, PestanaFicha.Detalles, Id(Parte(2)), Entero(Parte(3)), ct); break;
            case "srvp" when Id(Parte(1)) is ulong s3: await FichaAsync(ctx, s3, PestanaFicha.Politica, Id(Parte(2)), Entero(Parte(3)), ct); break;
            case "srvo" when Id(Parte(1)) is ulong s4: await FichaAsync(ctx, s4, PestanaFicha.Promos, Id(Parte(2)), Entero(Parte(3)), ct); break;
            case "fch" when Id(Parte(1)) is ulong s5: await FichaAsync(ctx, s5, PestanaFicha.Fechas, Id(Parte(2)), Entero(Parte(3)), ct); break;
            case "ayu" when Parte(1) is null: await AyudaAsync(ctx, ct); break;
            case "ayu": await AyudaTemaAsync(ctx, Parte(1)!, ct); break;

            // ---- Cuenta ----
            case "cta": await MiCuentaAsync(ctx, ct); break;
            case "avs" when Parte(1) is { Length: > 0 } preferencia: await CambiarAvisoAsync(ctx, preferencia, ct); break;
            case "avs": await AvisosPreferenciasAsync(ctx, ct); break;
            case "ics" when Parte(1) is { Length: > 0 } codigoCalendario: await CalendarioAsync(ctx, codigoCalendario, ct); break;
            case "vinc": await VincularComandoAsync(ctx, string.Empty, ct); break;
            case "dsv" when Parte(1) == "si": await DesvincularAsync(ctx, ct); break;
            case "dsv": await DesvincularPreguntarAsync(ctx, ct); break;
            case "res" when Parte(1) is { Length: > 0 } codigo: await ReservacionAsync(ctx, codigo, ct); break;
            case "res": await ReservasAsync(ctx, ct); break;

            // ---- Soporte ----
            case "sop": await SoporteMenuAsync(ctx, ct); break;
            case "ase": await IniciarTicketAsync(ctx, "ASESORIA", null, null, totalPasos: 3, ct); break;
            case "prb" when Parte(1) is { Length: > 0 } codigoProblema: await ProblemaConCodigoAsync(ctx, codigoProblema, totalPasos: 3, ct); break;
            case "prbc" when Parte(1) is { Length: > 0 } codigoElegido: await ProblemaConCodigoAsync(ctx, codigoElegido, totalPasos: 4, ct); break;
            case "prb": await ProblemaElegirReservacionAsync(ctx, ct); break;
            case "prbr" when Id(Parte(1)) is ulong idReservacion: await ProblemaConReservacionAsync(ctx, idReservacion, ct); break;
            case "prbn": await IniciarTicketAsync(ctx, "PROBLEMA", null, null, totalPasos: 4, ct); break;
            case "tkt" when Id(Parte(1)) is ulong idTicket: await TicketAsync(ctx, idTicket, ct); break;
            case "tkt": await TicketsAsync(ctx, ct); break;
            case "rsp" when Id(Parte(1)) is ulong idResponder: await IniciarRespuestaAsync(ctx, idResponder, ct); break;
            case "cal" when Id(Parte(1)) is ulong idCalificar && int.TryParse(Parte(2), out var estrellas):
                await CalificarTicketAsync(ctx, idCalificar, estrellas, ct);
                break;

            // ---- Botones del flujo de un ticket (no lo interrumpen) ----
            case "ok": await ConfirmarTicketAsync(ctx, ct); break;
            case "atr": await TicketAtrasAsync(ctx, ct); break;
            case "eda": await TicketEditarAsync(ctx, Pasos.Asunto, ct); break;
            case "edm": await TicketEditarAsync(ctx, Pasos.Mensaje, ct); break;
            case "rev": await TicketResumenAsync(ctx, ct); break;
            case "no":
                await TerminarFlujoAsync(ctx, ct);
                await MostrarAsync(ctx, "Listo, cancelé lo que estábamos haciendo. No se envió nada.", TecladoMenu(ctx), ct);
                break;

            default:
                await MenuAsync(ctx, ct);
                break;
        }
    }

    /// <summary>Respuesta de texto cuando el chat está a la mitad de un flujo (vincular, ticket, respuesta).</summary>
    private async Task PasoAsync(ContextoBot ctx, EstadoConversacion estado, string texto, CancellationToken ct)
    {
        switch (estado.Flujo)
        {
            case Flujos.Vincular:
                await IntentarVincularAsync(ctx, texto, ct);
                break;
            case Flujos.Ticket:
                await PasoTicketAsync(ctx, estado, texto, ct);
                break;
            case Flujos.Respuesta:
                await PasoRespuestaAsync(ctx, estado, texto, ct);
                break;
            default:
                await TerminarFlujoAsync(ctx, ct);
                await MenuAsync(ctx, ct);
                break;
        }
    }

    // =============================================================================================
    // Inicio, menú y teclado fijo
    // =============================================================================================

    private async Task InicioAsync(ContextoBot ctx, string argumentos, CancellationToken ct)
    {
        // Enlaces t.me/<bot>?start=... : "v_CODIGO" (desde Mi cuenta › Telegram) o "soporte" (desde el sitio).
        if (argumentos.StartsWith("v_", StringComparison.OrdinalIgnoreCase))
        {
            await IntentarVincularAsync(ctx, argumentos, ct);
            await AsegurarTecladoFijoAsync(ctx, ct);
            return;
        }
        // "srv_15": botón "Pregúntanos por Telegram" de la ficha de un servicio en la web.
        if (argumentos.StartsWith("srv_", StringComparison.OrdinalIgnoreCase) && Id(argumentos[4..]) is ulong idServicio)
        {
            await FichaAsync(ctx, idServicio, PestanaFicha.Resumen, null, 1, ct);
            await AsegurarTecladoFijoAsync(ctx, ct);
            return;
        }
        if (argumentos.Equals("soporte", StringComparison.OrdinalIgnoreCase))
        {
            await SoporteMenuAsync(ctx, ct);
            await AsegurarTecladoFijoAsync(ctx, ct);
            return;
        }

        // Saludo de 3 líneas (lleva el teclado fijo: Telegram no deja ponerlo junto con botones bajo el texto)
        // y, aparte, el menú con sus botones y el resumen de la cuenta.
        var sb = new StringBuilder()
            .Linea(ctx.Vinculado
                ? $"¡Hola de nuevo, {FormatoBot.Html(ctx.Nombre)}! Soy el asistente de <b>FlyVibe Mex</b> ✈️"
                : $"¡Hola, {FormatoBot.Html(ctx.Nombre)}! Soy el asistente de <b>FlyVibe Mex</b> ✈️")
            .Linea("Escríbeme un destino (por ejemplo «Cancún») o usa los botones.")
            .Linea("🔒 Nunca te pediré tarjetas ni contraseñas: los pagos son solo en la web.");
        await EnviarConTecladoFijoAsync(ctx.ChatId, sb.ToString(), ct);
        await MenuAsync(ctx, ct, conPista: false);
    }

    /// <param name="conPista">Agrega "escríbeme un destino…" (no hace falta justo después del saludo, que ya lo dice).</param>
    private async Task MenuAsync(ContextoBot ctx, CancellationToken ct, bool conPista = true)
    {
        var sb = new StringBuilder().Linea("<b>Menú principal</b>").Linea();
        var teclado = new Teclado();
        if (ctx.Vinculado)
        {
            sb.Linea($"Cuenta vinculada: <b>{FormatoBot.Html(ctx.Nombre)}</b>");
            await ResumenCuentaAsync(ctx, sb, teclado, ct);
        }
        else
        {
            sb.Linea("Tu cuenta aún no está vinculada: puedes explorar sin ella, y al vincularla verás aquí tus viajes y tickets.");
        }
        if (conPista) sb.Linea().Linea("💡 Escríbeme un destino o un servicio (por ejemplo «hotel Cancún») y lo busco.");
        await MostrarAsync(ctx, sb.ToString(), TecladoMenu(ctx, teclado), ct);
    }

    /// <summary>
    /// Resumen del menú para una cuenta vinculada: próximo viaje y tickets con respuesta nueva, con botones directos.
    /// Usa los mismos servicios que "Mis reservaciones" y "Soporte" (filtrados por la cuenta). Si la BD falla, el menú
    /// se muestra igual, sin resumen.
    /// </summary>
    private async Task ResumenCuentaAsync(ContextoBot ctx, StringBuilder sb, Teclado teclado, CancellationToken ct)
    {
        try
        {
            var reservas = await _viajes.ReservacionesAsync(ctx.Cuenta, ct);
            if (reservas.Proximas.FirstOrDefault() is { } proximo)
            {
                sb.Linea($"✈️ Próximo viaje: <code>{FormatoBot.Html(proximo.codigo_reserva)}</code> · " +
                         $"{FormatoBot.HtmlRecortado(proximo.servicio, 40)} · {proximo.inicio.ToString("dd/MM", FormatoBot.Es)}");
                teclado.Boton($"✈️ Ver {proximo.codigo_reserva}", $"res:{proximo.codigo_reserva}");
            }

            var conRespuesta = (await _soporte.SolicitudesAsync(ctx.Cuenta, ct)).Tickets.Where(t => t.respuesta_nueva).ToList();
            if (conRespuesta.Count == 1)
            {
                sb.Linea("🔔 1 ticket con respuesta nueva");
                teclado.Boton($"🔔 Ver el ticket #{conRespuesta[0].id}", $"tkt:{conRespuesta[0].id}");
            }
            else if (conRespuesta.Count > 1)
            {
                sb.Linea($"🔔 {conRespuesta.Count} tickets con respuesta nueva");
                teclado.Boton("🔔 Ver mis tickets", "tkt");
            }
        }
        catch (Exception ex) when (GestorBd.EsErrorDeBd(ex))
        {
            _logger.LogWarning("Telegram: no se pudo armar el resumen del menú (la BD no respondió); se muestra sin resumen.");
        }
    }

    /// <summary>Botones del menú principal (debajo de los botones directos del resumen, si los hay).</summary>
    private static Teclado TecladoMenu(ContextoBot ctx, Teclado? teclado = null) =>
        (teclado ?? new Teclado())
            .Fila(("🔎 Explorar", "exp"), ("🧳 Mis reservaciones", "res"))
            .Fila(("💬 Soporte", "sop"), (ctx.Vinculado ? "👤 Mi cuenta" : "🔗 Vincular cuenta", "cta"))
            .Boton("ℹ️ Ayuda", "ayu");

    private static string LlaveTecladoFijo(long chatId) => $"tg:teclado:{chatId}";

    private void MarcarTecladoFijo(long chatId) =>
        _cache.Set(LlaveTecladoFijo(chatId), true, TimeSpan.FromDays(1));

    /// <summary>
    /// Manda el teclado fijo si este chat no lo ha recibido desde que arrancó la app (p. ej. usuarios que ya
    /// tenían el chat antes de esta versión). Telegram lo recuerda; no hace falta mandarlo en cada mensaje.
    /// </summary>
    private async Task AsegurarTecladoFijoAsync(ContextoBot ctx, CancellationToken ct)
    {
        if (_cache.TryGetValue(LlaveTecladoFijo(ctx.ChatId), out _)) return;
        await EnviarConTecladoFijoAsync(ctx.ChatId, "⌨️ Te dejé accesos directos abajo del chat.", ct);
    }

    private async Task EnviarConTecladoFijoAsync(long chatId, string html, CancellationToken ct)
    {
        await EnviarNuevoAsync(chatId, html, ComandosBot.TecladoFijo(), ct);
        MarcarTecladoFijo(chatId);
    }

    // =============================================================================================
    // Grupos (solo /idchat, para configurar el grupo de agentes)
    // =============================================================================================

    private async Task GrupoAsync(Message mensaje, CancellationToken ct)
    {
        if (mensaje.Text is not { } texto || !texto.StartsWith('/')) return;
        var (comando, _, destinatario) = Partir(texto.Trim());
        if (comando != "idchat") return;

        // "/idchat@OtroBot" es para otro bot.
        if (destinatario is not null && _estadoBot.NombreUsuario is { } yo &&
            !destinatario.Equals(yo, StringComparison.OrdinalIgnoreCase)) return;
        if (!_limitador.Permitir($"grupo:{mensaje.Chat.Id}", 5, TimeSpan.FromMinutes(1))) return;

        var id = mensaje.Chat.Id;
        var sb = new StringBuilder().Linea($"Id de este chat: <code>{id}</code>");
        if (_opciones.ChatIdAgentes == id)
        {
            sb.Linea("✅ Este es el grupo de agentes configurado: aquí llegan los tickets nuevos.");
        }
        else
        {
            sb.Linea("Para recibir aquí los tickets nuevos, guárdalo en la PC donde corre la app y reiníciala:")
              .Linea($"<code>dotnet user-secrets set \"TelegramBot:ChatIdAgentes\" \"{id}\"</code>");
        }
        await EnviarNuevoAsync(id, sb.ToString(), null, ct);
    }

    // =============================================================================================
    // Pantallas: reescribir el mensaje del botón o mandar uno nuevo
    // =============================================================================================

    /// <summary>
    /// Muestra una pantalla y devuelve el id del mensaje donde quedó (null si no se pudo mostrar).
    /// <list type="bullet">
    /// <item>Si se tocó un botón: se reescribe ESE mensaje (texto, o el pie si es una foto).</item>
    /// <item>Si cambia el tipo (texto ↔ foto): Telegram no convierte uno en otro; se borra el anterior y se manda uno nuevo.</item>
    /// <item>Si el usuario escribió algo, o el mensaje ya no se puede editar: mensaje nuevo.</item>
    /// <item>Tocar dos veces el mismo botón ("message is not modified") no es un error.</item>
    /// </list>
    /// </summary>
    /// <param name="foto">Ruta pública de la imagen (<c>/uploads/...</c>); si no existe o Telegram la rechazó, va en texto.</param>
    private async Task<int?> MostrarAsync(ContextoBot ctx, string html, Teclado? teclado, CancellationToken ct, string? foto = null)
    {
        var botones = teclado?.Crear();
        var conFoto = foto is not null && _fotos.Disponible(foto);
        var texto = FormatoBot.AjustarLargo(html, conFoto ? FormatoBot.LargoMaximoPie : FormatoBot.LargoMaximoMensaje);

        // Solo la primera pantalla de cada toque reemplaza al mensaje del botón; las siguientes van aparte.
        var editable = ctx.Editable;
        ctx.Editable = null;

        if (editable is not null)
        {
            if (editable.EsFoto == conFoto)
            {
                try
                {
                    if (conFoto)
                        await _bot.EditMessageCaption(ctx.ChatId, editable.Id, texto, parseMode: ParseMode.Html,
                            replyMarkup: botones, cancellationToken: ct);
                    else
                        await _bot.EditMessageText(ctx.ChatId, editable.Id, texto, parseMode: ParseMode.Html,
                            replyMarkup: botones, linkPreviewOptions: SinVistaPrevia, cancellationToken: ct);
                    return editable.Id;
                }
                catch (ApiRequestException ex) when (SinCambios(ex))
                {
                    return editable.Id;
                }
                catch (ApiRequestException)
                {
                    // Ya no se puede editar (muy viejo o borrado): se manda uno nuevo abajo.
                }
            }
            else
            {
                await BorrarAsync(ctx.ChatId, editable.Id, ct);
            }
        }

        if (conFoto && await EnviarFotoAsync(ctx.ChatId, foto!, texto, botones, ct) is int idFoto)
            return idFoto;

        var nuevo = await _bot.SendMessage(ctx.ChatId, texto, parseMode: ParseMode.Html, replyMarkup: botones,
            linkPreviewOptions: SinVistaPrevia, cancellationToken: ct);
        return nuevo.Id;
    }

    /// <summary>
    /// Manda la foto de un servicio con su pie. La primera vez sube el archivo de <c>wwwroot/uploads</c> (funciona
    /// también con localhost) y guarda el <c>file_id</c> que devuelve Telegram para no volver a subirla.
    /// Devuelve null si Telegram no aceptó la imagen (se recuerda y la ficha se muestra en texto).
    /// </summary>
    private async Task<int?> EnviarFotoAsync(long chatId, string rutaPublica, string pie, InlineKeyboardMarkup? botones,
        CancellationToken ct)
    {
        try
        {
            if (_fotos.IdTelegram(rutaPublica) is { } idTelegram)
            {
                var reenviada = await _bot.SendPhoto(chatId, InputFile.FromFileId(idTelegram), caption: pie,
                    parseMode: ParseMode.Html, replyMarkup: botones, cancellationToken: ct);
                return reenviada.Id;
            }

            if (_fotos.RutaFisica(rutaPublica) is not { } archivo) return null;
            await using var contenido = File.OpenRead(archivo);
            var enviada = await _bot.SendPhoto(chatId, InputFile.FromStream(contenido, Path.GetFileName(archivo)), caption: pie,
                parseMode: ParseMode.Html, replyMarkup: botones, cancellationToken: ct);
            if (enviada.Photo is { Length: > 0 } tamanos) _fotos.Recordar(rutaPublica, tamanos[^1].FileId);
            return enviada.Id;
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 400)
        {
            _logger.LogInformation("Telegram no aceptó la imagen de un servicio; la ficha se muestra sin foto.");
            _fotos.Rechazar(rutaPublica);
            return null;
        }
        catch (IOException)
        {
            return null;   // el archivo se está reemplazando en este momento: se muestra sin foto
        }
    }

    /// <summary>Mensaje nuevo sin pasar por la lógica de pantallas (grupo, teclado fijo).</summary>
    private Task<Message> EnviarNuevoAsync(long chatId, string html, ReplyMarkup? botones, CancellationToken ct) =>
        _bot.SendMessage(chatId, FormatoBot.AjustarLargo(html), parseMode: ParseMode.Html, replyMarkup: botones,
            linkPreviewOptions: SinVistaPrevia, cancellationToken: ct);

    private static bool SinCambios(ApiRequestException ex) =>
        ex.ErrorCode == 400 && ex.Message.Contains("not modified", StringComparison.OrdinalIgnoreCase);

    private async Task BorrarAsync(long chatId, int idMensaje, CancellationToken ct)
    {
        try
        {
            await _bot.DeleteMessage(chatId, idMensaje, ct);
        }
        catch (ApiRequestException)
        {
            // Más de 48 h o ya borrado: se queda donde está.
        }
    }

    private async Task QuitarBotonesAsync(long chatId, int idMensaje, CancellationToken ct)
    {
        try
        {
            await _bot.EditMessageReplyMarkup(chatId, idMensaje, replyMarkup: null, cancellationToken: ct);
        }
        catch (ApiRequestException)
        {
            // Ya no tenía botones o ya no se puede editar.
        }
    }

    private async Task EscribiendoAsync(long chatId, CancellationToken ct)
    {
        try
        {
            await _bot.SendChatAction(chatId, ChatAction.Typing, cancellationToken: ct);
        }
        catch (RequestException)
        {
            // Solo es un indicador: si falla, se responde igual.
        }
    }

    // =============================================================================================
    // Flujos de varios pasos
    // =============================================================================================

    /// <summary>
    /// Muestra el mensaje de un paso y guarda el estado. Antes le quita los botones al mensaje del paso anterior
    /// (para que no queden botones viejos activos), salvo que sea el mismo mensaje que se va a reescribir.
    /// </summary>
    private async Task MostrarPasoAsync(ContextoBot ctx, string flujo, string paso, Dictionary<string, string> datos,
        string html, Teclado? teclado, CancellationToken ct)
    {
        if (datos.TryGetValue(DatoMensajePaso, out var anterior) && int.TryParse(anterior, out var idAnterior)
            && idAnterior != ctx.Editable?.Id)
            await QuitarBotonesAsync(ctx.ChatId, idAnterior, ct);

        datos.Remove(DatoMensajePaso);
        if (await MostrarAsync(ctx, html, teclado, ct) is int idPaso) datos[DatoMensajePaso] = idPaso.ToString();
        _estados.Guardar(ctx.ChatId, flujo, paso, datos);
    }

    /// <summary>Termina el flujo en curso (si hay) y le quita los botones al mensaje de su último paso.</summary>
    private async Task TerminarFlujoAsync(ContextoBot ctx, CancellationToken ct)
    {
        if (_estados.Obtener(ctx.ChatId) is not { } estado) return;
        _estados.Limpiar(ctx.ChatId);
        if (int.TryParse(estado.Dato(DatoMensajePaso), out var idPaso) && idPaso != ctx.Editable?.Id)
            await QuitarBotonesAsync(ctx.ChatId, idPaso, ct);
    }

    // =============================================================================================
    // Ayudantes
    // =============================================================================================

    /// <summary>
    /// Agrega un enlace al sitio: botón si la dirección es pública; si es localhost (demo en la misma PC), Telegram no
    /// acepta el botón y el enlace se escribe en el texto.
    /// </summary>
    private void EnlaceWeb(Teclado teclado, StringBuilder sb, string texto, string ruta)
    {
        var url = FormatoBot.UrlWeb(_opciones.UrlSitioWeb, ruta);
        if (url is null) return;
        if (!teclado.Enlace(texto, url))
            sb.Linea().Linea($"🌐 {FormatoBot.Html(texto)}: {FormatoBot.Html(url)}");
    }

    /// <summary>Si el chat no está vinculado, explica cómo hacerlo y devuelve false.</summary>
    private async Task<bool> RequiereCuentaAsync(ContextoBot ctx, CancellationToken ct)
    {
        if (ctx.Vinculado) return true;

        var sb = new StringBuilder()
            .Linea("🔗 Para eso necesito que vincules tu cuenta de FlyVibeMex:")
            .Linea("1. Entra a la web › <b>Mi cuenta › Telegram</b> (con tu sesión iniciada).")
            .Linea("2. Toca <b>Generar código</b>.")
            .Linea("3. Toca «Ya tengo mi código» y escríbelo aquí (o usa el botón de la web, que lo manda solo).");
        var teclado = new Teclado();
        EnlaceWeb(teclado, sb, "Abrir Mi cuenta › Telegram", "/Miusuario/Telegram");
        teclado.Boton("🔑 Ya tengo mi código", "vinc").Navegacion();
        await MostrarAsync(ctx, sb.ToString(), teclado, ct);
        return false;
    }

    private async Task AdvertirTarjetaAsync(ContextoBot ctx, int idMensaje, CancellationToken ct)
    {
        // En un chat privado el bot puede borrar los mensajes recibidos (menos de 48 h).
        await BorrarAsync(ctx.ChatId, idMensaje, ct);

        await MostrarAsync(ctx,
            "🔒 Por seguridad borré tu mensaje: parecía un número de tarjeta. Nunca mandes por aquí tarjetas, CVV ni contraseñas; " +
            "los pagos se hacen solo en la web. Si estabas escribiendo un ticket, vuelve a escribirlo sin ese número.",
            _estados.Obtener(ctx.ChatId) is not null ? new Teclado().Boton("✖ Cancelar", "no") : new Teclado().Navegacion(), ct);
    }

    /// <summary>"/start@FlyVibeMexBot v_ABC" → ("start", "v_ABC", "FlyVibeMexBot").</summary>
    private static (string comando, string argumentos, string? destinatario) Partir(string texto)
    {
        var separador = texto.IndexOfAny(new[] { ' ', '\n' });
        var cabeza = separador < 0 ? texto : texto[..separador];
        var argumentos = separador < 0 ? string.Empty : texto[(separador + 1)..].Trim();

        var comando = cabeza.TrimStart('/');
        string? destinatario = null;
        var arroba = comando.IndexOf('@');
        if (arroba >= 0)
        {
            destinatario = comando[(arroba + 1)..];
            comando = comando[..arroba];
        }
        return (comando.ToLowerInvariant(), argumentos, destinatario);
    }

    private static ulong? Id(string? texto) => ulong.TryParse(texto, out var id) && id > 0 ? id : null;

    private static int Entero(string? texto) => int.TryParse(texto, out var n) && n > 0 ? n : 1;
}
