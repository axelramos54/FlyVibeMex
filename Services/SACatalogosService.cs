using FlyVibeMex.Data;
using FlyVibeMex.Models;
using Microsoft.EntityFrameworkCore;

namespace FlyVibeMex.Services;

/// <summary>
/// Catálogos base del panel SA: ubicaciones, métodos de pago y bancos (instituciones financieras). Nunca se borran:
/// se activan / desactivan (otras tablas los referencian).
/// </summary>
public interface ISACatalogosService
{
    // ---- Ubicaciones ----
    Task<ListaUbicacionesViewModel> UbicacionesAsync(string? texto, bool incluirInactivas, CancellationToken cancellationToken = default);
    Task<UbicacionFormViewModel?> UbicacionAsync(ulong id, CancellationToken cancellationToken = default);
    Task<ResultadoOperacion> GuardarUbicacionAsync(ulong? id, UbicacionFormViewModel datos, ulong idSA, CancellationToken cancellationToken = default);
    /// <summary>Qué dejará de aparecer en el catálogo si se desactiva (pantalla de confirmación).</summary>
    Task<BajaUbicacionViewModel?> ImpactoBajaUbicacionAsync(ulong id, CancellationToken cancellationToken = default);
    Task<ResultadoOperacion> CambiarEstadoUbicacionAsync(ulong id, bool activo, ulong idSA, CancellationToken cancellationToken = default);

    // ---- Métodos de pago ----
    Task<List<MetodoPagoListadoItem>> MetodosPagoAsync(CancellationToken cancellationToken = default);
    Task<MetodoPagoFormViewModel?> MetodoPagoAsync(ushort id, CancellationToken cancellationToken = default);
    Task<ResultadoOperacion> GuardarMetodoPagoAsync(ushort id, MetodoPagoFormViewModel datos, ulong idSA, CancellationToken cancellationToken = default);
    Task<ResultadoOperacion> CambiarEstadoMetodoPagoAsync(ushort id, bool activo, ulong idSA, CancellationToken cancellationToken = default);

    // ---- Bancos ----
    Task<List<InstitucionListadoItem>> BancosAsync(CancellationToken cancellationToken = default);
    Task<InstitucionFormViewModel?> BancoAsync(ulong id, CancellationToken cancellationToken = default);
    Task<ResultadoOperacion> GuardarBancoAsync(ulong? id, InstitucionFormViewModel datos, ulong idSA, CancellationToken cancellationToken = default);
    Task<ResultadoOperacion> CambiarEstadoBancoAsync(ulong id, bool activo, ulong idSA, CancellationToken cancellationToken = default);
}

/// <summary>Implementación de <see cref="ISACatalogosService"/> con EF Core (consultas LINQ) y bitácora.</summary>
public sealed class SACatalogosService : ISACatalogosService
{
    private readonly FlyVibeMexDbContext _db;
    private readonly ILogger<SACatalogosService> _logger;

