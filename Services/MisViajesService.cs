using FlyVibeMex.Data;
using FlyVibeMex.Models;
using Microsoft.EntityFrameworkCore;

namespace FlyVibeMex.Services;

/// <summary>Implementación de <see cref="IMisViajesService"/> con EF Core (todas las consultas con LINQ).</summary>
/// <remarks>
/// Todas las consultas filtran por <c>reservaciones.id_usuarioglobal = cuenta</c>: un cliente nunca ve ni cancela
/// reservaciones de otra persona (evita IDOR), aunque cambie el id en el navegador.
/// La cancelación en sí (transacción, cupo, reembolso) está en <see cref="CancelacionesService"/>, compartida con el
/// panel de Reservaciones del personal.
/// </remarks>
public sealed class MisViajesService : IMisViajesService
{
    private const string EstadoCancelada = "CANCELADA";
    private const string EstadoCompletada = "COMPLETADA";
    private const string EstadoPagoAprobado = "APROBADO";

    private readonly FlyVibeMexDbContext _db;
    private readonly IReservacionService _reservaciones;
    private readonly ICancelacionesService _cancelaciones;
    private readonly ILogger<MisViajesService> _logger;

    public MisViajesService(FlyVibeMexDbContext db, IReservacionService reservaciones, ICancelacionesService cancelaciones,
        ILogger<MisViajesService> logger)
    {
        _db = db;
        _reservaciones = reservaciones;
        _cancelaciones = cancelaciones;
        _logger = logger;
    }

    // =============================================================================================
    // Mis reservaciones
    // =============================================================================================

    public async Task<MisReservacionesViewModel> ReservacionesAsync(ContextoCuenta contexto, CancellationToken cancellationToken = default)
    {
        var tarjetas = await TarjetasAsync(contexto.IdCuenta, null, cancellationToken);
        return new MisReservacionesViewModel
        {
            Contexto = contexto,
            Proximas = tarjetas.Where(t => t.grupo == GrupoReservacion.Proximo).OrderBy(t => t.inicio).ToList(),
            Pasadas = tarjetas.Where(t => t.grupo == GrupoReservacion.Pasado).OrderByDescending(t => t.inicio).ToList(),
            Canceladas = tarjetas.Where(t => t.grupo == GrupoReservacion.Cancelado).OrderByDescending(t => t.inicio).ToList()
        };
    }

    public async Task<DetalleReservacionViewModel?> DetalleAsync(ContextoCuenta contexto, string codigoReserva, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(codigoReserva) || codigoReserva.Length > 30) return null;

        var ids = await _db.Reservaciones.AsNoTracking()
            .Where(r => r.codigo_reserva == codigoReserva && r.id_usuarioglobal == contexto.IdCuenta)
            .Select(r => r.id)
            .Take(1)
            .ToListAsync(cancellationToken);
        if (ids.Count == 0) return null;   // no existe o es de otra persona: misma respuesta
        var idReservacion = ids[0];

        var tarjeta = (await TarjetasAsync(contexto.IdCuenta, idReservacion, cancellationToken)).FirstOrDefault();
        var comprobante = await _reservaciones.ConfirmacionAsync(contexto.IdCuenta, codigoReserva, cancellationToken);
        if (tarjeta is null || comprobante is null) return null;

        var historial = await (
                from h in _db.HistorialEstadosReservacion.AsNoTracking()
                join e in _db.EstadosReservacion.AsNoTracking() on h.id_estado_reservacion equals e.id
                where h.id_reservacion == idReservacion
                orderby h.fecha_cambio, h.id
                select new { h.fecha_cambio, estado = e.nombre, h.motivo, h.id_usuario_cambio })
            .ToListAsync(cancellationToken);

        var nombres = new Dictionary<ulong, string>();
        foreach (var idUsuario in historial.Where(h => h.id_usuario_cambio is not null && h.id_usuario_cambio != contexto.IdCuenta)
                                           .Select(h => h.id_usuario_cambio!.Value).Distinct())
        {
            var nombre = await _db.Usuarios.AsNoTracking().Where(u => u.id == idUsuario)
                .Select(u => u.nombre + " " + u.apellido_paterno).FirstOrDefaultAsync(cancellationToken);
            nombres[idUsuario] = nombre is null ? "Personal de FlyVibeMex" : $"{nombre} (FlyVibeMex)";
        }

