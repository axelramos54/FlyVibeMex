using System.Text;
using FlyVibeMex.Models;
using FlyVibeMex.Services;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace FlyVibeMex.Bot;

/// <summary>Quién escribe y si su cuenta está vinculada (se arma en cada mensaje o botón).</summary>
public sealed class ContextoBot
{
    public required long ChatId { get; init; }

    /// <summary>Nombre de pila en Telegram (solo para saludar si no está vinculado).</summary>
    public required string NombreTelegram { get; init; }

    /// <summary>Cliente de FlyVibeMex vinculado a este chat (null = sin vincular).</summary>
    public ClienteTelegram? Cliente { get; init; }

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

/// <summary>
/// Router del bot (docs/06): recibe cada mensaje o botón, decide qué comando o paso de un flujo es y responde.
/// Partes: Catálogo y ayuda (público) · Cuenta (vincular, reservaciones) · Soporte (tickets).
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
/// </remarks>
public sealed partial class ManejadorActualizaciones
{
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

        var ctx = await ContextoAsync(chatId, mensaje.From, ct);
        var texto = mensaje.Text?.Trim();
        if (string.IsNullOrEmpty(texto))
        {
            await EnviarAsync(ctx.ChatId, "Por ahora solo entiendo mensajes de texto. Escribe /menu para ver lo que puedo hacer.", null, ct);
            return;
        }

        if (texto.StartsWith('/'))
        {
            var (comando, argumentos, _) = Partir(texto);
            await ComandoAsync(ctx, comando, argumentos, ct);
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

        await EnviarAsync(ctx.ChatId, "No entendí ese mensaje 🤔. Usa los botones o escribe /menu.", TecladoMenu(ctx), ct);
    }

    private async Task BotonAsync(CallbackQuery boton, CancellationToken ct)
    {
        try
        {
            if (boton.Message is { } original && original.Chat.Type != ChatType.Private) return;
            var chatId = boton.Message?.Chat.Id ?? boton.From.Id;
            if (!_limitador.Permitir($"msg:{chatId}", 30, TimeSpan.FromMinutes(1))) return;

            var ctx = await ContextoAsync(chatId, boton.From, ct);
            await AccionAsync(ctx, boton.Data ?? string.Empty, ct);
        }
        finally
        {
            // Quita el "relojito" del botón en Telegram (hay que contestar siempre, aunque no haya nada que decir).
            try
            {
                await _bot.AnswerCallbackQuery(boton.Id, cancellationToken: ct);
            }
            catch (ApiRequestException)
            {
                // El botón ya era muy viejo: no pasa nada.
            }
        }
    }

    private async Task<ContextoBot> ContextoAsync(long chatId, User? usuario, CancellationToken ct) => new()
    {
        ChatId = chatId,
        NombreTelegram = string.IsNullOrWhiteSpace(usuario?.FirstName) ? "viajero" : usuario!.FirstName,
        Cliente = await _vinculos.ClientePorChatAsync(chatId, ct)
    };

    // =============================================================================================
    // Comandos (/algo) y botones (callback_data)
    // =============================================================================================

    private async Task ComandoAsync(ContextoBot ctx, string comando, string argumentos, CancellationToken ct)
    {
        // Un comando siempre interrumpe el flujo que estuviera a medias.
        _estados.Limpiar(ctx.ChatId);

        switch (comando)
        {
            case "start":
                await InicioAsync(ctx, argumentos, ct);
                break;
            case "menu":
                await MenuAsync(ctx, saludo: false, ct);
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
            case "tickets":
                await TicketsAsync(ctx, ct);
                break;
            case "asesoria":
            case "asesoría":
                await IniciarTicketAsync(ctx, "ASESORIA", null, null, ct);
                break;
            case "problema":
                await ProblemaElegirReservacionAsync(ctx, ct);
                break;
            case "cancelar":
                await EnviarAsync(ctx.ChatId, "Listo, cancelé lo que estábamos haciendo.", TecladoMenu(ctx), ct);
                break;
            case "idchat":
                await EnviarAsync(ctx.ChatId, "Ese comando es para el grupo de agentes: agrégame al grupo y escribe ahí /idchat.", null, ct);
                break;
            default:
                await EnviarAsync(ctx.ChatId, "No conozco ese comando. Escribe /menu para ver las opciones.", TecladoMenu(ctx), ct);
                break;
        }
    }

