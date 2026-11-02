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
    /// <summary>Mensaje del ticket en el grupo de agentes (referencia: solicitudes_soporte.id; guarda chat e id del mensaje).</summary>
    public const string TicketGrupo = "TICKET_GRUPO";
    /// <summary>Re-aviso de un PROBLEMA urgente que sigue sin agente (referencia: solicitudes_soporte.id).</summary>
    public const string UrgenteSinAgente = "URGENTE_SIN_AGENTE";
    /// <summary>Resumen diario del grupo (referencia: fecha AAAAMMDD).</summary>
    public const string ResumenDiario = "RESUMEN_DIARIO";
}

/// <summary>Estado de un ticket para su mensaje en el grupo de agentes (sin datos personales del cliente).</summary>
/// <param name="Agente">Nombre de quien lo atiende (null = sin agente).</param>
/// <param name="LeTocaAlPersonal">Sigue abierto y el último mensaje es del cliente (o no hay respuestas).</param>
public sealed record TicketParaGrupo(ulong Id, ulong IdCliente, string Tipo, string Asunto, string Estado, string? CodigoReserva,
    DateTime? InicioViaje, string? Agente, bool LeTocaAlPersonal, DateTime CreadaEn, DateTime ActualizadaEn, byte? Calificacion);

/// <summary>Dónde quedó el mensaje de un ticket en el grupo (para editarlo o responderle).</summary>
public sealed record MensajeGrupo(long ChatId, int IdMensaje);

