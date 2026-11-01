using FlyVibeMex.Data;
using FlyVibeMex.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace FlyVibeMex.Services;

/// <summary>Implementación de <see cref="IReservacionesPanelService"/> con EF Core (consultas LINQ, solo lectura).</summary>
public sealed class ReservacionesPanelService : IReservacionesPanelService
{
    private const string ReembolsoSolicitado = "SOLICITADO";
    private const string ReembolsoRechazado = "RECHAZADO";

    private readonly FlyVibeMexDbContext _db;
    private readonly ICancelacionesService _cancelaciones;

    public ReservacionesPanelService(FlyVibeMexDbContext db, ICancelacionesService cancelaciones)
    {
        _db = db;
        _cancelaciones = cancelaciones;
    }

    // =============================================================================================
    // Lista
    // =============================================================================================

    public async Task<ListaReservacionesPanelViewModel> ListaAsync(FiltroReservacionesPanel filtro, CancellationToken cancellationToken = default)
    {
        filtro.codigo = Recortar(filtro.codigo, 30);
        filtro.correo = Recortar(filtro.correo, 150);
        filtro.estado = Recortar(filtro.estado, 30);

        var q = from r in _db.Reservaciones.AsNoTracking()
                join u in _db.Usuarios.AsNoTracking() on r.id_usuarioglobal equals u.id
                join e in _db.EstadosReservacion.AsNoTracking() on r.id_estado_reservacion equals e.id
                select new { r, u.nombre, u.apellido_paterno, u.correo, estado = e.nombre };

        if (filtro.codigo is string codigo) q = q.Where(x => x.r.codigo_reserva.Contains(codigo));
        if (filtro.correo is string correo) q = q.Where(x => x.correo.Contains(correo));
        if (filtro.estado is string estado) q = q.Where(x => x.estado == estado);
        if (filtro.desde is DateTime desde)
        {
            var inicio = desde.Date;
            q = q.Where(x => x.r.creada_en >= inicio);
        }
        if (filtro.hasta is DateTime hasta)
        {
            var fin = hasta.Date.AddDays(1);
            q = q.Where(x => x.r.creada_en < fin);
        }

        var total = await q.CountAsync(cancellationToken);
        var paginas = Math.Max(1, (int)Math.Ceiling(total / (double)ListaReservacionesPanelViewModel.PorPagina));
        filtro.pagina = Math.Clamp(filtro.pagina, 1, paginas);

        var filas = await q.OrderByDescending(x => x.r.creada_en).ThenByDescending(x => x.r.id)
            .Skip((filtro.pagina - 1) * ListaReservacionesPanelViewModel.PorPagina)
            .Take(ListaReservacionesPanelViewModel.PorPagina)
            .ToListAsync(cancellationToken);

        // Servicios y reembolsos pendientes de las reservaciones de la página (por rango de ids: sin listas en SQL).
        var resultados = new List<ReservacionPanelItem>();
        if (filas.Count > 0)
        {
            var minimo = filas.Min(f => f.r.id);
            var maximo = filas.Max(f => f.r.id);
            var lineas = await (
                    from d in _db.DetallesReservacion.AsNoTracking()
                    join s in _db.Servicios.AsNoTracking() on d.id_servicio equals s.id
                    where d.id_reservacion >= minimo && d.id_reservacion <= maximo
                    orderby d.id
                    select new { d.id_reservacion, s.nombre, d.fecha_inicio })
                .ToListAsync(cancellationToken);
            var pendientes = await (
                    from rb in _db.Reembolsos.AsNoTracking()
                    join p in _db.Pagos.AsNoTracking() on rb.id_pago equals p.id
                    where rb.estado == ReembolsoSolicitado && p.id_reservacion >= minimo && p.id_reservacion <= maximo
                    select p.id_reservacion)
                .ToListAsync(cancellationToken);

            resultados = filas.Select(f =>
            {
                var mias = lineas.Where(l => l.id_reservacion == f.r.id).ToList();
                return new ReservacionPanelItem
                {
                    id = f.r.id,
                    codigo_reserva = f.r.codigo_reserva,
                    cliente = $"{f.nombre} {f.apellido_paterno}",
                    correo_cliente = f.correo,
                    estado = f.estado,
                    creada_en = f.r.creada_en,
                    inicio = mias.Count == 0 ? null : mias.Min(l => l.fecha_inicio),
                    servicio = mias.Count switch
                    {
                        0 => "(sin detalle)",
                        1 => mias[0].nombre,
                        _ => $"{mias[0].nombre} y {mias.Count - 1} más"
                    },
                    total = f.r.total,
                    moneda = f.r.moneda,
                    reembolso_pendiente = pendientes.Contains(f.r.id)
                };
            }).ToList();
        }

        var estados = await _db.EstadosReservacion.AsNoTracking().OrderBy(e => e.id).Select(e => e.nombre).ToListAsync(cancellationToken);

        return new ListaReservacionesPanelViewModel
        {
            Filtro = filtro,
            Total = total,
            Resultados = resultados,
            Estados = estados.Select(e => new SelectListItem(e, e, e == filtro.estado)).ToList(),
            ReembolsosPendientes = await _db.Reembolsos.AsNoTracking().CountAsync(rb => rb.estado == ReembolsoSolicitado, cancellationToken)
        };
    }