    private async Task AccionAsync(ContextoBot ctx, string datos, CancellationToken ct)
    {
        var partes = datos.Split(':');
        var accion = partes[0];
        string? Parte(int i) => partes.Length > i ? partes[i] : null;

        // Cualquier botón (salvo Enviar / Cancelar de un ticket) interrumpe el flujo que estuviera a medias.
        if (accion is not ("ok" or "no")) _estados.Limpiar(ctx.ChatId);

        switch (accion)
        {
            case "menu": await MenuAsync(ctx, saludo: false, ct); break;

            // ---- Catálogo y ayuda (público) ----
            case "exp": await CategoriasAsync(ctx, ct); break;
            case "cat": await ListaAsync(ctx, Parte(1), null, Entero(Parte(2)), ct); break;
            case "catd": await ListaAsync(ctx, Parte(1), Id(Parte(2)), Entero(Parte(3)), ct); break;
            case "dst": await DestinosAsync(ctx, Parte(1), ct); break;
            case "srv" when Id(Parte(1)) is ulong idServicio: await FichaAsync(ctx, idServicio, ct); break;
            case "fch" when Id(Parte(1)) is ulong idFechas: await FechasAsync(ctx, idFechas, ct); break;
            case "ayu" when Parte(1) is null: await AyudaAsync(ctx, ct); break;
            case "ayu": await AyudaTemaAsync(ctx, Parte(1)!, ct); break;

            // ---- Cuenta ----
            case "vinc": await VincularComandoAsync(ctx, string.Empty, ct); break;
            case "dsv" when Parte(1) == "si": await DesvincularAsync(ctx, ct); break;
            case "dsv": await DesvincularPreguntarAsync(ctx, ct); break;
            case "res" when Parte(1) is { Length: > 0 } codigo: await ReservacionAsync(ctx, codigo, ct); break;
            case "res": await ReservasAsync(ctx, ct); break;

            // ---- Soporte ----
            case "sop": await SoporteMenuAsync(ctx, ct); break;
            case "ase": await IniciarTicketAsync(ctx, "ASESORIA", null, null, ct); break;
            case "prb" when Parte(1) is { Length: > 0 } codigoProblema: await ProblemaConCodigoAsync(ctx, codigoProblema, ct); break;
            case "prb": await ProblemaElegirReservacionAsync(ctx, ct); break;
            case "prbr" when Id(Parte(1)) is ulong idReservacion: await ProblemaConReservacionAsync(ctx, idReservacion, ct); break;
            case "prbn": await IniciarTicketAsync(ctx, "PROBLEMA", null, null, ct); break;
            case "tkt" when Id(Parte(1)) is ulong idTicket: await TicketAsync(ctx, idTicket, ct); break;
            case "tkt": await TicketsAsync(ctx, ct); break;
            case "rsp" when Id(Parte(1)) is ulong idResponder: await IniciarRespuestaAsync(ctx, idResponder, ct); break;
            case "ok": await ConfirmarTicketAsync(ctx, ct); break;
            case "no":
                _estados.Limpiar(ctx.ChatId);
                await EnviarAsync(ctx.ChatId, "Listo, no se envió nada.", TecladoMenu(ctx), ct);
                break;

            default:
                await MenuAsync(ctx, saludo: false, ct);
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
                _estados.Limpiar(ctx.ChatId);
                await MenuAsync(ctx, saludo: false, ct);
                break;
        }
    }

    // =============================================================================================
    // Inicio y menú
    // =============================================================================================

