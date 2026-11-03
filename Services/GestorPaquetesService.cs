using FlyVibeMex.Data;
using FlyVibeMex.Models;
using Microsoft.EntityFrameworkCore;

namespace FlyVibeMex.Services;

/// <summary>Implementación de <see cref="IGestorPaquetesService"/> con EF Core (consultas LINQ) y bitácora.</summary>
public sealed class GestorPaquetesService : IGestorPaquetesService
{
    /// <summary>Lo que puede contener un paquete (un paquete no incluye otro paquete).</summary>
    private static readonly string[] TiposIncluibles = { "VUELO", "HOSPEDAJE", "VEHICULO", "EVENTO" };

    private readonly FlyVibeMexDbContext _db;
    private readonly IAlmacenImagenes _imagenes;
    private readonly ILogger<GestorPaquetesService> _logger;

    public GestorPaquetesService(FlyVibeMexDbContext db, IAlmacenImagenes imagenes, ILogger<GestorPaquetesService> logger)
    {
        _db = db;
        _imagenes = imagenes;
        _logger = logger;
    }

    private static DateOnly Hoy() => DateOnly.FromDateTime(DateTime.Today);

    // =============================================================================================
    // Consultas
    // =============================================================================================

    public async Task<ListaPaquetesViewModel> ListaAsync(string? nombre, bool incluirInactivos, CancellationToken cancellationToken = default)
    {
        var q = from s in _db.Servicios.AsNoTracking()
                join pq in _db.Paquetes.AsNoTracking() on s.id equals pq.id_servicio
                join p in _db.Proveedores.AsNoTracking() on s.id_proveedor equals p.id
                where s.tipo_servicio == "PAQUETE"
                select new { s, pq, proveedor = p.nombre };
        if (!incluirInactivos) q = q.Where(x => x.s.activo);
        if (nombre is not null) q = q.Where(x => x.s.nombre.Contains(nombre));
        var filas = await q.OrderBy(x => x.s.nombre).Take(200).ToListAsync(cancellationToken);

        var vm = new ListaPaquetesViewModel { Nombre = nombre, IncluirInactivos = incluirInactivos };
        if (filas.Count == 0) return vm;

        var componentes = await (
                from ps in _db.PaquetesServicios.AsNoTracking()
                join s in _db.Servicios.AsNoTracking() on ps.id_servicio_incluido equals s.id
                select new { ps.id_servicio, ps.orden, ps.cantidad, s.nombre, s.tipo_servicio, s.id })
            .ToListAsync(cancellationToken);
        var ciudades = await _db.Ubicaciones.AsNoTracking().ToDictionaryAsync(u => u.id, u => u.municipio_ciudad, cancellationToken);
        var ciudadHotel = await _db.Hospedajes.AsNoTracking().ToDictionaryAsync(h => h.id_servicio, h => h.id_ubicacion, cancellationToken);
        var destinoVuelo = await _db.Vuelos.AsNoTracking().ToDictionaryAsync(v => v.id_servicio, v => v.id_destino, cancellationToken);

        var hoy = Hoy();
        var dias = await _db.DisponibilidadServicios.AsNoTracking()
            .Where(d => d.fecha >= hoy && d.cantidad_disponible > 0)
            .GroupBy(d => d.id_servicio)
            .Select(g => new { id = g.Key, dias = g.Count() })
            .ToDictionaryAsync(x => x.id, x => x.dias, cancellationToken);

        foreach (var f in filas)
        {
            var mios = componentes.Where(c => c.id_servicio == f.s.id).OrderBy(c => c.orden ?? ushort.MaxValue).ToList();

            // Mismo criterio que el catálogo: el destino elegido; si no, la ciudad del hospedaje; si no, el destino del vuelo.
            var destino = f.pq.id_ubicacion_destino
                ?? mios.Where(c => ciudadHotel.ContainsKey(c.id)).Select(c => (ulong?)ciudadHotel[c.id]).FirstOrDefault()
                ?? mios.Where(c => destinoVuelo.ContainsKey(c.id)).Select(c => (ulong?)destinoVuelo[c.id]).FirstOrDefault();

            vm.Resultados.Add(new PaqueteListadoItem
            {
                Servicio = f.s,
                proveedor = f.proveedor,
                duracion_dias = f.pq.duracion_dias,
                min_personas = f.pq.min_personas,
                max_personas = f.pq.max_personas,
                destino = destino is ulong d ? ciudades.GetValueOrDefault(d) : null,
                destino_explicito = f.pq.id_ubicacion_destino is not null,
                componentes = mios.Select(c => $"{c.nombre} ({ServicioFormViewModel.NombreTipo(c.tipo_servicio)} × {c.cantidad:0.##})").ToList(),
                dias_con_cupo = dias.GetValueOrDefault(f.s.id)
            });
        }
        return vm;
    }