        return new DetalleReservacionViewModel
        {
            Contexto = contexto,
            Comprobante = comprobante,
            Reservacion = tarjeta,
            Historial = historial.Select(h => new HistorialEstadoItem(
                    h.fecha_cambio,
                    h.estado,
                    h.motivo,
                    h.id_usuario_cambio is null ? "Sistema"
                        : h.id_usuario_cambio == contexto.IdCuenta ? "Cliente"
                        : nombres.GetValueOrDefault(h.id_usuario_cambio.Value, "Personal de FlyVibeMex")))
                .ToList(),
            Reembolsos = await ReembolsosAsync(contexto.IdCuenta, idReservacion, cancellationToken)
        };
    }

    // =============================================================================================
    // Mis compras
    // =============================================================================================

    public async Task<MisComprasViewModel> ComprasAsync(ContextoCuenta contexto, CancellationToken cancellationToken = default)
    {
        var pagos = await (
                from p in _db.Pagos.AsNoTracking()
                join r in _db.Reservaciones.AsNoTracking() on p.id_reservacion equals r.id
                join m in _db.MetodosPago.AsNoTracking() on p.id_metodo_pago equals m.id
                join t in _db.TarjetasUsuarios.AsNoTracking() on p.id_tarjeta equals t.id
                join e in _db.EstadosPago.AsNoTracking() on p.id_estado_pago equals e.id
                where r.id_usuarioglobal == contexto.IdCuenta
                orderby p.fecha_pago descending, p.id descending
                select new
                {
                    p.id_reservacion,
                    p.fecha_pago,
                    r.codigo_reserva,
                    p.monto,
                    p.moneda,
                    metodo = m.nombre,
                    t.marca,
                    t.ultimos4,
                    estado = e.nombre,
                    p.referencia_externa
                })
            .ToListAsync(cancellationToken);

        var servicios = await NombresPorReservacionAsync(contexto.IdCuenta, cancellationToken);

        return new MisComprasViewModel
        {
            Contexto = contexto,
            Pagos = pagos.Select(p => new PagoListadoViewModel
            {
                fecha = p.fecha_pago,
                codigo_reserva = p.codigo_reserva,
                servicio = servicios.GetValueOrDefault(p.id_reservacion, string.Empty),
                monto = p.monto,
                moneda = p.moneda,
                metodo = p.metodo,
                tarjeta = $"{p.marca} •••• {p.ultimos4}",
                estado = p.estado,
                referencia = string.IsNullOrWhiteSpace(p.referencia_externa) ? null : p.referencia_externa
            }).ToList(),
            Reembolsos = await ReembolsosAsync(contexto.IdCuenta, null, cancellationToken)
        };
    }

    // =============================================================================================
    // Cancelar (con o sin reembolso)
    // =============================================================================================

    public async Task<ResultadoOperacion> CancelarAsync(ulong idUsuario, ulong idReservacion, bool reembolsar, string? motivo,
        CancellationToken cancellationToken = default)
    {
        motivo = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();
        if (reembolsar && (motivo is null || motivo.Length < 10 || motivo.Length > 250))
            return ResultadoOperacion.Falla("El motivo del reembolso debe tener entre 10 y 250 caracteres.");
        if (motivo is { Length: > 250 })
            return ResultadoOperacion.Falla("El motivo puede tener máximo 250 caracteres.");

        // Se recalcula con la política GUARDADA en la compra (nunca con lo que mostró el navegador).
        var reservacion = (await TarjetasAsync(idUsuario, idReservacion, cancellationToken)).FirstOrDefault();
        if (reservacion is null)
            return ResultadoOperacion.Falla("No encontramos esa reservación en tu cuenta.");

        var opciones = reservacion.cancelacion;
        if (!opciones.puede_cancelar)
            return ResultadoOperacion.Falla(opciones.motivo_no_cancelar ?? "Esta reservación no se puede cancelar.");
        if (reembolsar && !opciones.puede_reembolsar)
            return ResultadoOperacion.Falla(opciones.motivo_no_reembolsar ?? "Esta cancelación no tiene reembolso.");

        var resultado = await _cancelaciones.EjecutarAsync(new OrdenCancelacion(
            idReservacion, idUsuario, IdDueno: idUsuario, PorPersonal: false, motivo,
            reembolsar ? ModoReembolso.Solicitar : ModoReembolso.Ninguno,
            reembolsar ? opciones.monto_reembolso : 0,
            reembolsar ? $"Solicitado por el cliente: {opciones.porcentaje:0.##} % según la política de la compra." : null),
            cancellationToken);

        if (resultado == ResultadoCancelacion.Cambio)
            return ResultadoOperacion.Falla("La reservación cambió mientras la cancelabas. Recarga la página y revisa su estado.");
        if (resultado == ResultadoCancelacion.ErrorBd)
            return ResultadoOperacion.Falla("No pudimos cancelar la reservación en este momento. Intenta de nuevo más tarde.");

        _logger.LogInformation("Reservación {Codigo} cancelada por el cliente {IdUsuario} (reembolso: {Reembolso}).",
            reservacion.codigo_reserva, idUsuario, reembolsar);

        return ResultadoOperacion.Ok(reembolsar
            ? $"Reservación {reservacion.codigo_reserva} cancelada. Solicitamos un reembolso de ${opciones.monto_reembolso:N2} " +
              $"{opciones.moneda}; un agente lo revisará."
            : $"Reservación {reservacion.codigo_reserva} cancelada.");
    }

    // =============================================================================================
    // Consultas de apoyo
    // =============================================================================================

    /// <summary>
    /// Tarjetas de las reservaciones de la cuenta (o solo <paramref name="idReservacion"/>), con grupo, reembolso y
    /// opciones de cancelación ya calculadas.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>La política de cancelación sale del SNAPSHOT, no del catálogo.</b> Las horas de reembolso, el
    /// porcentaje tardío y el texto se leen de <c>detalle_reservacion</c>, donde se copiaron al comprar (R4).
    /// Si se leyeran de <c>servicios</c>, un cambio de política del gestor alteraría las condiciones de compras
    /// ya hechas: el cliente vería otras reglas que las que aceptó al pagar.
    /// </para>
    /// <para>
    /// <b>Son cinco consultas y un cruce en memoria, a propósito.</b> Cabeceras, detalles, pagos aprobados,
    /// reembolsos e imágenes se leen de golpe —cada una filtrada por las reservaciones de esta cuenta— y se
    /// combinan después. La alternativa, consultar por cada reservación, sería un N+1.
    /// </para>
    /// </remarks>
    private async Task<List<ReservacionTarjetaViewModel>> TarjetasAsync(ulong idCuenta, ulong? idReservacion, CancellationToken cancellationToken)
    {
        var reservas = _db.Reservaciones.AsNoTracking().Where(r => r.id_usuarioglobal == idCuenta);
        if (idReservacion is ulong id) reservas = reservas.Where(r => r.id == id);

        // ---- Paso 1: cabeceras (una fila por reservación) con el nombre de su estado ----
        var cabeceras = await (
                from r in reservas
                join e in _db.EstadosReservacion.AsNoTracking() on r.id_estado_reservacion equals e.id
                orderby r.creada_en descending
                select new { r.id, r.codigo_reserva, r.total, r.moneda, estado = e.nombre, e.es_final })
            .ToListAsync(cancellationToken);
        if (cabeceras.Count == 0) return new List<ReservacionTarjetaViewModel>();

        // ---- Paso 2: las líneas, con la política COPIADA al comprar (R4: ver <remarks>) ----
        var detalles = await (
                from d in _db.DetallesReservacion.AsNoTracking()
                join r in reservas on d.id_reservacion equals r.id
                join s in _db.Servicios.AsNoTracking() on d.id_servicio equals s.id
                orderby d.id
                select new
                {
                    d.id_reservacion,
                    d.id_servicio,
                    s.nombre,
                    s.tipo_servicio,
                    d.descripcion_snapshot,
                    d.fecha_inicio,
                    d.fecha_fin,
                    d.permite_cancelacion,
                    d.horas_reembolso_total,
                    d.porcentaje_reembolso_tardio,
                    d.politica_cancelacion
                })
            .ToListAsync(cancellationToken);

        // ---- Paso 3: el pago aprobado más reciente de cada reservación (es el monto reembolsable) ----
        var pagosAprobados = await (
                from p in _db.Pagos.AsNoTracking()
                join r in reservas on p.id_reservacion equals r.id
                join e in _db.EstadosPago.AsNoTracking() on p.id_estado_pago equals e.id
                where e.nombre == EstadoPagoAprobado
                orderby p.id descending
                select new { p.id_reservacion, p.monto })
            .ToListAsync(cancellationToken);

        // ---- Paso 4: el reembolso más reciente, para mostrar su estado en la tarjeta ----
        var reembolsos = await (
                from rb in _db.Reembolsos.AsNoTracking()
                join p in _db.Pagos.AsNoTracking() on rb.id_pago equals p.id
                join r in reservas on p.id_reservacion equals r.id
                orderby rb.fecha_solicitud descending
                select new { p.id_reservacion, rb.estado, rb.monto })
            .ToListAsync(cancellationToken);

        // ---- Paso 5: armar cada tarjeta cruzando lo anterior en memoria ----
        var imagenes = await ImagenesAsync(cancellationToken);
        var ahora = DateTime.Now;
        var resultado = new List<ReservacionTarjetaViewModel>();

        foreach (var c in cabeceras)
        {
            var lineas = detalles.Where(x => x.id_reservacion == c.id).ToList();
            var primera = lineas.FirstOrDefault();
            if (primera is null) continue;   // reservación sin detalle (datos incompletos): no se muestra

            var inicio = lineas.Min(x => x.fecha_inicio);
            var fin = lineas.Max(x => x.fecha_fin);
            var pago = pagosAprobados.FirstOrDefault(p => p.id_reservacion == c.id);
            var reembolso = reembolsos.FirstOrDefault(x => x.id_reservacion == c.id);

            // Grupo de la pestaña: cancelada manda sobre todo; después, pasada si ya terminó el viaje.
            var grupo = c.estado == EstadoCancelada ? GrupoReservacion.Cancelado
                : c.estado == EstadoCompletada || fin < ahora ? GrupoReservacion.Pasado
                : GrupoReservacion.Proximo;

            resultado.Add(new ReservacionTarjetaViewModel
            {
                id = c.id,
                codigo_reserva = c.codigo_reserva,
                id_servicio = primera.id_servicio,
                servicio = lineas.Count == 1 ? primera.nombre : $"{primera.nombre} y {lineas.Count - 1} servicio(s) más",
                tipo = primera.tipo_servicio,
                descripcion = string.Join(" · ", lineas.Select(x => x.descripcion_snapshot)),
                imagen_ruta = imagenes.GetValueOrDefault(primera.id_servicio),
                inicio = inicio,
                fin = fin,
                total = c.total,
                moneda = c.moneda,
                estado = c.estado,
                grupo = grupo,
                estado_reembolso = reembolso?.estado,
                monto_reembolso_solicitado = reembolso?.monto,
                // La política es la de la primera línea (en esta versión cada reservación tiene un solo servicio).
                cancelacion = ReglasCancelacion.Calcular(
                    c.es_final, inicio, primera.permite_cancelacion, primera.horas_reembolso_total, primera.porcentaje_reembolso_tardio,
                    primera.politica_cancelacion, pago?.monto, c.moneda, ahora)
            });
        }

        return resultado;
    }

    private async Task<List<ReembolsoListadoViewModel>> ReembolsosAsync(ulong idCuenta, ulong? idReservacion, CancellationToken cancellationToken)
    {
        var reservas = _db.Reservaciones.AsNoTracking().Where(r => r.id_usuarioglobal == idCuenta);
        if (idReservacion is ulong id) reservas = reservas.Where(r => r.id == id);

        return await (
                from rb in _db.Reembolsos.AsNoTracking()
                join p in _db.Pagos.AsNoTracking() on rb.id_pago equals p.id
                join r in reservas on p.id_reservacion equals r.id
                orderby rb.fecha_solicitud descending
                select new ReembolsoListadoViewModel
                {
                    id_reservacion = r.id,
                    fecha_solicitud = rb.fecha_solicitud,
                    codigo_reserva = r.codigo_reserva,
                    monto = rb.monto,
                    moneda = p.moneda,
                    estado = rb.estado,
                    motivo = rb.motivo,
                    fecha_resolucion = rb.fecha_resolucion,
                    referencia = rb.referencia_externa,
                    // Lo que escribió el personal al rechazarlo (lo ve el cliente).
                    motivo_rechazo = rb.estado == "RECHAZADO"
                        ? _db.HistorialEstadosReembolso
                            .Where(h => h.id_reembolso == rb.id && h.estado == "RECHAZADO")
                            .OrderByDescending(h => h.id)
                            .Select(h => h.detalle)
                            .FirstOrDefault()
                        : null
                })
            .ToListAsync(cancellationToken);
    }

    /// <summary>Nombre del servicio de cada reservación de la cuenta (para "Mis compras").</summary>
    private async Task<Dictionary<ulong, string>> NombresPorReservacionAsync(ulong idCuenta, CancellationToken cancellationToken)
    {
        var lineas = await (
                from d in _db.DetallesReservacion.AsNoTracking()
                join r in _db.Reservaciones.AsNoTracking() on d.id_reservacion equals r.id
                join s in _db.Servicios.AsNoTracking() on d.id_servicio equals s.id
                where r.id_usuarioglobal == idCuenta
                orderby d.id
                select new { d.id_reservacion, s.nombre })
            .ToListAsync(cancellationToken);

        return lineas
            .GroupBy(l => l.id_reservacion)
            .ToDictionary(g => g.Key, g => g.Count() == 1 ? g.First().nombre : $"{g.First().nombre} y {g.Count() - 1} más");
    }

    /// <summary>Imagen de cada servicio que tenga una.</summary>
    /// <remarks>
    /// L6 (02/11/2026): antes esto eran TRES consultas —hospedajes, vehículos y paquetes— porque
    /// <c>imagen_ruta</c> estaba repetida en cada subtipo. Ahora vive en <c>servicios</c> (R1) y basta una,
    /// que además incluye los vuelos y los tours.
    /// </remarks>
    private async Task<Dictionary<ulong, string>> ImagenesAsync(CancellationToken cancellationToken) =>
        await _db.Servicios.AsNoTracking()
            .Where(srv => srv.imagen_ruta != null)
            .Select(srv => new { srv.id, ruta = srv.imagen_ruta! })
            .ToDictionaryAsync(x => x.id, x => x.ruta, cancellationToken);
}
