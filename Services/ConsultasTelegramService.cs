using FlyVibeMex.Data;
using FlyVibeMex.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FlyVibeMex.Services;

/// <summary>Valores de <c>telegram_avisos.tipo</c> (avisos automáticos que manda <c>Bot/VigilanteViajes.cs</c>).</summary>
public static class TiposAvisoTelegram
{
    /// <summary>Reservación CONFIRMADA o CANCELADA (referencia: historial_estado_reservacion.id).</summary>
    public const string Reservacion = "RESERVACION";
    /// <summary>Cambio del reembolso (referencia: historial_estado_reembolso.id).</summary>
    public const string Reembolso = "REEMBOLSO";
    /// <summary>Recordatorios antes del viaje (referencia: reservaciones.id).</summary>
    public const string Recordatorio72 = "RECORDATORIO_72";
    public const string Recordatorio24 = "RECORDATORIO_24";
}

/// <summary>Qué se reservó y cuándo (primer servicio de la reservación, inicio más temprano y fin más tardío).</summary>
public sealed record ViajeResumen(string Servicio, DateTime Inicio, DateTime Fin);

/// <summary>Cambio de estado de una reservación o de su reembolso que hay que avisarle al cliente vinculado.</summary>
/// <param name="Tipo"><see cref="TiposAvisoTelegram.Reservacion"/> o <see cref="TiposAvisoTelegram.Reembolso"/>.</param>
/// <param name="IdHistorial">Renglón del historial (es la referencia que se guarda en telegram_avisos).</param>
/// <param name="Estado">CONFIRMADA / CANCELADA, o SOLICITADO / APROBADO / RECHAZADO / PROCESADO.</param>
/// <param name="Nota">Motivo de la reservación o detalle del reembolso (lo escribe el personal o el sistema).</param>
/// <param name="Monto">Total de la reservación o monto del reembolso.</param>
public sealed record CambioParaAviso(string Tipo, ulong IdHistorial, ulong IdReservacion, ulong IdCliente, long ChatId,
    string Codigo, string Estado, string? Nota, DateTime Fecha, decimal Monto, string Moneda)
{
    public ViajeResumen? Viaje { get; init; }
}