    public SACatalogosService(FlyVibeMexDbContext db, ILogger<SACatalogosService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // =============================================================================================
    // Ubicaciones
    // =============================================================================================

    public async Task<ListaUbicacionesViewModel> UbicacionesAsync(string? texto, bool incluirInactivas, CancellationToken cancellationToken = default)
    {
        texto = GestorBd.Limpiar(texto);
        if (texto is { Length: > 100 }) texto = texto[..100];

        var q = _db.Ubicaciones.AsNoTracking().AsQueryable();
        if (!incluirInactivas) q = q.Where(u => u.activo);
        if (texto is not null)
            q = q.Where(u => u.municipio_ciudad.Contains(texto) || u.estado.Contains(texto) || u.codigo_postal.Contains(texto));

        var ubicaciones = await q.OrderBy(u => u.estado).ThenBy(u => u.municipio_ciudad).ToListAsync(cancellationToken);
        var usos = await UsosAsync(cancellationToken);

        return new ListaUbicacionesViewModel
        {
            Texto = texto,
            IncluirInactivas = incluirInactivas,
            Resultados = ubicaciones.Select(u => Item(u, usos)).ToList()
        };
    }

    public async Task<UbicacionFormViewModel?> UbicacionAsync(ulong id, CancellationToken cancellationToken = default)
    {
        var u = await _db.Ubicaciones.AsNoTracking().FirstOrDefaultAsync(x => x.id == id, cancellationToken);
        return u is null ? null : new UbicacionFormViewModel
        {
            id = u.id,
            pais = u.pais,
            estado = u.estado,
            municipio_ciudad = u.municipio_ciudad,
            codigo_postal = u.codigo_postal
        };
    }

    public async Task<ResultadoOperacion> GuardarUbicacionAsync(ulong? id, UbicacionFormViewModel datos, ulong idSA, CancellationToken cancellationToken = default)
    {
        var pais = datos.pais.Trim();
        var estado = datos.estado.Trim();
        var ciudad = datos.municipio_ciudad.Trim();
        var cp = datos.codigo_postal.Trim();

        if (id is ulong idActual && !await _db.Ubicaciones.AsNoTracking().AnyAsync(u => u.id == idActual, cancellationToken))
            return ResultadoOperacion.Falla("La ubicación no existe.");
        // La intercalación de SQL Server no distingue mayúsculas: "cancún" = "Cancún".
        if (await _db.Ubicaciones.AsNoTracking().AnyAsync(u => u.pais == pais && u.estado == estado && u.municipio_ciudad == ciudad
                                                               && (id == null || u.id != id), cancellationToken))
            return new ResultadoOperacion(false, "Revisa los datos marcados.",
                new() { (nameof(datos.municipio_ciudad), "Ya existe esa ciudad en ese estado.") });

        try
        {
            await GestorBd.EnTransaccionAsync(_db, async () =>
            {
                var ahora = DateTime.Now;
                ulong idGuardado;
                if (id is ulong idEditar)
                {
                    idGuardado = idEditar;
                    await _db.Ubicaciones.Where(u => u.id == idEditar)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(u => u.pais, pais)
                            .SetProperty(u => u.estado, estado)
                            .SetProperty(u => u.municipio_ciudad, ciudad)
                            .SetProperty(u => u.codigo_postal, cp)
                            .SetProperty(u => u.actualizado_en, ahora), cancellationToken);
                }
                else
                {
                    var nueva = new Ubicaciones
                    {
                        pais = pais,
                        estado = estado,
                        municipio_ciudad = ciudad,
                        codigo_postal = cp,
                        activo = true,
                        creado_en = ahora,
                        actualizado_en = ahora
                    };
                    _db.Ubicaciones.Add(nueva);
                    await _db.SaveChangesAsync(cancellationToken);
                    idGuardado = nueva.id;
                }
                _db.RegistrarBitacora(idSA, id is null ? "CREAR" : "ACTUALIZAR", "ubicaciones", idGuardado,
                    $"{ciudad}, {estado}, {pais} (C.P. {cp}).");
                await _db.SaveChangesAsync(cancellationToken);
            }, cancellationToken);
        }
        catch (Exception ex) when (GestorBd.EsErrorDeBd(ex))
        {
            _logger.LogError(ex, "No se pudo guardar la ubicación {Ciudad}.", ciudad);
            return ResultadoOperacion.Falla("No se pudo guardar la ubicación. Intenta de nuevo.");
        }
        return ResultadoOperacion.Ok(id is null ? $"Se agregó {ciudad}, {estado}." : $"Se guardaron los cambios de {ciudad}, {estado}.");
    }

    public async Task<BajaUbicacionViewModel?> ImpactoBajaUbicacionAsync(ulong id, CancellationToken cancellationToken = default)
    {
        var u = await _db.Ubicaciones.AsNoTracking().FirstOrDefaultAsync(x => x.id == id, cancellationToken);
        if (u is null) return null;
        var usos = await UsosAsync(cancellationToken);
        return new BajaUbicacionViewModel
        {
            Ubicacion = Item(u, usos),
            Ejemplos = usos.TryGetValue(id, out var uso) ? uso.ejemplos.Take(20).ToList() : new List<string>()
        };
    }

