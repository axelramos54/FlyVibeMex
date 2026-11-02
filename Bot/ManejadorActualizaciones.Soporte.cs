using System.ComponentModel.DataAnnotations;
using System.Text;
using FlyVibeMex.Models;
using FlyVibeMex.Services;

namespace FlyVibeMex.Bot;

/// <summary>
/// Soporte desde el bot (regla R9, docs/06 módulo 3) con <see cref="ISoporteClienteService"/>, el MISMO servicio que
/// la pantalla Soporte de la web: abrir un ticket de asesoría o de problema, ver los tickets y responder en su
/// conversación. Las validaciones son las del formulario web (<see cref="NuevaSolicitudViewModel"/> y
/// <see cref="RespuestaTicketForm"/>), y el aviso al grupo de agentes lo manda el propio servicio al crear el ticket.
/// </summary>
/// <remarks>
/// Flujo de un ticket (cada paso dice "Paso X de N" y trae ✖ Cancelar):
/// [Reservación, solo si es un problema y hay que elegirla] → Asunto → Detalle → Confirmar.
/// En el resumen se puede corregir el asunto o el detalle sin empezar de nuevo.
/// </remarks>
public sealed partial class ManejadorActualizaciones
{
    private const string DatoTipo = "tipo";
    private const string DatoIdReservacion = "id_reservacion";
    private const string DatoCodigo = "codigo";
    private const string DatoAsunto = "asunto";
    private const string DatoMensaje = "mensaje";
    private const string DatoIdTicket = "id_ticket";
    private const string DatoTotalPasos = "total";

    private const string RutaSoporte = "💬 Soporte";

    private async Task SoporteMenuAsync(ContextoBot ctx, CancellationToken ct)
    {
        var sb = new StringBuilder()
            .Linea(FormatoBot.Ruta(RutaSoporte))
            .Linea()
            .Linea("<b>¿En qué te ayudamos?</b>")
            .Linea("Un agente de FlyVibeMex atiende tu ticket y te responde aquí y en la web.");
        var teclado = new Teclado()
            .Fila(("💡 Pedir asesoría", "ase"), ("🆘 Reportar un problema", "prb"))
            .Boton("🎫 Mis tickets", "tkt")
            .Navegacion();
        if (!ctx.Vinculado) sb.Linea().Linea("<i>Para abrir o ver tickets necesitas vincular tu cuenta.</i>");
        await MostrarAsync(ctx, sb.ToString(), teclado, ct);
    }

    private static string RutaTicket(string tipo) =>
        FormatoBot.Ruta(RutaSoporte, tipo == "PROBLEMA" ? "🆘 Problema" : "💡 Asesoría");

    // =============================================================================================
    // Nuevo ticket · paso 1 (solo PROBLEMA): elegir la reservación
    // =============================================================================================

    private const int MaximoReservacionesProblema = 8;