    public async Task<PaqueteFormViewModel> NuevoAsync(ulong? idProveedor, CancellationToken cancellationToken = default)
    {
        var vm = new PaqueteFormViewModel
        {
            id_proveedor = idProveedor is > 0 ? idProveedor : null,
            inventario_desde = DateTime.Today,
            inventario_hasta = DateTime.Today.AddDays(89)
        };
        await PrepararAsync(vm, cancellationToken);
        return vm;
    }

    public async Task<PaqueteFormViewModel?> FormularioAsync(ulong id, CancellationToken cancellationToken = default)
    {
        var s = await _db.Servicios.AsNoTracking().FirstOrDefaultAsync(x => x.id == id && x.tipo_servicio == "PAQUETE", cancellationToken);
        if (s is null) return null;
        var pq = await _db.Paquetes.AsNoTracking().FirstOrDefaultAsync(x => x.id_servicio == id, cancellationToken);
        var incluidos = await _db.PaquetesServicios.AsNoTracking().Where(x => x.id_servicio == id).ToListAsync(cancellationToken);

        var vm = new PaqueteFormViewModel
        {
            id = s.id,
            id_proveedor = s.id_proveedor,
            nombre = s.nombre,
            descripcion = s.descripcion,
            precio_base = s.precio_base,
            moneda = string.IsNullOrWhiteSpace(s.moneda) ? "MXN" : s.moneda,
            activo = s.activo,
            permite_cancelacion = s.permite_cancelacion,
            horas_reembolso_total = s.horas_reembolso_total,
            porcentaje_reembolso_tardio = s.porcentaje_reembolso_tardio,
            politica_cancelacion = s.politica_cancelacion,
            duracion_dias = pq?.duracion_dias ?? 1,
            min_personas = pq?.min_personas ?? 1,
            max_personas = pq?.max_personas ?? 1,
            id_ubicacion_destino = pq?.id_ubicacion_destino,
            imagen_actual = s.imagen_ruta,   // L6: vive en el servicio
            componentes = incluidos.Select(i => new PaqueteComponenteForm
            {
                id_servicio = i.id_servicio_incluido,
                incluido = true,
                cantidad = i.cantidad,
                orden = i.orden
            }).ToList()
        };
        await PrepararAsync(vm, cancellationToken);
        return vm;
    }

    public async Task PrepararAsync(PaqueteFormViewModel datos, CancellationToken cancellationToken = default)
    {
        var marcados = datos.componentes.Where(c => c.incluido)
            .GroupBy(c => c.id_servicio)
            .ToDictionary(g => g.Key, g => g.First());
        datos.Candidatos = await GestorBd.CandidatosAsync(_db, TiposIncluibles, marcados.Keys, cancellationToken);

        // Una fila por candidato, en el mismo orden que la tabla de la vista (así los índices del formulario coinciden).
        datos.componentes = datos.Candidatos
            .Select(c => marcados.TryGetValue(c.id, out var m)
                ? m
                : new PaqueteComponenteForm { id_servicio = c.id, incluido = false, cantidad = 1 })
            .ToList();
    }

    // =============================================================================================
    // Alta y actualización
    // =============================================================================================

