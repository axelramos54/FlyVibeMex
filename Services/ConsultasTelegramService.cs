using FlyVibeMex.Data;
using Microsoft.EntityFrameworkCore;

namespace FlyVibeMex.Services;

/// <summary>Datos de un ticket para el aviso al grupo de agentes.</summary>
/// <param name="InicioViaje">Inicio más cercano del viaje de la reservación ligada (para la etiqueta URGENTE).</param>
public sealed record TicketParaAviso(ulong Id, string Tipo, string Asunto, string Estado, ulong IdCliente,
    string? CodigoReserva, DateTime? InicioViaje);

/// <summary>Último mensaje del personal en un ticket (para avisar al cliente).</summary>
public sealed record RespuestaParaAviso(ulong IdTicket, string Asunto, ulong IdCliente, string Mensaje);

/// <summary>
/// Consultas de solo lectura que necesita el bot de Telegram y que no tenían ya un servicio del sitio
/// (todo lo demás lo reutiliza: catálogo, Mis reservaciones, Soporte, contenido del sitio).
/// LINQ parametrizado; nunca devuelven datos de tarjetas ni de contraseñas.
/// </summary>
public interface IConsultasTelegramService
{
    /// <summary>Métodos de pago activos (para /ayuda). Recordatorio: el cobro se hace solo en la web.</summary>
    Task<List<(string nombre, string? descripcion)>> MetodosPagoActivosAsync(CancellationToken cancellationToken = default);

    Task<TicketParaAviso?> TicketParaAvisoAsync(ulong idTicket, CancellationToken cancellationToken = default);

    Task<RespuestaParaAviso?> UltimaRespuestaDelPersonalAsync(ulong idTicket, CancellationToken cancellationToken = default);
}

public sealed class ConsultasTelegramService : IConsultasTelegramService
{
    private readonly FlyVibeMexDbContext _db;

    public ConsultasTelegramService(FlyVibeMexDbContext db)
    {
        _db = db;
    }

    public async Task<List<(string nombre, string? descripcion)>> MetodosPagoActivosAsync(CancellationToken cancellationToken = default)
    {
        var metodos = await _db.MetodosPago.AsNoTracking()
            .Where(m => m.activo)
            .OrderBy(m => m.id)
            .Select(m => new { m.nombre, m.descripcion })
            .ToListAsync(cancellationToken);
        return metodos.Select(m => (m.nombre, (string?)m.descripcion)).ToList();
    }

    public async Task<TicketParaAviso?> TicketParaAvisoAsync(ulong idTicket, CancellationToken cancellationToken = default)
    {
        var t = await _db.SolicitudesSoporte.AsNoTracking()
            .Where(s => s.id == idTicket)
            .Select(s => new { s.id, s.tipo, s.asunto, s.estado, s.id_usuarioglobal, s.id_reservacion })
            .FirstOrDefaultAsync(cancellationToken);
        if (t is null) return null;

        string? codigo = null;
        DateTime? inicio = null;
        if (t.id_reservacion is ulong idReservacion)
        {
            codigo = await _db.Reservaciones.AsNoTracking()
                .Where(r => r.id == idReservacion)
                .Select(r => r.codigo_reserva)
                .FirstOrDefaultAsync(cancellationToken);
            inicio = await _db.DetallesReservacion.AsNoTracking()
                .Where(d => d.id_reservacion == idReservacion)
                .OrderBy(d => d.fecha_inicio)
                .Select(d => (DateTime?)d.fecha_inicio)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new TicketParaAviso(t.id, t.tipo, t.asunto, t.estado, t.id_usuarioglobal, codigo, inicio);
    }

    public async Task<RespuestaParaAviso?> UltimaRespuestaDelPersonalAsync(ulong idTicket, CancellationToken cancellationToken = default)
    {
        var ticket = await _db.SolicitudesSoporte.AsNoTracking()
            .Where(s => s.id == idTicket)
            .Select(s => new { s.id, s.asunto, s.id_usuarioglobal })
            .FirstOrDefaultAsync(cancellationToken);
        if (ticket is null) return null;

        var mensaje = await _db.MensajesSoporte.AsNoTracking()
            .Where(m => m.id_solicitud == idTicket && m.es_personal)
            .OrderByDescending(m => m.creado_en).ThenByDescending(m => m.id)
            .Select(m => m.mensaje)
            .FirstOrDefaultAsync(cancellationToken);
        return mensaje is null ? null : new RespuestaParaAviso(ticket.id, ticket.asunto, ticket.id_usuarioglobal, mensaje);
    }
}
