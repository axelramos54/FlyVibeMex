using FlyVibeMex.Data;
using FlyVibeMex.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace FlyVibeMex.Services;

/// <summary>Implementación de <see cref="ISoportePanelService"/> con EF Core (consultas LINQ) y bitácora.</summary>
/// <remarks>
/// Misma lógica que los procedimientos pa_tomar_ticket, pa_responder_ticket, pa_reasignar_ticket y pa_cerrar_ticket
/// (sql/02_procedimientos_almacenados.sql). Cada cambio de estado es un UPDATE condicional: si otra persona cambió el
/// ticket al mismo tiempo, no se toca nada y se avisa.
/// </remarks>
public sealed class SoportePanelService : ISoportePanelService
{
    private const string Abierta = "ABIERTA";
    private const string EnProceso = "EN_PROCESO";
    private const string Cerrada = "CERRADA";

    private readonly FlyVibeMexDbContext _db;
    private readonly IReservacionesPanelService _reservaciones;
    private readonly IAvisosTelegram _avisos;
    private readonly ILogger<SoportePanelService> _logger;

    public SoportePanelService(FlyVibeMexDbContext db, IReservacionesPanelService reservaciones, IAvisosTelegram avisos,
        ILogger<SoportePanelService> logger)
    {
        _db = db;
        _reservaciones = reservaciones;
        _avisos = avisos;
        _logger = logger;
    }

    // =============================================================================================
    // Consultas
    // =============================================================================================

    public async Task<ResumenSoporteViewModel> ResumenAsync(ulong idAgente, bool esSupervisor, CancellationToken cancellationToken = default)
    {
        var activos = await MisTicketsAsync(idAgente, cerrados: false, cancellationToken);
        return new ResumenSoporteViewModel
        {
            EnFila = await _db.SolicitudesSoporte.AsNoTracking()
                .CountAsync(t => t.estado == Abierta && t.id_agente_asignado == null, cancellationToken),
            MisActivos = activos.Count,
            EsperanRespuesta = activos.Count(t => t.espera_respuesta),
            EsSupervisor = esSupervisor
        };
    }

    public async Task<TicketDetalleViewModel?> SiguienteAsync(ulong idAgente, bool esSupervisor, CancellationToken cancellationToken = default)
    {
        var fila = _db.SolicitudesSoporte.AsNoTracking().Where(t => t.estado == Abierta && t.id_agente_asignado == null);
        var ids = await fila.OrderBy(t => t.creada_en).ThenBy(t => t.id).Select(t => t.id).Take(1).ToListAsync(cancellationToken);
        if (ids.Count == 0) return null;

        var vm = await DetalleAsync(ids[0], idAgente, esSupervisor, cancellationToken);
        if (vm is not null) vm.EnFila = await fila.CountAsync(cancellationToken);
        return vm;
    }

    public async Task<List<TicketPanelItem>> MisTicketsAsync(ulong idAgente, bool cerrados, CancellationToken cancellationToken = default)
    {
        var q = _db.SolicitudesSoporte.AsNoTracking().Where(t => t.id_agente_asignado == idAgente);
        q = cerrados ? q.Where(t => t.estado == Cerrada) : q.Where(t => t.estado != Cerrada);
        var items = await ItemsAsync(q.OrderByDescending(t => t.actualizada_en).Take(200), cancellationToken);

        // El orden se aplica en memoria (el JOIN de ItemsAsync no lo conserva).
        return cerrados
            ? items.OrderByDescending(t => t.actualizada_en).ToList()
            : items.OrderByDescending(t => t.espera_respuesta).ThenBy(t => t.actualizada_en).ToList();
    }