    public async Task<ResultadoOperacion> CrearAsync(PaqueteFormViewModel datos, ulong idUsuario, CancellationToken cancellationToken = default)
    {
        var errores = new List<(string campo, string mensaje)>();
        await ValidarProveedorAsync(datos.id_proveedor, true, errores, cancellationToken);
        var incluidos = await ValidarPaqueteAsync(datos, idPaquete: null, errores, cancellationToken);
        var rango = ValidarInventarioInicial(datos, errores);
        ValidarImagen(datos, errores);
        if (errores.Count > 0) return new ResultadoOperacion(false, "Revisa los datos marcados.", errores);

        PoliticaCancelacion.Normalizar(datos);
        string? rutaImagen = null;
        ulong idCreado = 0;

        try
        {
            if (datos.imagen is { Length: > 0 })
                rutaImagen = await _imagenes.GuardarAsync(datos.imagen, CarpetaImagen.Servicios, cancellationToken);

            await GestorBd.EnTransaccionAsync(_db, async () =>
            {
                var ahora = DateTime.Now;
                var servicio = new Servicios
                {
                    id_proveedor = datos.id_proveedor!.Value,
                    tipo_servicio = "PAQUETE",
                    nombre = datos.nombre.Trim(),
                    descripcion = GestorBd.Limpiar(datos.descripcion),
                    precio_base = datos.precio_base,
                    moneda = datos.moneda,
                    permite_cancelacion = datos.permite_cancelacion,
                    horas_reembolso_total = datos.horas_reembolso_total,
                    porcentaje_reembolso_tardio = datos.porcentaje_reembolso_tardio,
                    politica_cancelacion = datos.politica_cancelacion,
                    imagen_ruta = rutaImagen,        // L6: dato común del servicio (R1)
                    activo = true,
                    creado_en = ahora,
                    actualizado_en = ahora
                };
                _db.Servicios.Add(servicio);
                await _db.SaveChangesAsync(cancellationToken);
                idCreado = servicio.id;

                _db.Paquetes.Add(new Paquetes
                {
                    id_servicio = servicio.id,
                    duracion_dias = (ushort)datos.duracion_dias,
                    min_personas = (ushort)datos.min_personas,
                    max_personas = (ushort)datos.max_personas,
                    id_ubicacion_destino = datos.id_ubicacion_destino
                });
                await _db.SaveChangesAsync(cancellationToken);   // paquete_servicio apunta a paquete (FK)

                foreach (var c in incluidos)
                    _db.PaquetesServicios.Add(new PaqueteServicio
                    {
                        id_servicio = servicio.id,
                        id_servicio_incluido = c.id_servicio,
                        cantidad = c.cantidad,
                        orden = c.orden
                    });

                for (var dia = rango.desde; dia <= rango.hasta; dia = dia.AddDays(1))
                    _db.DisponibilidadServicios.Add(new DisponibilidadServicio
                    {
                        id_servicio = servicio.id,
                        fecha = dia,
                        cantidad_total = datos.inventario_cupo!.Value,
                        cantidad_disponible = datos.inventario_cupo!.Value,
                        precio_fecha = datos.inventario_precio ?? datos.precio_base,
                        actualizado_en = ahora
                    });

                _db.RegistrarBitacora(idUsuario, "CREAR", "servicios", servicio.id,
                    $"Alta del paquete \"{servicio.nombre}\" con {incluidos.Count} servicio(s) incluido(s) y cupo " +
                    $"{datos.inventario_cupo} del {rango.desde:dd/MM/yyyy} al {rango.hasta:dd/MM/yyyy}.");
                await _db.SaveChangesAsync(cancellationToken);
            }, cancellationToken);
        }
        catch (Exception ex) when (GestorBd.EsErrorDeBd(ex))
        {
            _imagenes.Eliminar(rutaImagen);
            _logger.LogError(ex, "No se pudo dar de alta el paquete \"{Nombre}\".", datos.nombre);
            return ResultadoOperacion.Falla("No se pudo guardar el paquete. Revisa los datos e intenta de nuevo.");
        }

        _logger.LogInformation("Usuario {IdUsuario}: alta del paquete {IdPaquete}.", idUsuario, idCreado);
        return ResultadoOperacion.Ok($"Se dio de alta el paquete \"{datos.nombre.Trim()}\" con su disponibilidad. Ya aparece en el catálogo.");
    }