    private async Task ProblemaElegirReservacionAsync(ContextoBot ctx, CancellationToken ct)
    {
        if (!await RequiereCuentaAsync(ctx, ct)) return;

        // Mismas reservaciones que "Mis reservaciones" (siempre de la cuenta vinculada): primero las próximas,
        // luego las pasadas y las canceladas (un problema también puede ser de un reembolso).
        var vm = await _viajes.ReservacionesAsync(ctx.Cuenta, ct);
        var todas = vm.Proximas.Concat(vm.Pasadas).Concat(vm.Canceladas).ToList();

        var sb = new StringBuilder()
            .Linea(RutaTicket("PROBLEMA"))
            .Linea(FormatoBot.Progreso(1, 4, "Reservación"))
            .Linea();
        var teclado = new Teclado();

        if (todas.Count == 0)
        {
            // Antes se saltaba este paso sin avisar; ahora se dice en qué cuenta se buscó.
            _logger.LogInformation("Telegram: la cuenta {IdUsuario} no tiene reservaciones para reportar un problema.", ctx.Cliente!.IdUsuario);
            sb.Linea($"No encontré reservaciones en la cuenta de <b>{FormatoBot.Html(ctx.Nombre)}</b>.")
              .Linea()
              .Linea("Si tu reservación está en otra cuenta, vincula esa cuenta (en la web: Mi cuenta › Telegram). " +
                     "También puedes seguir sin reservación y escribir el código en el detalle.");
            teclado.Boton("Continuar sin reservación", "prbn");
        }
        else
        {
            sb.Linea("<b>¿Con cuál reservación?</b> (o elige «Sin reservación»)");
            foreach (var r in todas.Take(MaximoReservacionesProblema))
                teclado.Boton($"{r.codigo_reserva} · {FormatoBot.Recortar(r.servicio, 22)} · {r.inicio:dd/MM}", $"prbc:{r.codigo_reserva}");
            if (todas.Count > MaximoReservacionesProblema)
                sb.Linea().Linea($"<i>Te muestro las {MaximoReservacionesProblema} más recientes. Si no está aquí, elige «Sin reservación» y escribe el código en el detalle.</i>");
            teclado.Boton("Sin reservación", "prbn");
        }

        teclado.Fila(("← Atrás", "sop"), ("✖ Cancelar", "no"));
        await MostrarAsync(ctx, sb.ToString(), teclado, ct);
    }

    /// <summary>Botón de una reservación de la lista (se vuelve a comprobar que sea del cliente).</summary>
    private async Task ProblemaConReservacionAsync(ContextoBot ctx, ulong idReservacion, CancellationToken ct)
    {
        if (!await RequiereCuentaAsync(ctx, ct)) return;

        var reservacion = (await _soporte.SolicitudesAsync(ctx.Cuenta, ct)).Reservaciones
            .FirstOrDefault(r => r.Value == idReservacion.ToString());
        if (reservacion is null)
        {
            await MostrarAsync(ctx, "No encontré esa reservación en tu cuenta.",
                new Teclado().Boton("🆘 Reportar un problema", "prb").Navegacion(), ct);
            return;
        }
        await IniciarTicketAsync(ctx, "PROBLEMA", idReservacion, reservacion.Text, totalPasos: 4, ct);
    }

    /// <summary>
    /// Reservación elegida por su código: desde la lista del paso 1 (<paramref name="totalPasos"/> = 4) o desde el detalle
    /// de una reservación, "Reportar un problema con esta reservación" (3 pasos: no hay que elegirla).
    /// </summary>
    private async Task ProblemaConCodigoAsync(ContextoBot ctx, string codigo, int totalPasos, CancellationToken ct)
    {
        if (!await RequiereCuentaAsync(ctx, ct)) return;

        var detalle = await _viajes.DetalleAsync(ctx.Cuenta, codigo, ct);   // filtra por la cuenta
        if (detalle is null)
        {
            await MostrarAsync(ctx, "No encontré esa reservación en tu cuenta.", new Teclado().Navegacion("Mis reservaciones", "res"), ct);
            return;
        }
        await IniciarTicketAsync(ctx, "PROBLEMA", detalle.Reservacion.id, detalle.Reservacion.codigo_reserva, totalPasos, ct);
    }

    // =============================================================================================
    // Nuevo ticket · Asunto → Detalle → Confirmar
    // =============================================================================================

    /// <param name="totalPasos">4 si antes se eligió la reservación de una lista; 3 en los demás casos.</param>
    private async Task IniciarTicketAsync(ContextoBot ctx, string tipo, ulong? idReservacion, string? codigo, int totalPasos,
        CancellationToken ct)
    {
        if (!await RequiereCuentaAsync(ctx, ct)) return;

        var datos = new Dictionary<string, string> { [DatoTipo] = tipo, [DatoTotalPasos] = totalPasos.ToString() };
        if (idReservacion is ulong id) datos[DatoIdReservacion] = id.ToString();
        if (codigo is not null) datos[DatoCodigo] = codigo;
        await PasoAsuntoAsync(ctx, datos, error: null, ct);
    }

    private static int TotalPasos(IReadOnlyDictionary<string, string> datos) =>
        datos.TryGetValue(DatoTotalPasos, out var total) && int.TryParse(total, out var n) && n is 3 or 4 ? n : 3;

