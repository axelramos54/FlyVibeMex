using FlyVibeMex.Data;
using FlyVibeMex.Models;
using Microsoft.EntityFrameworkCore;

namespace FlyVibeMex.Services;

/// <summary>
/// Consultas de la conversación de los tickets (tabla <c>mensajes_soporte</c>) que comparten el soporte del cliente
/// (<see cref="SoporteClienteService"/>) y el panel (<see cref="SoportePanelService"/>).
/// </summary>
public static class ConversacionSoporte
{
    /// <summary>Por ticket: número de respuestas y si la ÚLTIMA la escribió el personal.</summary>
    public static async Task<Dictionary<ulong, (int mensajes, bool ultimoEsPersonal)>> ResumenAsync(
        FlyVibeMexDbContext db, CancellationToken cancellationToken)
    {
        var conteos = await db.MensajesSoporte.AsNoTracking()
            .GroupBy(m => m.id_solicitud)
            .Select(g => new { id = g.Key, n = g.Count() })
            .ToDictionaryAsync(x => x.id, x => x.n, cancellationToken);

        // El último mensaje de cada ticket: aquel después del cual no hay otro (se insertan en orden).
        var ultimos = await db.MensajesSoporte.AsNoTracking()
            .Where(m => !db.MensajesSoporte.Any(x => x.id_solicitud == m.id_solicitud && x.id > m.id))
            .Select(m => new { m.id_solicitud, m.es_personal })
            .ToDictionaryAsync(x => x.id_solicitud, x => x.es_personal, cancellationToken);

        return conteos.ToDictionary(c => c.Key, c => (c.Value, ultimos.GetValueOrDefault(c.Key)));
    }

    /// <summary>
    /// La conversación completa en orden: primero el mensaje con el que se abrió el ticket y después las respuestas.
    /// </summary>
    /// <param name="vistaCliente">true = la ve el cliente ("Tú" / "Nombre · FlyVibeMex"); false = la ve el personal.</param>
    public static async Task<List<MensajeTicketItem>> MensajesAsync(FlyVibeMexDbContext db, SolicitudesSoporte ticket,
        string nombreCliente, bool vistaCliente, CancellationToken cancellationToken)
    {
        var idTicket = ticket.id;
        var filas = await (
                from m in db.MensajesSoporte.AsNoTracking()
                join u in db.Usuarios.AsNoTracking() on m.id_usuario equals u.id
                where m.id_solicitud == idTicket
                orderby m.creado_en, m.id
                select new { m.creado_en, m.es_personal, m.mensaje, nombre = u.nombre + " " + u.apellido_paterno })
            .ToListAsync(cancellationToken);

        string Autor(bool esPersonal, string nombre) => vistaCliente
            ? (esPersonal ? $"{nombre} · FlyVibeMex" : "Tú")
            : (esPersonal ? $"{nombre} (personal)" : nombre);

        var mensajes = new List<MensajeTicketItem>
        {
            new(ticket.creada_en, Autor(false, nombreCliente), false, ticket.mensaje)
        };
        mensajes.AddRange(filas.Select(f => new MensajeTicketItem(f.creado_en, Autor(f.es_personal, f.nombre), f.es_personal, f.mensaje)));
        return mensajes;
    }

    /// <summary>Nombre corto ("Nombre Apellido") de una cuenta; <c>null</c> si no existe.</summary>
    public static Task<string?> NombreAsync(FlyVibeMexDbContext db, ulong idUsuario, CancellationToken cancellationToken) =>
        db.Usuarios.AsNoTracking()
            .Where(u => u.id == idUsuario)
            .Select(u => u.nombre + " " + u.apellido_paterno)
            .FirstOrDefaultAsync(cancellationToken);
}
