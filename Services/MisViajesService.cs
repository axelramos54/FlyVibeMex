using FlyVibeMex.Data;
using FlyVibeMex.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FlyVibeMex.Services;

/// <summary>Implementación de <see cref="IMisViajesService"/> con EF Core (todas las consultas con LINQ).</summary>
/// <remarks>
/// Todas las consultas filtran por <c>reservaciones.id_usuarioglobal = cuenta</c>: un cliente nunca ve ni cancela
/// reservaciones de otra persona (evita IDOR), aunque cambie el id en el navegador.
/// </remarks>
public sealed class MisViajesService : IMisViajesService
{
    private const string EstadoCancelada = "CANCELADA";
    private const string EstadoCompletada = "COMPLETADA";
    private const string EstadoPagoAprobado = "APROBADO";
    private const string ReembolsoSolicitado = "SOLICITADO";

    private readonly FlyVibeMexDbContext _db;
    private readonly IReservacionService _reservaciones;
    private readonly ILogger<MisViajesService> _logger;

    public MisViajesService(FlyVibeMexDbContext db, IReservacionService reservaciones, ILogger<MisViajesService> logger)
    {
        _db = db;
        _reservaciones = reservaciones;
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

        try
        {
            var estrategia = _db.Database.CreateExecutionStrategy();
            await estrategia.ExecuteAsync(() =>
                CancelarEnTransaccionAsync(idUsuario, idReservacion, reembolsar, motivo, opciones, cancellationToken));
        }
        catch (ReservacionCambioException)
        {
            return ResultadoOperacion.Falla("La reservación cambió mientras la cancelabas. Recarga la página y revisa su estado.");
        }
        catch (Exception ex) when (ex is DbUpdateException or SqlException or RetryLimitExceededException or InvalidOperationException)
        {
            _logger.LogError(ex, "No se pudo cancelar la reservación {IdReservacion}.", idReservacion);
            return ResultadoOperacion.Falla("No pudimos cancelar la reservación en este momento. Intenta de nuevo más tarde.");
        }

        _logger.LogInformation("Reservación {Codigo} cancelada por el cliente {IdUsuario} (reembolso: {Reembolso}).",
            reservacion.codigo_reserva, idUsuario, reembolsar);

        return ResultadoOperacion.Ok(reembolsar
            ? $"Reservación {reservacion.codigo_reserva} cancelada. Solicitamos un reembolso de ${opciones.monto_reembolso:N2} " +
              $"{opciones.moneda}; un agente lo revisará."
            : $"Reservación {reservacion.codigo_reserva} cancelada.");
    }

