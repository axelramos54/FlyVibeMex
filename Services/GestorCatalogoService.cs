using FlyVibeMex.Data;
using FlyVibeMex.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FlyVibeMex.Services;

/// <summary>Implementación de <see cref="IGestorCatalogoService"/> con EF Core (todas las consultas con LINQ).</summary>
/// <remarks>
/// - Las escrituras de varias tablas van en una transacción dentro de la execution strategy (la conexión tiene
///   reintentos activos): si se reintenta, se repite TODO el bloque desde cero.
/// - Las bajas son lógicas (activo = 0): detalle_reservacion, paquete_servicio y promocion_servicio apuntan al servicio.
/// - Cada cambio deja un registro en la bitácora en la misma transacción.
/// </remarks>
public sealed class GestorCatalogoService : IGestorCatalogoService
{
    private const int MaximoResultados = 200;

    private readonly FlyVibeMexDbContext _db;
    private readonly IAlmacenImagenes _imagenes;
    private readonly ILogger<GestorCatalogoService> _logger;

    public GestorCatalogoService(FlyVibeMexDbContext db, IAlmacenImagenes imagenes, ILogger<GestorCatalogoService> logger)
    {
        _db = db;
        _imagenes = imagenes;
        _logger = logger;
    }

    private static DateOnly Hoy() => DateOnly.FromDateTime(DateTime.Today);

    // =============================================================================================
    // Ubicaciones y proveedores
    // =============================================================================================

    public Task<List<Ubicaciones>> UbicacionesAsync(CancellationToken cancellationToken = default) =>
        _db.Ubicaciones.AsNoTracking()
            .Where(u => u.activo)
            .OrderBy(u => u.municipio_ciudad).ThenBy(u => u.estado)
            .ToListAsync(cancellationToken);

    public Task<List<Proveedores>> ProveedoresAsync(string? nombre, string? tipo, bool incluirInactivos, int maximo = 200,
        CancellationToken cancellationToken = default)
    {
        var q = _db.Proveedores.AsNoTracking().AsQueryable();
        if (!incluirInactivos) q = q.Where(p => p.activo);
        if (tipo is not null) q = q.Where(p => p.tipo_proveedor == tipo);
        if (nombre is not null) q = q.Where(p => p.nombre.Contains(nombre));
        return q.OrderBy(p => p.nombre).Take(Math.Clamp(maximo, 1, MaximoResultados)).ToListAsync(cancellationToken);
    }

    public Task<Proveedores?> ProveedorAsync(ulong id, CancellationToken cancellationToken = default) =>
        _db.Proveedores.AsNoTracking().FirstOrDefaultAsync(p => p.id == id, cancellationToken);