    public async Task<ResultadoOperacion> ActualizarAsync(ulong id, PaqueteFormViewModel datos, ulong idUsuario, CancellationToken cancellationToken = default)
    {
        var actual = await _db.Servicios.AsNoTracking().FirstOrDefaultAsync(s => s.id == id && s.tipo_servicio == "PAQUETE", cancellationToken);
        if (actual is null) return ResultadoOperacion.Falla("El paquete ya no existe.");

        var errores = new List<(string campo, string mensaje)>();
        await ValidarProveedorAsync(datos.id_proveedor, datos.id_proveedor != actual.id_proveedor, errores, cancellationToken);
        var incluidos = await ValidarPaqueteAsync(datos, id, errores, cancellationToken);
        ValidarImagen(datos, errores);
        if (errores.Count > 0) return new ResultadoOperacion(false, "Revisa los datos marcados.", errores);

        PoliticaCancelacion.Normalizar(datos);
        string? rutaNueva = null;
        string? rutaAnterior = null;

        try
        {
            if (datos.imagen is { Length: > 0 })
                rutaNueva = await _imagenes.GuardarAsync(datos.imagen, CarpetaImagen.Servicios, cancellationToken);

            await GestorBd.EnTransaccionAsync(_db, async () =>
            {
                rutaAnterior = null;
                var ahora = DateTime.Now;
                var idProveedor = datos.id_proveedor!.Value;
                var nombre = datos.nombre.Trim();
                var descripcion = GestorBd.Limpiar(datos.descripcion);

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

                var paquete = await _db.Paquetes.FirstOrDefaultAsync(p => p.id_servicio == id, cancellationToken);
                if (paquete is null)
                {
                    paquete = new Paquetes { id_servicio = id };
                    _db.Paquetes.Add(paquete);
                }
                paquete.duracion_dias = (ushort)datos.duracion_dias;
                paquete.min_personas = (ushort)datos.min_personas;
                paquete.max_personas = (ushort)datos.max_personas;
                paquete.id_ubicacion_destino = datos.id_ubicacion_destino;
                if (rutaNueva is not null || datos.quitar_imagen)
                {
                    // L6: la imagen vive en `servicios`, no en `paquete`.
                    rutaAnterior = await _db.Servicios.AsNoTracking().Where(srv => srv.id == id)
                        .Select(srv => srv.imagen_ruta).FirstOrDefaultAsync(cancellationToken);
                    await _db.Servicios.Where(srv => srv.id == id)
                        .ExecuteUpdateAsync(srv => srv.SetProperty(x => x.imagen_ruta, rutaNueva), cancellationToken);
                }
                await _db.SaveChangesAsync(cancellationToken);

                // Servicios incluidos: se reemplazan completos (las compras pasadas no dependen de esta tabla).
                await _db.PaquetesServicios.Where(ps => ps.id_servicio == id).ExecuteDeleteAsync(cancellationToken);
                foreach (var c in incluidos)
                    _db.PaquetesServicios.Add(new PaqueteServicio
                    {
                        id_servicio = id,
                        id_servicio_incluido = c.id_servicio,
                        cantidad = c.cantidad,
                        orden = c.orden
                    });

                _db.RegistrarBitacora(idUsuario, "ACTUALIZAR", "servicios", id,
                    $"Datos del paquete \"{nombre}\" actualizados ({incluidos.Count} servicio(s) incluido(s))" +
                    $"{(datos.activo ? "" : "; queda inactivo")}.");
                await _db.SaveChangesAsync(cancellationToken);
            }, cancellationToken);
        }
        catch (Exception ex) when (GestorBd.EsErrorDeBd(ex))
        {
            _imagenes.Eliminar(rutaNueva);
            _logger.LogError(ex, "No se pudo actualizar el paquete {IdPaquete}.", id);
            return ResultadoOperacion.Falla("No se pudieron guardar los cambios. Intenta de nuevo.");
        }

        // Solo se borra si ningún otro servicio sigue usando ese archivo (L6b).
        if (rutaAnterior != rutaNueva)
            await _db.BorrarImagenSiNadieLaUsaAsync(_imagenes, rutaAnterior, cancellationToken);
        return ResultadoOperacion.Ok($"Se guardaron los cambios del paquete \"{datos.nombre.Trim()}\". Las compras anteriores conservan sus condiciones.");
    }

    // =============================================================================================
    // Validaciones
    // =============================================================================================

