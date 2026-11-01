using System.ComponentModel.DataAnnotations;
using System.Text;
using FlyVibeMex.Models;
using FlyVibeMex.Services;

namespace FlyVibeMex.Bot;

/// <summary>
/// Soporte desde el bot (regla R9, docs/06 módulo 3) con <see cref="ISoporteClienteService"/>, el MISMO servicio que
/// la pantalla Soporte de la web: abrir un ticket de asesoría o de problema (flujo asunto → mensaje → confirmar),
/// ver los tickets y responder en su conversación. Las validaciones son las del formulario web
/// (<see cref="NuevaSolicitudViewModel"/> y <see cref="RespuestaTicketForm"/>), y el aviso al grupo de agentes lo
/// manda el propio servicio al crear el ticket.
/// </summary>
public sealed partial class ManejadorActualizaciones
{
    private const string DatoTipo = "tipo";
    private const string DatoIdReservacion = "id_reservacion";
    private const string DatoCodigo = "codigo";
    private const string DatoAsunto = "asunto";
    private const string DatoMensaje = "mensaje";
    private const string DatoIdTicket = "id_ticket";

    private async Task SoporteMenuAsync(ContextoBot ctx, CancellationToken ct)
    {
        var sb = new StringBuilder()
            .Linea("<b>Soporte</b> · ¿en qué te ayudamos?")
            .Linea("Un agente de FlyVibeMex atiende tu ticket y te responde aquí y en la web.");
        var teclado = new Teclado()
            .Fila(("💡 Pedir asesoría", "ase"), ("🆘 Reportar un problema", "prb"))
            .Fila(("🎫 Mis tickets", "tkt"), ("↩ Menú", "menu"));
        if (!ctx.Vinculado) sb.Linea().Linea("<i>Para abrir o ver tickets necesitas vincular tu cuenta.</i>");
        await EnviarAsync(ctx.ChatId, sb.ToString(), teclado, ct);
    }

    // =============================================================================================
    // Nuevo ticket: elegir reservación (solo PROBLEMA) → asunto → mensaje → confirmar
    // =============================================================================================

    private async Task ProblemaElegirReservacionAsync(ContextoBot ctx, CancellationToken ct)
    {
        if (!await RequiereCuentaAsync(ctx, ct)) return;

        // Solo las reservaciones de la cuenta (mismo listado que la lista desplegable de la web).
        var reservaciones = (await _soporte.SolicitudesAsync(ctx.Cuenta, ct)).Reservaciones;
        if (reservaciones.Count == 0)
        {
            await IniciarTicketAsync(ctx, "PROBLEMA", null, null, ct);
            return;
        }

        var teclado = new Teclado();
        foreach (var par in reservaciones.Take(8).Chunk(2))
            teclado.Fila(par.Select(r => (r.Text, $"prbr:{r.Value}")).ToArray());
        teclado.Fila(("Sin reservación", "prbn"), ("Cancelar", "menu"));
        await EnviarAsync(ctx.ChatId, "🆘 <b>Reportar un problema</b>\n¿Con cuál reservación? (o elige «Sin reservación»)", teclado, ct);
    }

    /// <summary>Botón de una reservación de la lista (se vuelve a comprobar que sea del cliente).</summary>
    private async Task ProblemaConReservacionAsync(ContextoBot ctx, ulong idReservacion, CancellationToken ct)
    {
        if (!await RequiereCuentaAsync(ctx, ct)) return;

        var reservacion = (await _soporte.SolicitudesAsync(ctx.Cuenta, ct)).Reservaciones
            .FirstOrDefault(r => r.Value == idReservacion.ToString());
        if (reservacion is null)
        {
            await EnviarAsync(ctx.ChatId, "No encontré esa reservación en tu cuenta.", new Teclado().Boton("🆘 Reportar un problema", "prb"), ct);
            return;
        }
        await IniciarTicketAsync(ctx, "PROBLEMA", idReservacion, reservacion.Text, ct);
    }

