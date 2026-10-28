using System.Globalization;
using FlyVibeMex.Data;
using FlyVibeMex.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FlyVibeMex.Services;

/// <summary>Implementación de <see cref="IReservacionService"/> con EF Core.</summary>
/// <remarks>
/// Reglas que respeta:
///  - R3 inventario: el cupo se descuenta con un UPDATE condicional (solo si alcanza) dentro de la transacción;
///    si dos clientes compran el último lugar al mismo tiempo, solo uno lo consigue.
///  - R4 snapshot: el detalle copia precio, descuento, fechas, descripción y política de cancelación.
///  - R5 tarjetas: solo token + marca + últimos 4 + vencimiento. Nunca número completo ni CVV.
///  - R6 bitácora: cada cambio de estado de la reservación y del pago queda en su historial.
/// Program.cs activa EnableRetryOnFailure, por eso la transacción va dentro de CreateExecutionStrategy().
/// </remarks>
public sealed class ReservacionService : IReservacionService
{
    private const string EstadoReservacionPendiente = "PENDIENTE";
    private const string EstadoReservacionConfirmada = "CONFIRMADA";
    private const string EstadoPagoPendiente = "PENDIENTE";
    private const string EstadoPagoAprobado = "APROBADO";

    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-MX");

    private readonly FlyVibeMexDbContext _db;
    private readonly ICatalogoService _catalogo;
    private readonly ILogger<ReservacionService> _logger;

    public ReservacionService(FlyVibeMexDbContext db, ICatalogoService catalogo, ILogger<ReservacionService> logger)
    {
        _db = db;
        _catalogo = catalogo;
        _logger = logger;
    }

    private static DateOnly Hoy() => DateOnly.FromDateTime(DateTime.Today);

    // =============================================================================================
    // Resumen
    // =============================================================================================

    public async Task<ResumenReservaViewModel?> ArmarResumenAsync(ulong idUsuario, PagoFormViewModel solicitud,
        CancellationToken cancellationToken = default)
    {
        var tipo = await _catalogo.TipoDeServicioAsync(solicitud.id, cancellationToken);
        if (tipo is null) return null;

        var busqueda = solicitud.ComoBusqueda();
        busqueda.tipo = tipo;
        var servicio = await _catalogo.DetalleAsync(solicitud.id, busqueda, cotizar: true, cancellationToken);
        if (servicio is null) return null;

        var vm = new ResumenReservaViewModel
        {
            Solicitud = solicitud,
            Servicio = servicio,
            Cotizacion = servicio.Cotizacion
        };

        if (vm.Cotizacion is { Disponible: true } cot)
        {
            var (promocion, error) = await AplicarPromocionAsync(solicitud.codigo, solicitud.id, cot.subtotal, cancellationToken);
            vm.Promocion = promocion;
            vm.ErrorPromocion = error;
            vm.Descuento = promocion?.descuento ?? 0m;
            vm.Total = cot.subtotal - vm.Descuento;
            solicitud.total_confirmado = vm.Total.ToString("0.00", CultureInfo.InvariantCulture);
        }

        vm.Metodos = await _db.MetodosPago.AsNoTracking()
            .Where(m => m.activo)
            .OrderBy(m => m.id)
            .Select(m => new SelectListItem(m.nombre, m.id.ToString()))
            .ToListAsync(cancellationToken);

        vm.Tarjetas = await TarjetasVigentesAsync(idUsuario, cancellationToken);
        return vm;
    }

    // =============================================================================================
    // Pago
    // =============================================================================================