/// <summary>Cifras del resumen de soporte para el grupo de agentes (diario o con /pendientes).</summary>
public sealed record ResumenSoporte(int SinAgente, int EnProceso, int EsperanAlPersonal, int TotalUrgentes,
    List<TicketParaGrupo> Urgentes, List<TicketParaGrupo> MasAntiguos, List<(string agente, int tickets)> PorAgente,
    int CerradosAyer, int CalificacionesSemana, decimal? PromedioSemana);

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
    Task<bool> ApartarAvisoAsync(ulong? idUsuario, string tipo, ulong idReferencia, CancellationToken cancellationToken = default);

    /// <summary>Quita un aviso apartado que no se pudo entregar por un problema pasajero (se reintenta en el siguiente ciclo).</summary>
    Task LiberarAvisoAsync(string tipo, ulong idReferencia, CancellationToken cancellationToken = default);

    // ---- Grupo de agentes (lote 4) ----

    /// <summary>Estado actual de un ticket para su mensaje en el grupo; null si no existe.</summary>
    Task<TicketParaGrupo?> TicketParaGrupoAsync(ulong idTicket, CancellationToken cancellationToken = default);

    /// <summary>Mensaje del ticket en el grupo (si se guardó al crearlo); null en tickets anteriores a esta función.</summary>
    Task<MensajeGrupo?> MensajeGrupoAsync(ulong idTicket, CancellationToken cancellationToken = default);

    /// <summary>Guarda dónde quedó el mensaje de un ticket nuevo en el grupo (false si ya estaba guardado).</summary>
    Task<bool> GuardarMensajeGrupoAsync(ulong idTicket, ulong idCliente, long chatId, int idMensaje, CancellationToken cancellationToken = default);

    /// <summary>Pendientes, urgentes, más antiguos, carga por agente, cerrados ayer y calificación de los últimos 7 días.</summary>
    Task<ResumenSoporte> ResumenSoporteAsync(DateTime ahora, CancellationToken cancellationToken = default);

    /// <summary>
    /// PROBLEMAS abiertos sin agente desde hace más de <paramref name="espera"/>, con viaje en menos de 72 h, que aún no
    /// tuvieron su re-aviso.
    /// </summary>
    Task<List<TicketParaGrupo>> UrgentesSinAgenteAsync(DateTime ahora, TimeSpan espera, CancellationToken cancellationToken = default);
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

    public Task<bool> ApartarAvisoAsync(ulong? idUsuario, string tipo, ulong idReferencia, CancellationToken cancellationToken = default) =>
        InsertarAvisoAsync(new TelegramAvisos
        {
            id_usuarioglobal = idUsuario, tipo = tipo, id_referencia = idReferencia, enviado_en = DateTime.Now
        }, cancellationToken);

    public Task<bool> GuardarMensajeGrupoAsync(ulong idTicket, ulong idCliente, long chatId, int idMensaje,
        CancellationToken cancellationToken = default) =>
        InsertarAvisoAsync(new TelegramAvisos
        {
            id_usuarioglobal = idCliente, tipo = TiposAvisoTelegram.TicketGrupo, id_referencia = idTicket,
            chat_id = chatId, id_mensaje = idMensaje, enviado_en = DateTime.Now
        }, cancellationToken);

    public async Task<MensajeGrupo?> MensajeGrupoAsync(ulong idTicket, CancellationToken cancellationToken = default)
    {
        var fila = await _db.TelegramAvisos.AsNoTracking()
            .Where(a => a.tipo == TiposAvisoTelegram.TicketGrupo && a.id_referencia == idTicket)
            .Select(a => new { a.chat_id, a.id_mensaje })
            .FirstOrDefaultAsync(cancellationToken);
        return fila is { chat_id: long chat, id_mensaje: int mensaje } ? new MensajeGrupo(chat, mensaje) : null;
    }

    public async Task<TicketParaGrupo?> TicketParaGrupoAsync(ulong idTicket, CancellationToken cancellationToken = default) =>
        (await TicketsParaGrupoAsync(_db.SolicitudesSoporte.Where(t => t.id == idTicket), cancellationToken)).FirstOrDefault();

    public async Task<ResumenSoporte> ResumenSoporteAsync(DateTime ahora, CancellationToken cancellationToken = default)
    {
        var pendientes = await TicketsParaGrupoAsync(_db.SolicitudesSoporte.Where(t => t.estado != TicketCerrado), cancellationToken);

        var urgentes = pendientes.Where(t => EsUrgente(t, ahora)).OrderBy(t => t.InicioViaje).ToList();
        var masAntiguos = pendientes
            .Where(t => t.Estado == TicketAbierto || t.LeTocaAlPersonal)   // los que esperan al personal
            .OrderBy(t => t.ActualizadaEn).ThenBy(t => t.Id)
            .Take(5)
            .ToList();
        var porAgente = pendientes
            .Where(t => t.Estado == TicketEnProceso && t.Agente is not null)
            .GroupBy(t => t.Agente!)
            .Select(g => (agente: g.Key, tickets: g.Count()))
            .OrderByDescending(x => x.tickets).ThenBy(x => x.agente)
            .ToList();

        var hoy = ahora.Date;
        var ayer = hoy.AddDays(-1);
        var cerradosAyer = await _db.SolicitudesSoporte.AsNoTracking()
            .CountAsync(t => t.estado == TicketCerrado && t.actualizada_en >= ayer && t.actualizada_en < hoy, cancellationToken);

        var semana = ahora.AddDays(-7);
        var calificaciones = await _db.SolicitudesSoporte.AsNoTracking()
            .Where(t => t.calificacion != null && t.calificada_en >= semana)
            .Select(t => t.calificacion)
            .ToListAsync(cancellationToken);
        decimal? promedio = calificaciones.Count == 0
            ? null
            : Math.Round((decimal)calificaciones.Average(c => (int)c!.Value), 1, MidpointRounding.AwayFromZero);

        return new ResumenSoporte(
            SinAgente: pendientes.Count(t => t.Estado == TicketAbierto),
            EnProceso: pendientes.Count(t => t.Estado == TicketEnProceso),
            EsperanAlPersonal: pendientes.Count(t => t.Estado == TicketEnProceso && t.LeTocaAlPersonal),
            TotalUrgentes: urgentes.Count,
            Urgentes: urgentes.Take(5).ToList(),
            MasAntiguos: masAntiguos,
            PorAgente: porAgente,
            CerradosAyer: cerradosAyer,
            CalificacionesSemana: calificaciones.Count,
            PromedioSemana: promedio);
    }

    public async Task<List<TicketParaGrupo>> UrgentesSinAgenteAsync(DateTime ahora, TimeSpan espera, CancellationToken cancellationToken = default)
    {
        var limite = ahora - espera;
        var candidatos = await TicketsParaGrupoAsync(_db.SolicitudesSoporte.Where(t =>
                t.estado == TicketAbierto && t.id_agente_asignado == null && t.tipo == "PROBLEMA" && t.id_reservacion != null
                && t.creada_en <= limite
                && !_db.TelegramAvisos.Any(a => a.tipo == TiposAvisoTelegram.UrgenteSinAgente && a.id_referencia == t.id)),
            cancellationToken);
        return candidatos.Where(t => EsUrgente(t, ahora)).ToList();
    }

    private const string TicketAbierto = "ABIERTA";
    private const string TicketEnProceso = "EN_PROCESO";
    private const string TicketCerrado = "CERRADA";

    /// <summary>PROBLEMA sin cerrar cuyo viaje empieza en menos de 72 h (misma regla que la etiqueta URGENTE del aviso).</summary>
    public static bool EsUrgente(TicketParaGrupo t, DateTime ahora) =>
        t.Tipo == "PROBLEMA" && t.Estado != TicketCerrado
        && t.InicioViaje is DateTime inicio && inicio > ahora && inicio - ahora <= TimeSpan.FromHours(72);

    /// <summary>Arma <see cref="TicketParaGrupo"/> para los tickets de la consulta (agente, reservación, a quién le toca).</summary>
    private async Task<List<TicketParaGrupo>> TicketsParaGrupoAsync(IQueryable<SolicitudesSoporte> consulta, CancellationToken cancellationToken)
    {
        var tickets = await consulta.AsNoTracking()
            .Select(t => new { t.id, t.id_usuarioglobal, t.tipo, t.asunto, t.estado, t.id_reservacion, t.id_agente_asignado,
                               t.creada_en, t.actualizada_en, t.calificacion })
            .ToListAsync(cancellationToken);
        if (tickets.Count == 0) return new List<TicketParaGrupo>();

        var conversacion = await ConversacionSoporte.ResumenAsync(_db, cancellationToken);

        var idsReservacion = tickets.Where(t => t.id_reservacion is not null).Select(t => t.id_reservacion!.Value).Distinct().ToList();
        var reservaciones = idsReservacion.Count == 0
            ? new Dictionary<ulong, (string codigo, DateTime? inicio)>()
            : (await _db.Reservaciones.AsNoTracking()
                    .Where(r => idsReservacion.Contains(r.id))
                    .Select(r => new
                    {
                        r.id,
                        r.codigo_reserva,
                        inicio = _db.DetallesReservacion.Where(d => d.id_reservacion == r.id).Min(d => (DateTime?)d.fecha_inicio)
                    })
                    .ToListAsync(cancellationToken))
                .ToDictionary(r => r.id, r => (r.codigo_reserva, r.inicio));

        var agentes = new Dictionary<ulong, string>();
        foreach (var idAgente in tickets.Where(t => t.id_agente_asignado is not null).Select(t => t.id_agente_asignado!.Value).Distinct())
            agentes[idAgente] = await ConversacionSoporte.NombreAsync(_db, idAgente, cancellationToken) ?? $"Usuario {idAgente}";

        return tickets.Select(t =>
        {
            var (mensajes, ultimoEsPersonal) = conversacion.GetValueOrDefault(t.id);
            (string codigo, DateTime? inicio)? reservacion =
                t.id_reservacion is ulong idReservacion && reservaciones.TryGetValue(idReservacion, out var r) ? r : null;
            return new TicketParaGrupo(t.id, t.id_usuarioglobal, t.tipo, t.asunto, t.estado, reservacion?.codigo, reservacion?.inicio,
                t.id_agente_asignado is ulong a ? agentes.GetValueOrDefault(a) : null,
                t.estado != TicketCerrado && (mensajes == 0 || !ultimoEsPersonal),
                t.creada_en, t.actualizada_en, t.calificacion);
        }).ToList();
    }

    private async Task<bool> InsertarAvisoAsync(TelegramAvisos aviso, CancellationToken cancellationToken)
    {
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