    private async Task PasoAsuntoAsync(ContextoBot ctx, Dictionary<string, string> datos, string? error, CancellationToken ct)
    {
        var tipo = datos[DatoTipo];
        var total = TotalPasos(datos);
        var corrigiendo = datos.ContainsKey(DatoMensaje);   // ya pasó por el resumen

        var sb = new StringBuilder().Linea(RutaTicket(tipo)).Linea(FormatoBot.Progreso(total - 2, total, "Asunto")).Linea();
        if (error is not null) sb.Linea($"⚠️ {FormatoBot.Html(error)}").Linea();
        if (datos.TryGetValue(DatoCodigo, out var codigo)) sb.Linea($"Reservación: <code>{FormatoBot.Html(codigo)}</code>");
        if (datos.TryGetValue(DatoAsunto, out var actual)) sb.Linea($"Asunto actual: «{FormatoBot.Html(actual)}»");
        sb.Linea("Escribe el <b>asunto</b> (de 5 a 120 caracteres).")
          .Linea(tipo == "PROBLEMA" ? "Ejemplo: <i>El hotel no tiene mi reservación</i>" : "Ejemplo: <i>Paquete para 6 personas en diciembre</i>")
          .Linea()
          .Linea("🔒 No escribas datos de tarjeta ni contraseñas.");

        // "← Atrás": al resumen si se está corrigiendo; si no, al paso anterior (elegir reservación) o a Soporte.
        var atras = corrigiendo ? ("← Volver al resumen", "rev") : total == 4 ? ("← Atrás", "prb") : ("← Atrás", "sop");
        var teclado = new Teclado().Fila(atras, ("✖ Cancelar", "no"));
        await MostrarPasoAsync(ctx, Flujos.Ticket, Pasos.Asunto, datos, sb.ToString(), teclado, ct);
    }

    private async Task PasoDetalleAsync(ContextoBot ctx, Dictionary<string, string> datos, string? error, CancellationToken ct)
    {
        var total = TotalPasos(datos);
        var corrigiendo = datos.ContainsKey(DatoMensaje);

        var sb = new StringBuilder().Linea(RutaTicket(datos[DatoTipo])).Linea(FormatoBot.Progreso(total - 1, total, "Detalle")).Linea();
        if (error is not null) sb.Linea($"⚠️ {FormatoBot.Html(error)}").Linea();
        sb.Linea($"Asunto: {FormatoBot.Html(datos[DatoAsunto])}")
          .Linea()
          .Linea("Ahora cuéntanos el <b>detalle</b> en un solo mensaje (de 10 a 2000 caracteres): qué pasó, fechas, lo que necesitas.");
        if (corrigiendo) sb.Linea().Linea("<i>Lo que escribas reemplaza el detalle anterior.</i>");

        var teclado = new Teclado().Fila(corrigiendo ? ("← Volver al resumen", "rev") : ("← Atrás", "atr"), ("✖ Cancelar", "no"));
        await MostrarPasoAsync(ctx, Flujos.Ticket, Pasos.Mensaje, datos, sb.ToString(), teclado, ct);
    }

    private async Task PasoResumenAsync(ContextoBot ctx, Dictionary<string, string> datos, CancellationToken ct)
    {
        var total = TotalPasos(datos);
        var sb = new StringBuilder()
            .Linea(RutaTicket(datos[DatoTipo]))
            .Linea(FormatoBot.Progreso(total, total, "Confirmar"))
            .Linea()
            .Linea("<b>Revisa tu solicitud</b>")
            .Linea($"Tipo: {FormatoBot.TipoTicket(datos[DatoTipo])}");
        if (datos.TryGetValue(DatoCodigo, out var codigo)) sb.Linea($"Reservación: <code>{FormatoBot.Html(codigo)}</code>");
        sb.Linea($"Asunto: {FormatoBot.Html(datos[DatoAsunto])}")
          .Linea()
          .Linea(FormatoBot.HtmlRecortado(datos[DatoMensaje], 1500));

        var teclado = new Teclado()
            .Boton("✅ Enviar", "ok")
            .Fila(("✏️ Asunto", "eda"), ("✏️ Detalle", "edm"))
            .Boton("✖ Cancelar", "no");
        await MostrarPasoAsync(ctx, Flujos.Ticket, Pasos.Confirmar, datos, sb.ToString(), teclado, ct);
    }