    public async Task<ResultadoOperacion> CambiarEstadoUbicacionAsync(ulong id, bool activo, ulong idSA, CancellationToken cancellationToken = default)
    {
        var u = await _db.Ubicaciones.AsNoTracking().Where(x => x.id == id)
            .Select(x => new { x.activo, x.municipio_ciudad, x.estado }).FirstOrDefaultAsync(cancellationToken);
        if (u is null) return ResultadoOperacion.Falla("La ubicación no existe.");
        if (u.activo == activo) return ResultadoOperacion.Falla(activo ? "Ya está activa." : "Ya está inactiva.");

        var usos = await UsosAsync(cancellationToken);
        var afectados = usos.TryGetValue(id, out var uso) ? uso.servicios + uso.paquetes : 0;

        await GestorBd.EnTransaccionAsync(_db, async () =>
        {
            var ahora = DateTime.Now;
            await _db.Ubicaciones.Where(x => x.id == id && x.activo != activo)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.activo, activo).SetProperty(x => x.actualizado_en, ahora), cancellationToken);
            _db.RegistrarBitacora(idSA, activo ? "REACTIVAR" : "BAJA", "ubicaciones", id,
                $"{u.municipio_ciudad}, {u.estado} {(activo ? "activada" : "desactivada")}" +
                (activo ? "." : $": {afectados} servicio(s) o paquete(s) dejan de aparecer en el catálogo."));
            await _db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        return ResultadoOperacion.Ok(activo
            ? $"{u.municipio_ciudad} activada: sus servicios vuelven a aparecer en el catálogo."
            : $"{u.municipio_ciudad} desactivada. {afectados} servicio(s) o paquete(s) dejaron de aparecer en el catálogo.");
    }

    private static UbicacionListadoItem Item(Ubicaciones u, Dictionary<ulong, (int proveedores, int servicios, int paquetes, List<string> ejemplos)> usos)
    {
        var uso = usos.GetValueOrDefault(u.id);
        return new UbicacionListadoItem
        {
            id = u.id,
            pais = u.pais,
            estado = u.estado,
            municipio_ciudad = u.municipio_ciudad,
            codigo_postal = u.codigo_postal,
            activo = u.activo,
            proveedores = uso.proveedores,
            servicios = uso.servicios,
            paquetes = uso.paquetes
        };
    }

    /// <summary>
    /// Por ubicación: proveedores activos con esa dirección, servicios ACTIVOS que se usan ahí (hospedaje, vehículo,
    /// vuelo como origen o destino, tour por la ubicación de su proveedor) y paquetes activos con ese destino.
    /// Se calcula en memoria (tablas pequeñas) con los mismos criterios que el catálogo.
    /// </summary>
    private async Task<Dictionary<ulong, (int proveedores, int servicios, int paquetes, List<string> ejemplos)>> UsosAsync(CancellationToken cancellationToken)
    {
        var servicios = await _db.Servicios.AsNoTracking().Where(s => s.activo)
            .Select(s => new { s.id, s.nombre, s.tipo_servicio, s.id_proveedor }).ToListAsync(cancellationToken);
        var proveedores = await _db.Proveedores.AsNoTracking()
            .Select(p => new { p.id, p.id_ubicacion, p.activo }).ToListAsync(cancellationToken);
        var hospedajes = await _db.Hospedajes.AsNoTracking().Select(h => new { h.id_servicio, h.id_ubicacion }).ToListAsync(cancellationToken);
        var vehiculos = await _db.Vehiculos.AsNoTracking().Select(v => new { v.id_servicio, v.id_ubicacion_retiro }).ToListAsync(cancellationToken);
        var vuelos = await _db.Vuelos.AsNoTracking().Select(v => new { v.id_servicio, v.id_origen, v.id_destino }).ToListAsync(cancellationToken);
        var paquetes = await _db.Paquetes.AsNoTracking().Where(p => p.id_ubicacion_destino != null)
            .Select(p => new { p.id_servicio, p.id_ubicacion_destino }).ToListAsync(cancellationToken);

        var provUbicacion = proveedores.ToDictionary(p => p.id, p => p.id_ubicacion);
        var activos = servicios.ToDictionary(s => s.id);
        var resultado = new Dictionary<ulong, (int proveedores, int servicios, int paquetes, List<string> ejemplos)>();

        (int proveedores, int servicios, int paquetes, List<string> ejemplos) Uso(ulong idUbicacion) =>
            resultado.TryGetValue(idUbicacion, out var u) ? u : (0, 0, 0, new List<string>());

        void SumarServicio(ulong idUbicacion, ulong idServicio, bool esPaquete)
        {
            if (!activos.TryGetValue(idServicio, out var s)) return;   // solo servicios activos
            var u = Uso(idUbicacion);
            if (esPaquete) u.paquetes++; else u.servicios++;
            u.ejemplos.Add($"{s.nombre} ({ServicioFormViewModel.NombreTipo(s.tipo_servicio)})");
            resultado[idUbicacion] = u;
        }

        foreach (var p in proveedores.Where(x => x.activo && x.id_ubicacion is not null))
        {
            var u = Uso(p.id_ubicacion!.Value);
            u.proveedores++;
            resultado[p.id_ubicacion!.Value] = u;
        }
        foreach (var h in hospedajes) SumarServicio(h.id_ubicacion, h.id_servicio, false);
        foreach (var v in vehiculos) SumarServicio(v.id_ubicacion_retiro, v.id_servicio, false);
        foreach (var v in vuelos)
        {
            SumarServicio(v.id_origen, v.id_servicio, false);
            if (v.id_destino != v.id_origen) SumarServicio(v.id_destino, v.id_servicio, false);
        }
        foreach (var s in servicios.Where(x => x.tipo_servicio == "EVENTO"))
            if (provUbicacion.GetValueOrDefault(s.id_proveedor) is ulong idUbicacion)
                SumarServicio(idUbicacion, s.id, false);
        foreach (var p in paquetes) SumarServicio(p.id_ubicacion_destino!.Value, p.id_servicio, true);

        return resultado;
    }

