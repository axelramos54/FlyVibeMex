using FlyVibeMex.Data;
using FlyVibeMex.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace FlyVibeMex.Services;

/// <summary>Implementación de <see cref="ISoporteClienteService"/> con EF Core (consultas LINQ).</summary>
public sealed class SoporteClienteService : ISoporteClienteService
{
    private readonly FlyVibeMexDbContext _db;
    private readonly IAvisosTelegram _avisos;
    private readonly ILogger<SoporteClienteService> _logger;

    public SoporteClienteService(FlyVibeMexDbContext db, IAvisosTelegram avisos, ILogger<SoporteClienteService> logger)
    {
        _db = db;
        _avisos = avisos;
        _logger = logger;
    }

    public async Task<MisSolicitudesViewModel> SolicitudesAsync(ContextoCuenta contexto, CancellationToken cancellationToken = default)
    {
        // Reservaciones de la cuenta (id → código), para la lista desplegable y para mostrar el código en cada ticket.
        var reservaciones = await _db.Reservaciones.AsNoTracking()
            .Where(r => r.id_usuarioglobal == contexto.IdCuenta)
            .OrderByDescending(r => r.creada_en)
            .Select(r => new { r.id, r.codigo_reserva })
            .ToListAsync(cancellationToken);
        var codigos = reservaciones.ToDictionary(r => r.id, r => r.codigo_reserva);

        var tickets = await _db.SolicitudesSoporte.AsNoTracking()
            .Where(t => t.id_usuarioglobal == contexto.IdCuenta)
            .OrderByDescending(t => t.creada_en)
            .Select(t => new { t.id, t.tipo, t.asunto, t.mensaje, t.estado, t.id_agente_asignado, t.id_reservacion, t.creada_en, t.actualizada_en, t.calificacion })
            .ToListAsync(cancellationToken);

        var conversacion = await ConversacionSoporte.ResumenAsync(_db, cancellationToken);

        // Nombre de los agentes asignados (pocos: una consulta por agente distinto).
        var agentes = new Dictionary<ulong, string>();
        foreach (var idAgente in tickets.Where(t => t.id_agente_asignado is not null).Select(t => t.id_agente_asignado!.Value).Distinct())
        {
            var nombre = await _db.Usuarios.AsNoTracking()
                .Where(u => u.id == idAgente)
                .Select(u => u.nombre + " " + u.apellido_paterno)
                .FirstOrDefaultAsync(cancellationToken);
            agentes[idAgente] = nombre ?? "Agente de FlyVibeMex";
        }

        return new MisSolicitudesViewModel
        {
            Contexto = contexto,
            Tickets = tickets.Select(t => new TicketListadoViewModel
            {
                id = t.id,
                tipo = t.tipo,
                asunto = t.asunto,
                mensaje = t.mensaje,
                estado = t.estado,
                agente = t.id_agente_asignado is ulong a ? agentes.GetValueOrDefault(a) : null,
                codigo_reserva = t.id_reservacion is ulong r ? codigos.GetValueOrDefault(r) : null,
                creada_en = t.creada_en,
                actualizada_en = t.actualizada_en,
                mensajes = conversacion.GetValueOrDefault(t.id).mensajes,
                // Le toca al cliente: la última respuesta es del personal y el ticket sigue abierto.
                respuesta_nueva = t.estado != "CERRADA" && conversacion.TryGetValue(t.id, out var c) && c.ultimoEsPersonal,
                calificacion = t.calificacion
            }).ToList(),
            Reservaciones = reservaciones.Select(r => new SelectListItem(r.codigo_reserva, r.id.ToString())).ToList()
        };
    }

