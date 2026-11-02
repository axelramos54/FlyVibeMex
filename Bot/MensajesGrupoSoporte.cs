using System.Text;
using FlyVibeMex.Services;

namespace FlyVibeMex.Bot;

/// <summary>
/// Textos que el bot manda al grupo privado de agentes: el mensaje de cada ticket (se EDITA cuando el ticket cambia),
/// el resumen de soporte (diario y /pendientes), el re-aviso de urgentes y el aviso de "el cliente respondió".
/// </summary>
/// <remarks>
/// Privacidad: al grupo nunca va el nombre, correo ni teléfono del cliente; solo folio, tipo, asunto, reservación,
/// estado y el nombre del agente que atiende. El resto se ve en el panel, con sesión.
/// </remarks>
public static class MensajesGrupoSoporte
{
    /// <summary>Mensaje de un ticket en el grupo, con su estado actual y el botón al panel (a ESE ticket).</summary>
    public static (string html, Teclado teclado) Ticket(TicketParaGrupo t, DateTime ahora, string urlSitio)
    {
        var (icono, estado) = t.Estado switch
        {
            "ABIERTA" => ("🆕", "Abierto · <b>sin agente</b>"),
            "EN_PROCESO" => ("🔵", $"En proceso · Atiende: <b>{FormatoBot.Html(t.Agente ?? "—")}</b> · " +
                                  (t.LeTocaAlPersonal ? "💬 le toca al agente" : "⏳ esperando al cliente")),
            "CERRADA" => ("✅", "Cerrado" + (t.Calificacion is byte c ? $" · {FormatoBot.Estrellas(c)} ({c}/5)" : "")),
            _ => ("🎫", FormatoBot.Html(t.Estado))
        };

        var sb = new StringBuilder()
            .Linea($"{icono} <b>Ticket #{t.Id}</b> · {FormatoBot.TipoTicket(t.Tipo)}")
            .Linea($"<b>Asunto:</b> {FormatoBot.HtmlRecortado(t.Asunto, 120)}");
        if (t.CodigoReserva is not null)
            sb.Linea($"<b>Reservación:</b> <code>{FormatoBot.Html(t.CodigoReserva)}</code>");
        if (ConsultasTelegramService.EsUrgente(t, ahora) && t.InicioViaje is DateTime inicio)
            sb.Linea($"⚠️ <b>URGENTE</b>: el viaje empieza el {FormatoBot.FechaHora(inicio)} (en menos de 72 h).");
        sb.Linea($"<b>Estado:</b> {estado}")
          .Linea($"<i>Abierto el {FormatoBot.FechaHora(t.CreadaEn)} · última actividad {FormatoBot.FechaHora(t.ActualizadaEn)}</i>");

        var teclado = new Teclado();
        EnlacePanel(teclado, sb, urlSitio, "Abrir en el panel", $"/Admin/Soporte/Ticket/{t.Id}");
        return (sb.ToString(), teclado);
    }