    /// <summary>Desde el detalle de una reservación ("Reportar un problema con esta reservación").</summary>
    private async Task ProblemaConCodigoAsync(ContextoBot ctx, string codigo, CancellationToken ct)
    {
        if (!await RequiereCuentaAsync(ctx, ct)) return;

        var detalle = await _viajes.DetalleAsync(ctx.Cuenta, codigo, ct);   // filtra por la cuenta
        if (detalle is null)
        {
            await EnviarAsync(ctx.ChatId, "No encontré esa reservación en tu cuenta.", new Teclado().Boton("🧳 Mis reservaciones", "res"), ct);
            return;
        }
        await IniciarTicketAsync(ctx, "PROBLEMA", detalle.Reservacion.id, detalle.Reservacion.codigo_reserva, ct);
    }

    private async Task IniciarTicketAsync(ContextoBot ctx, string tipo, ulong? idReservacion, string? codigo, CancellationToken ct)
    {
        if (!await RequiereCuentaAsync(ctx, ct)) return;

        var datos = new Dictionary<string, string> { [DatoTipo] = tipo };
        if (idReservacion is ulong id) datos[DatoIdReservacion] = id.ToString();
        if (codigo is not null) datos[DatoCodigo] = codigo;
        _estados.Guardar(ctx.ChatId, Flujos.Ticket, Pasos.Asunto, datos);

        var sb = new StringBuilder()
            .Linea(tipo == "PROBLEMA" ? "🆘 <b>Reportar un problema</b>" : "💡 <b>Pedir asesoría</b>");
        if (codigo is not null) sb.Linea($"Reservación: <code>{FormatoBot.Html(codigo)}</code>");
        sb.Linea()
          .Linea("Escribe el <b>asunto</b> (de 5 a 120 caracteres).")
          .Linea(tipo == "PROBLEMA" ? "Ejemplo: <i>El hotel no tiene mi reservación</i>" : "Ejemplo: <i>Paquete para 6 personas en diciembre</i>")
          .Linea()
          .Linea("🔒 No escribas datos de tarjeta ni contraseñas. Para salir: /cancelar");
        await EnviarAsync(ctx.ChatId, sb.ToString(), null, ct);
    }

    private async Task PasoTicketAsync(ContextoBot ctx, EstadoConversacion estado, string texto, CancellationToken ct)
    {
        if (!await RequiereCuentaAsync(ctx, ct))
        {
            _estados.Limpiar(ctx.ChatId);
            return;
        }

        var datos = new Dictionary<string, string>(estado.Datos);
        switch (estado.Paso)
        {
            case Pasos.Asunto:
            {
                if (ErrorDeSolicitud(nameof(NuevaSolicitudViewModel.asunto), texto) is { } error)
                {
                    _estados.Guardar(ctx.ChatId, Flujos.Ticket, Pasos.Asunto, datos);   // renueva los 10 minutos
                    await EnviarAsync(ctx.ChatId, $"{FormatoBot.Html(error)} Escribe el asunto de nuevo.", null, ct);
                    return;
                }
                datos[DatoAsunto] = texto;
                _estados.Guardar(ctx.ChatId, Flujos.Ticket, Pasos.Mensaje, datos);
                await EnviarAsync(ctx.ChatId,
                    "Ahora cuéntanos el <b>detalle</b> en un solo mensaje (de 10 a 2000 caracteres): qué pasó, fechas, lo que necesitas.",
                    null, ct);
                return;
            }
            case Pasos.Mensaje:
            {
                if (ErrorDeSolicitud(nameof(NuevaSolicitudViewModel.mensaje), texto) is { } error)
                {
                    _estados.Guardar(ctx.ChatId, Flujos.Ticket, Pasos.Mensaje, datos);
                    await EnviarAsync(ctx.ChatId, $"{FormatoBot.Html(error)} Escríbelo de nuevo.", null, ct);
                    return;
                }
                datos[DatoMensaje] = texto;
                _estados.Guardar(ctx.ChatId, Flujos.Ticket, Pasos.Confirmar, datos);

                var sb = new StringBuilder()
                    .Linea("<b>Revisa tu solicitud</b>")
                    .Linea($"Tipo: {FormatoBot.TipoTicket(datos[DatoTipo])}");
                if (datos.TryGetValue(DatoCodigo, out var codigo)) sb.Linea($"Reservación: <code>{FormatoBot.Html(codigo)}</code>");
                sb.Linea($"Asunto: {FormatoBot.Html(datos[DatoAsunto])}")
                  .Linea()
                  .Linea(FormatoBot.HtmlRecortado(texto, 1500));
                var teclado = new Teclado().Fila(("✅ Enviar", "ok"), ("✖️ Cancelar", "no"));
                await EnviarAsync(ctx.ChatId, sb.ToString(), teclado, ct);
                return;
            }
            default:
                await EnviarAsync(ctx.ChatId, "Usa los botones ✅ Enviar o ✖️ Cancelar (o escribe /cancelar).", null, ct);
                return;
        }
    }