    /// <summary>Texto que escribió el usuario dentro del flujo del ticket.</summary>
    private async Task PasoTicketAsync(ContextoBot ctx, EstadoConversacion estado, string texto, CancellationToken ct)
    {
        if (!ctx.Vinculado)
        {
            await TerminarFlujoAsync(ctx, ct);
            await RequiereCuentaAsync(ctx, ct);
            return;
        }

        var datos = new Dictionary<string, string>(estado.Datos);
        switch (estado.Paso)
        {
            case Pasos.Asunto:
                if (ErrorDeSolicitud(nameof(NuevaSolicitudViewModel.asunto), texto) is { } errorAsunto)
                {
                    await PasoAsuntoAsync(ctx, datos, errorAsunto, ct);   // renueva los 10 minutos
                    return;
                }
                datos[DatoAsunto] = texto;
                // Si ya había detalle (se estaba corrigiendo el asunto desde el resumen), se regresa al resumen.
                if (datos.ContainsKey(DatoMensaje))
                    await PasoResumenAsync(ctx, datos, ct);
                else
                    await PasoDetalleAsync(ctx, datos, error: null, ct);
                return;

            case Pasos.Mensaje:
                if (ErrorDeSolicitud(nameof(NuevaSolicitudViewModel.mensaje), texto) is { } errorMensaje)
                {
                    await PasoDetalleAsync(ctx, datos, errorMensaje, ct);
                    return;
                }
                datos[DatoMensaje] = texto;
                await PasoResumenAsync(ctx, datos, ct);
                return;

            default:
                await MostrarAsync(ctx, "Usa los botones del resumen: ✅ Enviar, ✏️ Asunto, ✏️ Detalle o ✖ Cancelar.", null, ct);
                return;
        }
    }

    /// <summary>"← Atrás" en el paso Detalle: regresa a Asunto (con el asunto que ya escribió).</summary>
    private async Task TicketAtrasAsync(ContextoBot ctx, CancellationToken ct)
    {
        if (_estados.Obtener(ctx.ChatId) is not { Flujo: Flujos.Ticket, Paso: Pasos.Mensaje } estado || !ctx.Vinculado)
        {
            await SolicitudVencidaAsync(ctx, ct);
            return;
        }
        await PasoAsuntoAsync(ctx, new Dictionary<string, string>(estado.Datos), error: null, ct);
    }

    /// <summary>"✏️ Asunto" / "✏️ Detalle" en el resumen.</summary>
    private async Task TicketEditarAsync(ContextoBot ctx, string paso, CancellationToken ct)
    {
        if (_estados.Obtener(ctx.ChatId) is not { Flujo: Flujos.Ticket, Paso: Pasos.Confirmar } estado || !ctx.Vinculado)
        {
            await SolicitudVencidaAsync(ctx, ct);
            return;
        }
        var datos = new Dictionary<string, string>(estado.Datos);
        if (paso == Pasos.Asunto)
            await PasoAsuntoAsync(ctx, datos, error: null, ct);
        else
            await PasoDetalleAsync(ctx, datos, error: null, ct);
    }

    /// <summary>"← Volver al resumen" mientras se corrige el asunto o el detalle.</summary>
    private async Task TicketResumenAsync(ContextoBot ctx, CancellationToken ct)
    {
        if (_estados.Obtener(ctx.ChatId) is not { Flujo: Flujos.Ticket } estado || !ctx.Vinculado
            || estado.Dato(DatoAsunto) is null || estado.Dato(DatoMensaje) is null)
        {
            await SolicitudVencidaAsync(ctx, ct);
            return;
        }
        await PasoResumenAsync(ctx, new Dictionary<string, string>(estado.Datos), ct);
    }