    /// <summary>
    /// Pasos de la cancelación en una transacción (dentro de la execution strategy: si se reintenta, se repite todo).
    /// </summary>
    private async Task CancelarEnTransaccionAsync(ulong idUsuario, ulong idReservacion, bool reembolsar, string? motivo,
        OpcionesCancelacion opciones, CancellationToken cancellationToken)
    {
        _db.ChangeTracker.Clear();
        await using var transaccion = await _db.Database.BeginTransactionAsync(cancellationToken);
        var ahora = DateTime.Now;

        var idCancelada = await IdEstadoReservacionAsync(EstadoCancelada, cancellationToken);

        // a) Estado actual (debe seguir sin ser final) y UPDATE condicional: si otra petición la canceló al mismo
        //    tiempo, no se actualiza ninguna fila y se deshace todo.
        var actual = await (
                from r in _db.Reservaciones.AsNoTracking()
                join e in _db.EstadosReservacion.AsNoTracking() on r.id_estado_reservacion equals e.id
                where r.id == idReservacion && r.id_usuarioglobal == idUsuario
                select new { r.id_estado_reservacion, e.es_final })
            .FirstOrDefaultAsync(cancellationToken);
        if (actual is null || actual.es_final) throw new ReservacionCambioException();

        var estadoAnterior = actual.id_estado_reservacion;
        var filas = await _db.Reservaciones
            .Where(r => r.id == idReservacion && r.id_estado_reservacion == estadoAnterior)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.id_estado_reservacion, idCancelada), cancellationToken);
        if (filas != 1) throw new ReservacionCambioException();

        _db.HistorialEstadosReservacion.Add(new HistorialEstadoReservacion
        {
            id_reservacion = idReservacion,
            id_estado_reservacion = idCancelada,
            id_usuario_cambio = idUsuario,
            fecha_cambio = ahora,
            motivo = motivo ?? (reembolsar ? "Cancelada por el cliente con reembolso" : "Cancelada por el cliente")
        });

        // b) Devolver el cupo (R3) de cada servicio de la reservación. Nunca por encima de cantidad_total.
        var detalles = await (
                from d in _db.DetallesReservacion.AsNoTracking()
                join s in _db.Servicios.AsNoTracking() on d.id_servicio equals s.id
                where d.id_reservacion == idReservacion
                select new { d.id_servicio, s.tipo_servicio, d.fecha_inicio, d.fecha_fin, d.cantidad })
            .ToListAsync(cancellationToken);

        foreach (var d in detalles)
        {
            var (desde, hasta) = FechasDeInventario(d.tipo_servicio, d.fecha_inicio, d.fecha_fin);
            var cantidad = (int)Math.Ceiling(d.cantidad);
            var idServicio = d.id_servicio;
            await _db.DisponibilidadServicios
                .Where(x => x.id_servicio == idServicio && x.fecha >= desde && x.fecha <= hasta
                            && x.cantidad_disponible + cantidad <= x.cantidad_total)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.cantidad_disponible, x => x.cantidad_disponible + cantidad)
                    .SetProperty(x => x.actualizado_en, ahora), cancellationToken);
            // Si una fecha no tiene fila (p. ej. compras de demostración fuera de la ventana del seed) se ignora.
        }

        // c) Reembolso SOLICITADO del pago aprobado (R8) + su bitácora. Lo aprueba un agente en el panel.
        if (reembolsar)
        {
            var idPago = await (
                    from p in _db.Pagos.AsNoTracking()
                    join e in _db.EstadosPago.AsNoTracking() on p.id_estado_pago equals e.id
                    where p.id_reservacion == idReservacion && e.nombre == EstadoPagoAprobado
                    orderby p.id descending
                    select p.id)
                .Take(1)
                .ToListAsync(cancellationToken);
            if (idPago.Count == 0) throw new ReservacionCambioException();

            var reembolso = new Reembolsos
            {
                id_pago = idPago[0],
                monto = opciones.monto_reembolso,
                motivo = motivo!,
                estado = ReembolsoSolicitado,
                fecha_solicitud = ahora,
                fecha_resolucion = null,
                referencia_externa = null
            };
            _db.Reembolsos.Add(reembolso);
            await _db.SaveChangesAsync(cancellationToken);

            _db.HistorialEstadosReembolso.Add(new HistorialEstadoReembolso
            {
                id_reembolso = reembolso.id,
                id_usuario_cambio = idUsuario,
                estado = ReembolsoSolicitado,
                fecha_cambio = ahora,
                detalle = $"Solicitado por el cliente: {opciones.porcentaje:0.##} % según la política de la compra."
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaccion.CommitAsync(cancellationToken);
    }

    // =============================================================================================
    // Consultas de apoyo
    // =============================================================================================

    /// <summary>
    /// Tarjetas de las reservaciones de la cuenta (o solo <paramref name="idReservacion"/>), con grupo, reembolso y
    /// opciones de cancelación ya calculadas.
    /// </summary>
    private async Task<List<ReservacionTarjetaViewModel>> TarjetasAsync(ulong idCuenta, ulong? idReservacion, CancellationToken cancellationToken)
    {
        var reservas = _db.Reservaciones.AsNoTracking().Where(r => r.id_usuarioglobal == idCuenta);
        if (idReservacion is ulong id) reservas = reservas.Where(r => r.id == id);

        var cabeceras = await (
                from r in reservas
                join e in _db.EstadosReservacion.AsNoTracking() on r.id_estado_reservacion equals e.id
                orderby r.creada_en descending
                select new { r.id, r.codigo_reserva, r.total, r.moneda, estado = e.nombre, e.es_final })
            .ToListAsync(cancellationToken);
        if (cabeceras.Count == 0) return new List<ReservacionTarjetaViewModel>();

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

        var pagosAprobados = await (
                from p in _db.Pagos.AsNoTracking()
                join r in reservas on p.id_reservacion equals r.id
                join e in _db.EstadosPago.AsNoTracking() on p.id_estado_pago equals e.id
                where e.nombre == EstadoPagoAprobado
                orderby p.id descending
                select new { p.id_reservacion, p.monto })
            .ToListAsync(cancellationToken);

        var reembolsos = await (
                from rb in _db.Reembolsos.AsNoTracking()
                join p in _db.Pagos.AsNoTracking() on rb.id_pago equals p.id
                join r in reservas on p.id_reservacion equals r.id
                orderby rb.fecha_solicitud descending
                select new { p.id_reservacion, rb.estado, rb.monto })
            .ToListAsync(cancellationToken);

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
                    fecha_solicitud = rb.fecha_solicitud,
                    codigo_reserva = r.codigo_reserva,
                    monto = rb.monto,
                    moneda = p.moneda,
                    estado = rb.estado,
                    motivo = rb.motivo,
                    fecha_resolucion = rb.fecha_resolucion
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

    /// <summary>Imagen de cada servicio que tenga una (hospedajes, vehículos y paquetes).</summary>
    private async Task<Dictionary<ulong, string>> ImagenesAsync(CancellationToken cancellationToken)
    {
        var imagenes = new Dictionary<ulong, string>();
        foreach (var h in await _db.Hospedajes.AsNoTracking().Where(x => x.imagen_ruta != null)
                     .Select(x => new { x.id_servicio, x.imagen_ruta }).ToListAsync(cancellationToken))
            imagenes[h.id_servicio] = h.imagen_ruta!;
        foreach (var v in await _db.Vehiculos.AsNoTracking().Where(x => x.imagen_ruta != null)
                     .Select(x => new { x.id_servicio, x.imagen_ruta }).ToListAsync(cancellationToken))
            imagenes[v.id_servicio] = v.imagen_ruta!;
        foreach (var p in await _db.Paquetes.AsNoTracking().Where(x => x.imagen_ruta != null)
                     .Select(x => new { x.id_servicio, x.imagen_ruta }).ToListAsync(cancellationToken))
            imagenes[p.id_servicio] = p.imagen_ruta!;
        return imagenes;
    }

    /// <summary>
    /// Días de disponibilidad que ocupa un servicio comprado: hospedaje y vehículo de la fecha de inicio a la víspera
    /// de la fecha de fin (noches / días de renta); vuelo, tour y paquete, solo el día de inicio.
    /// </summary>
    private static (DateOnly desde, DateOnly hasta) FechasDeInventario(string tipo, DateTime inicio, DateTime fin)
    {
        var desde = DateOnly.FromDateTime(inicio);
        if (TipoCatalogo.UsaRango(tipo))
        {
            var ultimo = DateOnly.FromDateTime(fin).AddDays(-1);
            return (desde, ultimo < desde ? desde : ultimo);
        }
        return (desde, desde);
    }

    private async Task<ushort> IdEstadoReservacionAsync(string nombre, CancellationToken cancellationToken)
    {
        var ids = await _db.EstadosReservacion.AsNoTracking()
            .Where(e => e.nombre == nombre)
            .Select(e => e.id)
            .Take(1)
            .ToListAsync(cancellationToken);
        return ids.Count > 0
            ? ids[0]
            : throw new InvalidOperationException($"Falta el estado de reservación '{nombre}'. Ejecuta sql/02_seed_demo.sql.");
    }

    /// <summary>La reservación ya no está como cuando se calculó (otra petición la canceló o cambió de estado).</summary>
    private sealed class ReservacionCambioException : Exception
    {
        public ReservacionCambioException() : base("La reservación cambió durante la cancelación.") { }
    }
}