    public async Task<ResultadoOperacion> GuardarProveedorAsync(ulong? id, ProveedorFormViewModel datos, ulong idUsuario,
        CancellationToken cancellationToken = default)
    {
        var nombre = datos.nombre.Trim();
        var telefono = datos.telefono.Trim();
        var correo = datos.correo.Trim().ToLowerInvariant();
        var errores = new List<(string campo, string mensaje)>();

        if (datos.id_ubicacion is ulong idUbicacion && !await UbicacionActivaAsync(idUbicacion, cancellationToken))
            errores.Add((nameof(datos.id_ubicacion), "Elige una ubicación de la lista."));
        if (await _db.Proveedores.AsNoTracking().AnyAsync(p => p.correo == correo && p.id != (id ?? 0), cancellationToken))
            errores.Add((nameof(datos.correo), "Ya hay otro proveedor con ese correo."));
        if (await _db.Proveedores.AsNoTracking().AnyAsync(p => p.telefono == telefono && p.id != (id ?? 0), cancellationToken))
            errores.Add((nameof(datos.telefono), "Ya hay otro proveedor con ese teléfono."));
        if (errores.Count > 0) return new ResultadoOperacion(false, "Revisa los datos.", errores);

        try
        {
            if (id is null)
            {
                await EnTransaccionAsync(async () =>
                {
                    var ahora = DateTime.Now;
                    var proveedor = new Proveedores
                    {
                        id_ubicacion = datos.id_ubicacion,
                        tipo_proveedor = datos.tipo_proveedor,
                        nombre = nombre,
                        telefono = telefono,
                        correo = correo,
                        activo = true,
                        creado_en = ahora,
                        actualizado_en = ahora
                    };
                    _db.Proveedores.Add(proveedor);
                    await _db.SaveChangesAsync(cancellationToken);

                    _db.RegistrarBitacora(idUsuario, "CREAR", "proveedores", proveedor.id, $"Alta del proveedor \"{nombre}\" ({datos.tipo_proveedor}).");
                    await _db.SaveChangesAsync(cancellationToken);
                }, cancellationToken);
                return ResultadoOperacion.Ok($"Se registró el proveedor \"{nombre}\".");
            }

            var idProveedor = id.Value;
            var encontrado = true;
            await EnTransaccionAsync(async () =>
            {
                var filas = await _db.Proveedores
                    .Where(p => p.id == idProveedor)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(p => p.id_ubicacion, datos.id_ubicacion)
                        .SetProperty(p => p.tipo_proveedor, datos.tipo_proveedor)
                        .SetProperty(p => p.nombre, nombre)
                        .SetProperty(p => p.telefono, telefono)
                        .SetProperty(p => p.correo, correo)
                        .SetProperty(p => p.activo, datos.activo)
                        .SetProperty(p => p.actualizado_en, DateTime.Now), cancellationToken);
                encontrado = filas == 1;
                if (!encontrado) return;

                _db.RegistrarBitacora(idUsuario, "ACTUALIZAR", "proveedores", idProveedor,
                    $"Datos del proveedor \"{nombre}\" actualizados{(datos.activo ? "" : " (queda inactivo)")}.");
                await _db.SaveChangesAsync(cancellationToken);
            }, cancellationToken);

            return encontrado
                ? ResultadoOperacion.Ok($"Se guardaron los cambios de \"{nombre}\".")
                : ResultadoOperacion.Falla("El proveedor ya no existe.");
        }
        catch (Exception ex) when (EsErrorDeBd(ex))
        {
            _logger.LogError(ex, "No se pudo guardar el proveedor {IdProveedor}.", id);
            return ResultadoOperacion.Falla("No se pudo guardar el proveedor. Intenta de nuevo.");
        }
    }

    public async Task<ResultadoOperacion> CambiarEstadoProveedorAsync(ulong id, bool activo, ulong idUsuario,
        CancellationToken cancellationToken = default)
    {
        var proveedor = await ProveedorAsync(id, cancellationToken);
        if (proveedor is null) return ResultadoOperacion.Falla("El proveedor no existe.");
        if (proveedor.activo == activo)
            return ResultadoOperacion.Ok($"\"{proveedor.nombre}\" ya estaba {(activo ? "activo" : "dado de baja")}.");

        await EnTransaccionAsync(async () =>
        {
            await _db.Proveedores.Where(p => p.id == id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.activo, activo)
                    .SetProperty(p => p.actualizado_en, DateTime.Now), cancellationToken);
            _db.RegistrarBitacora(idUsuario, activo ? "REACTIVAR" : "BAJA", "proveedores", id,
                activo ? $"Proveedor \"{proveedor.nombre}\" reactivado."
                       : $"Baja del proveedor \"{proveedor.nombre}\" (sus servicios dejan de mostrarse en el catálogo).");
            await _db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        return ResultadoOperacion.Ok(activo
            ? $"\"{proveedor.nombre}\" se reactivó."
            : $"\"{proveedor.nombre}\" se dio de baja. Sus servicios ya no aparecen en el sitio.");
    }

    // =============================================================================================
    // Servicios: consultas
    // =============================================================================================

    public async Task<ListaServiciosViewModel> ListaServiciosAsync(string? nombre, string? tipo, ulong? idProveedor, bool incluirInactivos,
        CancellationToken cancellationToken = default)
    {
        var q = _db.Servicios.AsNoTracking().AsQueryable();
        if (!incluirInactivos) q = q.Where(s => s.activo);
        if (tipo is not null) q = q.Where(s => s.tipo_servicio == tipo);
        if (idProveedor is not null) q = q.Where(s => s.id_proveedor == idProveedor.Value);
        if (nombre is not null) q = q.Where(s => s.nombre.Contains(nombre));
        var pagina = q.OrderBy(s => s.nombre).ThenBy(s => s.id).Take(MaximoResultados);

        var vm = new ListaServiciosViewModel
        {
            Resultados = await pagina.ToListAsync(cancellationToken),
            NombresProveedor = await _db.Proveedores.AsNoTracking().ToDictionaryAsync(p => p.id, p => p.nombre, cancellationToken)
        };
        if (vm.Resultados.Count == 0) return vm;

        // Detalle del subtipo (lo que distingue, p. ej., dos salidas del mismo vuelo).
        var ciudades = await _db.Ubicaciones.AsNoTracking()
            .ToDictionaryAsync(u => u.id, u => u.municipio_ciudad, cancellationToken);
        string Ciudad(ulong idUbicacion) => ciudades.GetValueOrDefault(idUbicacion, "?");

        foreach (var v in await (from x in _db.Vuelos.AsNoTracking() join s in pagina on x.id_servicio equals s.id select x).ToListAsync(cancellationToken))
            vm.Detalles[v.id_servicio] = $"{v.codigo_vuelo} · {Ciudad(v.id_origen)} → {Ciudad(v.id_destino)} · {v.fecha_salida:dd/MM/yyyy HH:mm}";
        foreach (var h in await (from x in _db.Hospedajes.AsNoTracking() join s in pagina on x.id_servicio equals s.id select x).ToListAsync(cancellationToken))
            vm.Detalles[h.id_servicio] = $"{h.tipo_habitacion} · {Ciudad(h.id_ubicacion)}";
        foreach (var v in await (from x in _db.Vehiculos.AsNoTracking() join s in pagina on x.id_servicio equals s.id select x).ToListAsync(cancellationToken))
            vm.Detalles[v.id_servicio] = $"{v.marca} {v.modelo} · {Ciudad(v.id_ubicacion_retiro)}";

        // Días futuros con cupo: sin ellos el servicio no aparece en las búsquedas del sitio.
        var hoy = Hoy();
        var dias = await (
                from d in _db.DisponibilidadServicios.AsNoTracking()
                join s in pagina on d.id_servicio equals s.id
                where d.fecha >= hoy && d.cantidad_disponible > 0
                group d by d.id_servicio into g
                select new { id = g.Key, dias = g.Count() })
            .ToListAsync(cancellationToken);
        foreach (var d in dias) vm.DiasDisponibles[d.id] = d.dias;

        return vm;
    }

    public Task<List<Servicios>> ServiciosDeProveedorAsync(ulong idProveedor, string tipoServicio, CancellationToken cancellationToken = default) =>
        _db.Servicios.AsNoTracking()
            .Where(s => s.id_proveedor == idProveedor && s.tipo_servicio == tipoServicio)
            .OrderBy(s => s.nombre).ThenBy(s => s.id)
            .Take(MaximoResultados)
            .ToListAsync(cancellationToken);

    public Task<Servicios?> ServicioAsync(ulong id, CancellationToken cancellationToken = default) =>
        _db.Servicios.AsNoTracking().FirstOrDefaultAsync(s => s.id == id, cancellationToken);

    public async Task<ServicioFormBase?> FormularioAsync(ulong id, CancellationToken cancellationToken = default)
    {
        var s = await ServicioAsync(id, cancellationToken);
        if (s is null) return null;

        ServicioFormBase? formulario;
        switch (s.tipo_servicio)
        {
            case "VUELO":
            {
                var v = await _db.Vuelos.AsNoTracking().FirstOrDefaultAsync(x => x.id_servicio == id, cancellationToken);
                formulario = new VueloFormViewModel
                {
                    id_origen = v?.id_origen,
                    id_destino = v?.id_destino,
                    codigo_vuelo = v?.codigo_vuelo ?? string.Empty,
                    fecha_salida = v?.fecha_salida,
                    fecha_llegada = v?.fecha_llegada,
                    clase = v?.clase ?? "TURISTA"
                };
                break;
            }
            case "HOSPEDAJE":
            {
                var h = await _db.Hospedajes.AsNoTracking().FirstOrDefaultAsync(x => x.id_servicio == id, cancellationToken);
                formulario = new HospedajeFormViewModel
                {
                    id_ubicacion = h?.id_ubicacion,
                    tipo_habitacion = h?.tipo_habitacion ?? string.Empty,
                    categoria = h?.categoria,
                    capacidad_huespedes = h?.capacidad_huespedes ?? 1,
                    hora_entrada = h?.hora_entrada,
                    hora_salida = h?.hora_salida,
                    imagen_actual = h?.imagen_ruta
                };
                break;
            }
            case "VEHICULO":
            {
                var v = await _db.Vehiculos.AsNoTracking().FirstOrDefaultAsync(x => x.id_servicio == id, cancellationToken);
                formulario = new VehiculoFormViewModel
                {
                    id_ubicacion_retiro = v?.id_ubicacion_retiro,
                    categoria = v?.categoria ?? string.Empty,
                    marca = v?.marca ?? string.Empty,
                    modelo = v?.modelo ?? string.Empty,
                    capacidad_pasajeros = v?.capacidad_pasajeros ?? 4,
                    imagen_actual = v?.imagen_ruta
                };
                break;
            }
            case "EVENTO":
                formulario = new EventoFormViewModel();
                break;
            default:
                return null;   // PAQUETE: tiene su propia pantalla
        }

        formulario.id = s.id;
        formulario.id_proveedor = s.id_proveedor;
        formulario.nombre = s.nombre;
        formulario.descripcion = s.descripcion;
        formulario.precio_base = s.precio_base;
        formulario.moneda = string.IsNullOrWhiteSpace(s.moneda) ? "MXN" : s.moneda;
        formulario.activo = s.activo;
        formulario.permite_cancelacion = s.permite_cancelacion;
        formulario.horas_reembolso_total = s.horas_reembolso_total;
        formulario.porcentaje_reembolso_tardio = s.porcentaje_reembolso_tardio;
        formulario.politica_cancelacion = s.politica_cancelacion;
        return formulario;
    }

    // =============================================================================================
    // Servicios: alta
    // =============================================================================================

    public async Task<ResultadoOperacion> CrearServicioAsync(ServicioFormBase datos, ulong idUsuario, CancellationToken cancellationToken = default)
    {
        var errores = new List<(string campo, string mensaje)>();
        await ValidarProveedorAsync(datos.id_proveedor, true, errores, cancellationToken);
        await ValidarSubtipoAsync(datos, errores, cancellationToken);

        List<DateTime> salidas = new();
        TimeSpan duracionVuelo = TimeSpan.Zero;
        (DateOnly desde, DateOnly hasta) rango = default;

        if (datos is VueloFormViewModel vuelo)
        {
            salidas = CalcularSalidas(vuelo, errores);
            if (vuelo.fecha_salida is DateTime salida && vuelo.fecha_llegada is DateTime llegada) duracionVuelo = llegada - salida;
        }
        else if (datos is ServicioConInventarioFormBase inventario)
        {
            rango = ValidarInventarioInicial(inventario, errores);
        }
        ValidarImagen(datos, errores);

        if (errores.Count > 0) return new ResultadoOperacion(false, "Revisa los datos marcados.", errores);

        PoliticaCancelacion.Normalizar(datos);
        string? rutaImagen = null;
        var idsCreados = new List<ulong>();

        try
        {
            rutaImagen = await GuardarImagenNuevaAsync(datos, cancellationToken);

            await EnTransaccionAsync(async () =>
            {
                idsCreados.Clear();
                var ahora = DateTime.Now;

                if (datos is VueloFormViewModel v)
                {
                    // Una salida = un servicio + su vuelo + la disponibilidad de ese día.
                    var servicios = salidas.Select(_ => NuevoServicio(datos, ahora)).ToList();
                    _db.Servicios.AddRange(servicios);
                    await _db.SaveChangesAsync(cancellationToken);

                    for (int i = 0; i < salidas.Count; i++)
                    {
                        var idServicio = servicios[i].id;
                        _db.Vuelos.Add(new Vuelos
                        {
                            id_servicio = idServicio,
                            id_origen = v.id_origen!.Value,
                            id_destino = v.id_destino!.Value,
                            codigo_vuelo = v.codigo_vuelo.Trim().ToUpperInvariant(),
                            fecha_salida = salidas[i],
                            fecha_llegada = salidas[i] + duracionVuelo,
                            clase = v.clase
                        });
                        _db.DisponibilidadServicios.Add(NuevoDia(idServicio, DateOnly.FromDateTime(salidas[i]), v.asientos,
                            v.precio_asiento ?? datos.precio_base, ahora));
                        idsCreados.Add(idServicio);
                    }

                    _db.RegistrarBitacora(idUsuario, "CREAR", "servicios", servicios[0].id,
                        $"Alta del vuelo {v.codigo_vuelo.ToUpperInvariant()}: {salidas.Count} salida(s) del {salidas[0]:dd/MM/yyyy} " +
                        $"al {salidas[^1]:dd/MM/yyyy}, {v.asientos} asientos cada una.");
                    await _db.SaveChangesAsync(cancellationToken);
                    return;
                }

                var servicio = NuevoServicio(datos, ahora);
                _db.Servicios.Add(servicio);
                await _db.SaveChangesAsync(cancellationToken);
                idsCreados.Add(servicio.id);

                switch (datos)
                {
                    case HospedajeFormViewModel h:
                        _db.Hospedajes.Add(new Hospedajes
                        {
                            id_servicio = servicio.id,
                            id_ubicacion = h.id_ubicacion!.Value,
                            tipo_habitacion = h.tipo_habitacion.Trim(),
                            categoria = Limpiar(h.categoria),
                            capacidad_huespedes = h.capacidad_huespedes,
                            hora_entrada = h.hora_entrada!.Value,
                            hora_salida = h.hora_salida!.Value,
                            imagen_ruta = rutaImagen
                        });
                        break;
                    case VehiculoFormViewModel ve:
                        _db.Vehiculos.Add(new Vehiculos
                        {
                            id_servicio = servicio.id,
                            id_ubicacion_retiro = ve.id_ubicacion_retiro!.Value,
                            categoria = ve.categoria.Trim(),
                            marca = ve.marca.Trim(),
                            modelo = ve.modelo.Trim(),
                            capacidad_pasajeros = ve.capacidad_pasajeros,
                            imagen_ruta = rutaImagen
                        });
                        break;
                }

                var inv = (ServicioConInventarioFormBase)datos;
                for (var dia = rango.desde; dia <= rango.hasta; dia = dia.AddDays(1))
                    _db.DisponibilidadServicios.Add(NuevoDia(servicio.id, dia, inv.inventario_cupo!.Value,
                        inv.inventario_precio ?? datos.precio_base, ahora));

                _db.RegistrarBitacora(idUsuario, "CREAR", "servicios", servicio.id,
                    $"Alta de {TiposGestor.Nombre(datos.Slug).ToLower()} \"{servicio.nombre}\" con cupo {inv.inventario_cupo} " +
                    $"del {rango.desde:dd/MM/yyyy} al {rango.hasta:dd/MM/yyyy}.");
                await _db.SaveChangesAsync(cancellationToken);
            }, cancellationToken);
        }
        catch (Exception ex) when (EsErrorDeBd(ex))
        {
            _imagenes.Eliminar(rutaImagen);   // no dejar archivos huérfanos
            _logger.LogError(ex, "No se pudo dar de alta el servicio \"{Nombre}\" ({Tipo}).", datos.nombre, datos.TipoServicio);
            return ResultadoOperacion.Falla("No se pudo guardar el servicio. Revisa los datos e intenta de nuevo.");
        }

        _logger.LogInformation("Usuario {IdUsuario}: alta de {Cantidad} servicio(s) {Tipo}.", idUsuario, idsCreados.Count, datos.TipoServicio);
        return ResultadoOperacion.Ok(idsCreados.Count > 1
            ? $"Se crearon {idsCreados.Count} salidas del vuelo \"{datos.nombre.Trim()}\". Ya aparecen en el catálogo."
            : $"Se dio de alta \"{datos.nombre.Trim()}\" con su disponibilidad. Ya aparece en el catálogo.");
    }

    // =============================================================================================
    // Servicios: actualizar, baja y reactivación
    // =============================================================================================

    public async Task<ResultadoOperacion> ActualizarServicioAsync(ulong id, ServicioFormBase datos, ulong idUsuario,
        CancellationToken cancellationToken = default)
    {
        var actual = await ServicioAsync(id, cancellationToken);
        if (actual is null) return ResultadoOperacion.Falla("El servicio ya no existe.");
        if (actual.tipo_servicio != datos.TipoServicio)
            return ResultadoOperacion.Falla("No se puede cambiar el tipo de un servicio existente.");

        var errores = new List<(string campo, string mensaje)>();
        // Un servicio viejo puede tener un proveedor dado de baja: solo se exige activo si se cambió.
        await ValidarProveedorAsync(datos.id_proveedor, datos.id_proveedor != actual.id_proveedor, errores, cancellationToken);
        await ValidarSubtipoAsync(datos, errores, cancellationToken);
        ValidarImagen(datos, errores);

        DateOnly? nuevaFechaVuelo = null;
        if (datos is VueloFormViewModel vuelo && vuelo.fecha_salida is DateTime salida)
        {
            var anterior = await _db.Vuelos.AsNoTracking().Where(v => v.id_servicio == id).Select(v => (DateTime?)v.fecha_salida)
                .FirstOrDefaultAsync(cancellationToken);
            var fechaNueva = DateOnly.FromDateTime(salida);
            if (anterior is DateTime fechaAnterior && DateOnly.FromDateTime(fechaAnterior) != fechaNueva)
            {
                var vendidos = await _db.DisponibilidadServicios.AsNoTracking()
                    .Where(d => d.id_servicio == id)
                    .SumAsync(d => d.cantidad_total - d.cantidad_disponible, cancellationToken);
                if (vendidos > 0)
                    errores.Add((nameof(vuelo.fecha_salida),
                        $"Esta salida ya tiene {vendidos} asiento(s) vendido(s): puedes cambiar la hora, pero no el día."));
                else
                    nuevaFechaVuelo = fechaNueva;
            }
        }

        if (errores.Count > 0) return new ResultadoOperacion(false, "Revisa los datos marcados.", errores);

        PoliticaCancelacion.Normalizar(datos);
        string? rutaNueva = null;
        string? rutaAnterior = null;

        try
        {
            rutaNueva = await GuardarImagenNuevaAsync(datos, cancellationToken);

            await EnTransaccionAsync(async () =>
            {
                rutaAnterior = null;
                var ahora = DateTime.Now;
                var idProveedor = datos.id_proveedor!.Value;
                var nombre = datos.nombre.Trim();
                var descripcion = Limpiar(datos.descripcion);

                await _db.Servicios.Where(s => s.id == id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.id_proveedor, idProveedor)
                        .SetProperty(x => x.nombre, nombre)
                        .SetProperty(x => x.descripcion, descripcion)
                        .SetProperty(x => x.precio_base, datos.precio_base)
                        .SetProperty(x => x.moneda, datos.moneda)
                        .SetProperty(x => x.activo, datos.activo)
                        .SetProperty(x => x.permite_cancelacion, datos.permite_cancelacion)
                        .SetProperty(x => x.horas_reembolso_total, datos.horas_reembolso_total)
                        .SetProperty(x => x.porcentaje_reembolso_tardio, datos.porcentaje_reembolso_tardio)
                        .SetProperty(x => x.politica_cancelacion, datos.politica_cancelacion)
                        .SetProperty(x => x.actualizado_en, ahora), cancellationToken);

                // Subtipo: si no existe (servicio creado antes sin su subtipo) se crea ahora.
                switch (datos)
                {
                    case VueloFormViewModel v:
                    {
                        var fila = await _db.Vuelos.FirstOrDefaultAsync(x => x.id_servicio == id, cancellationToken);
                        if (fila is null)
                        {
                            fila = new Vuelos { id_servicio = id };
                            _db.Vuelos.Add(fila);
                        }
                        fila.id_origen = v.id_origen!.Value;
                        fila.id_destino = v.id_destino!.Value;
                        fila.codigo_vuelo = v.codigo_vuelo.Trim().ToUpperInvariant();
                        fila.fecha_salida = v.fecha_salida!.Value;
                        fila.fecha_llegada = v.fecha_llegada!.Value;
                        fila.clase = v.clase;

                        if (nuevaFechaVuelo is DateOnly nuevaFecha)
                        {
                            await _db.DisponibilidadServicios.Where(d => d.id_servicio == id)
                                .ExecuteUpdateAsync(s => s
                                    .SetProperty(d => d.fecha, nuevaFecha)
                                    .SetProperty(d => d.actualizado_en, ahora), cancellationToken);
                        }
                        break;
                    }
                    case HospedajeFormViewModel h:
                    {
                        var fila = await _db.Hospedajes.FirstOrDefaultAsync(x => x.id_servicio == id, cancellationToken);
                        if (fila is null)
                        {
                            fila = new Hospedajes { id_servicio = id };
                            _db.Hospedajes.Add(fila);
                        }
                        fila.id_ubicacion = h.id_ubicacion!.Value;
                        fila.tipo_habitacion = h.tipo_habitacion.Trim();
                        fila.categoria = Limpiar(h.categoria);
                        fila.capacidad_huespedes = h.capacidad_huespedes;
                        fila.hora_entrada = h.hora_entrada!.Value;
                        fila.hora_salida = h.hora_salida!.Value;
                        if (rutaNueva is not null || h.quitar_imagen)
                        {
                            rutaAnterior = fila.imagen_ruta;
                            fila.imagen_ruta = rutaNueva;
                        }
                        break;
                    }
                    case VehiculoFormViewModel ve:
                    {
                        var fila = await _db.Vehiculos.FirstOrDefaultAsync(x => x.id_servicio == id, cancellationToken);
                        if (fila is null)
                        {
                            fila = new Vehiculos { id_servicio = id };
                            _db.Vehiculos.Add(fila);
                        }
                        fila.id_ubicacion_retiro = ve.id_ubicacion_retiro!.Value;
                        fila.categoria = ve.categoria.Trim();
                        fila.marca = ve.marca.Trim();
                        fila.modelo = ve.modelo.Trim();
                        fila.capacidad_pasajeros = ve.capacidad_pasajeros;
                        if (rutaNueva is not null || ve.quitar_imagen)
                        {
                            rutaAnterior = fila.imagen_ruta;
                            fila.imagen_ruta = rutaNueva;
                        }
                        break;
                    }
                }

                _db.RegistrarBitacora(idUsuario, "ACTUALIZAR", "servicios", id,
                    $"Datos de \"{datos.nombre.Trim()}\" actualizados{(datos.activo ? "" : " (queda inactivo)")}.");
                await _db.SaveChangesAsync(cancellationToken);
            }, cancellationToken);
        }
        catch (Exception ex) when (EsErrorDeBd(ex))
        {
            _imagenes.Eliminar(rutaNueva);
            _logger.LogError(ex, "No se pudo actualizar el servicio {IdServicio}.", id);
            return ResultadoOperacion.Falla("No se pudieron guardar los cambios. Intenta de nuevo.");
        }

        // La imagen anterior se borra solo cuando la BD ya apunta a la nueva.
        if (rutaAnterior is not null && rutaAnterior != rutaNueva) _imagenes.Eliminar(rutaAnterior);
        return ResultadoOperacion.Ok($"Se guardaron los cambios de \"{datos.nombre.Trim()}\". Las compras anteriores conservan sus condiciones.");
    }

    public async Task<ResultadoOperacion> CambiarEstadoServicioAsync(ulong id, bool activo, ulong idUsuario, CancellationToken cancellationToken = default)
    {
        var servicio = await ServicioAsync(id, cancellationToken);
        if (servicio is null) return ResultadoOperacion.Falla("El servicio no existe.");
        if (servicio.activo == activo)
            return ResultadoOperacion.Ok($"\"{servicio.nombre}\" ya estaba {(activo ? "activo" : "dado de baja")}.");

        if (activo)
        {
            var proveedorActivo = await _db.Proveedores.AsNoTracking().AnyAsync(p => p.id == servicio.id_proveedor && p.activo, cancellationToken);
            if (!proveedorActivo)
                return ResultadoOperacion.Falla($"El proveedor de \"{servicio.nombre}\" está dado de baja. Reactívalo primero.");
        }

        await EnTransaccionAsync(async () =>
        {
            await _db.Servicios.Where(s => s.id == id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.activo, activo)
                    .SetProperty(x => x.actualizado_en, DateTime.Now), cancellationToken);
            _db.RegistrarBitacora(idUsuario, activo ? "REACTIVAR" : "BAJA", "servicios", id,
                $"{(activo ? "Reactivación" : "Baja")} de \"{servicio.nombre}\".");
            await _db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        return ResultadoOperacion.Ok(activo
            ? $"\"{servicio.nombre}\" se reactivó."
            : $"\"{servicio.nombre}\" se dio de baja: ya no se puede reservar. Las reservaciones existentes no cambian.");
    }

    // =============================================================================================
    // Disponibilidad
    // =============================================================================================

    public async Task<DisponibilidadViewModel?> DisponibilidadAsync(ulong idServicio, DateOnly? desde, CancellationToken cancellationToken = default)
    {
        var servicio = await ServicioAsync(idServicio, cancellationToken);
        if (servicio is null) return null;

        var vm = new DisponibilidadViewModel
        {
            Servicio = servicio,
            NombreProveedor = await _db.Proveedores.AsNoTracking().Where(p => p.id == servicio.id_proveedor)
                .Select(p => p.nombre).FirstOrDefaultAsync(cancellationToken) ?? $"Proveedor #{servicio.id_proveedor}"
        };

        if (servicio.tipo_servicio == "VUELO")
        {
            var salida = await _db.Vuelos.AsNoTracking().Where(v => v.id_servicio == idServicio)
                .Select(v => (DateTime?)v.fecha_salida).FirstOrDefaultAsync(cancellationToken);
            if (salida is DateTime fecha) vm.FechaFija = DateOnly.FromDateTime(fecha);
        }

        vm.Desde = vm.FechaFija ?? desde ?? Hoy();
        vm.Hasta = vm.FechaFija ?? vm.Desde.AddDays(DisponibilidadViewModel.DiasPorPagina - 1);

        var inicio = vm.Desde;
        var fin = vm.Hasta;
        vm.Dias = await _db.DisponibilidadServicios.AsNoTracking()
            .Where(d => d.id_servicio == idServicio && d.fecha >= inicio && d.fecha <= fin)
            .OrderBy(d => d.fecha)
            .Select(d => new DiaDisponibilidad(d.fecha, d.cantidad_total, d.cantidad_disponible, d.precio_fecha))
            .ToListAsync(cancellationToken);

        var desdeFormulario = vm.FechaFija ?? (vm.Desde < Hoy() ? Hoy() : vm.Desde);
        vm.Formulario = new DisponibilidadFormViewModel
        {
            desde = desdeFormulario.ToDateTime(TimeOnly.MinValue),
            hasta = (vm.FechaFija ?? desdeFormulario.AddDays(29)).ToDateTime(TimeOnly.MinValue),
            cupo = vm.Dias.LastOrDefault()?.total
        };
        return vm;
    }

    public async Task<ResultadoOperacion> GuardarDisponibilidadAsync(ulong idServicio, DisponibilidadFormViewModel datos, ulong idUsuario,
        CancellationToken cancellationToken = default)
    {
        var servicio = await ServicioAsync(idServicio, cancellationToken);
        if (servicio is null) return ResultadoOperacion.Falla("El servicio no existe.");

        var errores = new List<(string campo, string mensaje)>();
        DateOnly desde, hasta;

        if (servicio.tipo_servicio == "VUELO")
        {
            // Un vuelo es una salida: su disponibilidad es solo el día de salida.
            var salida = await _db.Vuelos.AsNoTracking().Where(v => v.id_servicio == idServicio)
                .Select(v => (DateTime?)v.fecha_salida).FirstOrDefaultAsync(cancellationToken);
            if (salida is null) return ResultadoOperacion.Falla("Este vuelo no tiene sus datos de salida. Actualízalo primero.");
            desde = hasta = DateOnly.FromDateTime(salida.Value);
        }
        else
        {
            if (datos.desde is null)
            {
                errores.Add((nameof(datos.desde), "Indica la fecha inicial."));
                return new ResultadoOperacion(false, "Revisa los datos.", errores);
            }
            desde = DateOnly.FromDateTime(datos.desde.Value);
            hasta = datos.hasta is DateTime h ? DateOnly.FromDateTime(h) : desde;

            if (desde < Hoy()) errores.Add((nameof(datos.desde), "No se pueden cambiar fechas pasadas."));
            if (hasta < desde) errores.Add((nameof(datos.hasta), "Debe ser igual o posterior a la fecha inicial."));
            else if (hasta.DayNumber - desde.DayNumber + 1 > DisponibilidadViewModel.MaximoDiasPorCambio)
                errores.Add((nameof(datos.hasta), $"Máximo {DisponibilidadViewModel.MaximoDiasPorCambio} días por cambio."));
        }
        if (errores.Count > 0) return new ResultadoOperacion(false, "Revisa los datos.", errores);

        var cupo = datos.cupo!.Value;
        var precio = datos.precio;
        int nuevos = 0, actualizados = 0;

        try
        {
            await EnTransaccionAsync(async () =>
            {
                var ahora = DateTime.Now;
                var existentes = await _db.DisponibilidadServicios
                    .Where(d => d.id_servicio == idServicio && d.fecha >= desde && d.fecha <= hasta)
                    .Select(d => d.fecha)
                    .ToListAsync(cancellationToken);

                // Un solo UPDATE: SQL Server calcula el nuevo disponible con los valores ANTERIORES de la fila, y la
                // condición evita bajar el cupo por debajo de lo vendido aunque se venda algo en este mismo instante.
                actualizados = await _db.DisponibilidadServicios
                    .Where(d => d.id_servicio == idServicio && d.fecha >= desde && d.fecha <= hasta
                                && d.cantidad_total - d.cantidad_disponible <= cupo)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(d => d.cantidad_total, cupo)
                        .SetProperty(d => d.cantidad_disponible, d => cupo - (d.cantidad_total - d.cantidad_disponible))
                        .SetProperty(d => d.precio_fecha, d => precio ?? d.precio_fecha)
                        .SetProperty(d => d.actualizado_en, ahora), cancellationToken);

                if (actualizados != existentes.Count)
                {
                    var conflictos = await _db.DisponibilidadServicios.AsNoTracking()
                        .Where(d => d.id_servicio == idServicio && d.fecha >= desde && d.fecha <= hasta
                                    && d.cantidad_total - d.cantidad_disponible > cupo)
                        .OrderBy(d => d.fecha)
                        .Select(d => new { d.fecha, vendidos = d.cantidad_total - d.cantidad_disponible })
                        .Take(5)
                        .ToListAsync(cancellationToken);
                    throw new CupoMenorQueVendidoException(string.Join(", ",
                        conflictos.Select(c => $"{c.fecha:dd/MM} ({c.vendidos} vendido(s))")));
                }

                var yaHay = existentes.ToHashSet();
                nuevos = 0;
                for (var dia = desde; dia <= hasta; dia = dia.AddDays(1))
                {
                    if (yaHay.Contains(dia)) continue;
                    _db.DisponibilidadServicios.Add(NuevoDia(idServicio, dia, cupo, precio ?? servicio.precio_base, ahora));
                    nuevos++;
                }

                _db.RegistrarBitacora(idUsuario, "DISPONIBILIDAD", "servicios", idServicio,
                    $"\"{servicio.nombre}\": cupo {cupo} del {desde:dd/MM/yyyy} al {hasta:dd/MM/yyyy}" +
                    $"{(precio is decimal p ? $", precio {p:N2}" : string.Empty)} ({nuevos} día(s) nuevos, {actualizados} ajustados).");
                await _db.SaveChangesAsync(cancellationToken);
            }, cancellationToken);
        }
        catch (CupoMenorQueVendidoException ex)
        {
            return new ResultadoOperacion(false, "No se guardó nada.",
                new() { (nameof(datos.cupo), $"El cupo no puede ser menor a lo ya vendido en: {ex.Message}.") });
        }
        catch (Exception ex) when (EsErrorDeBd(ex))
        {
            _logger.LogError(ex, "No se pudo guardar la disponibilidad del servicio {IdServicio}.", idServicio);
            return ResultadoOperacion.Falla("No se pudo guardar la disponibilidad. Intenta de nuevo.");
        }

        return ResultadoOperacion.Ok(cupo == 0
            ? $"Se cerraron las fechas del {desde:dd/MM/yyyy} al {hasta:dd/MM/yyyy} (cupo 0)."
            : $"Disponibilidad guardada: {nuevos} día(s) nuevos y {actualizados} ajustados.");
    }

    // =============================================================================================
    // Validaciones
    // =============================================================================================

    private async Task ValidarProveedorAsync(ulong? idProveedor, bool exigirActivo, List<(string, string)> errores,
        CancellationToken cancellationToken)
    {
        if (idProveedor is null or 0) return;   // ya lo marca [Required]
        var proveedor = await ProveedorAsync(idProveedor.Value, cancellationToken);
        if (proveedor is null)
            errores.Add((nameof(ServicioFormBase.id_proveedor), "El proveedor seleccionado no existe."));
        else if (exigirActivo && !proveedor.activo)
            errores.Add((nameof(ServicioFormBase.id_proveedor), "El proveedor está dado de baja. Reactívalo antes de asociarle servicios."));
    }

    /// <summary>Validaciones del subtipo que no caben en DataAnnotations (ubicaciones, fechas y horarios).</summary>
    private async Task ValidarSubtipoAsync(ServicioFormBase datos, List<(string, string)> errores, CancellationToken cancellationToken)
    {
        switch (datos)
        {
            case VueloFormViewModel v:
                if (v.id_origen is ulong origen && !await UbicacionActivaAsync(origen, cancellationToken))
                    errores.Add((nameof(v.id_origen), "Elige un origen de la lista."));
                if (v.id_destino is ulong destino && !await UbicacionActivaAsync(destino, cancellationToken))
                    errores.Add((nameof(v.id_destino), "Elige un destino de la lista."));
                if (v.id_origen is not null && v.id_origen == v.id_destino)
                    errores.Add((nameof(v.id_destino), "El destino debe ser distinto del origen."));
                if (v.fecha_salida is DateTime salida && v.fecha_llegada is DateTime llegada)
                {
                    if (llegada <= salida)
                        errores.Add((nameof(v.fecha_llegada), "La llegada debe ser posterior a la salida."));
                    else if (llegada - salida > TimeSpan.FromHours(48))
                        errores.Add((nameof(v.fecha_llegada), "Un vuelo no puede durar más de 48 horas."));
                    if (v.EsAlta && salida <= DateTime.Now)
                        errores.Add((nameof(v.fecha_salida), "La primera salida debe ser en el futuro."));
                }
                break;

            case HospedajeFormViewModel h:
                if (h.id_ubicacion is ulong ubicacion && !await UbicacionActivaAsync(ubicacion, cancellationToken))
                    errores.Add((nameof(h.id_ubicacion), "Elige una ubicación de la lista."));
                break;

            case VehiculoFormViewModel ve:
                if (ve.id_ubicacion_retiro is ulong retiro && !await UbicacionActivaAsync(retiro, cancellationToken))
                    errores.Add((nameof(ve.id_ubicacion_retiro), "Elige una ubicación de la lista."));
                break;

            case EventoFormViewModel e when e.id_proveedor is ulong idProveedor:
                // El tour se muestra en la ciudad de su proveedor: sin ubicación no se podría buscar por destino.
                var tieneUbicacion = await _db.Proveedores.AsNoTracking()
                    .AnyAsync(p => p.id == idProveedor && p.id_ubicacion != null, cancellationToken);
                if (!tieneUbicacion)
                    errores.Add((nameof(e.id_proveedor), "Este proveedor no tiene ubicación y el tour se muestra en su ciudad. Asígnale una ubicación primero."));
                break;
        }
    }

    private static (DateOnly desde, DateOnly hasta) ValidarInventarioInicial(ServicioConInventarioFormBase f, List<(string, string)> errores)
    {
        if (f.inventario_cupo is null)
            errores.Add((nameof(f.inventario_cupo), "Indica el cupo por día."));
        if (f.inventario_desde is null)
        {
            errores.Add((nameof(f.inventario_desde), "Indica desde qué fecha está disponible."));
            return default;
        }

        var desde = DateOnly.FromDateTime(f.inventario_desde.Value);
        var hasta = f.inventario_hasta is DateTime h ? DateOnly.FromDateTime(h) : desde;
        if (desde < Hoy())
            errores.Add((nameof(f.inventario_desde), "No puede ser una fecha pasada."));
        if (hasta < desde)
            errores.Add((nameof(f.inventario_hasta), "Debe ser igual o posterior a la fecha inicial."));
        else if (hasta.DayNumber - desde.DayNumber + 1 > DisponibilidadViewModel.MaximoDiasPorCambio)
            errores.Add((nameof(f.inventario_hasta), $"Máximo {DisponibilidadViewModel.MaximoDiasPorCambio} días; agrega más después en Disponibilidad."));
        return (desde, hasta);
    }

    /// <summary>Fechas y horas de cada salida según "repetir" (NO, DIARIO o SEMANAL).</summary>
    private static List<DateTime> CalcularSalidas(VueloFormViewModel v, List<(string, string)> errores)
    {
        var salidas = new List<DateTime>();
        if (v.fecha_salida is not DateTime primera) return salidas;
        if (v.repetir == "NO")
        {
            salidas.Add(primera);
            return salidas;
        }

        if (v.repetir_hasta is not DateTime hastaFecha)
        {
            errores.Add((nameof(v.repetir_hasta), "Indica hasta qué fecha se repite."));
            return salidas;
        }
        var hasta = hastaFecha.Date;
        if (hasta < primera.Date)
        {
            errores.Add((nameof(v.repetir_hasta), "Debe ser igual o posterior a la primera salida."));
            return salidas;
        }
        if ((hasta - primera.Date).TotalDays > 366)
        {
            errores.Add((nameof(v.repetir_hasta), "Máximo un año a partir de la primera salida."));
            return salidas;
        }

        var dias = v.dias_semana.Where(d => d is >= 0 and <= 6).ToHashSet();
        if (v.repetir == "SEMANAL" && dias.Count == 0)
        {
            errores.Add((nameof(v.dias_semana), "Marca al menos un día de la semana."));
            return salidas;
        }

        for (var dia = primera.Date; dia <= hasta; dia = dia.AddDays(1))
        {
            if (v.repetir == "SEMANAL" && !dias.Contains((int)dia.DayOfWeek)) continue;
            salidas.Add(dia + primera.TimeOfDay);
        }

        if (salidas.Count == 0)
            errores.Add((nameof(v.dias_semana), "Con esos días no hay ninguna salida en el periodo."));
        else if (salidas.Count > VueloFormViewModel.MaximoSalidas)
            errores.Add((nameof(v.repetir_hasta),
                $"Serían {salidas.Count} salidas; el máximo es {VueloFormViewModel.MaximoSalidas} por alta. Acorta el periodo."));
        return salidas;
    }

    private void ValidarImagen(ServicioFormBase datos, List<(string, string)> errores)
    {
        var imagen = datos switch
        {
            HospedajeFormViewModel h => h.imagen,
            VehiculoFormViewModel v => v.imagen,
            _ => null
        };
        var error = _imagenes.Validar(imagen);
        if (error is not null) errores.Add(("imagen", error));
    }

    // =============================================================================================
    // Ayudantes
    // =============================================================================================

    private Task<bool> UbicacionActivaAsync(ulong id, CancellationToken cancellationToken) =>
        _db.Ubicaciones.AsNoTracking().AnyAsync(u => u.id == id && u.activo, cancellationToken);

    private async Task<string?> GuardarImagenNuevaAsync(ServicioFormBase datos, CancellationToken cancellationToken) => datos switch
    {
        HospedajeFormViewModel { imagen.Length: > 0 } h => await _imagenes.GuardarAsync(h.imagen!, CarpetaImagen.Hospedajes, cancellationToken),
        VehiculoFormViewModel { imagen.Length: > 0 } v => await _imagenes.GuardarAsync(v.imagen!, CarpetaImagen.Vehiculos, cancellationToken),
        _ => null
    };

    private static Servicios NuevoServicio(ServicioFormBase datos, DateTime ahora) => new()
    {
        id_proveedor = datos.id_proveedor!.Value,
        tipo_servicio = datos.TipoServicio,
        nombre = datos.nombre.Trim(),
        descripcion = Limpiar(datos.descripcion),
        precio_base = datos.precio_base,
        moneda = datos.moneda,
        permite_cancelacion = datos.permite_cancelacion,
        horas_reembolso_total = datos.horas_reembolso_total,
        porcentaje_reembolso_tardio = datos.porcentaje_reembolso_tardio,
        politica_cancelacion = datos.politica_cancelacion,
        activo = true,
        creado_en = ahora,
        actualizado_en = ahora
    };

    private static DisponibilidadServicio NuevoDia(ulong idServicio, DateOnly fecha, int cupo, decimal precio, DateTime ahora) => new()
    {
        id_servicio = idServicio,
        fecha = fecha,
        cantidad_total = cupo,
        cantidad_disponible = cupo,
        precio_fecha = precio,
        actualizado_en = ahora
    };

    private static string? Limpiar(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    /// <summary>Ejecuta <paramref name="trabajo"/> en una transacción dentro de la execution strategy (reintentos).</summary>
    private async Task EnTransaccionAsync(Func<Task> trabajo, CancellationToken cancellationToken)
    {
        var estrategia = _db.Database.CreateExecutionStrategy();
        await estrategia.ExecuteAsync(async () =>
        {
            _db.ChangeTracker.Clear();
            await using var transaccion = await _db.Database.BeginTransactionAsync(cancellationToken);
            await trabajo();
            await transaccion.CommitAsync(cancellationToken);
        });
    }

    private static bool EsErrorDeBd(Exception ex) =>
        ex is DbUpdateException or SqlException or RetryLimitExceededException or InvalidOperationException;

    /// <summary>El nuevo cupo es menor a lo que ya se vendió en alguna fecha: se deshace todo el cambio.</summary>
    private sealed class CupoMenorQueVendidoException : Exception
    {
        public CupoMenorQueVendidoException(string fechas) : base(fechas) { }
    }
}