    public async Task<ResultadoPago> PagarAsync(ulong idUsuario, PagoFormViewModel pago, CancellationToken cancellationToken = default)
    {
        var hoy = Hoy();

        // 1) Recalcular precio y cupo (nunca se confía en lo que manda el navegador).
        var cot = await _catalogo.CotizarAsync(pago.id, pago.ComoBusqueda(), cancellationToken);
        if (cot is null)
            return ResultadoPago.Error(string.Empty, "Este servicio ya no está disponible para esas fechas.", cambio: true);
        if (!cot.Disponible)
            return ResultadoPago.Error(string.Empty, cot.motivo ?? "Ya no hay cupo para esas fechas.", cambio: true);

        var (promocion, errorPromocion) = await AplicarPromocionAsync(pago.codigo, pago.id, cot.subtotal, cancellationToken);
        if (errorPromocion is not null)
            return ResultadoPago.Error(nameof(pago.codigo), errorPromocion, cambio: true);

        var descuento = promocion?.descuento ?? 0m;
        var total = cot.subtotal - descuento;

        // 2) Si el total cambió desde que el cliente vio el resumen: no se cobra, se avisa.
        if (!decimal.TryParse(pago.total_confirmado, NumberStyles.Number, CultureInfo.InvariantCulture, out var totalVisto)
            || totalVisto != total)
        {
            return ResultadoPago.Error(string.Empty,
                $"El precio cambió mientras revisabas: el total ahora es ${total.ToString("N2", Es)} {cot.moneda}. " +
                "Revisa el resumen y vuelve a confirmar el pago.", cambio: true);
        }

        // 3) Método de pago.
        if (pago.id_metodo_pago is not ushort idMetodo ||
            !await _db.MetodosPago.AsNoTracking().AnyAsync(m => m.id == idMetodo && m.activo, cancellationToken))
        {
            return ResultadoPago.Error(nameof(pago.id_metodo_pago), "Elige un método de pago.");
        }

        // 4) Tarjeta: guardada (debe ser del cliente, activa y vigente) o nueva (se valida aquí).
        string ultimos4;
        TarjetasUsuarios? tarjetaNueva = null;
        ulong? idTarjetaGuardada = null;
        if (pago.id_tarjeta is ulong idTarjeta)
        {
            var guardada = await _db.TarjetasUsuarios.AsNoTracking()
                .Where(t => t.id == idTarjeta && t.id_usuariog == idUsuario && t.activo)
                .Select(t => new { t.id, t.ultimos4, t.mes_expiracion, t.anio_expiracion })
                .FirstOrDefaultAsync(cancellationToken);
            if (guardada is null)
                return ResultadoPago.Error(nameof(pago.id_tarjeta), "Elige una de tus tarjetas o captura una nueva.");
            if (!PasarelaSimulada.Vigente(guardada.mes_expiracion, guardada.anio_expiracion, hoy))
                return ResultadoPago.Error(nameof(pago.id_tarjeta), "Esa tarjeta está vencida. Elige otra o captura una nueva.");
            ultimos4 = guardada.ultimos4;
            idTarjetaGuardada = guardada.id;
        }
        else
        {
            var errores = PasarelaSimulada.ValidarTarjetaNueva(pago, hoy);
            if (errores.Count > 0) return new ResultadoPago { Errores = errores };

            var digitos = PasarelaSimulada.SoloDigitos(pago.numero_tarjeta);
            ultimos4 = digitos[^4..];
            tarjetaNueva = new TarjetasUsuarios
            {
                id_usuariog = idUsuario,
                id_institucion = null,
                token_referencia = PasarelaSimulada.NuevoToken(),
                marca = PasarelaSimulada.Marca(digitos)!,
                ultimos4 = ultimos4,
                mes_expiracion = pago.mes_expiracion!.Value,
                anio_expiracion = pago.anio_expiracion!.Value,
                alias = string.IsNullOrWhiteSpace(pago.alias) ? null : pago.alias.Trim(),
                activo = pago.guardar_tarjeta,   // sin "Guardar" queda oculta: el pago necesita su id (pagos.id_tarjeta)
                creado_en = DateTime.Now
            };
        }

        // 5) Cobro simulado. Si se rechaza, no se crea NADA (ni tarjeta, ni reservación, ni se aparta cupo).
        if (!PasarelaSimulada.CobroAprobado(ultimos4))
        {
            return ResultadoPago.Error(pago.id_tarjeta is null ? nameof(pago.numero_tarjeta) : nameof(pago.id_tarjeta),
                "El banco rechazó el pago (tarjeta de prueba terminada en 0002). No se hizo ningún cargo ni se apartó " +
                "lugar; intenta con otra tarjeta.");
        }

        // 6) Guardar todo en una sola transacción.
        try
        {
            var estrategia = _db.Database.CreateExecutionStrategy();
            var codigo = await estrategia.ExecuteAsync(() =>
                GuardarCompraAsync(idUsuario, pago.id, cot, promocion, idMetodo, idTarjetaGuardada, tarjetaNueva, cancellationToken));

            _logger.LogInformation("Reservación {Codigo} creada por el usuario {IdUsuario}.", codigo, idUsuario);
            return new ResultadoPago { CodigoReserva = codigo };
        }
        catch (CupoAgotadoException)
        {
            return ResultadoPago.Error(string.Empty,
                "Se agotó el cupo mientras pagabas. No se hizo ningún cargo. Revisa otras fechas.", cambio: true);
        }
        catch (Exception ex) when (ex is DbUpdateException or SqlException or RetryLimitExceededException or InvalidOperationException)
        {
            // El detalle técnico solo queda en el log; nunca datos de la tarjeta.
            _logger.LogError(ex, "No se pudo guardar la compra del servicio {IdServicio}.", pago.id);
            return ResultadoPago.Error(string.Empty,
                "No pudimos completar la compra en este momento. No se hizo ningún cargo; intenta de nuevo más tarde.");
        }
    }