    // =============================================================================================
    // Métodos de pago
    // =============================================================================================

    public async Task<List<MetodoPagoListadoItem>> MetodosPagoAsync(CancellationToken cancellationToken = default)
    {
        var metodos = await _db.MetodosPago.AsNoTracking().OrderBy(m => m.id).ToListAsync(cancellationToken);
        var pagos = await _db.Pagos.AsNoTracking().GroupBy(p => p.id_metodo_pago)
            .Select(g => new { id = g.Key, n = g.Count() }).ToDictionaryAsync(x => x.id, x => x.n, cancellationToken);
        return metodos.Select(m => new MetodoPagoListadoItem
        {
            id = m.id,
            nombre = m.nombre,
            descripcion = string.IsNullOrWhiteSpace(m.descripcion) ? null : m.descripcion,
            activo = m.activo,
            pagos = pagos.GetValueOrDefault(m.id)
        }).ToList();
    }

    public async Task<MetodoPagoFormViewModel?> MetodoPagoAsync(ushort id, CancellationToken cancellationToken = default)
    {
        var m = await _db.MetodosPago.AsNoTracking().FirstOrDefaultAsync(x => x.id == id, cancellationToken);
        return m is null ? null : new MetodoPagoFormViewModel { id = m.id, nombre = m.nombre, descripcion = m.descripcion };
    }