    private async Task ConfirmarTicketAsync(ContextoBot ctx, CancellationToken ct)
    {
        var estado = _estados.Obtener(ctx.ChatId);
        if (estado is not { Flujo: Flujos.Ticket, Paso: Pasos.Confirmar })
        {
            await EnviarAsync(ctx.ChatId,
                "Esa solicitud ya no está activa (pasaron más de 10 minutos). Empieza de nuevo con /asesoria o /problema.", null, ct);
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
            await EnviarAsync(ctx.ChatId, $"{FormatoBot.Html(errores[0].ErrorMessage)} Empieza de nuevo con /asesoria o /problema.", null, ct);
            return;
        }

        var resultado = await _soporte.CrearAsync(ctx.Cliente!.IdUsuario, nueva, ct);
        var teclado = new Teclado();
        if (resultado.Exitoso && resultado.IdCreado is ulong idTicket)
        {
            _logger.LogInformation("Telegram: el cliente {IdUsuario} abrió el ticket {IdTicket} desde el bot.", ctx.Cliente!.IdUsuario, idTicket);
            teclado.Fila(("🎫 Ver mi ticket", $"tkt:{idTicket}"), ("Menú", "menu"));
        }
        else
            teclado.Boton("Menú", "menu");

        var texto = resultado.Exitoso
            ? $"✅ {FormatoBot.Html(resultado.Mensaje)}\nTe avisaré aquí cuando te respondan."
            : $"❌ {FormatoBot.Html(resultado.Errores is { Count: > 0 } lista ? lista[0].mensaje : resultado.Mensaje)}";
        await EnviarAsync(ctx.ChatId, texto, teclado, ct);
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

        var sb = new StringBuilder();
        var teclado = new Teclado();
        if (tickets.Count == 0)
        {
            sb.Linea("No tienes tickets de soporte.");
        }
        else
        {
            sb.Linea(abiertos.Count > 0 ? $"🎫 <b>Tus tickets abiertos</b> ({abiertos.Count})" : "🎫 <b>Tus últimos tickets</b> (todos cerrados)");
            foreach (var t in mostrar)
            {
                var aviso = t.respuesta_nueva ? " · 🔔 te respondieron" : "";
                sb.Linea()
                  .Linea($"<b>#{t.id}</b> · {FormatoBot.TipoTicket(t.tipo)} · {FormatoBot.EstadoTicket(t.estado)}{aviso}")
                  .Linea($"   {FormatoBot.HtmlRecortado(t.asunto, 100)}{(t.codigo_reserva is null ? "" : $" · <code>{FormatoBot.Html(t.codigo_reserva)}</code>")}");
                teclado.Boton($"#{t.id} · {FormatoBot.Recortar(t.asunto, 30)}", $"tkt:{t.id}");
            }
            var cerrados = tickets.Count - abiertos.Count;
            if (abiertos.Count > 0 && cerrados > 0) sb.Linea().Linea($"Cerrados: {cerrados} (los ves en la web).");
        }

        teclado.Fila(("💡 Asesoría", "ase"), ("🆘 Problema", "prb"));
        teclado.Boton("↩ Menú", "menu");
        EnlaceWeb(teclado, sb, "Soporte en la web", "/Solicitar");
        await EnviarAsync(ctx.ChatId, sb.ToString(), teclado, ct);
    }

