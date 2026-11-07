using System.Globalization;
using FlyVibeMex.Data;
using FlyVibeMex.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace FlyVibeMex.Services;

/// <summary>
/// Implementación de <see cref="ICatalogoService"/> con EF Core.
/// </summary>
/// <remarks>
/// Estrategia (pensada para el tamaño del proyecto, decenas de servicios):
/// 1) Se cargan los servicios candidatos del tipo con LINQ (activos, proveedor activo, con su subtipo).
/// 2) Se carga en UNA consulta la disponibilidad de la ventana de fechas (máximo ~37 días).
/// 3) Los filtros por destino, cupo y precio se calculan en memoria con <see cref="Cotizar"/>, que es la misma
///    regla que usará la reserva (fase C).
/// Si el catálogo creciera a miles de servicios, los filtros de destino y fecha se moverían a la consulta SQL.
/// </remarks>
public sealed class CatalogoService : ICatalogoService
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-MX");

    private readonly FlyVibeMexDbContext _db;

    public CatalogoService(FlyVibeMexDbContext db)
    {
        _db = db;
    }

    private static DateOnly Hoy() => DateOnly.FromDateTime(DateTime.Today);

    // =============================================================================================
    // Sección (lista de tarjetas)
    // =============================================================================================

    public async Task LlenarSeccionAsync(BusquedaCatalogoViewModel b, bool aplicarFechas, CancellationToken cancellationToken = default)
    {
        var ubicaciones = await UbicacionesAsync(cancellationToken);
        var candidatos = await CandidatosAsync(b.tipo, null, ubicaciones, cancellationToken);

        // Listas desplegables: solo ciudades que tienen servicios de este tipo.
        b.Destinos = OpcionesUbicacion(candidatos.Select(c => c.id_destino), ubicaciones);
        if (b.tipo == TipoCatalogo.Vuelo)
            b.Origenes = OpcionesUbicacion(candidatos.Select(c => c.id_origen), ubicaciones);

        var filtrados = candidatos
            .Where(c => b.destino is null || c.id_destino == b.destino)
            .Where(c => b.tipo != TipoCatalogo.Vuelo || b.origen is null || c.id_origen == b.origen)
            .ToList();

        if (b.tipo == TipoCatalogo.Hospedaje)
        {
            b.etiquetas = b.etiquetas.Where(EtiquetasHospedaje.EsValida).Distinct().ToList();   // nunca confiar en el navegador
            b.TiposHabitacion = candidatos.Select(c => c.tipo_habitacion?.Trim())
                .Where(t => !string.IsNullOrEmpty(t)).Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.Create(Es, ignoreCase: true)).ToList();
            filtrados = filtrados
                .Where(c => string.IsNullOrWhiteSpace(b.tipo_habitacion)
                            || string.Equals(c.tipo_habitacion?.Trim(), b.tipo_habitacion.Trim(), StringComparison.OrdinalIgnoreCase))
                .Where(c => b.etiquetas.All(e => c.etiquetas.Contains(e)))
                .ToList();
        }

        b.BusquedaConFechas = aplicarFechas && b.desde is not null;
        var (inicioVentana, finVentana) = VentanaSeccion(b);
        var disponibilidad = await DisponibilidadAsync(null, b.tipo, inicioVentana, finVentana, cancellationToken);
        var ahora = DateTime.Now;

        var tarjetas = new List<TarjetaServicioViewModel>();
        foreach (var c in filtrados)
        {
            var dias = disponibilidad.GetValueOrDefault(c.id) ?? new Dictionary<DateOnly, FilaDisponibilidad>();

            // Vuelos: solo los que aún no salen y cuya fecha de salida cae en la ventana (±3 días o próximos 30).
            DateOnly? fechaVuelo = null;
            if (c.tipo == TipoCatalogo.Vuelo)
            {
                if (c.salida is null || c.salida <= ahora) continue;
                fechaVuelo = DateOnly.FromDateTime(c.salida.Value);
                if (fechaVuelo < inicioVentana || fechaVuelo > finVentana) continue;
            }

            var tarjeta = new TarjetaServicioViewModel
            {
                id = c.id,
                tipo = c.tipo,
                nombre = c.nombre,
                descripcion = c.descripcion,
                imagen_ruta = c.imagen_ruta,
                moneda = c.moneda,
                ubicacion = c.ubicacion_texto,
                etiquetas = c.etiquetas,
                fecha_texto = c.tipo == TipoCatalogo.Vuelo
                    ? $"{c.codigo_vuelo} · sale {c.salida!.Value.ToString("ddd dd/MM HH:mm", Es)}"
                    : null
            };

            if (b.BusquedaConFechas)
            {
                var cotizacion = Cotizar(c, b, dias);
                if (cotizacion.estado == EstadoCotizacion.NoAplica) continue;   // no sirve para esta búsqueda

                tarjeta.agotado = !cotizacion.Disponible;
                tarjeta.total = cotizacion.Disponible ? cotizacion.subtotal : null;
                tarjeta.resumen_total = cotizacion.resumen;
                tarjeta.precio_unitario = cotizacion.dias.Where(d => d.existe).Select(d => (decimal?)d.precio).Min();
                tarjeta.fecha_buscada = fechaVuelo is not null && fechaVuelo == b.desde;
            }
            else
            {
                // Sin fechas: precio "desde" = el más bajo de los días con cupo en la ventana.
                var conCupo = dias.Values
                    .Where(d => d.disponible > 0 && (fechaVuelo is null || d.fecha == fechaVuelo))
                    .ToList();
                tarjeta.agotado = conCupo.Count == 0;
                tarjeta.precio_unitario = conCupo.Count > 0 ? conCupo.Min(d => d.precio) : null;
            }

            tarjetas.Add(tarjeta);
        }
        static decimal? PrecioDe(TarjetaServicioViewModel t) => t.total ?? t.precio_unitario;
        tarjetas = tarjetas
            .Where(t => b.precio_min is null || (PrecioDe(t) is decimal p1 && p1 >= b.precio_min))
            .Where(t => b.precio_max is null || (PrecioDe(t) is decimal p2 && p2 <= b.precio_max))
            .ToList();
        b.Resultados = Ordenar(tarjetas, b.orden);
    }

    // =============================================================================================
    // Detalle y cotización
    // =============================================================================================

    public async Task<string?> TipoDeServicioAsync(ulong idServicio, CancellationToken cancellationToken = default)
    {
        var tipo = await _db.Servicios.AsNoTracking()
            .Where(s => s.id == idServicio && s.activo)
            .Select(s => s.tipo_servicio)
            .FirstOrDefaultAsync(cancellationToken);

        return TipoCatalogo.EsValido(tipo) ? tipo : null;
    }

    public async Task<DetalleServicioViewModel?> DetalleAsync(ulong idServicio, BusquedaCatalogoViewModel b, bool cotizar,
        CancellationToken cancellationToken = default)
    {
        var ubicaciones = await UbicacionesAsync(cancellationToken);
        var c = (await CandidatosAsync(b.tipo, idServicio, ubicaciones, cancellationToken)).FirstOrDefault();
        if (c is null) return null;

        // Un paquete exige un mínimo de personas: si aún no se buscó, se propone ese mínimo.
        if (c.tipo == TipoCatalogo.Paquete && b.desde is null)
            b.personas = Math.Max(b.personas, c.min_personas);

        var vm = new DetalleServicioViewModel
        {
            id = c.id,
            tipo = c.tipo,
            nombre = c.nombre,
            descripcion = c.descripcion,
            imagen_ruta = c.imagen_ruta,
            moneda = c.moneda,
            proveedor = c.proveedor,
            ubicacion = c.ubicacion_texto,
            Datos = DatosDe(c, ubicaciones),
            politica = string.IsNullOrWhiteSpace(c.politica_cancelacion)
                ? PoliticaCancelacion.Describir(c.permite_cancelacion, c.horas_reembolso_total, c.porcentaje_reembolso_tardio)
                : c.politica_cancelacion,
            Busqueda = b,
            Promociones = await PromocionesVigentesAsync(idServicio, cancellationToken)
        };

        if (c.tipo == TipoCatalogo.Paquete)
        {
            vm.Incluidos = await (
                    from ps in _db.PaquetesServicios.AsNoTracking()
                    join s in _db.Servicios.AsNoTracking() on ps.id_servicio_incluido equals s.id
                    where ps.id_servicio == idServicio
                    orderby ps.orden
                    select new IncluidoPaquete(s.nombre, s.tipo_servicio, ps.cantidad))
                .ToListAsync(cancellationToken);
        }

        // Disponibilidad de los próximos 30 días (no aplica a vuelos: tienen una sola fecha).
        if (c.tipo != TipoCatalogo.Vuelo)
        {
            var hoy = Hoy();
            var proximas = await DisponibilidadAsync(idServicio, null, hoy, hoy.AddDays(BusquedaCatalogoViewModel.DiasSinBusqueda - 1), cancellationToken);
            vm.ProximasFechas = (proximas.GetValueOrDefault(idServicio)?.Values ?? Enumerable.Empty<FilaDisponibilidad>())
                .OrderBy(f => f.fecha)
                .Select(f => new DiaCotizado(f.fecha, f.precio, f.disponible, true, f.disponible > 0))
                .ToList();
        }

        // Cotización: con fechas válidas, o siempre en vuelos (su fecha es la de salida).
        if (cotizar && (c.tipo == TipoCatalogo.Vuelo || b.desde is not null))
        {
            vm.Cotizacion = await CotizarCandidatoAsync(c, b, cancellationToken);
        }

        return vm;
    }

    public async Task<CotizacionServicio?> CotizarAsync(ulong idServicio, BusquedaCatalogoViewModel b, CancellationToken cancellationToken = default)
    {
        var tipo = await TipoDeServicioAsync(idServicio, cancellationToken);
        if (tipo is null) return null;
        b.tipo = tipo;
        if (tipo != TipoCatalogo.Vuelo && b.desde is null) return null;
        if (TipoCatalogo.UsaRango(tipo) && (b.hasta is null || b.hasta <= b.desde)) return null;

        var ubicaciones = await UbicacionesAsync(cancellationToken);
        var c = (await CandidatosAsync(tipo, idServicio, ubicaciones, cancellationToken)).FirstOrDefault();
        return c is null ? null : await CotizarCandidatoAsync(c, b, cancellationToken);
    }

    private async Task<CotizacionServicio> CotizarCandidatoAsync(Candidato c, BusquedaCatalogoViewModel b, CancellationToken cancellationToken)
    {
        DateOnly inicio, fin;
        if (c.tipo == TipoCatalogo.Vuelo)
        {
            inicio = fin = DateOnly.FromDateTime(c.salida!.Value);
        }
        else if (TipoCatalogo.UsaRango(c.tipo))
        {
            inicio = b.desde!.Value;
            fin = b.hasta!.Value.AddDays(-1);   // la última noche / día de renta es la víspera de la salida
        }
        else
        {
            inicio = fin = b.desde!.Value;
        }

        var filas = await DisponibilidadAsync(c.id, null, inicio, fin, cancellationToken);
        return Cotizar(c, b, filas.GetValueOrDefault(c.id) ?? new Dictionary<DateOnly, FilaDisponibilidad>());
    }

    /// <summary>
    /// Regla ÚNICA de precio y cupo (la usan la sección, el detalle y la reserva).
    /// Requiere <c>b.desde</c> (y <c>b.hasta</c> en hospedaje / vehículo), excepto en vuelos.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Qué significa <c>cantidad</c>:</b> es la UNIDAD que se descuenta de
    /// <c>disponibilidad_servicio</c> por cada día del periodo (R3), y cambia según el tipo:
    /// hospedaje = habitaciones · vehículo = 1 (el auto, no sus pasajeros) · vuelo = asientos (una persona, un
    /// asiento) · paquete y tour = personas. De ahí que un vehículo para 5 pasajeros descuente 1 y un vuelo
    /// para 5 pasajeros descuente 5.
    /// </para>
    /// <para>
    /// <b>Qué significa <c>precio_unitario</c>:</b> la SUMA de los precios diarios del periodo, es decir el
    /// precio de UNA unidad por toda la estancia (no por día). El total es <c>precio_unitario × cantidad</c>.
    /// Es la misma convención que guarda <c>detalle_reservacion</c> al comprar, para que el snapshot (R4)
    /// coincida con lo que vio el cliente.
    /// </para>
    /// <para>
    /// <b>Tres resultados posibles:</b> <c>NoAplica</c> = la búsqueda no tiene sentido para este servicio (no
    /// cabe la gente, el vuelo ya salió) y la tarjeta se oculta; <c>SinCupo</c> = sirve pero no alcanza el
    /// inventario, y la tarjeta se muestra en gris; y el caso normal, con precio.
    /// </para>
    /// </remarks>
    private static CotizacionServicio Cotizar(Candidato c, BusquedaCatalogoViewModel b, IReadOnlyDictionary<DateOnly, FilaDisponibilidad> filas)
    {
        var personas = Math.Clamp(b.personas, 1, BusquedaCatalogoViewModel.MaxPersonas);
        var cot = new CotizacionServicio { moneda = c.moneda };
        List<DateOnly> fechas;

        // ---- Paso 1: según el tipo, qué fechas se cobran, cuántas unidades y si la búsqueda cabe ----
        // Cada rama fija `fechas` (los días que se descuentan), `cantidad` (la unidad del tipo),
        // `inicio`/`fin` (fechas reales del servicio, con sus horas) y `resumen` (el texto que ve el cliente).
        // Si la búsqueda no cabe en el servicio, sale por MarcarNoAplica y la tarjeta ni se muestra.
        switch (c.tipo)
        {
            case TipoCatalogo.Hospedaje:
            {
                var habitaciones = Math.Clamp(b.habitaciones, 1, BusquedaCatalogoViewModel.MaxHabitaciones);
                var entrada = b.desde!.Value;
                var salida = b.hasta!.Value;
                fechas = Rango(entrada, salida);
                cot.cantidad = habitaciones;
                cot.inicio = entrada.ToDateTime(c.hora_entrada ?? new TimeOnly(15, 0));
                cot.fin = salida.ToDateTime(c.hora_salida ?? new TimeOnly(12, 0));
                cot.resumen = $"{Plural(fechas.Count, "noche", "noches")} · {Plural(habitaciones, "habitación", "habitaciones")} · {Plural(personas, "huésped", "huéspedes")}";
                if (personas > c.capacidad * habitaciones)
                    return cot.MarcarNoAplica($"Cada habitación es para máximo {Plural(c.capacidad, "huésped", "huéspedes")}: agrega habitaciones.");
                break;
            }
            case TipoCatalogo.Vehiculo:
            {
                var retiro = b.desde!.Value;
                var devolucion = b.hasta!.Value;
                fechas = Rango(retiro, devolucion);
                cot.cantidad = 1;
                cot.inicio = retiro.ToDateTime(new TimeOnly(10, 0));
                cot.fin = devolucion.ToDateTime(new TimeOnly(10, 0));
                cot.resumen = $"{Plural(fechas.Count, "día", "días")} de renta · {Plural(personas, "pasajero", "pasajeros")}";
                if (personas > c.capacidad)
                    return cot.MarcarNoAplica($"Este vehículo es para máximo {Plural(c.capacidad, "pasajero", "pasajeros")}.");
                break;
            }
            case TipoCatalogo.Vuelo:
            {
                var salida = c.salida!.Value;
                fechas = new List<DateOnly> { DateOnly.FromDateTime(salida) };
                cot.cantidad = personas;
                cot.inicio = salida;
                cot.fin = c.llegada ?? salida;
                cot.resumen = Plural(personas, "pasajero", "pasajeros");
                if (salida <= DateTime.Now)
                    return cot.MarcarNoAplica("Este vuelo ya salió.");
                break;
            }
            case TipoCatalogo.Paquete:
            {
                var dia = b.desde!.Value;
                fechas = new List<DateOnly> { dia };
                cot.cantidad = personas;
                cot.inicio = dia.ToDateTime(TimeOnly.MinValue);
                cot.fin = dia.AddDays(Math.Max(c.duracion_dias, 1) - 1).ToDateTime(new TimeOnly(23, 59));
                cot.resumen = $"{Plural(c.duracion_dias, "día", "días")} · {Plural(personas, "persona", "personas")}";
                if (personas < c.min_personas || personas > c.max_personas)
                    return cot.MarcarNoAplica($"Este paquete es para {c.min_personas} a {c.max_personas} personas.");
                break;
            }
            default:   // EVENTO (Tours)
            {
                var dia = b.desde!.Value;
                fechas = new List<DateOnly> { dia };
                cot.cantidad = personas;
                cot.inicio = dia.ToDateTime(TimeOnly.MinValue);
                cot.fin = dia.ToDateTime(new TimeOnly(23, 59));
                cot.resumen = Plural(personas, "persona", "personas");
                break;
            }
        }

        // ---- Paso 2: cruzar esas fechas con el inventario día por día ----
        // `existe = false` distingue "no hay fila cargada para ese día" de "hay fila pero sin cupo": son dos
        // problemas distintos para el gestor, y el mensaje del paso 4 lo refleja.
        cot.unidades_periodo = fechas.Count;
        foreach (var fecha in fechas)
        {
            if (filas.TryGetValue(fecha, out var fila))
                cot.dias.Add(new DiaCotizado(fecha, fila.precio, fila.disponible, true, fila.disponible >= cot.cantidad));
            else
                cot.dias.Add(new DiaCotizado(fecha, 0, 0, false, false));
        }

        // ---- Paso 3: precio de una unidad por todo el periodo (ver <remarks>) ----
        cot.precio_unitario = cot.dias.Sum(d => d.precio);

        // ---- Paso 4: si falla un solo día, no hay reserva posible ----
        // Se informa el PRIMER día que falla, no todos: al cliente le sirve una fecha concreta que corregir.
        var falla = cot.dias.FirstOrDefault(d => !d.alcanza);
        if (falla is not null)
        {
            cot.estado = EstadoCotizacion.SinCupo;
            cot.motivo = falla.existe
                ? $"Sin cupo suficiente el {falla.fecha.ToString("ddd dd/MM/yyyy", Es)} (quedan {falla.disponible})."
                : $"No hay disponibilidad cargada para el {falla.fecha.ToString("ddd dd/MM/yyyy", Es)}.";
        }

        return cot;
    }

    // =============================================================================================
    // Consultas de apoyo
    // =============================================================================================

    /// <summary>Datos de un servicio candidato, ya unidos con su subtipo y su ubicación.</summary>
    private sealed class Candidato
    {
        public ulong id { get; set; }
        public string tipo { get; set; } = string.Empty;
        public string nombre { get; set; } = string.Empty;
        public string? descripcion { get; set; }
        public string moneda { get; set; } = "MXN";
        public string proveedor { get; set; } = string.Empty;
        public ulong? id_ubicacion_proveedor { get; set; }

        public bool permite_cancelacion { get; set; }
        public int horas_reembolso_total { get; set; }
        public decimal porcentaje_reembolso_tardio { get; set; }
        public string? politica_cancelacion { get; set; }

        // ---- Se llenan en memoria según el subtipo ----
        public ulong? id_destino { get; set; }          // ciudad del hotel / retiro / destino del vuelo / paquete / tour
        public ulong? id_origen { get; set; }           // solo vuelos
        public string ubicacion_texto { get; set; } = string.Empty;
        public string? imagen_ruta { get; set; }
        public int capacidad { get; set; } = int.MaxValue;

        public List<string> etiquetas { get; set; } = new();
        public TimeOnly? hora_entrada { get; set; }
        public TimeOnly? hora_salida { get; set; }
        public DateTime? salida { get; set; }
        public DateTime? llegada { get; set; }
        public string codigo_vuelo { get; set; } = string.Empty;
        public string clase { get; set; } = string.Empty;
        public string tipo_habitacion { get; set; } = string.Empty;
        public string? categoria { get; set; }
        public string marca_modelo { get; set; } = string.Empty;
        public int duracion_dias { get; set; } = 1;
        public int min_personas { get; set; } = 1;
        public int max_personas { get; set; } = BusquedaCatalogoViewModel.MaxPersonas;
    }

    private sealed record FilaDisponibilidad(ulong id_servicio, DateOnly fecha, int disponible, decimal precio);

    private sealed record Ubicacion(string ciudad, string completa);

    /// <summary>Ubicaciones activas (tabla pequeña): id → "Cancún" / "Cancún, Quintana Roo".</summary>
    private async Task<Dictionary<ulong, Ubicacion>> UbicacionesAsync(CancellationToken cancellationToken)
    {
        var lista = await _db.Ubicaciones.AsNoTracking()
            .Where(u => u.activo)
            .Select(u => new { u.id, u.municipio_ciudad, u.estado })
            .ToListAsync(cancellationToken);

        return lista.ToDictionary(u => u.id, u => new Ubicacion(u.municipio_ciudad, $"{u.municipio_ciudad}, {u.estado}"));
    }

    /// <summary>
    /// Servicios activos del tipo (o solo <paramref name="idServicio"/>) con su subtipo. Se descartan los que no
    /// tienen subtipo o cuya ubicación está inactiva.
    /// </summary>
    private async Task<List<Candidato>> CandidatosAsync(string tipo, ulong? idServicio, Dictionary<ulong, Ubicacion> ubicaciones,
        CancellationToken cancellationToken)
    {
        var servicios = _db.Servicios.AsNoTracking().Where(s => s.activo && s.tipo_servicio == tipo);
        if (idServicio is ulong id) servicios = servicios.Where(s => s.id == id);

        var candidatos = await (
                from s in servicios
                join p in _db.Proveedores.AsNoTracking() on s.id_proveedor equals p.id
                where p.activo
                select new Candidato
                {
                    id = s.id,
                    tipo = s.tipo_servicio,
                    nombre = s.nombre,
                    descripcion = s.descripcion,
                    moneda = s.moneda,
                    proveedor = p.nombre,
                    id_ubicacion_proveedor = p.id_ubicacion,
                    permite_cancelacion = s.permite_cancelacion,
                    horas_reembolso_total = s.horas_reembolso_total,
                    porcentaje_reembolso_tardio = s.porcentaje_reembolso_tardio,
                    politica_cancelacion = s.politica_cancelacion,
                    imagen_ruta = s.imagen_ruta        // L6: la imagen ya viene del supertipo
                })
            .ToListAsync(cancellationToken);

        if (candidatos.Count == 0) return candidatos;

        // Cada subtipo completa sus propios campos. Las consultas se UNEN a `servicios`, que ya trae el filtro
        // del tipo y —en el detalle— el del id: así el SQL lee solo las filas de subtipo que de verdad se usan.
        // Antes se leía la tabla de subtipo COMPLETA (todas las filas y todas las columnas), incluso cuando solo
        // se mostraba el detalle de un servicio.  [L3.8 y L4.4 de docs/PLAN_CALIDAD_Y_OPTIMIZACION.md]
        switch (tipo)
        {
            case TipoCatalogo.Hospedaje:
                await CompletarHospedajesAsync(candidatos, servicios, cancellationToken);
                break;
            case TipoCatalogo.Vehiculo:
                await CompletarVehiculosAsync(candidatos, servicios, cancellationToken);
                break;
            case TipoCatalogo.Vuelo:
                await CompletarVuelosAsync(candidatos, servicios, cancellationToken);
                break;
            case TipoCatalogo.Paquete:
                await CompletarPaquetesAsync(candidatos, servicios, cancellationToken);
                break;
            default:   // EVENTO (Tours): la ciudad es la ubicación de su proveedor
                foreach (var c in candidatos)
                    c.id_destino = c.id_ubicacion_proveedor;
                break;
        }

        // Solo los que tienen subtipo y ubicaciones activas (en vuelos, origen y destino).
        var validos = candidatos
            .Where(c => c.id_destino is ulong d && ubicaciones.ContainsKey(d))
            .Where(c => tipo != TipoCatalogo.Vuelo || (c.salida is not null && c.id_origen is ulong o && ubicaciones.ContainsKey(o)))
            .ToList();

        foreach (var c in validos)
        {
            var destino = ubicaciones[c.id_destino!.Value];
            c.ubicacion_texto = tipo == TipoCatalogo.Vuelo
                ? $"{ubicaciones[c.id_origen!.Value].ciudad} → {destino.ciudad}"
                : destino.completa;
        }

        return validos;
    }

    /// <summary>Completa los campos propios de HOSPEDAJE (ciudad, capacidad, horarios, habitación, imagen).</summary>
    private async Task CompletarHospedajesAsync(List<Candidato> candidatos, IQueryable<Servicios> servicios,
        CancellationToken cancellationToken)
    {
        var hospedajes = await (
                from h in _db.Hospedajes.AsNoTracking()
                join s in servicios on h.id_servicio equals s.id
                select new
                {
                    h.id_servicio,
                    h.id_ubicacion,
                    h.capacidad_huespedes,
                    h.hora_entrada,
                    h.hora_salida,
                    h.tipo_habitacion,
                    h.categoria
                })
            .ToDictionaryAsync(h => h.id_servicio, cancellationToken);
        
        var etiquetasPorHotel = (await _db.HospedajeEtiquetas.AsNoTracking()
                .Where(e => servicios.Any(s => s.id == e.id_servicio))
                .ToListAsync(cancellationToken))
            .GroupBy(e => e.id_servicio)
            .ToDictionary(g => g.Key, g => g.Select(e => e.etiqueta).OrderBy(e => e).ToList());

        foreach (var c in candidatos)
        {
            if (!hospedajes.TryGetValue(c.id, out var hosp)) continue;
            c.id_destino = hosp.id_ubicacion;
            c.capacidad = hosp.capacidad_huespedes;
            c.hora_entrada = hosp.hora_entrada;
            c.hora_salida = hosp.hora_salida;
            c.tipo_habitacion = hosp.tipo_habitacion;
            c.categoria = hosp.categoria;
            c.etiquetas = etiquetasPorHotel.GetValueOrDefault(c.id) ?? new List<string>();
        }
    }

    /// <summary>Completa los campos propios de VEHICULO (ciudad de retiro, capacidad, marca y modelo, imagen).</summary>
    private async Task CompletarVehiculosAsync(List<Candidato> candidatos, IQueryable<Servicios> servicios,
        CancellationToken cancellationToken)
    {
        var vehiculos = await (
                from v in _db.Vehiculos.AsNoTracking()
                join s in servicios on v.id_servicio equals s.id
                select new
                {
                    v.id_servicio,
                    v.id_ubicacion_retiro,
                    v.capacidad_pasajeros,
                    v.categoria,
                    v.marca,
                    v.modelo
                })
            .ToDictionaryAsync(v => v.id_servicio, cancellationToken);

        foreach (var c in candidatos)
        {
            if (!vehiculos.TryGetValue(c.id, out var veh)) continue;
            c.id_destino = veh.id_ubicacion_retiro;
            c.capacidad = veh.capacidad_pasajeros;
            c.categoria = veh.categoria;
            c.marca_modelo = $"{veh.marca} {veh.modelo}";
        }
    }

    /// <summary>Completa los campos propios de VUELO (ruta, horario, código y clase).</summary>
    private async Task CompletarVuelosAsync(List<Candidato> candidatos, IQueryable<Servicios> servicios,
        CancellationToken cancellationToken)
    {
        var vuelos = await (
                from v in _db.Vuelos.AsNoTracking()
                join s in servicios on v.id_servicio equals s.id
                select new
                {
                    v.id_servicio,
                    v.id_origen,
                    v.id_destino,
                    v.fecha_salida,
                    v.fecha_llegada,
                    v.codigo_vuelo,
                    v.clase
                })
            .ToDictionaryAsync(v => v.id_servicio, cancellationToken);

        foreach (var c in candidatos)
        {
            if (!vuelos.TryGetValue(c.id, out var vue)) continue;
            c.id_origen = vue.id_origen;
            c.id_destino = vue.id_destino;
            c.salida = vue.fecha_salida;
            c.llegada = vue.fecha_llegada;
            c.codigo_vuelo = vue.codigo_vuelo;
            c.clase = vue.clase;
        }
    }

    /// <summary>
    /// Completa los campos propios de PAQUETE (duración, personas, imagen) y deduce su destino.
    /// </summary>
    /// <remarks>
    /// Destino: el que indicó el gestor; si no, la ciudad de su hospedaje incluido; si no, el destino de su vuelo
    /// incluido. El orden de los incluidos importa (decide cuál ciudad gana cuando hay más de un hospedaje), así
    /// que se ordena por <c>orden</c> con los nulos al final —igual que antes— ANTES de agrupar: <c>ToLookup</c>
    /// conserva ese orden dentro de cada paquete.  [L3.9 de docs/PLAN_CALIDAD_Y_OPTIMIZACION.md: antes se
    /// recorría la lista completa del puente una vez por paquete, O(n×m)]
    /// Las ciudades de hospedajes y los destinos de vuelos NO se pueden filtrar por `servicios` (son servicios
    /// INCLUIDOS, de otro tipo), pero ya se leen proyectados a dos columnas.
    /// </remarks>
    private async Task CompletarPaquetesAsync(List<Candidato> candidatos, IQueryable<Servicios> servicios,
        CancellationToken cancellationToken)
    {
        var paquetes = await (
                from pq in _db.Paquetes.AsNoTracking()
                join s in servicios on pq.id_servicio equals s.id
                select new
                {
                    pq.id_servicio,
                    pq.id_ubicacion_destino,
                    pq.duracion_dias,
                    pq.min_personas,
                    pq.max_personas
                })
            .ToDictionaryAsync(pq => pq.id_servicio, cancellationToken);

        var componentes = await (
                from ps in _db.PaquetesServicios.AsNoTracking()
                join s in servicios on ps.id_servicio equals s.id
                select new { ps.id_servicio, ps.id_servicio_incluido, ps.orden })
            .ToListAsync(cancellationToken);

        var incluidosPorPaquete = componentes
            .OrderBy(x => x.orden ?? ushort.MaxValue)
            .ToLookup(x => x.id_servicio, x => x.id_servicio_incluido);

        var ciudadHotel = await _db.Hospedajes.AsNoTracking()
            .Select(h => new { h.id_servicio, h.id_ubicacion })
            .ToDictionaryAsync(h => h.id_servicio, h => h.id_ubicacion, cancellationToken);
        var destinoVuelo = await _db.Vuelos.AsNoTracking()
            .Select(v => new { v.id_servicio, v.id_destino })
            .ToDictionaryAsync(v => v.id_servicio, v => v.id_destino, cancellationToken);

        foreach (var c in candidatos)
        {
            if (!paquetes.TryGetValue(c.id, out var pq)) continue;

            var incluidos = incluidosPorPaquete[c.id];
            c.id_destino = pq.id_ubicacion_destino
                ?? incluidos.Where(ciudadHotel.ContainsKey).Select(i => (ulong?)ciudadHotel[i]).FirstOrDefault()
                ?? incluidos.Where(destinoVuelo.ContainsKey).Select(i => (ulong?)destinoVuelo[i]).FirstOrDefault();

            c.duracion_dias = pq.duracion_dias;
            c.min_personas = pq.min_personas;
            c.max_personas = pq.max_personas;
        }
    }

    /// <summary>
    /// Disponibilidad entre dos fechas (incluidas), agrupada por servicio y por día.
    /// Con <paramref name="idServicio"/> solo lee ese servicio; con <paramref name="tipo"/>, solo los servicios
    /// activos de ese tipo.
    /// </summary>
    /// <remarks>
    /// El filtro por <paramref name="tipo"/> se agregó en el lote L2 (L3.7 de
    /// <c>docs/PLAN_CALIDAD_Y_OPTIMIZACION.md</c>). Antes la sección leía la ventana de fechas de TODOS los
    /// servicios de la base —<c>disponibilidad_servicio</c> es la tabla con más filas del esquema— y descartaba
    /// en memoria los que no eran de su tipo. Se filtra con un <c>Any()</c> (que EF traduce a <c>EXISTS</c>) y no
    /// con un <c>Contains</c> sobre una lista de ids, para no depender de cómo EF convierte los <c>ulong</c>.
    /// </remarks>
    private async Task<Dictionary<ulong, Dictionary<DateOnly, FilaDisponibilidad>>> DisponibilidadAsync(
        ulong? idServicio, string? tipo, DateOnly desde, DateOnly hasta, CancellationToken cancellationToken)
    {
        var consulta = _db.DisponibilidadServicios.AsNoTracking().Where(d => d.fecha >= desde && d.fecha <= hasta);

        if (idServicio is ulong id)
        {
            consulta = consulta.Where(d => d.id_servicio == id);
        }
        else if (tipo is not null)
        {
            consulta = consulta.Where(d =>
                _db.Servicios.Any(s => s.id == d.id_servicio && s.activo && s.tipo_servicio == tipo));
        }

        var filas = await consulta
            .Select(d => new FilaDisponibilidad(d.id_servicio, d.fecha, d.cantidad_disponible, d.precio_fecha))
            .ToListAsync(cancellationToken);

        return filas
            .GroupBy(f => f.id_servicio)
            .ToDictionary(g => g.Key, g => g.ToDictionary(f => f.fecha));
    }

    private async Task<List<PromocionVisible>> PromocionesVigentesAsync(ulong idServicio, CancellationToken cancellationToken)
    {
        var ahora = DateTime.Now;
        return await (
                from pr in _db.Promociones.AsNoTracking()
                join ps in _db.PromocionesServicios.AsNoTracking() on pr.id equals ps.id_promocion
                where ps.id_servicio == idServicio && pr.activo && pr.fecha_inicio <= ahora && pr.fecha_fin >= ahora
                orderby pr.codigo
                select new PromocionVisible(pr.codigo, pr.nombre, pr.descripcion, pr.tipo_descuento, pr.valor, pr.minimo_compra))
            .ToListAsync(cancellationToken);
    }

    // =============================================================================================
    // Ayudantes
    // =============================================================================================

    /// <summary>Fechas que se leen de disponibilidad_servicio para la sección.</summary>
    private static (DateOnly inicio, DateOnly fin) VentanaSeccion(BusquedaCatalogoViewModel b)
    {
        var hoy = Hoy();
        if (!b.BusquedaConFechas)
            return (hoy, hoy.AddDays(BusquedaCatalogoViewModel.DiasSinBusqueda - 1));

        var desde = b.desde!.Value;
        if (TipoCatalogo.UsaRango(b.tipo))
            return (desde, b.hasta!.Value.AddDays(-1));
        if (b.tipo == TipoCatalogo.Vuelo)
            return (desde.AddDays(-BusquedaCatalogoViewModel.DiasFlexiblesVuelo), desde.AddDays(BusquedaCatalogoViewModel.DiasFlexiblesVuelo));
        return (desde, desde);
    }

    private static List<SelectListItem> OpcionesUbicacion(IEnumerable<ulong?> ids, Dictionary<ulong, Ubicacion> ubicaciones) =>
        ids.Where(i => i is not null)
            .Select(i => i!.Value)
            .Distinct()
            .Where(ubicaciones.ContainsKey)
            .Select(i => new SelectListItem(ubicaciones[i].completa, i.ToString()))
            .OrderBy(o => o.Text, StringComparer.Create(Es, ignoreCase: true))
            .ToList();

    private static List<TarjetaServicioViewModel> Ordenar(List<TarjetaServicioViewModel> tarjetas, OrdenCatalogo orden)
    {
        static decimal Precio(TarjetaServicioViewModel t) => t.total ?? t.precio_unitario ?? decimal.MaxValue;
        var comparador = StringComparer.Create(Es, ignoreCase: true);

        var disponibles = tarjetas.Where(t => !t.agotado);
        var ordenadas = orden switch
        {
            OrdenCatalogo.PrecioDesc => disponibles.OrderByDescending(Precio),
            OrdenCatalogo.Nombre => disponibles.OrderBy(t => t.nombre, comparador),
            _ => disponibles.OrderBy(Precio)
        };

        // Los agotados siempre al final.
        return ordenadas.Concat(tarjetas.Where(t => t.agotado).OrderBy(t => t.nombre, comparador)).ToList();
    }

    private static List<DatoServicio> DatosDe(Candidato c, Dictionary<ulong, Ubicacion> ubicaciones)
    {
        var datos = new List<DatoServicio>();
        switch (c.tipo)
        {
            case TipoCatalogo.Hospedaje:
                datos.Add(new("Habitación", c.tipo_habitacion));
                if (!string.IsNullOrWhiteSpace(c.categoria)) datos.Add(new("Categoría", c.categoria));
                datos.Add(new("Capacidad", $"{Plural(c.capacidad, "huésped", "huéspedes")} por habitación"));
                datos.Add(new("Entrada / salida", $"{c.hora_entrada:HH\\:mm} / {c.hora_salida:HH\\:mm}"));
                datos.Add(new("Ubicación", c.ubicacion_texto));
                break;
            case TipoCatalogo.Vehiculo:
                datos.Add(new("Vehículo", c.marca_modelo));
                if (!string.IsNullOrWhiteSpace(c.categoria)) datos.Add(new("Categoría", c.categoria));
                datos.Add(new("Capacidad", Plural(c.capacidad, "pasajero", "pasajeros")));
                datos.Add(new("Retiro y devolución", c.ubicacion_texto));
                break;
            case TipoCatalogo.Vuelo:
                datos.Add(new("Vuelo", c.codigo_vuelo));
                datos.Add(new("Ruta", c.ubicacion_texto));
                datos.Add(new("Salida", c.salida?.ToString("dddd dd/MM/yyyy HH:mm", Es) ?? "—"));
                datos.Add(new("Llegada", c.llegada?.ToString("dddd dd/MM/yyyy HH:mm", Es) ?? "—"));
                datos.Add(new("Clase", c.clase));
                break;
            case TipoCatalogo.Paquete:
                datos.Add(new("Duración", Plural(c.duracion_dias, "día", "días")));
                datos.Add(new("Personas", $"De {c.min_personas} a {c.max_personas}"));
                datos.Add(new("Destino", c.ubicacion_texto));
                break;
            default:
                datos.Add(new("Ciudad", c.ubicacion_texto));
                break;
        }
        datos.Add(new("Proveedor", c.proveedor));
        return datos;
    }

    /// <summary>Fechas [desde, hasta) — una por noche / día de renta.</summary>
    private static List<DateOnly> Rango(DateOnly desde, DateOnly hasta) =>
        Enumerable.Range(0, Math.Max(hasta.DayNumber - desde.DayNumber, 0)).Select(desde.AddDays).ToList();

    private static string Plural(int n, string singular, string plural) => $"{n} {(n == 1 ? singular : plural)}";
}
