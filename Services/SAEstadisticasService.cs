using FlyVibeMex.Data;
using FlyVibeMex.Models;
using Microsoft.EntityFrameworkCore;

namespace FlyVibeMex.Services;

/// <summary>Estadísticas de ventas del panel SA (consulta de solo lectura).</summary>
public interface ISAEstadisticasService
{
    /// <summary>Resumen de ventas del periodo: totales, por tipo de servicio, por día (o mes) y lo más vendido.</summary>
    Task<EstadisticasVentasViewModel> VentasAsync(FiltroEstadisticas filtro, CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementación de <see cref="ISAEstadisticasService"/> con EF Core (consultas LINQ, como el resto de la app).
/// </summary>
/// <remarks>
/// <para>Las reglas son las de <c>pa_reporte_ventas</c> (sql/02_procedimientos_almacenados.sql), para que el panel
/// y el reporte que se corre en SSMS den los mismos números:</para>
/// <list type="bullet">
/// <item>La venta se fecha en el PRIMER pago cobrado de la reservación (estado APROBADO o REEMBOLSADO).</item>
/// <item>Los importes son los históricos de la reservación (regla R4): no se recalculan con los precios de hoy.</item>
/// <item>Se restan los reembolsos PROCESADOS, por su fecha de resolución.</item>
/// <item>La reservación se atribuye al tipo de servicio de su primera línea de detalle.</item>
/// </list>
/// <para>Son dos consultas a la base (ventas y reembolsos) más una de las líneas de esas reservaciones; las sumas
/// y los agrupamientos se hacen en memoria sobre las filas del periodo, que son pocas. Si algún día el periodo
/// trajera decenas de miles de reservaciones, conviene mover los GROUP BY a la base.</para>
/// </remarks>
public sealed class SAEstadisticasService : ISAEstadisticasService
{
    /// <summary>Estados de pago que cuentan como cobro: un cobro devuelto también fue una venta ese día.</summary>
    private static readonly string[] EstadosCobrados = { "APROBADO", "REEMBOLSADO" };

    private const string ReembolsoProcesado = "PROCESADO";
    private const string ReservacionCancelada = "CANCELADA";
    private const string SinTipo = "(sin detalle)";

    private readonly FlyVibeMexDbContext _db;

    public SAEstadisticasService(FlyVibeMexDbContext db)
    {
        _db = db;
    }

    public async Task<EstadisticasVentasViewModel> VentasAsync(FiltroEstadisticas filtro,
        CancellationToken cancellationToken = default)
    {
        var (desde, hasta) = filtro.Periodo();
        filtro.desde = desde;             // el formulario vuelve a mostrar el periodo que de verdad se usó
        filtro.hasta = hasta;
        var fin = hasta.AddDays(1);       // intervalo [desde, fin): así entra completo el último día

        var vm = new EstadisticasVentasViewModel { Filtro = filtro };

        var idsCobrados = await _db.EstadosPago.AsNoTracking()
            .Where(e => EstadosCobrados.Contains(e.nombre))
            .Select(e => e.id)
            .ToListAsync(cancellationToken);

        var idCancelada = await _db.EstadosReservacion.AsNoTracking()
            .Where(e => e.nombre == ReservacionCancelada)
            .Select(e => (ushort?)e.id)
            .FirstOrDefaultAsync(cancellationToken);

        // Una fila por reservación cobrada en el periodo, fechada en su primer cobro.
        var ventas = await (
            from r in _db.Reservaciones.AsNoTracking()
            join cobro in (
                    from p in _db.Pagos.AsNoTracking()
                    where idsCobrados.Contains(p.id_estado_pago)
                    group p by p.id_reservacion into g
                    select new { id_reservacion = g.Key, fecha = g.Min(x => x.fecha_pago) })
                on r.id equals cobro.id_reservacion
            where cobro.fecha >= desde && cobro.fecha < fin
            select new
            {
                r.id,
                cobro.fecha,
                r.subtotal,
                r.descuento_total,
                r.total,
                r.moneda,
                r.id_estado_reservacion
            }).ToListAsync(cancellationToken);

        // Reembolsos ya procesados en el periodo (por fecha de resolución), con la reservación a la que pertenecen.
        var reembolsos = await (
            from rb in _db.Reembolsos.AsNoTracking()
            join p in _db.Pagos.AsNoTracking() on rb.id_pago equals p.id
            where rb.estado == ReembolsoProcesado && rb.fecha_resolucion >= desde && rb.fecha_resolucion < fin
            select new { p.id_reservacion, rb.monto }).ToListAsync(cancellationToken);

        // Primera línea de cada reservación involucrada: de ahí salen el tipo de servicio y el nombre.
        var ids = ventas.Select(v => v.id).Concat(reembolsos.Select(r => r.id_reservacion)).Distinct().ToList();
        var primera = new Dictionary<ulong, (ulong id_servicio, string servicio, string tipo)>();
        if (ids.Count > 0)
        {
            var lineas = await (
                from d in _db.DetallesReservacion.AsNoTracking()
                join s in _db.Servicios.AsNoTracking() on d.id_servicio equals s.id
                where ids.Contains(d.id_reservacion)
                select new
                {
                    d.id_reservacion,
                    id_detalle = d.id,
                    id_servicio = s.id,
                    servicio = s.nombre,
                    tipo = s.tipo_servicio
                }).ToListAsync(cancellationToken);

            primera = lineas
                .GroupBy(l => l.id_reservacion)
                .ToDictionary(g => g.Key, g =>
                {
                    var l = g.OrderBy(x => x.id_detalle).First();
                    return (l.id_servicio, l.servicio, l.tipo);
                });
        }

        string TipoDe(ulong idReservacion) => primera.TryGetValue(idReservacion, out var linea) ? linea.tipo : SinTipo;

        // ---------------- Totales ----------------
        var monedas = ventas.Select(v => v.moneda).Distinct().ToList();
        if (monedas.Count > 0) vm.Moneda = monedas[0];
        vm.VariasMonedas = monedas.Count > 1;

        vm.Reservaciones = ventas.Count;
        vm.Subtotal = ventas.Sum(v => v.subtotal);
        vm.Descuentos = ventas.Sum(v => v.descuento_total);
        vm.Vendido = ventas.Sum(v => v.total);
        vm.Reembolsado = reembolsos.Sum(r => r.monto);
        vm.ReembolsosProcesados = reembolsos.Count;
        vm.Canceladas = idCancelada is ushort cancelada ? ventas.Count(v => v.id_estado_reservacion == cancelada) : 0;

        // ---------------- Por tipo de servicio ----------------
        var tipos = ventas.Select(v => TipoDe(v.id))
            .Concat(reembolsos.Select(r => TipoDe(r.id_reservacion)))
            .Distinct()
            .ToList();

        vm.PorTipo = tipos
            .Select(t => new VentasPorTipo
            {
                tipo = t,
                reservaciones = ventas.Count(v => TipoDe(v.id) == t),
                subtotal = ventas.Where(v => TipoDe(v.id) == t).Sum(v => v.subtotal),
                descuentos = ventas.Where(v => TipoDe(v.id) == t).Sum(v => v.descuento_total),
                vendido = ventas.Where(v => TipoDe(v.id) == t).Sum(v => v.total),
                reembolsado = reembolsos.Where(r => TipoDe(r.id_reservacion) == t).Sum(r => r.monto)
            })
            .OrderByDescending(f => f.vendido)
            .ThenBy(f => f.tipo)
            .ToList();

        // ---------------- Por día (o por mes si el periodo es largo) ----------------
        vm.AgrupadoPorMes = filtro.Dias() > FiltroEstadisticas.MaximoDiasEnGrafica;
        vm.PorPeriodo = vm.AgrupadoPorMes
            ? PorMes(ventas.Select(v => (v.fecha, v.total)).ToList(), desde, hasta)
            : PorDia(ventas.Select(v => (v.fecha, v.total)).ToList(), desde, hasta);

        // ---------------- Lo que más se vendió ----------------
        vm.MasVendidos = ventas
            .Where(v => primera.ContainsKey(v.id))
            .GroupBy(v => primera[v.id].id_servicio)
            .Select(g => new ServicioVendido
            {
                id_servicio = g.Key,
                servicio = primera[g.First().id].servicio,
                tipo = primera[g.First().id].tipo,
                reservaciones = g.Count(),
                vendido = g.Sum(v => v.total)
            })
            .OrderByDescending(s => s.vendido)
            .ThenBy(s => s.servicio)
            .Take(EstadisticasVentasViewModel.TopServicios)
            .ToList();

        return vm;
    }

    /// <summary>Una barra por día del periodo. Los días sin ventas van en cero: la gráfica no debe saltárselos.</summary>
    private static List<VentasPorPeriodo> PorDia(List<(DateTime fecha, decimal total)> ventas, DateTime desde, DateTime hasta)
    {
        var agrupado = ventas
            .GroupBy(v => v.fecha.Date)
            .ToDictionary(g => g.Key, g => (n: g.Count(), total: g.Sum(v => v.total)));

        var lista = new List<VentasPorPeriodo>();
        for (var dia = desde; dia <= hasta; dia = dia.AddDays(1))
        {
            agrupado.TryGetValue(dia, out var d);
            lista.Add(new VentasPorPeriodo
            {
                inicio = dia,
                etiqueta = dia.ToString("dd"),
                etiquetaLarga = dia.ToString("dddd d 'de' MMMM"),
                reservaciones = d.n,
                vendido = d.total
            });
        }
        return lista;
    }

    /// <summary>Una barra por mes del periodo, para rangos largos (un trimestre, un año).</summary>
    private static List<VentasPorPeriodo> PorMes(List<(DateTime fecha, decimal total)> ventas, DateTime desde, DateTime hasta)
    {
        var agrupado = ventas
            .GroupBy(v => new DateTime(v.fecha.Year, v.fecha.Month, 1))
            .ToDictionary(g => g.Key, g => (n: g.Count(), total: g.Sum(v => v.total)));

        var formatoCorto = desde.Year == hasta.Year ? "MMM" : "MM/yy";
        var lista = new List<VentasPorPeriodo>();
        for (var mes = new DateTime(desde.Year, desde.Month, 1); mes <= hasta; mes = mes.AddMonths(1))
        {
            agrupado.TryGetValue(mes, out var d);
            lista.Add(new VentasPorPeriodo
            {
                inicio = mes,
                etiqueta = mes.ToString(formatoCorto),
                etiquetaLarga = mes.ToString("MMMM 'de' yyyy"),
                reservaciones = d.n,
                vendido = d.total
            });
        }
        return lista;
    }
}
