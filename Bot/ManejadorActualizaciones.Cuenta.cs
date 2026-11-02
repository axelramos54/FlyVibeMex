using System.Text;
using FlyVibeMex.Services;

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

    private const string RutaVincular = "🔗 Vincular cuenta";
    private const string RutaReservas = "🧳 Mis reservaciones";

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

        var sb = new StringBuilder().Linea(FormatoBot.Ruta(RutaVincular)).Linea();
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

        var (resultado, nombre) = await _vinculos.VincularAsync(codigo, ctx.ChatId, ct);
        switch (resultado)
        {
            case ResultadoVinculacion.Vinculado:
            {
                var sb = new StringBuilder()
                    .Linea($"✅ ¡Listo, <b>{FormatoBot.Html(nombre)}</b>! Tu cuenta de FlyVibeMex quedó vinculada.")
                    .Linea()
                    .Linea("Ya puedes ver tus reservaciones, abrir tickets y te avisaré aquí cuando un agente te responda.")
                    .Linea("Puedes desvincularla cuando quieras con /desvincular o desde la web.");
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

    private async Task DesvincularPreguntarAsync(ContextoBot ctx, CancellationToken ct)
    {
        if (!ctx.Vinculado)
        {
            await MostrarAsync(ctx, "Este chat no está vinculado a ninguna cuenta.", TecladoMenu(ctx), ct);
            return;
        }

        var teclado = new Teclado().Fila(("Sí, desvincular", "dsv:si"), ("✖ No", "menu"));
        await MostrarAsync(ctx,
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
        sb.Linea(r.cancelacion.puede_cancelar
            ? $"Puedes cancelarla en la web. {FormatoBot.Html(r.cancelacion.TextoReembolso)}"
            : $"No se puede cancelar: {FormatoBot.Html(r.cancelacion.motivo_no_cancelar ?? "revisa los detalles en la web.")}");

        if (d.Historial.Count > 0)
        {
            sb.Linea().Linea("<b>Historial</b>");
            foreach (var h in d.Historial.TakeLast(3))
                sb.Linea($"• {FormatoBot.FechaHora(h.fecha)} · {FormatoBot.Estado(h.estado)} · {FormatoBot.Html(h.quien)}");
        }

        var teclado = new Teclado().Boton("🆘 Reportar un problema con esta reservación", $"prb:{c.codigo_reserva}");
        EnlaceWeb(teclado, sb, "Ver o cancelar en la web", $"/Miusuario/Reservacion/{Uri.EscapeDataString(c.codigo_reserva)}");
        teclado.Navegacion("Mis reservaciones", "res");
        await MostrarAsync(ctx, sb.ToString(), teclado, ct);
    }
}