    // =============================================================================================
    // Resumen y detalle
    // =============================================================================================

    public async Task<ulong?> IdPorCodigoAsync(string codigoReserva, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(codigoReserva) || codigoReserva.Length > 30) return null;
        var ids = await _db.Reservaciones.AsNoTracking().Where(r => r.codigo_reserva == codigoReserva)
            .Select(r => r.id).Take(1).ToListAsync(cancellationToken);
        return ids.Count > 0 ? ids[0] : null;
    }

    public async Task<ResumenReservacionViewModel?> ResumenAsync(ulong idReservacion, CancellationToken cancellationToken = default)
    {
        var cabecera = await (
                from rv in _db.Reservaciones.AsNoTracking()
                join u in _db.Usuarios.AsNoTracking() on rv.id_usuarioglobal equals u.id
                join e in _db.EstadosReservacion.AsNoTracking() on rv.id_estado_reservacion equals e.id
                where rv.id == idReservacion
                select new { r = rv, cliente = u.nombre + " " + u.apellido_paterno, u.correo, u.telefono, estado = e.nombre })
            .FirstOrDefaultAsync(cancellationToken);
        if (cabecera is null) return null;

        var lineas = await (
                from d in _db.DetallesReservacion.AsNoTracking()
                join s in _db.Servicios.AsNoTracking() on d.id_servicio equals s.id
                where d.id_reservacion == idReservacion
                orderby d.id
                select new
                {
                    s.nombre,
                    s.tipo_servicio,
                    d.descripcion_snapshot,
                    d.fecha_inicio,
                    d.fecha_fin,
                    d.cantidad,
                    d.precio_unitario,
                    d.descuento_aplicado,
                    d.subtotal,
                    d.permite_cancelacion,
                    d.horas_reembolso_total,
                    d.porcentaje_reembolso_tardio,
                    d.politica_cancelacion
                })
            .ToListAsync(cancellationToken);

        var pago = await (
                from p in _db.Pagos.AsNoTracking()
                join e in _db.EstadosPago.AsNoTracking() on p.id_estado_pago equals e.id
                where p.id_reservacion == idReservacion
                orderby p.id descending
                select new { p.monto, estado = e.nombre })
            .FirstOrDefaultAsync(cancellationToken);

        var reembolso = await (
                from rb in _db.Reembolsos.AsNoTracking()
                join p in _db.Pagos.AsNoTracking() on rb.id_pago equals p.id
                where p.id_reservacion == idReservacion
                orderby rb.id descending
                select new { rb.monto, rb.estado })
            .FirstOrDefaultAsync(cancellationToken);

        var r = cabecera.r;
        return new ResumenReservacionViewModel
        {
            id = r.id,
            codigo_reserva = r.codigo_reserva,
            estado = cabecera.estado,
            creada_en = r.creada_en,
            subtotal = r.subtotal,
            descuento_total = r.descuento_total,
            total = r.total,
            moneda = r.moneda,
            id_cliente = r.id_usuarioglobal,
            cliente = cabecera.cliente,
            correo_cliente = cabecera.correo,
            telefono_cliente = cabecera.telefono,
            Lineas = lineas.Select(l => new LineaReservacionItem
            {
                servicio = l.nombre,
                tipo = l.tipo_servicio,
                descripcion = l.descripcion_snapshot,
                inicio = l.fecha_inicio,
                fin = l.fecha_fin,
                cantidad = l.cantidad,
                precio_unitario = l.precio_unitario,
                descuento = l.descuento_aplicado,
                subtotal = l.subtotal,
                politica = string.IsNullOrWhiteSpace(l.politica_cancelacion)
                    ? PoliticaCancelacion.Describir(l.permite_cancelacion, l.horas_reembolso_total, l.porcentaje_reembolso_tardio)
                    : l.politica_cancelacion
            }).ToList(),
            estado_pago = pago?.estado,
            monto_pagado = pago?.monto,
            estado_reembolso = reembolso?.estado,
            monto_reembolso = reembolso?.monto
        };
    }

    public async Task<DetalleReservacionPanelViewModel?> DetalleAsync(string codigoReserva, CancellationToken cancellationToken = default)
    {
        var id = await IdPorCodigoAsync(codigoReserva, cancellationToken);
        if (id is not ulong idReservacion) return null;
        var resumen = await ResumenAsync(idReservacion, cancellationToken);
        if (resumen is null) return null;

        var nombres = new Dictionary<ulong, string>();
        async Task<string> QuienAsync(ulong? idUsuario)
        {
            if (idUsuario is not ulong u) return "Sistema";
            if (u == resumen.id_cliente) return "Cliente";
            if (!nombres.TryGetValue(u, out var nombre))
            {
                nombre = (await ConversacionSoporte.NombreAsync(_db, u, cancellationToken)) is string n ? $"{n} (personal)" : "Personal";
                nombres[u] = nombre;
            }
            return nombre;
        }

        // Promoción usada (si alguna línea la tiene).
        var idsPromocion = await _db.DetallesReservacion.AsNoTracking()
            .Where(d => d.id_reservacion == idReservacion && d.id_promocion != null)
            .Select(d => d.id_promocion!.Value)
            .Take(1)
            .ToListAsync(cancellationToken);
        string? promocion = null;
        if (idsPromocion.Count > 0)
        {
            var idPromocion = idsPromocion[0];
            promocion = await _db.Promociones.AsNoTracking().Where(p => p.id == idPromocion).Select(p => p.codigo).FirstOrDefaultAsync(cancellationToken);
        }

        // Historial de estados de la reservación.
        var historialFilas = await (
                from h in _db.HistorialEstadosReservacion.AsNoTracking()
                join e in _db.EstadosReservacion.AsNoTracking() on h.id_estado_reservacion equals e.id
                where h.id_reservacion == idReservacion
                orderby h.fecha_cambio, h.id
                select new { h.fecha_cambio, estado = e.nombre, h.motivo, h.id_usuario_cambio })
            .ToListAsync(cancellationToken);
        var historial = new List<HistorialEstadoItem>();
        foreach (var h in historialFilas)
            historial.Add(new HistorialEstadoItem(h.fecha_cambio, h.estado, h.motivo, await QuienAsync(h.id_usuario_cambio)));

        // Pagos con su historial.
        var pagosFilas = await (
                from p in _db.Pagos.AsNoTracking()
                join m in _db.MetodosPago.AsNoTracking() on p.id_metodo_pago equals m.id
                join t in _db.TarjetasUsuarios.AsNoTracking() on p.id_tarjeta equals t.id
                join e in _db.EstadosPago.AsNoTracking() on p.id_estado_pago equals e.id
                where p.id_reservacion == idReservacion
                orderby p.id
                select new { p.id, p.fecha_pago, p.monto, p.moneda, estado = e.nombre, metodo = m.nombre, t.marca, t.ultimos4, p.referencia_externa })
            .ToListAsync(cancellationToken);
        var historialPagos = await (
                from h in _db.HistorialEstadosPago.AsNoTracking()
                join p in _db.Pagos.AsNoTracking() on h.id_pago equals p.id
                join e in _db.EstadosPago.AsNoTracking() on h.id_estado_pago equals e.id
                where p.id_reservacion == idReservacion
                orderby h.fecha_cambio, h.id
                select new { h.id_pago, h.fecha_cambio, estado = e.nombre, h.detalle })
            .ToListAsync(cancellationToken);

        // Reembolsos con su historial.
        var reembolsos = await ReembolsosDeAsync(idReservacion, cancellationToken);
        foreach (var rb in reembolsos)
        {
            var idReembolso = rb.id;
            var filas = await _db.HistorialEstadosReembolso.AsNoTracking()
                .Where(h => h.id_reembolso == idReembolso)
                .OrderBy(h => h.fecha_cambio).ThenBy(h => h.id)
                .Select(h => new { h.fecha_cambio, h.estado, h.detalle, h.id_usuario_cambio })
                .ToListAsync(cancellationToken);
            foreach (var h in filas)
                rb.Historial.Add(new HistorialEstadoItem(h.fecha_cambio, h.estado, h.detalle, await QuienAsync(h.id_usuario_cambio)));
        }

        // Tickets ligados a la reservación.
        var tickets = await _db.SolicitudesSoporte.AsNoTracking()
            .Where(t => t.id_reservacion == idReservacion)
            .OrderByDescending(t => t.creada_en)
            .Select(t => new { t.id, t.asunto, t.estado, t.tipo, t.id_agente_asignado, t.creada_en, t.actualizada_en })
            .ToListAsync(cancellationToken);
        var itemsTickets = new List<TicketPanelItem>();
        foreach (var t in tickets)
        {
            itemsTickets.Add(new TicketPanelItem
            {
                id = t.id,
                asunto = t.asunto,
                estado = t.estado,
                tipo = t.tipo,
                id_agente = t.id_agente_asignado,
                agente = t.id_agente_asignado is ulong idAgente ? await ConversacionSoporte.NombreAsync(_db, idAgente, cancellationToken) : null,
                creada_en = t.creada_en,
                actualizada_en = t.actualizada_en
            });
        }

        return new DetalleReservacionPanelViewModel
        {
            Resumen = resumen,
            promocion = promocion,
            Historial = historial,
            Pagos = pagosFilas.Select(p => new PagoPanelItem
            {
                id = p.id,
                fecha = p.fecha_pago,
                monto = p.monto,
                moneda = p.moneda,
                estado = p.estado,
                metodo = p.metodo,
                tarjeta = $"{p.marca} •••• {p.ultimos4}",
                referencia = string.IsNullOrWhiteSpace(p.referencia_externa) ? null : p.referencia_externa,
                Historial = historialPagos.Where(h => h.id_pago == p.id).Select(h => new HistorialPagoItem(h.fecha_cambio, h.estado, h.detalle)).ToList()
            }).ToList(),
            Reembolsos = reembolsos,
            Tickets = itemsTickets,
            Cancelacion = await _cancelaciones.OpcionesAsync(idReservacion, cancellationToken) ?? new OpcionesCancelacion()
        };
    }

    // =============================================================================================
    // Reembolsos
    // =============================================================================================

    public async Task<ListaReembolsosPanelViewModel> ReembolsosAsync(string? vista, CancellationToken cancellationToken = default)
    {
        vista = ListaReembolsosPanelViewModel.NormalizarVista(vista);
        var q = ConsultaReembolsos();
        q = vista == "pendientes"
            ? q.Where(x => x.estado == ReembolsoSolicitado).OrderBy(x => x.fecha_solicitud).ThenBy(x => x.id)
            : q.OrderByDescending(x => x.fecha_solicitud).ThenByDescending(x => x.id).Take(200);

        var lista = await q.ToListAsync(cancellationToken);
        return new ListaReembolsosPanelViewModel { Vista = vista, Resultados = lista };
    }

    private Task<List<ReembolsoPanelItem>> ReembolsosDeAsync(ulong idReservacion, CancellationToken cancellationToken) =>
        ConsultaReembolsos().Where(x => x.id_reservacion == idReservacion).OrderBy(x => x.id).ToListAsync(cancellationToken);

    /// <summary>Reembolsos con su reservación y su cliente (el historial se llena aparte).</summary>
    private IQueryable<ReembolsoPanelItem> ConsultaReembolsos() =>
        from rb in _db.Reembolsos.AsNoTracking()
        join p in _db.Pagos.AsNoTracking() on rb.id_pago equals p.id
        join r in _db.Reservaciones.AsNoTracking() on p.id_reservacion equals r.id
        join u in _db.Usuarios.AsNoTracking() on r.id_usuarioglobal equals u.id
        select new ReembolsoPanelItem
        {
            id = rb.id,
            id_reservacion = r.id,
            codigo_reserva = r.codigo_reserva,
            cliente = u.nombre + " " + u.apellido_paterno,
            correo_cliente = u.correo,
            fecha_solicitud = rb.fecha_solicitud,
            monto = rb.monto,
            monto_pagado = p.monto,
            moneda = p.moneda,
            motivo = rb.motivo,
            estado = rb.estado,
            fecha_resolucion = rb.fecha_resolucion,
            referencia = rb.referencia_externa
        };

    private static string? Recortar(string? texto, int maximo)
    {
        texto = GestorBd.Limpiar(texto);
        return texto is { Length: > 0 } && texto.Length > maximo ? texto[..maximo] : texto;
    }
}