    private async Task SolicitudVencidaAsync(ContextoBot ctx, CancellationToken ct) =>
        await MostrarAsync(ctx,
            "Esa solicitud ya no está activa (pasaron más de 10 minutos sin respuesta). Empieza de nuevo desde Soporte.",
            new Teclado().Boton("💬 Soporte", "sop").Navegacion(), ct);

    private async Task ConfirmarTicketAsync(ContextoBot ctx, CancellationToken ct)
    {
        var estado = _estados.Obtener(ctx.ChatId);
        if (estado is not { Flujo: Flujos.Ticket, Paso: Pasos.Confirmar })
        {
            await SolicitudVencidaAsync(ctx, ct);
            return;
        }
        if (!await RequiereCuentaAsync(ctx, ct)) return;
        _estados.Limpiar(ctx.ChatId);   // un segundo toque a "Enviar" ya no crea otro ticket

        var nueva = new NuevaSolicitudViewModel
        {
            tipo = estado.Dato(DatoTipo) ?? "ASESORIA",
            asunto = estado.Dato(DatoAsunto) ?? string.Empty,
            mensaje = estado.Dato(DatoMensaje) ?? string.Empty,
            id_reservacion = ulong.TryParse(estado.Dato(DatoIdReservacion), out var id) ? id : null
        };

        // Mismas reglas que el formulario de la web (DataAnnotations del ViewModel).
        var errores = new List<ValidationResult>();
        if (!Validator.TryValidateObject(nueva, new ValidationContext(nueva), errores, validateAllProperties: true))
        {
            await MostrarAsync(ctx, $"❌ {FormatoBot.Html(errores[0].ErrorMessage)} Empieza de nuevo desde Soporte.",
                new Teclado().Boton("💬 Soporte", "sop").Navegacion(), ct);
            return;
        }

        // El resumen se reescribe con el resultado: ya no queda un botón "Enviar" que tocar dos veces.
        var resultado = await _soporte.CrearAsync(ctx.Cliente!.IdUsuario, nueva, ct);
        var sb = new StringBuilder().Linea(RutaTicket(nueva.tipo)).Linea();
        var teclado = new Teclado();
        if (resultado.Exitoso && resultado.IdCreado is ulong idTicket)
        {
            _logger.LogInformation("Telegram: el cliente {IdUsuario} abrió el ticket {IdTicket} desde el bot.", ctx.Cliente!.IdUsuario, idTicket);
            sb.Linea($"✅ <b>Ticket #{idTicket} enviado</b>")
              .Linea(FormatoBot.Html(resultado.Mensaje))
              .Linea()
              .Linea("Te avisaré aquí cuando te respondan.");
            teclado.Boton("🎫 Ver mi ticket", $"tkt:{idTicket}").Navegacion();
        }
        else if (resultado.Exitoso)
        {
            sb.Linea($"✅ {FormatoBot.Html(resultado.Mensaje)}").Linea("Te avisaré aquí cuando te respondan.");
            teclado.Navegacion();
        }
        else
        {
            sb.Linea($"❌ {FormatoBot.Html(resultado.Errores is { Count: > 0 } lista ? lista[0].mensaje : resultado.Mensaje)}");
            teclado.Boton("💬 Soporte", "sop").Navegacion();
        }
        await MostrarAsync(ctx, sb.ToString(), teclado, ct);
    }

    /// <summary>Valida un campo con las DataAnnotations de <see cref="NuevaSolicitudViewModel"/>; null = válido.</summary>
    private static string? ErrorDeSolicitud(string propiedad, string valor) =>
        ErrorDeCampo(new NuevaSolicitudViewModel(), propiedad, valor);

    private static string? ErrorDeCampo(object modelo, string propiedad, string valor)
    {
        var errores = new List<ValidationResult>();
        var contexto = new ValidationContext(modelo) { MemberName = propiedad };
        return Validator.TryValidateProperty(valor, contexto, errores)
            ? null
            : errores.FirstOrDefault()?.ErrorMessage ?? "Revisa el texto.";
    }