    public async Task<TicketDetalleViewModel?> DetalleAsync(ulong idTicket, ulong idUsuario, bool esSupervisor, CancellationToken cancellationToken = default)
    {
        var ticket = await _db.SolicitudesSoporte.AsNoTracking().FirstOrDefaultAsync(t => t.id == idTicket, cancellationToken);
        if (ticket is null) return null;

        var enFilaLibre = ticket.estado == Abierta && ticket.id_agente_asignado is null;
        var esSuyo = ticket.id_agente_asignado == idUsuario;
        if (!esSupervisor && !esSuyo && !enFilaLibre) return null;   // un agente no ve los tickets de otro

        var item = (await ItemsAsync(_db.SolicitudesSoporte.AsNoTracking().Where(t => t.id == idTicket), cancellationToken)).First();
        var telefono = await _db.Usuarios.AsNoTracking().Where(u => u.id == ticket.id_usuarioglobal)
            .Select(u => u.telefono).FirstOrDefaultAsync(cancellationToken);

        var vm = new TicketDetalleViewModel
        {
            Ticket = item,
            telefono_cliente = telefono,
            Mensajes = await ConversacionSoporte.MensajesAsync(_db, ticket, item.cliente, vistaCliente: false, cancellationToken),
            Reservacion = ticket.id_reservacion is ulong idReservacion
                ? await _reservaciones.ResumenAsync(idReservacion, cancellationToken)
                : null,
            PuedeTomar = enFilaLibre,
            PuedeResponder = ticket.estado == EnProceso && esSuyo,
            PuedeCerrar = ticket.estado == EnProceso && esSuyo,
            PuedeReasignar = esSupervisor && ticket.estado != Cerrada
        };

        if (vm.PuedeReasignar)
        {
            vm.Agentes = (await AgentesAsync(cancellationToken))
                .Where(a => a.id != ticket.id_agente_asignado)
                .Select(a => new SelectListItem($"{a.nombre} ({a.rol})", a.id.ToString()))
                .ToList();
        }
        return vm;
    }

    public async Task<ListaTicketsViewModel> TodosAsync(string? estado, ulong? idAgente, string? texto, int pagina, CancellationToken cancellationToken = default)
    {
        estado = ListaTicketsViewModel.NormalizarEstado(estado);
        texto = GestorBd.Limpiar(texto);
        if (texto is { Length: > 100 }) texto = texto[..100];

        var q = _db.SolicitudesSoporte.AsNoTracking().AsQueryable();
        if (estado is not null) q = q.Where(t => t.estado == estado);
        if (idAgente == 0) q = q.Where(t => t.id_agente_asignado == null);
        else if (idAgente is ulong agente) q = q.Where(t => t.id_agente_asignado == agente);
        if (texto is not null)
        {
            if (ulong.TryParse(texto.TrimStart('#'), out var numero))
                q = q.Where(t => t.id == numero);
            else
                q = q.Where(t => t.asunto.Contains(texto)
                                 || _db.Usuarios.Any(u => u.id == t.id_usuarioglobal && (u.correo.Contains(texto) || u.nombre.Contains(texto))));
        }

        var total = await q.CountAsync(cancellationToken);
        var paginas = Math.Max(1, (int)Math.Ceiling(total / (double)ListaTicketsViewModel.PorPagina));
        pagina = Math.Clamp(pagina, 1, paginas);

        var paginaQ = q.OrderBy(t => t.estado == Cerrada ? 1 : 0)
            .ThenByDescending(t => t.actualizada_en)
            .ThenByDescending(t => t.id)
            .Skip((pagina - 1) * ListaTicketsViewModel.PorPagina)
            .Take(ListaTicketsViewModel.PorPagina);

        var agentes = await AgentesAsync(cancellationToken);
        var opciones = new List<SelectListItem> { new("Sin asignar", "0", idAgente == 0) };
        opciones.AddRange(agentes.Select(a => new SelectListItem($"{a.nombre} ({a.rol})", a.id.ToString(), a.id == idAgente)));

        return new ListaTicketsViewModel
        {
            Estado = estado,
            IdAgente = idAgente,
            Texto = texto,
            Pagina = pagina,
            Total = total,
            Resultados = (await ItemsAsync(paginaQ, cancellationToken))
                .OrderBy(t => t.estado == Cerrada ? 1 : 0).ThenByDescending(t => t.actualizada_en).ThenByDescending(t => t.id)
                .ToList(),
            Agentes = opciones
        };
    }