    private async Task TicketAsync(ContextoBot ctx, ulong idTicket, CancellationToken ct)
    {
        if (!await RequiereCuentaAsync(ctx, ct)) return;

        var vm = await _soporte.TicketAsync(ctx.Cuenta, idTicket, ct);   // null si no existe o es de otra persona
        if (vm is null)
        {
            await EnviarAsync(ctx.ChatId, "No encontré ese ticket en tu cuenta.", new Teclado().Boton("🎫 Mis tickets", "tkt"), ct);
            return;
        }

        var t = vm.Ticket;
        var sb = new StringBuilder()
            .Linea($"🎫 <b>Ticket #{t.id}</b> · {FormatoBot.TipoTicket(t.tipo)} · {FormatoBot.EstadoTicket(t.estado)}")
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
        teclado.Fila(("🎫 Mis tickets", "tkt"), ("Menú", "menu"));
        EnlaceWeb(teclado, sb, "Ver en la web", $"/Solicitar/Ticket/{t.id}");
        await EnviarAsync(ctx.ChatId, sb.ToString(), teclado, ct);
    }

    private async Task IniciarRespuestaAsync(ContextoBot ctx, ulong idTicket, CancellationToken ct)
    {
        if (!await RequiereCuentaAsync(ctx, ct)) return;

        var vm = await _soporte.TicketAsync(ctx.Cuenta, idTicket, ct);
        if (vm is null)
        {
            await EnviarAsync(ctx.ChatId, "No encontré ese ticket en tu cuenta.", new Teclado().Boton("🎫 Mis tickets", "tkt"), ct);
            return;
        }
        if (vm.Ticket.estado == "CERRADA")
        {
            await EnviarAsync(ctx.ChatId, "Ese ticket ya está cerrado. Si necesitas algo más, abre otro.",
                new Teclado().Fila(("💡 Asesoría", "ase"), ("🆘 Problema", "prb")), ct);
            return;
        }

        _estados.Guardar(ctx.ChatId, Flujos.Respuesta, Pasos.Mensaje,
            new Dictionary<string, string> { [DatoIdTicket] = idTicket.ToString() });
        await EnviarAsync(ctx.ChatId,
            $"✍️ Escribe tu respuesta para el ticket <b>#{idTicket}</b> «{FormatoBot.HtmlRecortado(vm.Ticket.asunto, 80)}» " +
            "(de 2 a 2000 caracteres). Para salir: /cancelar", null, ct);
    }

    private async Task PasoRespuestaAsync(ContextoBot ctx, EstadoConversacion estado, string texto, CancellationToken ct)
    {
        if (!await RequiereCuentaAsync(ctx, ct) || !ulong.TryParse(estado.Dato(DatoIdTicket), out var idTicket))
        {
            _estados.Limpiar(ctx.ChatId);
            return;
        }

        if (ErrorDeCampo(new RespuestaTicketForm(), nameof(RespuestaTicketForm.mensaje), texto) is { } error)
        {
            _estados.Guardar(ctx.ChatId, Flujos.Respuesta, Pasos.Mensaje, estado.Datos);
            await EnviarAsync(ctx.ChatId, $"{FormatoBot.Html(error)} Escríbelo de nuevo.", null, ct);
            return;
        }

        _estados.Limpiar(ctx.ChatId);
        // UPDATE condicional en el servicio: solo el dueño y solo si el ticket no se cerró mientras escribía.
        var resultado = await _soporte.ResponderAsync(ctx.Cliente!.IdUsuario, idTicket, texto, ct);
        var teclado = new Teclado().Fila(("🎫 Ver el ticket", $"tkt:{idTicket}"), ("Menú", "menu"));
        await EnviarAsync(ctx.ChatId, (resultado.Exitoso ? "✅ " : "❌ ") + FormatoBot.Html(resultado.Mensaje), teclado, ct);
    }
}