    public async Task<ResultadoOperacion> CrearAsync(ulong idUsuario, NuevaSolicitudViewModel nueva, CancellationToken cancellationToken = default)
    {
        if (nueva.id_reservacion is ulong idReservacion &&
            !await _db.Reservaciones.AsNoTracking().AnyAsync(r => r.id == idReservacion && r.id_usuarioglobal == idUsuario, cancellationToken))
        {
            return new ResultadoOperacion(false, "Revisa los datos.",
                new() { (nameof(nueva.id_reservacion), "Elige una de tus reservaciones.") });
        }

        var ahora = DateTime.Now;
        var ticket = new SolicitudesSoporte
        {
            id_usuarioglobal = idUsuario,
            id_reservacion = nueva.id_reservacion,
            id_agente_asignado = null,
            tipo = nueva.tipo,
            asunto = nueva.asunto.Trim(),
            mensaje = nueva.mensaje.Trim(),
            estado = "ABIERTA",
            creada_en = ahora,
            actualizada_en = ahora
        };
        try
        {
            _db.SolicitudesSoporte.Add(ticket);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (GestorBd.EsErrorDeBd(ex))
        {
            _logger.LogError(ex, "No se pudo crear el ticket del cliente {IdUsuario}.", idUsuario);
            return ResultadoOperacion.Falla("No pudimos registrar tu solicitud. Intenta de nuevo más tarde.");
        }

        _logger.LogInformation("Ticket {IdTicket} creado por el cliente {IdUsuario}.", ticket.id, idUsuario);
        // Bot de Telegram (docs/06): aviso al grupo de agentes, en segundo plano (no hace esperar al cliente).
        _avisos.TicketCreado(ticket.id);
        return ResultadoOperacion.Ok($"Recibimos tu solicitud (ticket #{ticket.id}). Un agente la atenderá pronto.") with { IdCreado = ticket.id };
    }

    public async Task<TicketClienteViewModel?> TicketAsync(ContextoCuenta contexto, ulong idTicket, CancellationToken cancellationToken = default)
    {
        // Solo los tickets de la cuenta del contexto (otro id = "no existe": evita IDOR).
        var ticket = await _db.SolicitudesSoporte.AsNoTracking()
            .FirstOrDefaultAsync(t => t.id == idTicket && t.id_usuarioglobal == contexto.IdCuenta, cancellationToken);
        if (ticket is null) return null;

        var item = (await SolicitudesAsync(contexto, cancellationToken)).Tickets.FirstOrDefault(t => t.id == idTicket);
        if (item is null) return null;
        var nombreCliente = await ConversacionSoporte.NombreAsync(_db, ticket.id_usuarioglobal, cancellationToken) ?? "Cliente";

        return new TicketClienteViewModel
        {
            Contexto = contexto,
            Ticket = item,
            Mensajes = await ConversacionSoporte.MensajesAsync(_db, ticket, nombreCliente, vistaCliente: !contexto.EsInterno, cancellationToken)
        };
    }

    public async Task<ResultadoOperacion> ResponderAsync(ulong idUsuario, ulong idTicket, string? mensaje, CancellationToken cancellationToken = default)
    {
        mensaje = string.IsNullOrWhiteSpace(mensaje) ? null : mensaje.Trim();
        if (mensaje is null || mensaje.Length < 2 || mensaje.Length > 2000)
            return new ResultadoOperacion(false, "Revisa tu mensaje.",
                new() { (nameof(RespuestaTicketForm.mensaje), "El mensaje debe tener entre 2 y 2000 caracteres.") });

        try
        {
            var guardado = false;
            await GestorBd.EnTransaccionAsync(_db, async () =>
            {
                var ahora = DateTime.Now;
                // UPDATE condicional: solo el dueño y solo si el ticket no se cerró mientras escribía.
                var filas = await _db.SolicitudesSoporte
                    .Where(t => t.id == idTicket && t.id_usuarioglobal == idUsuario && t.estado != "CERRADA")
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.actualizada_en, ahora), cancellationToken);
                guardado = filas == 1;
                if (!guardado) return;

                _db.MensajesSoporte.Add(new MensajesSoporte
                {
                    id_solicitud = idTicket,
                    id_usuario = idUsuario,
                    es_personal = false,
                    mensaje = mensaje,
                    creado_en = ahora
                });
                await _db.SaveChangesAsync(cancellationToken);
            }, cancellationToken);

            if (guardado) _avisos.ClienteRespondio(idTicket);   // Telegram: grupo de agentes
            return guardado
                ? ResultadoOperacion.Ok("Enviamos tu mensaje. Te avisaremos aquí cuando te respondan.")
                : ResultadoOperacion.Falla("Este ticket ya está cerrado. Si necesitas algo más, abre una nueva solicitud.");
        }
        catch (Exception ex) when (GestorBd.EsErrorDeBd(ex))
        {
            _logger.LogError(ex, "No se pudo responder el ticket {IdTicket} del cliente {IdUsuario}.", idTicket, idUsuario);
            return ResultadoOperacion.Falla("No pudimos enviar tu mensaje. Intenta de nuevo más tarde.");
        }
    }

    public async Task<ResultadoCalificacion> CalificarAsync(ulong idUsuario, ulong idTicket, int estrellas, CancellationToken cancellationToken = default)
    {
        if (estrellas is < 1 or > 5) return ResultadoCalificacion.NoDisponible;

        try
        {
            // UPDATE condicional: solo el dueño, solo un ticket CERRADO y solo una vez (calificacion IS NULL).
            var ahora = DateTime.Now;
            var calificacion = (byte)estrellas;
            var filas = await _db.SolicitudesSoporte
                .Where(t => t.id == idTicket && t.id_usuarioglobal == idUsuario && t.estado == "CERRADA" && t.calificacion == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.calificacion, (byte?)calificacion)
                    .SetProperty(t => t.calificada_en, (DateTime?)ahora), cancellationToken);
            if (filas == 1)
            {
                _logger.LogInformation("El cliente {IdUsuario} calificó el ticket {IdTicket} con {Estrellas} estrellas.", idUsuario, idTicket, estrellas);
                _avisos.TicketActualizado(idTicket);   // la calificación aparece en el mensaje del ticket en el grupo
                return ResultadoCalificacion.Guardada;
            }

            var yaCalificado = await _db.SolicitudesSoporte.AsNoTracking()
                .AnyAsync(t => t.id == idTicket && t.id_usuarioglobal == idUsuario && t.calificacion != null, cancellationToken);
            return yaCalificado ? ResultadoCalificacion.YaCalificado : ResultadoCalificacion.NoDisponible;
        }
        catch (Exception ex) when (GestorBd.EsErrorDeBd(ex))
        {
            _logger.LogError(ex, "No se pudo calificar el ticket {IdTicket} del cliente {IdUsuario}.", idTicket, idUsuario);
            return ResultadoCalificacion.ErrorBd;
        }
    }
}