    // =============================================================================================
    // Acciones
    // =============================================================================================

    public async Task<ResultadoOperacion> TomarAsync(ulong idTicket, ulong idAgente, CancellationToken cancellationToken = default)
    {
        try
        {
            var tomado = false;
            await GestorBd.EnTransaccionAsync(_db, async () =>
            {
                var ahora = DateTime.Now;
                var filas = await _db.SolicitudesSoporte
                    .Where(t => t.id == idTicket && t.id_agente_asignado == null && t.estado == Abierta)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.id_agente_asignado, idAgente)
                        .SetProperty(t => t.estado, EnProceso)
                        .SetProperty(t => t.actualizada_en, ahora), cancellationToken);
                tomado = filas == 1;
                if (!tomado) return;

                _db.RegistrarBitacora(idAgente, "TOMAR", "solicitudes_soporte", idTicket, $"Ticket #{idTicket} tomado.");
                await _db.SaveChangesAsync(cancellationToken);
            }, cancellationToken);

            if (tomado) _avisos.TicketActualizado(idTicket);   // Telegram: el mensaje del ticket en el grupo dice quién lo atiende
            return tomado
                ? ResultadoOperacion.Ok($"Tomaste el ticket #{idTicket}. Ya aparece en \"Seguir atendiendo\".")
                : ResultadoOperacion.Falla("Otra persona ya tomó este ticket (o ya no está abierto).");
        }
        catch (Exception ex) when (GestorBd.EsErrorDeBd(ex))
        {
            _logger.LogError(ex, "No se pudo tomar el ticket {IdTicket}.", idTicket);
            return ResultadoOperacion.Falla("No se pudo tomar el ticket. Intenta de nuevo.");
        }
    }

    public async Task<ResultadoOperacion> ResponderAsync(ulong idTicket, ulong idAgente, string? mensaje, CancellationToken cancellationToken = default)
    {
        mensaje = GestorBd.Limpiar(mensaje);
        if (mensaje is null || mensaje.Length < 2 || mensaje.Length > 2000)
            return ResultadoOperacion.Falla("El mensaje debe tener entre 2 y 2000 caracteres.");

        try
        {
            var guardado = false;
            await GestorBd.EnTransaccionAsync(_db, async () =>
            {
                var ahora = DateTime.Now;
                // UPDATE condicional: si el ticket se cerró o se reasignó mientras escribía, no se agrega el mensaje.
                var filas = await _db.SolicitudesSoporte
                    .Where(t => t.id == idTicket && t.estado == EnProceso && t.id_agente_asignado == idAgente)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.actualizada_en, ahora), cancellationToken);
                guardado = filas == 1;
                if (!guardado) return;

                _db.MensajesSoporte.Add(new MensajesSoporte
                {
                    id_solicitud = idTicket,
                    id_usuario = idAgente,
                    es_personal = true,
                    mensaje = mensaje,
                    creado_en = ahora
                });
                await _db.SaveChangesAsync(cancellationToken);
            }, cancellationToken);

            // Bot de Telegram (docs/06): si el cliente vinculó su cuenta, le llega la respuesta (en segundo plano).
            if (guardado) _avisos.RespuestaDelPersonal(idTicket);

            return guardado
                ? ResultadoOperacion.Ok("Respuesta enviada. El cliente la verá en su sección de Soporte.")
                : ResultadoOperacion.Falla("Solo quien atiende el ticket (en proceso) puede responder. Puede que se haya cerrado o reasignado.");
        }
        catch (Exception ex) when (GestorBd.EsErrorDeBd(ex))
        {
            _logger.LogError(ex, "No se pudo responder el ticket {IdTicket}.", idTicket);
            return ResultadoOperacion.Falla("No se pudo enviar la respuesta. Intenta de nuevo.");
        }
    }

    public async Task<ResultadoOperacion> CerrarAsync(ulong idTicket, ulong idAgente, CancellationToken cancellationToken = default)
    {
        try
        {
            var cerrado = false;
            await GestorBd.EnTransaccionAsync(_db, async () =>
            {
                var ahora = DateTime.Now;
                var filas = await _db.SolicitudesSoporte
                    .Where(t => t.id == idTicket && t.estado == EnProceso && t.id_agente_asignado == idAgente)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.estado, Cerrada)
                        .SetProperty(t => t.actualizada_en, ahora), cancellationToken);
                cerrado = filas == 1;
                if (!cerrado) return;

                _db.RegistrarBitacora(idAgente, "CERRAR", "solicitudes_soporte", idTicket, $"Ticket #{idTicket} cerrado.");
                await _db.SaveChangesAsync(cancellationToken);
            }, cancellationToken);

            if (cerrado) _avisos.TicketCerrado(idTicket);   // Telegram: aviso al cliente vinculado

            return cerrado
                ? ResultadoOperacion.Ok($"Ticket #{idTicket} cerrado. Lo encuentras en tu historial.")
                : ResultadoOperacion.Falla("Solo quien atiende el ticket (en proceso) puede cerrarlo.");
        }
        catch (Exception ex) when (GestorBd.EsErrorDeBd(ex))
        {
            _logger.LogError(ex, "No se pudo cerrar el ticket {IdTicket}.", idTicket);
            return ResultadoOperacion.Falla("No se pudo cerrar el ticket. Intenta de nuevo.");
        }
    }

    public async Task<ResultadoOperacion> ReasignarAsync(ulong idTicket, ulong idAgenteNuevo, ulong idSupervisor, CancellationToken cancellationToken = default)
    {
        var ticket = await _db.SolicitudesSoporte.AsNoTracking()
            .Where(t => t.id == idTicket)
            .Select(t => new { t.estado, t.id_agente_asignado })
            .FirstOrDefaultAsync(cancellationToken);
        if (ticket is null) return ResultadoOperacion.Falla("El ticket no existe.");
        if (ticket.estado == Cerrada) return ResultadoOperacion.Falla("El ticket ya está cerrado.");
        if (ticket.id_agente_asignado == idAgenteNuevo) return ResultadoOperacion.Falla("El ticket ya está asignado a esa persona.");

        // La lista del servidor es la única fuente de verdad (nunca lo que mande el navegador).
        var agentes = await AgentesAsync(cancellationToken);
        var nuevo = agentes.FirstOrDefault(x => x.id == idAgenteNuevo);
        if (nuevo.id == 0) return ResultadoOperacion.Falla("Elige una cuenta activa de SOPORTE, ADMINISTRADOR o SA.");
        var anterior = ticket.id_agente_asignado;
        var nombreAnterior = anterior is ulong idAnterior
            ? await ConversacionSoporte.NombreAsync(_db, idAnterior, cancellationToken) ?? $"usuario {idAnterior}"
            : "(sin agente)";

        try
        {
            var reasignado = false;
            await GestorBd.EnTransaccionAsync(_db, async () =>
            {
                var ahora = DateTime.Now;
                var filas = await _db.SolicitudesSoporte
                    .Where(t => t.id == idTicket && t.estado != Cerrada && t.id_agente_asignado == anterior)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.id_agente_asignado, idAgenteNuevo)
                        .SetProperty(t => t.estado, EnProceso)
                        .SetProperty(t => t.actualizada_en, ahora), cancellationToken);
                reasignado = filas == 1;
                if (!reasignado) return;

                _db.RegistrarBitacora(idSupervisor, "REASIGNAR", "solicitudes_soporte", idTicket,
                    $"Ticket #{idTicket} reasignado de {nombreAnterior} a {nuevo.nombre}.");
                await _db.SaveChangesAsync(cancellationToken);
            }, cancellationToken);

            if (reasignado) _avisos.TicketActualizado(idTicket);
            return reasignado
                ? ResultadoOperacion.Ok($"Ticket #{idTicket} asignado a {nuevo.nombre}.")
                : ResultadoOperacion.Falla("El ticket cambió mientras lo reasignabas. Revisa su estado.");
        }
        catch (Exception ex) when (GestorBd.EsErrorDeBd(ex))
        {
            _logger.LogError(ex, "No se pudo reasignar el ticket {IdTicket}.", idTicket);
            return ResultadoOperacion.Falla("No se pudo reasignar el ticket. Intenta de nuevo.");
        }
    }

    // =============================================================================================
    // Ayudantes
    // =============================================================================================

    /// <summary>Cuentas activas que pueden atender tickets (SOPORTE, ADMINISTRADOR y SA).</summary>
    private async Task<List<(ulong id, string nombre, string rol)>> AgentesAsync(CancellationToken cancellationToken)
    {
        var filas = await (
                from u in _db.Usuarios.AsNoTracking()
                join r in _db.RolesUsuarios.AsNoTracking() on u.id_rol equals r.id
                where u.activo && (r.nombre == Roles.Soporte || r.nombre == Roles.Administrador || r.nombre == Roles.SA)
                orderby u.nombre, u.apellido_paterno
                select new { u.id, nombre = u.nombre + " " + u.apellido_paterno, rol = r.nombre })
            .ToListAsync(cancellationToken);
        return filas.Select(f => (f.id, f.nombre, f.rol)).ToList();
    }

    /// <summary>Convierte tickets en filas del panel: cliente, agente, reservación y estado de la conversación.</summary>
    private async Task<List<TicketPanelItem>> ItemsAsync(IQueryable<SolicitudesSoporte> tickets, CancellationToken cancellationToken)
    {
        var filas = await (
                from t in tickets
                join c in _db.Usuarios.AsNoTracking() on t.id_usuarioglobal equals c.id
                select new { t, cliente = c.nombre + " " + c.apellido_paterno, c.correo })
            .ToListAsync(cancellationToken);
        if (filas.Count == 0) return new List<TicketPanelItem>();

        var conversacion = await ConversacionSoporte.ResumenAsync(_db, cancellationToken);

        // Nombres de agentes y códigos de reservación (pocos distintos por página: una consulta por cada uno).
        var agentes = new Dictionary<ulong, string>();
        foreach (var id in filas.Where(f => f.t.id_agente_asignado is not null).Select(f => f.t.id_agente_asignado!.Value).Distinct())
            agentes[id] = await ConversacionSoporte.NombreAsync(_db, id, cancellationToken) ?? $"Usuario {id}";
        var codigos = new Dictionary<ulong, string>();
        foreach (var id in filas.Where(f => f.t.id_reservacion is not null).Select(f => f.t.id_reservacion!.Value).Distinct())
        {
            var codigo = await _db.Reservaciones.AsNoTracking().Where(r => r.id == id).Select(r => r.codigo_reserva).FirstOrDefaultAsync(cancellationToken);
            if (codigo is not null) codigos[id] = codigo;
        }

        return filas.Select(f =>
        {
            var (mensajes, ultimoEsPersonal) = conversacion.GetValueOrDefault(f.t.id);
            return new TicketPanelItem
            {
                id = f.t.id,
                tipo = f.t.tipo,
                asunto = f.t.asunto,
                estado = f.t.estado,
                id_cliente = f.t.id_usuarioglobal,
                cliente = f.cliente,
                correo_cliente = f.correo,
                id_agente = f.t.id_agente_asignado,
                agente = f.t.id_agente_asignado is ulong a ? agentes.GetValueOrDefault(a) : null,
                id_reservacion = f.t.id_reservacion,
                codigo_reserva = f.t.id_reservacion is ulong r ? codigos.GetValueOrDefault(r) : null,
                creada_en = f.t.creada_en,
                actualizada_en = f.t.actualizada_en,
                mensajes = mensajes,
                // Sin respuestas, el último mensaje es el inicial (del cliente).
                espera_respuesta = f.t.estado != Cerrada && (mensajes == 0 || !ultimoEsPersonal),
                calificacion = f.t.calificacion,
                calificada_en = f.t.calificada_en
            };
        }).ToList();
    }
}
