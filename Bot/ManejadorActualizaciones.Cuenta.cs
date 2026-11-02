using System.Text;
using FlyVibeMex.Services;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace FlyVibeMex.Bot;

/// <summary>
/// Cuenta del cliente en el bot: vincular / desvincular (código de un solo uso de Mi cuenta › Telegram) y consultar
/// sus reservaciones con <see cref="IMisViajesService"/> (mismo servicio que "Mis reservaciones" en la web: siempre
/// filtra por la cuenta vinculada, así que nadie puede ver reservaciones ajenas cambiando un código).
/// </summary>
public sealed partial class ManejadorActualizaciones
{
    /// <summary>Intentos de /vincular por chat (anti fuerza bruta del código).</summary>
    private const int MaximoIntentosVincular = 5;
    private static readonly TimeSpan VentanaIntentosVincular = TimeSpan.FromMinutes(15);

    private const string RutaCuenta = "👤 Mi cuenta";
    private const string RutaVincular = "🔗 Vincular";
    private const string RutaReservas = "🧳 Mis reservaciones";

    // =============================================================================================
    // Mi cuenta (vínculo con Telegram)
    // =============================================================================================

    private async Task MiCuentaAsync(ContextoBot ctx, CancellationToken ct)
    {
        var sb = new StringBuilder().Linea(FormatoBot.Ruta(RutaCuenta)).Linea();
        var teclado = new Teclado();

        if (!ctx.Vinculado)
        {
            sb.Linea("Este chat aún no está vinculado a una cuenta de FlyVibeMex.")
              .Linea()
              .Linea("<b>Para vincularla</b>")
              .Linea("1. Entra a la web › <b>Mi cuenta › Telegram</b> (con tu sesión iniciada).")
              .Linea("2. Toca <b>Generar código</b>.")
              .Linea("3. Toca «Ya tengo mi código» y escríbelo aquí (o usa el botón de la web, que lo manda solo).")
              .Linea()
              .Linea("Con tu cuenta vinculada verás tus reservaciones, abrirás tickets y te avisaré aquí cuando te respondan.");
            EnlaceWeb(teclado, sb, "Abrir Mi cuenta › Telegram", "/Miusuario/Telegram");
            teclado.Boton("🔑 Ya tengo mi código", "vinc").Navegacion();
            await MostrarAsync(ctx, sb.ToString(), teclado, ct);
            return;
        }

        var desde = await _vinculos.VinculadoDesdeAsync(ctx.Cliente!.IdUsuario, ct);
        sb.Linea($"Cuenta vinculada: <b>{FormatoBot.Html(ctx.Nombre)}</b>");
        if (desde is DateTime fecha) sb.Linea($"Vinculada desde el {FormatoBot.Fecha(fecha)}");
        sb.Linea()
          .Linea("🔔 Te aviso aquí cuando un agente responde o cierra uno de tus tickets.")
          .Linea("🔒 Tus pagos y tu contraseña nunca pasan por el bot.");

        teclado.Boton("🔔 Avisos", "avs")
               .Fila(("🔄 Vincular otra cuenta", "vinc"), ("🔓 Desvincular", "dsv"));
        EnlaceWeb(teclado, sb, "Mi cuenta en la web", "/Miusuario/Telegram");
        teclado.Navegacion();
        await MostrarAsync(ctx, sb.ToString(), teclado, ct);
    }

    // =============================================================================================
    // Vincular / desvincular
    // =============================================================================================

    private async Task VincularComandoAsync(ContextoBot ctx, string argumentos, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(argumentos))
        {
            await IntentarVincularAsync(ctx, argumentos, ct);
            return;
        }

        var sb = new StringBuilder().Linea(FormatoBot.Ruta(RutaCuenta, RutaVincular)).Linea();
        if (ctx.Vinculado)
            sb.Linea($"Este chat ya está vinculado a la cuenta de <b>{FormatoBot.Html(ctx.Nombre)}</b>.")
              .Linea("Si quieres vincular otra cuenta, genera un código con esa cuenta y mándamelo.")
              .Linea();

        sb.Linea("Escribe el código que generaste en la web (<b>Mi cuenta › Telegram</b>). Se ve así: <code>K7P2-9QXM</code>");