    private async Task InicioAsync(ContextoBot ctx, string argumentos, CancellationToken ct)
    {
        // Enlaces t.me/<bot>?start=... : "v_CODIGO" (desde Mi cuenta › Telegram) o "soporte" (desde el sitio).
        if (argumentos.StartsWith("v_", StringComparison.OrdinalIgnoreCase))
        {
            await IntentarVincularAsync(ctx, argumentos, ct);
            return;
        }
        if (argumentos.Equals("soporte", StringComparison.OrdinalIgnoreCase))
        {
            await SoporteMenuAsync(ctx, ct);
            return;
        }
        await MenuAsync(ctx, saludo: true, ct);
    }

    private async Task MenuAsync(ContextoBot ctx, bool saludo, CancellationToken ct)
    {
        var sb = new StringBuilder();
        if (saludo)
        {
            sb.Linea($"¡Hola, {FormatoBot.Html(ctx.Nombre)}! Soy el asistente de <b>FlyVibe Mex</b> ✈️")
              .Linea()
              .Linea("Te muestro vuelos, hoteles, vehículos, tours y paquetes. Si vinculas tu cuenta, también tus reservaciones y tus tickets de soporte.")
              .Linea()
              .Linea("🔒 Los pagos y las cancelaciones se hacen solo en la web: nunca te pediré datos de tarjeta ni tu contraseña.");
        }
        else
        {
            sb.Linea("<b>Menú principal</b>");
        }

        if (ctx.Vinculado)
            sb.Linea().Linea($"Cuenta vinculada: <b>{FormatoBot.Html(ctx.Nombre)}</b>.");
        else
            sb.Linea().Linea("Tu cuenta aún no está vinculada (puedes explorar sin vincularla).");

        await EnviarAsync(ctx.ChatId, sb.ToString(), TecladoMenu(ctx), ct);
    }

    private static Teclado TecladoMenu(ContextoBot ctx)
    {
        var teclado = new Teclado()
            .Fila(("🔎 Explorar", "exp"), ("🧳 Mis reservaciones", "res"))
            .Fila(("💬 Soporte", "sop"), ("ℹ️ Ayuda", "ayu"));
        if (!ctx.Vinculado) teclado.Boton("🔗 Vincular mi cuenta", "vinc");
        return teclado;
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
        await EnviarAsync(id, sb.ToString(), null, ct);
    }

    // =============================================================================================
    // Ayudantes
    // =============================================================================================

    private async Task EnviarAsync(long chatId, string html, Teclado? teclado, CancellationToken ct) =>
        await _bot.SendMessage(chatId, FormatoBot.AjustarLargo(html), parseMode: ParseMode.Html,
            replyMarkup: teclado?.Crear(), linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
            cancellationToken: ct);

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
            .Linea("3. Mándamelo aquí: <code>/vincular TU-CODIGO</code> (o usa el botón de la web).");
        var teclado = new Teclado();
        EnlaceWeb(teclado, sb, "Abrir Mi cuenta › Telegram", "/Miusuario/Telegram");
        teclado.Fila(("Ya tengo mi código", "vinc"), ("Menú", "menu"));
        await EnviarAsync(ctx.ChatId, sb.ToString(), teclado, ct);
        return false;
    }

    private async Task AdvertirTarjetaAsync(ContextoBot ctx, int idMensaje, CancellationToken ct)
    {
        // En un chat privado el bot puede borrar los mensajes recibidos (menos de 48 h).
        try
        {
            await _bot.DeleteMessage(ctx.ChatId, idMensaje, ct);
        }
        catch (ApiRequestException)
        {
            // Si no se pudo borrar, igual no se guarda ni se registra.
        }

        await EnviarAsync(ctx.ChatId,
            "🔒 Por seguridad borré tu mensaje: parecía un número de tarjeta. Nunca mandes por aquí tarjetas, CVV ni contraseñas; " +
            "los pagos se hacen solo en la web. Si estabas escribiendo un ticket, vuelve a escribirlo sin ese número.",
            null, ct);
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