    private async Task ValidarProveedorAsync(ulong? idProveedor, bool exigirActivo, List<(string, string)> errores, CancellationToken cancellationToken)
    {
        if (idProveedor is null or 0) return;   // ya lo marca [Required]
        var proveedor = await _db.Proveedores.AsNoTracking().FirstOrDefaultAsync(p => p.id == idProveedor.Value, cancellationToken);
        if (proveedor is null)
            errores.Add((nameof(PaqueteFormViewModel.id_proveedor), "El proveedor seleccionado no existe."));
        else if (exigirActivo && !proveedor.activo)
            errores.Add((nameof(PaqueteFormViewModel.id_proveedor), "El proveedor está dado de baja. Reactívalo antes de asociarle servicios."));
    }

    /// <summary>
    /// Personas, servicios incluidos y destino. Devuelve los componentes marcados (sin repetidos) para guardarlos.
    /// </summary>
    private async Task<List<PaqueteComponenteForm>> ValidarPaqueteAsync(PaqueteFormViewModel datos, ulong? idPaquete,
        List<(string, string)> errores, CancellationToken cancellationToken)
    {
        if (datos.min_personas > datos.max_personas)
            errores.Add((nameof(datos.max_personas), "Debe ser igual o mayor que el mínimo de personas."));

        var incluidos = datos.componentes
            .Where(c => c.incluido)
            .GroupBy(c => c.id_servicio)
            .Select(g => g.First())
            .ToList();
        if (incluidos.Count == 0)
        {
            errores.Add((nameof(datos.componentes), "Marca al menos un servicio incluido."));
            return incluidos;
        }
        if (idPaquete is ulong propio && incluidos.Any(c => c.id_servicio == propio))
            errores.Add((nameof(datos.componentes), "Un paquete no puede incluirse a sí mismo."));

        // Los candidatos válidos (activos, o ya incluidos antes) son la única fuente de verdad: nunca lo que diga el navegador.
        var yaIncluidos = idPaquete is ulong idp
            ? await _db.PaquetesServicios.AsNoTracking().Where(ps => ps.id_servicio == idp).Select(ps => ps.id_servicio_incluido).ToListAsync(cancellationToken)
            : new List<ulong>();
        var candidatos = (await GestorBd.CandidatosAsync(_db, TiposIncluibles, yaIncluidos, cancellationToken))
            .ToDictionary(c => c.id);

        var invalidos = incluidos.Where(c => !candidatos.ContainsKey(c.id_servicio)).ToList();
        if (invalidos.Count > 0)
            errores.Add((nameof(datos.componentes), "Algún servicio marcado ya no existe, está dado de baja o es otro paquete."));
        if (incluidos.Any(c => c.cantidad <= 0))
            errores.Add((nameof(datos.componentes), "Cada servicio incluido necesita una cantidad mayor a 0."));

        // Destino: opcional, salvo que los servicios estén en ciudades distintas o no haya hospedaje ni vuelo.
        if (datos.id_ubicacion_destino is ulong destino)
        {
            if (!await _db.Ubicaciones.AsNoTracking().AnyAsync(u => u.id == destino && u.activo, cancellationToken))
                errores.Add((nameof(datos.id_ubicacion_destino), "Elige un destino de la lista."));
        }
        else if (invalidos.Count == 0)
        {
            var elegidos = incluidos.Select(c => candidatos[c.id_servicio]).ToList();
            var ciudades = elegidos.Where(c => c.id_ciudad is not null).Select(c => c.id_ciudad).Distinct().Count();
            if (!elegidos.Any(c => c.tipo_servicio is "HOSPEDAJE" or "VUELO"))
                errores.Add((nameof(datos.id_ubicacion_destino), "El paquete no incluye hospedaje ni vuelo: elige su destino."));
            else if (ciudades > 1)
                errores.Add((nameof(datos.id_ubicacion_destino), "Los servicios incluidos están en ciudades distintas: elige el destino del paquete."));
        }

        return incluidos;
    }

    private static (DateOnly desde, DateOnly hasta) ValidarInventarioInicial(ServicioConInventarioFormBase f, List<(string, string)> errores)
    {
        if (f.inventario_cupo is null)
            errores.Add((nameof(f.inventario_cupo), "Indica cuántas personas por día."));
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

    private void ValidarImagen(PaqueteFormViewModel datos, List<(string, string)> errores)
    {
        var error = _imagenes.Validar(datos.imagen);
        if (error is not null) errores.Add((nameof(datos.imagen), error));
    }
}