    // =============================================================================================
    // Mis tickets, conversación y responder
    // =============================================================================================

    private async Task TicketsAsync(ContextoBot ctx, CancellationToken ct)
    {
        if (!await RequiereCuentaAsync(ctx, ct)) return;

        var tickets = (await _soporte.SolicitudesAsync(ctx.Cuenta, ct)).Tickets;
        var abiertos = tickets.Where(t => t.estado != "CERRADA").ToList();
        var mostrar = abiertos.Count > 0 ? abiertos.Take(8).ToList() : tickets.Take(3).ToList();

        var sb = new StringBuilder().Linea(FormatoBot.Ruta(RutaSoporte, "🎫 Mis tickets")).Linea();
        var teclado = new Teclado();
        if (tickets.Count == 0)
        {
            sb.Linea("No tienes tickets de soporte.");
        }
        else
        {
            sb.Linea(abiertos.Count > 0 ? $"<b>Tus tickets abiertos</b> ({abiertos.Count})" : "<b>Tus últimos tickets</b> (todos cerrados)");
            foreach (var t in mostrar)
            {
                var aviso = t.respuesta_nueva ? " · 🔔 te respondieron" : "";
                sb.Linea()
                  .Linea($"<b>#{t.id}</b> · {FormatoBot.TipoTicket(t.tipo)} · {FormatoBot.EstadoTicket(t.estado)}{aviso}")
                  .Linea($"   {FormatoBot.HtmlRecortado(t.asunto, 100)}{(t.codigo_reserva is null ? "" : $" · <code>{FormatoBot.Html(t.codigo_reserva)}</code>")}");
                teclado.Boton($"{(t.respuesta_nueva ? "🔔 " : "")}#{t.id} · {FormatoBot.Recortar(t.asunto, 30)}", $"tkt:{t.id}");
            }
            var cerrados = tickets.Count - abiertos.Count;
            if (abiertos.Count > 0 && cerrados > 0) sb.Linea().Linea($"Cerrados: {cerrados} (los ves en la web).");
        }

        teclado.Fila(("💡 Asesoría", "ase"), ("🆘 Problema", "prb"));
        EnlaceWeb(teclado, sb, "Soporte en la web", "/Solicitar");
        teclado.Navegacion("Soporte", "sop");
        await MostrarAsync(ctx, sb.ToString(), teclado, ct);
    }

    private async Task TicketAsync(ContextoBot ctx, ulong idTicket, CancellationToken ct)
    {
        if (!await RequiereCuentaAsync(ctx, ct)) return;

        var vm = await _soporte.TicketAsync(ctx.Cuenta, idTicket, ct);   // null si no existe o es de otra persona
        if (vm is null)
        {
            await MostrarAsync(ctx, "No encontré ese ticket en tu cuenta.", new Teclado().Navegacion("Mis tickets", "tkt"), ct);
            return;
        }

        var t = vm.Ticket;
        var sb = new StringBuilder()
            .Linea(FormatoBot.Ruta(RutaSoporte, "🎫 Mis tickets", $"#{t.id}"))
            .Linea()
            .Linea($"<b>Ticket #{t.id}</b> · {FormatoBot.TipoTicket(t.tipo)} · {FormatoBot.EstadoTicket(t.estado)}")
            .Linea($"Asunto: {FormatoBot.HtmlRecortado(t.asunto, 120)}");
        if (t.codigo_reserva is not null) sb.Linea($"Reservación: <code>{FormatoBot.Html(t.codigo_reserva)}</code>");
        sb.Linea(t.agente is null ? "Agente: aún sin asignar" : $"Agente: {FormatoBot.Html(t.agente)}");

        var mensajes = vm.Mensajes.TakeLast(6).ToList();
        if (vm.Mensajes.Count > mensajes.Count) sb.Linea().Linea($"<i>(últimos {mensajes.Count} de {vm.Mensajes.Count} mensajes)</i>");
        foreach (var m in mensajes)
        {
            sb.Linea()
              .Linea($"<b>{FormatoBot.Html(m.autor)}</b> · {FormatoBot.FechaHora(m.fecha)}")
              .Linea(FormatoBot.HtmlRecortado(m.texto, 500).Replace("\r", "").Replace("\n", " "));
        }

        var teclado = new Teclado();
        if (t.estado != "CERRADA") teclado.Boton("✍️ Responder", $"rsp:{t.id}");
        EnlaceWeb(teclado, sb, "Ver en la web", $"/Solicitar/Ticket/{t.id}");
        teclado.Navegacion("Mis tickets", "tkt");
        await MostrarAsync(ctx, sb.ToString(), teclado, ct);
    }