    /// <summary>Resumen de soporte: <paramref name="diario"/> = el de las 9:00; false = el de /pendientes.</summary>
    public static (string html, Teclado teclado) Resumen(ResumenSoporte r, DateTime ahora, string urlSitio, bool diario)
    {
        var sb = new StringBuilder()
            .Linea(diario
                ? $"📊 <b>Soporte · resumen del {ahora.ToString("dddd dd/MM", FormatoBot.Es)}</b>"
                : $"📊 <b>Soporte · pendientes ahora</b> ({ahora.ToString("HH:mm", FormatoBot.Es)})")
            .Linea()
            .Linea($"🆕 Sin agente: <b>{r.SinAgente}</b>")
            .Linea($"🔵 En proceso: <b>{r.EnProceso}</b> (le toca al agente en {r.EsperanAlPersonal})")
            .Linea($"⚠️ Urgentes (viaje en menos de 72 h): <b>{r.TotalUrgentes}</b>");

        foreach (var t in r.Urgentes)
            sb.Linea($"   • #{t.Id} · viaje el {FormatoBot.FechaHora(t.InicioViaje!.Value)} · {QuienAtiende(t)}");

        if (r.MasAntiguos.Count > 0)
        {
            sb.Linea().Linea("<b>Más antiguos esperando al personal</b>");
            foreach (var t in r.MasAntiguos)
                sb.Linea($"• #{t.Id} · {Antiguedad(ahora - t.ActualizadaEn)} · {FormatoBot.HtmlRecortado(t.Asunto, 40)} · {QuienAtiende(t)}");
        }

        if (r.PorAgente.Count > 0)
            sb.Linea().Linea("<b>Carga por agente</b>")
              .Linea(string.Join(" · ", r.PorAgente.Select(a => $"{FormatoBot.Html(a.agente)}: {a.tickets}")));

        sb.Linea()
          .Linea($"✅ Cerrados ayer: <b>{r.CerradosAyer}</b>")
          .Linea(r.PromedioSemana is decimal promedio
              ? $"⭐ Calificación (7 días): <b>{promedio:0.0}/5</b> ({r.CalificacionesSemana} ticket{(r.CalificacionesSemana == 1 ? "" : "s")})"
              : "⭐ Calificación (7 días): sin calificaciones");
        if (r.SinAgente == 0 && r.EsperanAlPersonal == 0) sb.Linea().Linea("🎉 No hay nada pendiente.");

        var teclado = new Teclado();
        EnlacePanel(teclado, sb, urlSitio, "Abrir el panel de soporte", "/Admin/Soporte");
        return (sb.ToString(), teclado);
    }

    /// <summary>Re-aviso (una sola vez) de un PROBLEMA urgente que sigue sin agente.</summary>
    public static (string html, Teclado teclado) Urgente(TicketParaGrupo t, DateTime ahora, string urlSitio)
    {
        var sb = new StringBuilder()
            .Linea($"⚠️ <b>Ticket #{t.Id} sigue sin agente</b>")
            .Linea($"{FormatoBot.TipoTicket(t.Tipo)} · «{FormatoBot.HtmlRecortado(t.Asunto, 80)}»" +
                   (t.CodigoReserva is null ? "" : $" · <code>{FormatoBot.Html(t.CodigoReserva)}</code>"))
            .Linea($"Lo abrieron hace {Antiguedad(ahora - t.CreadaEn)} y el viaje empieza el {FormatoBot.FechaHora(t.InicioViaje!.Value)}.");
        var teclado = new Teclado();
        EnlacePanel(teclado, sb, urlSitio, "Tomarlo en el panel", $"/Admin/Soporte/Ticket/{t.Id}");
        return (sb.ToString(), teclado);
    }

    /// <summary>Aviso corto (sí suena) cuando el cliente responde en un ticket que ya atiende un agente.</summary>
    public static string ClienteRespondio(TicketParaGrupo t) =>
        $"💬 <b>El cliente respondió en el ticket #{t.Id}</b> · Atiende: {FormatoBot.Html(t.Agente ?? "—")}";

    private static string QuienAtiende(TicketParaGrupo t) =>
        t.Agente is null ? "sin agente" : FormatoBot.Html(t.Agente);

    /// <summary>"25 min", "3 h", "2 d".</summary>
    public static string Antiguedad(TimeSpan tiempo) =>
        tiempo.TotalMinutes < 60 ? $"{Math.Max(1, (int)tiempo.TotalMinutes)} min"
        : tiempo.TotalHours < 48 ? $"{(int)tiempo.TotalHours} h"
        : $"{(int)tiempo.TotalDays} d";

    /// <summary>Botón al panel si la dirección es pública; con localhost, el enlace va en el texto.</summary>
    private static void EnlacePanel(Teclado teclado, StringBuilder sb, string urlSitio, string texto, string ruta)
    {
        var url = FormatoBot.UrlWeb(urlSitio, ruta);
        if (url is not null && !teclado.Enlace(texto, url))
            sb.Linea().Linea($"🌐 {FormatoBot.Html(texto)}: {FormatoBot.Html(url)}");
    }
}