    public async Task<ResultadoOperacion> GuardarMetodoPagoAsync(ushort id, MetodoPagoFormViewModel datos, ulong idSA, CancellationToken cancellationToken = default)
    {
        var nombre = datos.nombre.Trim();
        var descripcion = GestorBd.Limpiar(datos.descripcion) ?? string.Empty;
        if (!await _db.MetodosPago.AsNoTracking().AnyAsync(m => m.id == id, cancellationToken))
            return ResultadoOperacion.Falla("El método de pago no existe.");
        if (await _db.MetodosPago.AsNoTracking().AnyAsync(m => m.nombre == nombre && m.id != id, cancellationToken))
            return new ResultadoOperacion(false, "Revisa los datos marcados.", new() { (nameof(datos.nombre), "Ya existe un método con ese nombre.") });

        await GestorBd.EnTransaccionAsync(_db, async () =>
        {
            await _db.MetodosPago.Where(m => m.id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.nombre, nombre).SetProperty(m => m.descripcion, descripcion), cancellationToken);
            _db.RegistrarBitacora(idSA, "ACTUALIZAR", "metodos_pago", id, $"Método de pago \"{nombre}\": {descripcion}");
            await _db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
        return ResultadoOperacion.Ok($"Se guardó \"{nombre}\".");
    }

    public async Task<ResultadoOperacion> CambiarEstadoMetodoPagoAsync(ushort id, bool activo, ulong idSA, CancellationToken cancellationToken = default)
    {
        var m = await _db.MetodosPago.AsNoTracking().Where(x => x.id == id).Select(x => new { x.nombre, x.activo }).FirstOrDefaultAsync(cancellationToken);
        if (m is null) return ResultadoOperacion.Falla("El método de pago no existe.");
        if (m.activo == activo) return ResultadoOperacion.Falla(activo ? "Ya está activo." : "Ya está inactivo.");

        try
        {
            await GestorBd.EnTransaccionAsync(_db, async () =>
            {
                await _db.MetodosPago.Where(x => x.id == id && x.activo != activo)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.activo, activo), cancellationToken);
                // Siempre debe quedar al menos un método activo (si no, nadie puede pagar).
                if (!activo && !await _db.MetodosPago.AnyAsync(x => x.activo, cancellationToken))
                    throw new InvalidOperationException("SIN_METODOS");
                _db.RegistrarBitacora(idSA, activo ? "REACTIVAR" : "BAJA", "metodos_pago", id,
                    $"Método de pago \"{m.nombre}\" {(activo ? "activado" : "desactivado")}.");
                await _db.SaveChangesAsync(cancellationToken);
            }, cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message == "SIN_METODOS")
        {
            return ResultadoOperacion.Falla("Debe quedar al menos un método de pago activo; si no, nadie podría pagar.");
        }

        return ResultadoOperacion.Ok(activo
            ? $"\"{m.nombre}\" activado: ya aparece al pagar (el cobro se simula igual que con tarjeta)."
            : $"\"{m.nombre}\" desactivado: ya no aparece al pagar. Los pagos anteriores no cambian.");
    }

    // =============================================================================================
    // Bancos (instituciones financieras)
    // =============================================================================================

    public async Task<List<InstitucionListadoItem>> BancosAsync(CancellationToken cancellationToken = default)
    {
        var bancos = await _db.InstitucionesFinancieras.AsNoTracking().OrderBy(b => b.nombre).ToListAsync(cancellationToken);
        var tarjetas = await _db.TarjetasUsuarios.AsNoTracking().Where(t => t.id_institucion != null && t.activo)
            .GroupBy(t => t.id_institucion).Select(g => new { id = g.Key, n = g.Count() }).ToListAsync(cancellationToken);
        return bancos.Select(b => new InstitucionListadoItem
        {
            id = b.id,
            nombre = b.nombre,
            clave_institucion = b.clave_institucion,
            codigo_internacional = b.codigo_internacional,
            telefono = b.telefono,
            activo = b.activo,
            tarjetas = tarjetas.Where(t => t.id == b.id).Sum(t => t.n)
        }).ToList();
    }

    public async Task<InstitucionFormViewModel?> BancoAsync(ulong id, CancellationToken cancellationToken = default)
    {
        var b = await _db.InstitucionesFinancieras.AsNoTracking().FirstOrDefaultAsync(x => x.id == id, cancellationToken);
        return b is null ? null : new InstitucionFormViewModel
        {
            id = b.id,
            nombre = b.nombre,
            clave_institucion = b.clave_institucion,
            codigo_internacional = b.codigo_internacional,
            telefono = b.telefono
        };
    }