    private async Task IniciarRespuestaAsync(ContextoBot ctx, ulong idTicket, CancellationToken ct)
    {
        if (!await RequiereCuentaAsync(ctx, ct)) return;

        var vm = await _soporte.TicketAsync(ctx.Cuenta, idTicket, ct);
        if (vm is null)
        {
            await MostrarAsync(ctx, "No encontré ese ticket en tu cuenta.", new Teclado().Navegacion("Mis tickets", "tkt"), ct);
            return;
        }
        if (vm.Ticket.estado == "CERRADA")
        {
            await MostrarAsync(ctx, "Ese ticket ya está cerrado. Si necesitas algo más, abre otro.",
                new Teclado().Fila(("💡 Asesoría", "ase"), ("🆘 Problema", "prb")).Navegacion(), ct);
            return;
        }

        var datos = new Dictionary<string, string> { [DatoIdTicket] = idTicket.ToString() };
        await PasoRespuestaMensajeAsync(ctx, datos, vm.Ticket.asunto, error: null, ct);
    }

    private async Task PasoRespuestaMensajeAsync(ContextoBot ctx, Dictionary<string, string> datos, string? asunto, string? error,
        CancellationToken ct)
    {
        var idTicket = datos[DatoIdTicket];
        var sb = new StringBuilder().Linea(FormatoBot.Ruta(RutaSoporte, "🎫 Mis tickets", $"#{idTicket}")).Linea();
        if (error is not null) sb.Linea($"⚠️ {FormatoBot.Html(error)}").Linea();
        sb.Linea(asunto is null
            ? $"✍️ Escribe tu respuesta para el ticket <b>#{idTicket}</b> (de 2 a 2000 caracteres)."
            : $"✍️ Escribe tu respuesta para el ticket <b>#{idTicket}</b> «{FormatoBot.HtmlRecortado(asunto, 80)}» (de 2 a 2000 caracteres).");

        var teclado = new Teclado().Boton("✖ Cancelar", "no");
        await MostrarPasoAsync(ctx, Flujos.Respuesta, Pasos.Mensaje, datos, sb.ToString(), teclado, ct);
    }

    private async Task PasoRespuestaAsync(ContextoBot ctx, EstadoConversacion estado, string texto, CancellationToken ct)
    {
        if (!ctx.Vinculado || !ulong.TryParse(estado.Dato(DatoIdTicket), out var idTicket))
        {
            await TerminarFlujoAsync(ctx, ct);
            await RequiereCuentaAsync(ctx, ct);
            return;
        }

        if (ErrorDeCampo(new RespuestaTicketForm(), nameof(RespuestaTicketForm.mensaje), texto) is { } error)
        {
            await PasoRespuestaMensajeAsync(ctx, new Dictionary<string, string>(estado.Datos), asunto: null, error, ct);
            return;
        }

        await TerminarFlujoAsync(ctx, ct);
        // UPDATE condicional en el servicio: solo el dueño y solo si el ticket no se cerró mientras escribía.
        var resultado = await _soporte.ResponderAsync(ctx.Cliente!.IdUsuario, idTicket, texto, ct);
        var teclado = new Teclado().Boton("🎫 Ver el ticket", $"tkt:{idTicket}").Navegacion();
        await MostrarAsync(ctx, (resultado.Exitoso ? "✅ " : "❌ ") + FormatoBot.Html(resultado.Mensaje), teclado, ct);
    }
}