        var teclado = new Teclado();
        EnlaceWeb(teclado, sb, "Generar mi código en la web", "/Miusuario/Telegram");
        teclado.Boton("✖ Cancelar", "no");
        await MostrarPasoAsync(ctx, Flujos.Vincular, Pasos.Codigo, new Dictionary<string, string>(), sb.ToString(), teclado, ct);
    }

    private async Task IntentarVincularAsync(ContextoBot ctx, string codigo, CancellationToken ct)
    {
        await TerminarFlujoAsync(ctx, ct);

        if (!_limitador.Permitir($"vincular:{ctx.ChatId}", MaximoIntentosVincular, VentanaIntentosVincular))
        {
            _logger.LogWarning("Telegram: un chat superó el límite de intentos de /vincular.");
            await MostrarAsync(ctx,
                "Hiciste demasiados intentos. Espera 15 minutos y genera un código nuevo en la web (Mi cuenta › Telegram).",
                new Teclado().Navegacion(), ct);
            return;
        }

        var (resultado, nombre, chatAnterior) = await _vinculos.VincularAsync(codigo, ctx.ChatId, ct);
        switch (resultado)
        {
            case ResultadoVinculacion.Vinculado:
            {
                if (chatAnterior is long anterior && anterior != ctx.ChatId) await AvisarVinculoMovidoAsync(anterior, ct);

                var sb = new StringBuilder()
                    .Linea($"✅ ¡Listo, <b>{FormatoBot.Html(nombre)}</b>! Tu cuenta de FlyVibeMex quedó vinculada.")
                    .Linea()
                    .Linea("Ya puedes ver tus reservaciones, abrir tickets y te avisaré aquí cuando un agente te responda.")
                    .Linea("La ves o la desvinculas cuando quieras en 👤 Mi cuenta (aquí o en la web).");
                var teclado = new Teclado()
                    .Fila(("🧳 Mis reservaciones", "res"), ("💬 Soporte", "sop"))
                    .Navegacion();
                await MostrarAsync(ctx, sb.ToString(), teclado, ct);
                break;
            }
            case ResultadoVinculacion.CuentaNoValida:
                await MostrarAsync(ctx, "Esa cuenta no se puede vincular con el bot (solo cuentas de cliente activas).",
                    new Teclado().Navegacion(), ct);
                break;
            case ResultadoVinculacion.ErrorBd:
                await MostrarAsync(ctx, "No pude vincular tu cuenta en este momento. Intenta de nuevo en unos minutos.",
                    new Teclado().Boton("🔁 Reintentar", "vinc").Navegacion(), ct);
                break;
            default:
            {
                var sb = new StringBuilder()
                    .Linea("Ese código no es válido o ya venció (dura 10 minutos y sirve una sola vez).")
                    .Linea("Genera otro en la web: <b>Mi cuenta › Telegram</b>.");
                var teclado = new Teclado();
                EnlaceWeb(teclado, sb, "Generar un código nuevo", "/Miusuario/Telegram");
                teclado.Boton("🔁 Intentar con otro código", "vinc").Navegacion();
                await MostrarAsync(ctx, sb.ToString(), teclado, ct);
                break;
            }
        }
    }

    /// <summary>
    /// Aviso de seguridad al chat donde ANTES estaba vinculada la cuenta (siempre se manda; no se puede apagar).
    /// Si ese chat bloqueó al bot o no hay red, solo se registra: no debe impedir la vinculación nueva.
    /// </summary>
    private async Task AvisarVinculoMovidoAsync(long chatAnterior, CancellationToken ct)
    {
        var sb = new StringBuilder()
            .Linea("🔒 <b>Tu cuenta se vinculó en otro Telegram</b>")
            .Linea("Este chat ya no está vinculado a tu cuenta de FlyVibeMex: aquí ya no verás tus reservaciones ni recibirás avisos.")
            .Linea()
            .Linea("Si no fuiste tú, entra a la web, cambia tu contraseña y desvincula Telegram en Mi cuenta › Telegram.");
        try
        {
            await EnviarNuevoAsync(chatAnterior, sb.ToString(), null, ct);
        }
        catch (RequestException ex)
        {
            _logger.LogInformation("Telegram: no se pudo avisar al chat anterior de una cuenta re-vinculada ({Tipo}).", ex.GetType().Name);
        }
    }

    // =============================================================================================
    // 🔔 Avisos (preferencias)
    // =============================================================================================

    private async Task AvisosPreferenciasAsync(ContextoBot ctx, CancellationToken ct, PreferenciasAvisos? preferencias = null)
    {
        if (!await RequiereCuentaAsync(ctx, ct)) return;
        preferencias ??= await _vinculos.PreferenciasAsync(ctx.ChatId, ct);
        if (preferencias is null)   // se desvinculó en este momento desde la web
        {
            await MostrarAsync(ctx, "Este chat ya no está vinculado a una cuenta.", TecladoMenu(ctx), ct);
            return;
        }

        static string Casilla(bool encendido) => encendido ? "✅" : "⬜";
        var sb = new StringBuilder()
            .Linea(FormatoBot.Ruta(RutaCuenta, "🔔 Avisos"))
            .Linea()
            .Linea("<b>¿De qué quieres que te avise?</b> Toca una opción para encenderla o apagarla.")
            .Linea()
            .Linea("• <b>Reservaciones y reembolsos:</b> confirmación de compra, cancelaciones y cambios de tu reembolso.")
            .Linea("• <b>Recordatorios:</b> 3 días y 1 día antes de tu viaje.")
            .Linea("• <b>Soporte:</b> cuando un agente responde o cierra tu ticket.")
            .Linea()
            .Linea("<i>Los avisos de seguridad de tu cuenta siempre llegan.</i>");
        var teclado = new Teclado()
            .Boton($"{Casilla(preferencias.Reservaciones)} Reservaciones y reembolsos", "avs:res")
            .Boton($"{Casilla(preferencias.Recordatorios)} Recordatorios de viaje", "avs:rec")
            .Boton($"{Casilla(preferencias.Soporte)} Respuestas de soporte", "avs:sop")
            .Navegacion("Mi cuenta", "cta");
        await MostrarAsync(ctx, sb.ToString(), teclado, ct);
    }

    private async Task CambiarAvisoAsync(ContextoBot ctx, string clave, CancellationToken ct)
    {
        if (!await RequiereCuentaAsync(ctx, ct)) return;
        PreferenciaAviso? preferencia = clave switch
        {
            "res" => PreferenciaAviso.Reservaciones,
            "rec" => PreferenciaAviso.Recordatorios,
            "sop" => PreferenciaAviso.Soporte,
            _ => null
        };
        var nuevas = preferencia is PreferenciaAviso p
            ? await _vinculos.CambiarPreferenciaAsync(ctx.ChatId, p, ct)
            : null;
        await AvisosPreferenciasAsync(ctx, ct, nuevas);
    }

    private async Task DesvincularPreguntarAsync(ContextoBot ctx, CancellationToken ct)
    {
        if (!ctx.Vinculado)
        {
            await MostrarAsync(ctx, "Este chat no está vinculado a ninguna cuenta.", TecladoMenu(ctx), ct);
            return;
        }

        var teclado = new Teclado().Fila(("Sí, desvincular", "dsv:si"), ("✖ No", "cta"));
        await MostrarAsync(ctx,
            FormatoBot.Ruta(RutaCuenta, "🔓 Desvincular") + "\n\n" +
            $"¿Desvincular la cuenta de <b>{FormatoBot.Html(ctx.Nombre)}</b>? Ya no verás aquí tus reservaciones ni tus tickets, " +
            "ni te llegarán avisos. Tus datos en FlyVibeMex no cambian.", teclado, ct);
    }

    private async Task DesvincularAsync(ContextoBot ctx, CancellationToken ct)
    {
        var desvinculado = await _vinculos.DesvincularChatAsync(ctx.ChatId, ct);
        var sinCuenta = new ContextoBot { ChatId = ctx.ChatId, NombreTelegram = ctx.NombreTelegram };
        await MostrarAsync(ctx,
            desvinculado
                ? "Listo: desvinculé tu cuenta. Puedes volver a vincularla cuando quieras con un código nuevo."
                : "Este chat no estaba vinculado.",
            TecladoMenu(sinCuenta), ct);
    }

    // =============================================================================================
    // Reservaciones (solo lectura: cancelar y pedir reembolso se hace en la web)
    // =============================================================================================

    private async Task ReservasAsync(ContextoBot ctx, CancellationToken ct)
    {
        if (!await RequiereCuentaAsync(ctx, ct)) return;

        var vm = await _viajes.ReservacionesAsync(ctx.Cuenta, ct);
        var sb = new StringBuilder().Linea(FormatoBot.Ruta(RutaReservas)).Linea();
        var teclado = new Teclado();

        if (vm.Proximas.Count == 0)
        {
            sb.Linea("No tienes viajes próximos. 🧳");
        }
        else
        {
            sb.Linea($"<b>Tus próximos viajes</b> ({vm.Proximas.Count})");
            foreach (var r in vm.Proximas.Take(6))
            {
                sb.Linea()
                  .Linea($"• <code>{FormatoBot.Html(r.codigo_reserva)}</code> · {FormatoBot.HtmlRecortado(r.servicio, 70)}")
                  .Linea($"   {FormatoBot.Fecha(r.inicio)} → {FormatoBot.Fecha(r.fin)} · {FormatoBot.Estado(r.estado)} · {FormatoBot.Dinero(r.total, r.moneda)}");
                teclado.Boton($"{r.codigo_reserva} · {FormatoBot.Recortar(r.servicio, 25)}", $"res:{r.codigo_reserva}");
            }
            if (vm.Proximas.Count > 6) sb.Linea().Linea($"…y {vm.Proximas.Count - 6} más en la web.");
        }

        if (vm.Pasadas.Count > 0 || vm.Canceladas.Count > 0)
            sb.Linea().Linea($"Pasados: {vm.Pasadas.Count} · Cancelados: {vm.Canceladas.Count} (los ves en la web).");

        teclado.Boton("🔎 Explorar", "exp");
        EnlaceWeb(teclado, sb, "Mis reservaciones en la web", "/Miusuario/Misreservaciones");
        teclado.Navegacion();
        await MostrarAsync(ctx, sb.ToString(), teclado, ct);
    }

    private async Task ReservacionAsync(ContextoBot ctx, string codigo, CancellationToken ct)
    {
        if (!await RequiereCuentaAsync(ctx, ct)) return;

        // DetalleAsync filtra por la cuenta: si el código es de otra persona, responde igual que si no existiera.
        var d = await _viajes.DetalleAsync(ctx.Cuenta, codigo, ct);
        if (d is null)
        {
            await MostrarAsync(ctx, "No encontré esa reservación en tu cuenta.", new Teclado().Navegacion("Mis reservaciones", "res"), ct);
            return;
        }

        var c = d.Comprobante;
        var r = d.Reservacion;
        var sb = new StringBuilder()
            .Linea(FormatoBot.Ruta(RutaReservas, FormatoBot.Html(c.codigo_reserva)))
            .Linea()
            .Linea($"Estado: <b>{FormatoBot.Estado(c.estado)}</b>")
            .Linea()
            .Linea($"<b>{FormatoBot.HtmlRecortado(c.servicio, 120)}</b>");
        if (!string.IsNullOrWhiteSpace(c.descripcion))
            sb.Linea(FormatoBot.HtmlRecortado(c.descripcion, 200).Replace("\r", "").Replace("\n", " "));
        sb.Linea($"Del {FormatoBot.FechaHora(c.inicio)} al {FormatoBot.FechaHora(c.fin)} · Cantidad: {c.cantidad:0.##}")
          .Linea()
          .Linea($"Subtotal: {FormatoBot.Dinero(c.subtotal, c.moneda)}");
        if (c.descuento > 0)
            sb.Linea($"Descuento{(c.codigo_promocion is null ? "" : $" ({FormatoBot.Html(c.codigo_promocion)})")}: -{FormatoBot.Dinero(c.descuento, c.moneda)}");
        sb.Linea($"<b>Total: {FormatoBot.Dinero(c.total, c.moneda)}</b>");
        if (c.metodo_pago is not null)
            sb.Linea($"Pago: {FormatoBot.Html(c.metodo_pago)}{(c.fecha_pago is DateTime f ? $" · {FormatoBot.FechaHora(f)}" : "")}");

        if (d.Reembolsos.FirstOrDefault() is { } reembolso)
            sb.Linea($"Reembolso: {FormatoBot.Estado(reembolso.estado)} · {FormatoBot.Dinero(reembolso.monto, reembolso.moneda)}");

        sb.Linea()
          .Linea("<b>Política de cancelación</b> (la de tu compra)")
          .Linea(FormatoBot.HtmlRecortado(c.politica, 500).Replace("\r", "").Replace("\n", " "));
        SimuladorCancelacion(sb, r.cancelacion);

        if (d.Historial.Count > 0)
        {
            sb.Linea().Linea("<b>Historial</b>");
            foreach (var h in d.Historial.TakeLast(3))
                sb.Linea($"• {FormatoBot.FechaHora(h.fecha)} · {FormatoBot.Estado(h.estado)} · {FormatoBot.Html(h.quien)}");
        }

        var teclado = new Teclado();
        if (c.estado != "CANCELADA") teclado.Boton("📅 Agregar al calendario", $"ics:{c.codigo_reserva}");
        teclado.Boton("🆘 Reportar un problema con esta reservación", $"prb:{c.codigo_reserva}");
        EnlaceWeb(teclado, sb, "Ver o cancelar en la web", $"/Miusuario/Reservacion/{Uri.EscapeDataString(c.codigo_reserva)}");
        teclado.Navegacion("Mis reservaciones", "res");
        await MostrarAsync(ctx, sb.ToString(), teclado, ct);
    }

    /// <summary>
    /// "Si cancelas hoy…": cuánto se devolvería ahora y hasta cuándo aplica el 100 %, con la MISMA regla que usa la web
    /// al cancelar (<see cref="ReglasCancelacion"/>, política copiada en la compra). Solo informa: cancelar es en la web.
    /// </summary>
    private static void SimuladorCancelacion(StringBuilder sb, Models.OpcionesCancelacion o)
    {
        sb.Linea().Linea("<b>💸 Si cancelas hoy</b>");
        if (!o.puede_cancelar)
        {
            sb.Linea($"No se puede cancelar: {FormatoBot.Html(o.motivo_no_cancelar ?? "revisa los detalles en la web.")}");
            return;
        }
        if (!o.puede_reembolsar)
        {
            sb.Linea($"Puedes cancelarla en la web, pero sin reembolso: {FormatoBot.Html(o.motivo_no_reembolsar ?? "según la política de tu compra.")}");
            return;
        }

        sb.Linea($"Te devolverían <b>{FormatoBot.Dinero(o.monto_reembolso, o.moneda)}</b> ({o.porcentaje:0.##} %).");
        if (o.limite_reembolso_total is DateTime limite && o.porcentaje_tardio < 100m)
        {
            if (limite > DateTime.Now)
                sb.Linea($"Reembolso total hasta el {FormatoBot.FechaHora(limite)}; después, {o.porcentaje_tardio:0.##} % " +
                         $"({FormatoBot.Dinero(o.monto_reembolso_tardio, o.moneda)}).");
            else
                sb.Linea($"El reembolso total era hasta el {FormatoBot.FechaHora(limite)}.");
        }
        sb.Linea("<i>Es un cálculo informativo: se confirma al cancelar en la web.</i>");
    }

    /// <summary>"📅 Agregar al calendario": manda un archivo .ics de la reservación (siempre de la cuenta vinculada).</summary>
    private async Task CalendarioAsync(ContextoBot ctx, string codigo, CancellationToken ct)
    {
        if (!await RequiereCuentaAsync(ctx, ct)) return;

        var d = await _viajes.DetalleAsync(ctx.Cuenta, codigo, ct);   // filtra por la cuenta
        if (d is null)
        {
            await MostrarAsync(ctx, "No encontré esa reservación en tu cuenta.", new Teclado().Navegacion("Mis reservaciones", "res"), ct);
            return;
        }
        var c = d.Comprobante;
        if (c.estado == "CANCELADA")
        {
            await MostrarAsync(ctx, "Esa reservación está cancelada: no hace falta agregarla al calendario.",
                new Teclado().Navegacion("Mis reservaciones", "res"), ct);
            return;
        }

        // Archivo nuevo en el chat (no se reescribe el mensaje del botón).
        var archivo = CalendarioIcs.Crear(c.codigo_reserva, c.servicio, c.descripcion, c.inicio, c.fin);
        using var contenido = new MemoryStream(archivo);
        await _bot.SendDocument(ctx.ChatId, InputFile.FromStream(contenido, $"FlyVibeMex-{c.codigo_reserva}.ics"),
            caption: $"📅 <b>{FormatoBot.HtmlRecortado(c.servicio, 80)}</b> · <code>{FormatoBot.Html(c.codigo_reserva)}</code>\n" +
                     "Abre el archivo para agregar tu viaje al calendario (Google, Outlook o el de tu celular).",
            parseMode: ParseMode.Html, cancellationToken: ct);
    }
}