    public async Task<ResultadoOperacion> GuardarBancoAsync(ulong? id, InstitucionFormViewModel datos, ulong idSA, CancellationToken cancellationToken = default)
    {
        var nombre = datos.nombre.Trim();
        var clave = GestorBd.Limpiar(datos.clave_institucion)?.ToUpperInvariant();
        var codigo = GestorBd.Limpiar(datos.codigo_internacional)?.ToUpperInvariant();
        var telefono = GestorBd.Limpiar(datos.telefono);

        if (id is ulong idActual && !await _db.InstitucionesFinancieras.AsNoTracking().AnyAsync(b => b.id == idActual, cancellationToken))
            return ResultadoOperacion.Falla("El banco no existe.");
        var errores = new List<(string campo, string mensaje)>();
        if (await _db.InstitucionesFinancieras.AsNoTracking().AnyAsync(b => b.nombre == nombre && (id == null || b.id != id), cancellationToken))
            errores.Add((nameof(datos.nombre), "Ya existe un banco con ese nombre."));
        if (clave is not null && await _db.InstitucionesFinancieras.AsNoTracking().AnyAsync(b => b.clave_institucion == clave && (id == null || b.id != id), cancellationToken))
            errores.Add((nameof(datos.clave_institucion), "Esa clave ya la tiene otro banco."));
        if (codigo is not null && await _db.InstitucionesFinancieras.AsNoTracking().AnyAsync(b => b.codigo_internacional == codigo && (id == null || b.id != id), cancellationToken))
            errores.Add((nameof(datos.codigo_internacional), "Ese código ya lo tiene otro banco."));
        if (errores.Count > 0) return new ResultadoOperacion(false, "Revisa los datos marcados.", errores);

        try
        {
            await GestorBd.EnTransaccionAsync(_db, async () =>
            {
                ulong idGuardado;
                if (id is ulong idEditar)
                {
                    idGuardado = idEditar;
                    await _db.InstitucionesFinancieras.Where(b => b.id == idEditar)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(b => b.nombre, nombre)
                            .SetProperty(b => b.clave_institucion, clave)
                            .SetProperty(b => b.codigo_internacional, codigo)
                            .SetProperty(b => b.telefono, telefono), cancellationToken);
                }
                else
                {
                    var banco = new InstitucionesFinancieras
                    {
                        nombre = nombre,
                        clave_institucion = clave,
                        codigo_internacional = codigo,
                        telefono = telefono,
                        activo = true
                    };
                    _db.InstitucionesFinancieras.Add(banco);
                    await _db.SaveChangesAsync(cancellationToken);
                    idGuardado = banco.id;
                }
                _db.RegistrarBitacora(idSA, id is null ? "CREAR" : "ACTUALIZAR", "instituciones_financieras", idGuardado,
                    $"Banco \"{nombre}\" (clave {clave ?? "—"}, código {codigo ?? "—"}).");
                await _db.SaveChangesAsync(cancellationToken);
            }, cancellationToken);
        }
        catch (Exception ex) when (GestorBd.EsErrorDeBd(ex))
        {
            _logger.LogError(ex, "No se pudo guardar el banco {Nombre}.", nombre);
            return ResultadoOperacion.Falla("No se pudo guardar el banco. Revisa que la clave y el código no estén repetidos.");
        }
        return ResultadoOperacion.Ok(id is null ? $"Se agregó \"{nombre}\"." : $"Se guardaron los cambios de \"{nombre}\".");
    }

    public async Task<ResultadoOperacion> CambiarEstadoBancoAsync(ulong id, bool activo, ulong idSA, CancellationToken cancellationToken = default)
    {
        var b = await _db.InstitucionesFinancieras.AsNoTracking().Where(x => x.id == id).Select(x => new { x.nombre, x.activo }).FirstOrDefaultAsync(cancellationToken);
        if (b is null) return ResultadoOperacion.Falla("El banco no existe.");
        if (b.activo == activo) return ResultadoOperacion.Falla(activo ? "Ya está activo." : "Ya está inactivo.");

        await GestorBd.EnTransaccionAsync(_db, async () =>
        {
            await _db.InstitucionesFinancieras.Where(x => x.id == id && x.activo != activo)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.activo, activo), cancellationToken);
            _db.RegistrarBitacora(idSA, activo ? "REACTIVAR" : "BAJA", "instituciones_financieras", id,
                $"Banco \"{b.nombre}\" {(activo ? "activado" : "desactivado")}.");
            await _db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        return ResultadoOperacion.Ok(activo
            ? $"\"{b.nombre}\" activado: ya se puede elegir al guardar una tarjeta."
            : $"\"{b.nombre}\" desactivado: ya no se puede elegir. Las tarjetas que ya lo tienen no cambian.");
    }
}