    /// <summary>
    /// Pasos de la transacción. Se ejecuta dentro de la execution strategy: si hay un fallo transitorio de red, EF
    /// repite TODO el método (por eso empieza con ChangeTracker.Clear()).
    /// </summary>
    private async Task<string> GuardarCompraAsync(ulong idUsuario, ulong idServicio, CotizacionServicio cot,
        PromocionAplicada? promocion, ushort idMetodo, ulong? idTarjetaGuardada, TarjetasUsuarios? tarjetaNueva,
        CancellationToken cancellationToken)
    {
        _db.ChangeTracker.Clear();
        await using var transaccion = await _db.Database.BeginTransactionAsync(cancellationToken);
        var ahora = DateTime.Now;

        // Catálogos de estados (los crea sql/02_seed_demo.sql).
        var idPendiente = await IdEstadoReservacionAsync(EstadoReservacionPendiente, cancellationToken);
        var idConfirmada = await IdEstadoReservacionAsync(EstadoReservacionConfirmada, cancellationToken);
        var idPagoPendiente = await IdEstadoPagoAsync(EstadoPagoPendiente, cancellationToken);
        var idPagoAprobado = await IdEstadoPagoAsync(EstadoPagoAprobado, cancellationToken);

        // a) Descontar cupo (R3): un UPDATE por todas las fechas, solo donde alcanza. Si alguna fecha no alcanza,
        //    se actualizan menos filas de las esperadas y se deshace todo.
        var inicio = cot.dias.First().fecha;
        var fin = cot.dias.Last().fecha;
        var cantidad = cot.cantidad;
        var filas = await _db.DisponibilidadServicios
            .Where(d => d.id_servicio == idServicio && d.fecha >= inicio && d.fecha <= fin && d.cantidad_disponible >= cantidad)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.cantidad_disponible, d => d.cantidad_disponible - cantidad)
                .SetProperty(d => d.actualizado_en, ahora), cancellationToken);
        if (filas != cot.dias.Count) throw new CupoAgotadoException();

        // b) Tarjeta nueva (tokenizada).
        ulong idTarjeta;
        if (tarjetaNueva is not null)
        {
            var copia = new TarjetasUsuarios
            {
                id_usuariog = tarjetaNueva.id_usuariog,
                id_institucion = null,
                token_referencia = tarjetaNueva.token_referencia,
                marca = tarjetaNueva.marca,
                ultimos4 = tarjetaNueva.ultimos4,
                mes_expiracion = tarjetaNueva.mes_expiracion,
                anio_expiracion = tarjetaNueva.anio_expiracion,
                alias = tarjetaNueva.alias,
                activo = tarjetaNueva.activo,
                creado_en = ahora
            };
            _db.TarjetasUsuarios.Add(copia);
            await _db.SaveChangesAsync(cancellationToken);
            idTarjeta = copia.id;
        }
        else
        {
            idTarjeta = idTarjetaGuardada!.Value;
        }

        // c) Reservación (PENDIENTE) con totales históricos (R4).
        var servicio = await _db.Servicios.AsNoTracking()
            .Where(s => s.id == idServicio)
            .Select(s => new { s.nombre, s.permite_cancelacion, s.horas_reembolso_total, s.porcentaje_reembolso_tardio, s.politica_cancelacion })
            .FirstAsync(cancellationToken);

        var descuento = promocion?.descuento ?? 0m;
        var reservacion = new Reservaciones
        {
            id_usuarioglobal = idUsuario,
            id_estado_reservacion = idPendiente,
            codigo_reserva = await NuevoCodigoReservaAsync(ahora, cancellationToken),
            moneda = cot.moneda,
            subtotal = cot.subtotal,
            descuento_total = descuento,
            total = cot.subtotal - descuento,
            creada_en = ahora
        };
        _db.Reservaciones.Add(reservacion);
        await _db.SaveChangesAsync(cancellationToken);

        // d) Detalle con snapshot de precio, fechas, descripción y política de cancelación (R4).
        var descripcion = $"{servicio.nombre} — {cot.resumen}";
        _db.DetallesReservacion.Add(new DetallesReservacion
        {
            id_reservacion = reservacion.id,
            id_servicio = idServicio,
            id_promocion = promocion?.id,
            cantidad = cot.cantidad,
            fecha_inicio = cot.inicio,
            fecha_fin = cot.fin,
            precio_unitario = cot.precio_unitario,
            descuento_aplicado = descuento,
            subtotal = cot.subtotal,
            descripcion_snapshot = descripcion.Length > 250 ? descripcion[..250] : descripcion,
            permite_cancelacion = servicio.permite_cancelacion,
            horas_reembolso_total = servicio.horas_reembolso_total,
            porcentaje_reembolso_tardio = servicio.porcentaje_reembolso_tardio,
            politica_cancelacion = servicio.politica_cancelacion
        });
        _db.HistorialEstadosReservacion.Add(new HistorialEstadoReservacion
        {
            id_reservacion = reservacion.id,
            id_estado_reservacion = idPendiente,
            id_usuario_cambio = idUsuario,
            fecha_cambio = ahora,
            motivo = "Reservación creada"
        });

        // e) Pago aprobado (simulado) + su bitácora (R6, R7).
        var pagoBd = new Pagos
        {
            id_reservacion = reservacion.id,
            id_metodo_pago = idMetodo,
            id_tarjeta = idTarjeta,
            id_estado_pago = idPagoAprobado,
            monto = reservacion.total,
            moneda = cot.moneda,
            referencia_externa = PasarelaSimulada.NuevaReferencia(),
            fecha_pago = ahora,
            creado_en = ahora
        };
        _db.Pagos.Add(pagoBd);
        await _db.SaveChangesAsync(cancellationToken);

        _db.HistorialEstadosPago.Add(new HistorialEstadoPago
        {
            id_pago = pagoBd.id, id_estado_pago = idPagoPendiente, fecha_cambio = ahora,
            detalle = "Cobro enviado a la pasarela (simulada)"
        });
        _db.HistorialEstadosPago.Add(new HistorialEstadoPago
        {
            id_pago = pagoBd.id, id_estado_pago = idPagoAprobado, fecha_cambio = ahora.AddSeconds(1),
            detalle = "Cobro aprobado (simulado)"
        });

        // f) Reservación CONFIRMADA (cambio automático del sistema: id_usuario_cambio = NULL).
        reservacion.id_estado_reservacion = idConfirmada;
        _db.HistorialEstadosReservacion.Add(new HistorialEstadoReservacion
        {
            id_reservacion = reservacion.id,
            id_estado_reservacion = idConfirmada,
            id_usuario_cambio = null,
            fecha_cambio = ahora.AddSeconds(1),
            motivo = "Pago aprobado"
        });

        await _db.SaveChangesAsync(cancellationToken);
        await transaccion.CommitAsync(cancellationToken);
        return reservacion.codigo_reserva;
    }

    // =============================================================================================
    // Confirmación
    // =============================================================================================

    public async Task<ConfirmacionReservaViewModel?> ConfirmacionAsync(ulong idUsuario, string codigoReserva,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(codigoReserva) || codigoReserva.Length > 30) return null;

        var r = await _db.Reservaciones.AsNoTracking()
            .FirstOrDefaultAsync(x => x.codigo_reserva == codigoReserva && x.id_usuarioglobal == idUsuario, cancellationToken);
        if (r is null) return null;   // no existe o es de otra persona: misma respuesta

        var d = await _db.DetallesReservacion.AsNoTracking()
            .Where(x => x.id_reservacion == r.id)
            .OrderBy(x => x.id)
            .FirstOrDefaultAsync(cancellationToken);
        if (d is null) return null;

        var servicio = await _db.Servicios.AsNoTracking()
            .Where(s => s.id == d.id_servicio)
            .Select(s => new { s.nombre, s.tipo_servicio })
            .FirstOrDefaultAsync(cancellationToken);

        var estado = await _db.EstadosReservacion.AsNoTracking()
            .Where(e => e.id == r.id_estado_reservacion)
            .Select(e => e.nombre)
            .FirstOrDefaultAsync(cancellationToken);

        string? codigoPromocion = null;
        if (d.id_promocion is ulong idPromocion)
        {
            codigoPromocion = await _db.Promociones.AsNoTracking()
                .Where(p => p.id == idPromocion)
                .Select(p => p.codigo)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var pago = await (
                from p in _db.Pagos.AsNoTracking()
                join m in _db.MetodosPago.AsNoTracking() on p.id_metodo_pago equals m.id
                join t in _db.TarjetasUsuarios.AsNoTracking() on p.id_tarjeta equals t.id
                where p.id_reservacion == r.id
                orderby p.id descending
                select new { metodo = m.nombre, t.marca, t.ultimos4, p.referencia_externa, p.fecha_pago })
            .FirstOrDefaultAsync(cancellationToken);

        return new ConfirmacionReservaViewModel
        {
            codigo_reserva = r.codigo_reserva,
            creada_en = r.creada_en,
            estado = estado ?? string.Empty,
            servicio = servicio?.nombre ?? d.descripcion_snapshot,
            tipo = servicio?.tipo_servicio ?? string.Empty,
            id_servicio = d.id_servicio,
            descripcion = d.descripcion_snapshot,
            inicio = d.fecha_inicio,
            fin = d.fecha_fin,
            cantidad = d.cantidad,
            precio_unitario = d.precio_unitario,
            subtotal = r.subtotal,
            descuento = r.descuento_total,
            total = r.total,
            moneda = r.moneda,
            codigo_promocion = codigoPromocion,
            politica = string.IsNullOrWhiteSpace(d.politica_cancelacion)
                ? PoliticaCancelacion.Describir(d.permite_cancelacion, d.horas_reembolso_total, d.porcentaje_reembolso_tardio)
                : d.politica_cancelacion,
            metodo_pago = pago?.metodo,
            tarjeta = pago is null ? null : $"{pago.marca} •••• {pago.ultimos4}",
            referencia_pago = pago?.referencia_externa,
            fecha_pago = pago?.fecha_pago
        };
    }

    // =============================================================================================
    // Ayudantes
    // =============================================================================================

    /// <summary>
    /// Valida y calcula el descuento de un código. Un código por reservación. PORCENTAJE se aplica sobre el subtotal;
    /// MONTO nunca descuenta más que el subtotal.
    /// </summary>
    private async Task<(PromocionAplicada? promocion, string? error)> AplicarPromocionAsync(string? codigo, ulong idServicio,
        decimal subtotal, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(codigo)) return (null, null);

        var texto = codigo.Trim().ToUpperInvariant();
        var ahora = DateTime.Now;
        var promo = await _db.Promociones.AsNoTracking()
            .Where(p => p.codigo == texto)
            .Select(p => new { p.id, p.codigo, p.nombre, p.tipo_descuento, p.valor, p.minimo_compra, p.activo, p.fecha_inicio, p.fecha_fin })
            .FirstOrDefaultAsync(cancellationToken);

        if (promo is null || !promo.activo || promo.fecha_inicio > ahora || promo.fecha_fin < ahora)
            return (null, "El código no existe o ya no está vigente.");

        var aplica = await _db.PromocionesServicios.AsNoTracking()
            .AnyAsync(ps => ps.id_promocion == promo.id && ps.id_servicio == idServicio, cancellationToken);
        if (!aplica)
            return (null, "Este código no aplica a este servicio.");

        if (subtotal < promo.minimo_compra)
            return (null, $"Este código aplica en compras desde ${promo.minimo_compra.ToString("N2", Es)}.");

        var descuento = promo.tipo_descuento == "PORCENTAJE"
            ? Math.Round(subtotal * promo.valor / 100m, 2, MidpointRounding.AwayFromZero)
            : promo.valor;

        return (new PromocionAplicada(promo.id, promo.codigo, promo.nombre, Math.Min(descuento, subtotal)), null);
    }

    private async Task<List<TarjetaGuardada>> TarjetasVigentesAsync(ulong idUsuario, CancellationToken cancellationToken)
    {
        var hoy = Hoy();
        var tarjetas = await _db.TarjetasUsuarios.AsNoTracking()
            .Where(t => t.id_usuariog == idUsuario && t.activo)
            .OrderByDescending(t => t.creado_en)
            .Select(t => new TarjetaGuardada(t.id, t.marca, t.ultimos4, t.mes_expiracion, t.anio_expiracion, t.alias))
            .ToListAsync(cancellationToken);
        return tarjetas.Where(t => PasarelaSimulada.Vigente(t.mes_expiracion, t.anio_expiracion, hoy)).ToList();
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

    private async Task<ushort> IdEstadoPagoAsync(string nombre, CancellationToken cancellationToken)
    {
        var ids = await _db.EstadosPago.AsNoTracking()
            .Where(e => e.nombre == nombre)
            .Select(e => e.id)
            .Take(1)
            .ToListAsync(cancellationToken);
        return ids.Count > 0
            ? ids[0]
            : throw new InvalidOperationException($"Falta el estado de pago '{nombre}'. Ejecuta sql/02_seed_demo.sql.");
    }

    /// <summary>Código legible y único: FV-AAMMDD-XXXX (sin letras que se confunden: O, I, 0, 1).</summary>
    private async Task<string> NuevoCodigoReservaAsync(DateTime ahora, CancellationToken cancellationToken)
    {
        const string letras = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        for (var intento = 0; intento < 10; intento++)
        {
            var sufijo = new string(Enumerable.Range(0, 4).Select(_ => letras[Random.Shared.Next(letras.Length)]).ToArray());
            var codigo = $"FV-{ahora:yyMMdd}-{sufijo}";
            if (!await _db.Reservaciones.AsNoTracking().AnyAsync(r => r.codigo_reserva == codigo, cancellationToken))
                return codigo;
        }
        throw new InvalidOperationException("No se pudo generar un código de reserva único.");
    }

    /// <summary>Algún día ya no tiene cupo suficiente al momento de guardar (otro cliente ganó el lugar).</summary>
    private sealed class CupoAgotadoException : Exception
    {
        public CupoAgotadoException() : base("Sin cupo al guardar la compra.") { }
    }
}
