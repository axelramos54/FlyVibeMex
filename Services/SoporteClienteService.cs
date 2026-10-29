using FlyVibeMex.Data;
using FlyVibeMex.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace FlyVibeMex.Services;

/// <summary>Implementación de <see cref="ISoporteClienteService"/> con EF Core (consultas LINQ).</summary>
public sealed class SoporteClienteService : ISoporteClienteService
{
    private readonly FlyVibeMexDbContext _db;
    private readonly ILogger<SoporteClienteService> _logger;

    public SoporteClienteService(FlyVibeMexDbContext db, ILogger<SoporteClienteService> logger)
    {
        _db = db;
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
            .Select(t => new { t.id, t.tipo, t.asunto, t.mensaje, t.estado, t.id_agente_asignado, t.id_reservacion, t.creada_en, t.actualizada_en })
            .ToListAsync(cancellationToken);

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
                actualizada_en = t.actualizada_en
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
        _db.SolicitudesSoporte.Add(ticket);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Ticket {IdTicket} creado por el cliente {IdUsuario}.", ticket.id, idUsuario);
        return ResultadoOperacion.Ok($"Recibimos tu solicitud (ticket #{ticket.id}). Un agente la atenderá pronto.");
    }
}