/// <summary>Reservación CONFIRMADA de un cliente vinculado que empieza pronto (para los recordatorios 72 h / 24 h).</summary>
public sealed record ViajeParaRecordatorio(ulong IdReservacion, ulong IdCliente, long ChatId, string Codigo,
    ViajeResumen Viaje, DateTime CreadaEn, bool Enviado72, bool Enviado24);

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

    /// <summary>
    /// Cambios desde <paramref name="desde"/> que aún no se avisaron: reservaciones que pasaron a CONFIRMADA o CANCELADA
    /// y cualquier cambio de un reembolso. Solo de clientes activos con el bot vinculado y esos avisos encendidos.
    /// Se lee de los historiales, que escriben TODOS los caminos (web, panel y procedimientos almacenados).
    /// </summary>
    Task<List<CambioParaAviso>> CambiosParaAvisarAsync(DateTime desde, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reservaciones CONFIRMADAS de clientes vinculados (con recordatorios encendidos) que empiezan entre
    /// <paramref name="ahora"/> y <paramref name="ahora"/> + <paramref name="anticipacion"/>, con qué recordatorios ya se mandaron.
    /// </summary>
    Task<List<ViajeParaRecordatorio>> ViajesProximosAsync(DateTime ahora, TimeSpan anticipacion, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aparta un aviso ANTES de mandarlo (INSERT en telegram_avisos). Devuelve false si ya estaba apartado: el índice
    /// único (tipo, id_referencia) garantiza que nunca se mande dos veces, aunque haya reinicios.
    /// </summary>
    Task<bool> ApartarAvisoAsync(ulong idUsuario, string tipo, ulong idReferencia, CancellationToken cancellationToken = default);

    /// <summary>Quita un aviso apartado que no se pudo entregar por un problema pasajero (se reintenta en el siguiente ciclo).</summary>
    Task LiberarAvisoAsync(string tipo, ulong idReferencia, CancellationToken cancellationToken = default);
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

    private const string EstadoConfirmada = "CONFIRMADA";
    private const string EstadoCancelada = "CANCELADA";

    public async Task<List<CambioParaAviso>> CambiosParaAvisarAsync(DateTime desde, CancellationToken cancellationToken = default)
    {
        var reservaciones = await (
                from h in _db.HistorialEstadosReservacion.AsNoTracking()
                join e in _db.EstadosReservacion.AsNoTracking() on h.id_estado_reservacion equals e.id
                join r in _db.Reservaciones.AsNoTracking() on h.id_reservacion equals r.id
                join v in _db.TelegramVinculos.AsNoTracking() on r.id_usuarioglobal equals v.id_usuarioglobal
                join u in _db.Usuarios.AsNoTracking() on r.id_usuarioglobal equals u.id
                where h.fecha_cambio >= desde && v.activo && v.avisos_reservaciones && u.activo
                      && (e.nombre == EstadoConfirmada || e.nombre == EstadoCancelada)
                      && !_db.TelegramAvisos.Any(a => a.tipo == TiposAvisoTelegram.Reservacion && a.id_referencia == h.id)
                select new CambioParaAviso(TiposAvisoTelegram.Reservacion, h.id, r.id, r.id_usuarioglobal, v.chat_id,
                    r.codigo_reserva, e.nombre, h.motivo, h.fecha_cambio, r.total, r.moneda))
            .ToListAsync(cancellationToken);

        var reembolsos = await (
                from h in _db.HistorialEstadosReembolso.AsNoTracking()
                join rb in _db.Reembolsos.AsNoTracking() on h.id_reembolso equals rb.id
                join p in _db.Pagos.AsNoTracking() on rb.id_pago equals p.id
                join r in _db.Reservaciones.AsNoTracking() on p.id_reservacion equals r.id
                join v in _db.TelegramVinculos.AsNoTracking() on r.id_usuarioglobal equals v.id_usuarioglobal
                join u in _db.Usuarios.AsNoTracking() on r.id_usuarioglobal equals u.id
                where h.fecha_cambio >= desde && v.activo && v.avisos_reservaciones && u.activo
                      && !_db.TelegramAvisos.Any(a => a.tipo == TiposAvisoTelegram.Reembolso && a.id_referencia == h.id)
                select new CambioParaAviso(TiposAvisoTelegram.Reembolso, h.id, r.id, r.id_usuarioglobal, v.chat_id,
                    r.codigo_reserva, h.estado, h.detalle, h.fecha_cambio, rb.monto, p.moneda))
            .ToListAsync(cancellationToken);

        var cambios = reservaciones.Concat(reembolsos).ToList();
        var viajes = await ResumenViajesAsync(cambios.Select(c => c.IdReservacion), cancellationToken);
        return cambios
            .Select(c => c with { Viaje = viajes.GetValueOrDefault(c.IdReservacion) })
            .OrderBy(c => c.Fecha).ThenBy(c => c.IdHistorial)
            .ToList();
    }

    public async Task<List<ViajeParaRecordatorio>> ViajesProximosAsync(DateTime ahora, TimeSpan anticipacion,
        CancellationToken cancellationToken = default)
    {
        var hasta = ahora + anticipacion;
        var filas = await (
                from r in _db.Reservaciones.AsNoTracking()
                join e in _db.EstadosReservacion.AsNoTracking() on r.id_estado_reservacion equals e.id
                join v in _db.TelegramVinculos.AsNoTracking() on r.id_usuarioglobal equals v.id_usuarioglobal
                join u in _db.Usuarios.AsNoTracking() on r.id_usuarioglobal equals u.id
                let inicio = _db.DetallesReservacion.Where(d => d.id_reservacion == r.id).Min(d => (DateTime?)d.fecha_inicio)
                where e.nombre == EstadoConfirmada && v.activo && v.avisos_recordatorios && u.activo
                      && inicio > ahora && inicio <= hasta
                select new { r.id, r.id_usuarioglobal, v.chat_id, r.codigo_reserva, r.creada_en })
            .ToListAsync(cancellationToken);
        if (filas.Count == 0) return new List<ViajeParaRecordatorio>();

        var ids = filas.Select(f => f.id).ToList();
        var enviados = await _db.TelegramAvisos.AsNoTracking()
            .Where(a => (a.tipo == TiposAvisoTelegram.Recordatorio72 || a.tipo == TiposAvisoTelegram.Recordatorio24)
                        && ids.Contains(a.id_referencia))
            .Select(a => new { a.tipo, a.id_referencia })
            .ToListAsync(cancellationToken);
        var viajes = await ResumenViajesAsync(ids, cancellationToken);

        return filas
            .Where(f => viajes.ContainsKey(f.id))
            .Select(f => new ViajeParaRecordatorio(f.id, f.id_usuarioglobal, f.chat_id, f.codigo_reserva, viajes[f.id], f.creada_en,
                enviados.Any(a => a.tipo == TiposAvisoTelegram.Recordatorio72 && a.id_referencia == f.id),
                enviados.Any(a => a.tipo == TiposAvisoTelegram.Recordatorio24 && a.id_referencia == f.id)))
            .ToList();
    }

    public async Task<bool> ApartarAvisoAsync(ulong idUsuario, string tipo, ulong idReferencia, CancellationToken cancellationToken = default)
    {
        var aviso = new TelegramAvisos { id_usuarioglobal = idUsuario, tipo = tipo, id_referencia = idReferencia, enviado_en = DateTime.Now };
        _db.TelegramAvisos.Add(aviso);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return false;   // índice único: otro ciclo (u otra instancia de la app) ya lo apartó
        }
        finally
        {
            _db.Entry(aviso).State = EntityState.Detached;   // que un INSERT fallido no se repita en el siguiente SaveChanges
        }
    }

    public async Task LiberarAvisoAsync(string tipo, ulong idReferencia, CancellationToken cancellationToken = default) =>
        await _db.TelegramAvisos
            .Where(a => a.tipo == tipo && a.id_referencia == idReferencia)
            .ExecuteDeleteAsync(cancellationToken);

    /// <summary>Servicio (el primero por fecha), inicio más temprano y fin más tardío de cada reservación.</summary>
    private async Task<Dictionary<ulong, ViajeResumen>> ResumenViajesAsync(IEnumerable<ulong> idsReservacion,
        CancellationToken cancellationToken)
    {
        var ids = idsReservacion.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<ulong, ViajeResumen>();

        var detalles = await (
                from d in _db.DetallesReservacion.AsNoTracking()
                join s in _db.Servicios.AsNoTracking() on d.id_servicio equals s.id
                where ids.Contains(d.id_reservacion)
                select new { d.id, d.id_reservacion, s.nombre, d.fecha_inicio, d.fecha_fin })
            .ToListAsync(cancellationToken);

        return detalles
            .GroupBy(d => d.id_reservacion)
            .ToDictionary(g => g.Key, g =>
            {
                var primero = g.OrderBy(d => d.fecha_inicio).ThenBy(d => d.id).First();
                return new ViajeResumen(primero.nombre, g.Min(d => d.fecha_inicio), g.Max(d => d.fecha_fin));
            });
    }
}
