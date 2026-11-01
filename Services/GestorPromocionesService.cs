using FlyVibeMex.Data;
using FlyVibeMex.Models;
using Microsoft.EntityFrameworkCore;

namespace FlyVibeMex.Services;

/// <summary>Implementación de <see cref="IGestorPromocionesService"/> con EF Core (consultas LINQ) y bitácora.</summary>
/// <remarks>
/// Las reglas de aplicación (vigencia, servicio ligado, compra mínima, descuento sin pasar del subtotal) están en
/// ReservacionService.AplicarPromocionAsync. Aquí solo se captura la promoción; las compras pasadas guardan su
/// descuento en detalle_reservacion (R4), así que editar una promoción no las cambia.
/// </remarks>
public sealed class GestorPromocionesService : IGestorPromocionesService
{
    /// <summary>Todo lo que se vende puede tener promoción, también los paquetes.</summary>
    private static readonly string[] TiposConPromocion = { "VUELO", "HOSPEDAJE", "VEHICULO", "EVENTO", "PAQUETE" };

    private readonly FlyVibeMexDbContext _db;
    private readonly ILogger<GestorPromocionesService> _logger;

    public GestorPromocionesService(FlyVibeMexDbContext db, ILogger<GestorPromocionesService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // =============================================================================================
    // Consultas
    // =============================================================================================

    public async Task<ListaPromocionesViewModel> ListaAsync(string? codigo, string estado, CancellationToken cancellationToken = default)
    {
        var ahora = DateTime.Now;
        var q = _db.Promociones.AsNoTracking().AsQueryable();
        if (codigo is not null) q = q.Where(p => p.codigo.Contains(codigo) || (p.nombre != null && p.nombre.Contains(codigo)));
        q = estado switch
        {
            "vigentes" => q.Where(p => p.activo && p.fecha_inicio <= ahora && p.fecha_fin >= ahora),
            "proximas" => q.Where(p => p.activo && p.fecha_inicio > ahora),
            "vencidas" => q.Where(p => p.activo && p.fecha_fin < ahora),
            "inactivas" => q.Where(p => !p.activo),
            _ => q
        };

        var promos = await q.OrderByDescending(p => p.fecha_inicio).ThenBy(p => p.codigo).Take(200).ToListAsync(cancellationToken);
        var servicios = await _db.PromocionesServicios.AsNoTracking()
            .GroupBy(ps => ps.id_promocion).Select(g => new { id = g.Key, n = g.Count() })
            .ToDictionaryAsync(x => x.id, x => x.n, cancellationToken);
        var usos = await UsosAsync(cancellationToken);

        return new ListaPromocionesViewModel
        {
            Codigo = codigo,
            Estado = estado,
            Resultados = promos.Select(p => new PromocionListadoItem
            {
                id = p.id,
                codigo = p.codigo,
                nombre = p.nombre,
                tipo_descuento = p.tipo_descuento,
                valor = p.valor,
                minimo_compra = p.minimo_compra,
                fecha_inicio = p.fecha_inicio,
                fecha_fin = p.fecha_fin,
                activo = p.activo,
                servicios = servicios.GetValueOrDefault(p.id),
                usos = usos.GetValueOrDefault(p.id)
            }).ToList()
        };
    }

    public async Task<PromocionFormViewModel> NuevaAsync(CancellationToken cancellationToken = default)
    {
        var hoy = DateTime.Today;
        var vm = new PromocionFormViewModel
        {
            fecha_inicio = hoy,
            fecha_fin = hoy.AddDays(30).AddHours(23).AddMinutes(59)
        };
        await PrepararAsync(vm, cancellationToken);
        return vm;
    }

    public async Task<PromocionFormViewModel?> FormularioAsync(ulong id, CancellationToken cancellationToken = default)
    {
        var p = await _db.Promociones.AsNoTracking().FirstOrDefaultAsync(x => x.id == id, cancellationToken);
        if (p is null) return null;

        var vm = new PromocionFormViewModel
        {
            id = p.id,
            codigo = p.codigo,
            nombre = p.nombre,
            descripcion = p.descripcion,
            tipo_descuento = p.tipo_descuento,
            valor = p.valor,
            fecha_inicio = p.fecha_inicio,
            fecha_fin = p.fecha_fin,
            minimo_compra = p.minimo_compra,
            activo = p.activo,
            servicios = await _db.PromocionesServicios.AsNoTracking()
                .Where(ps => ps.id_promocion == id).Select(ps => ps.id_servicio).ToListAsync(cancellationToken)
        };
        await PrepararAsync(vm, cancellationToken);
        return vm;
    }

    public async Task PrepararAsync(PromocionFormViewModel datos, CancellationToken cancellationToken = default)
    {
        datos.servicios = datos.servicios.Distinct().ToList();
        datos.Candidatos = await GestorBd.CandidatosAsync(_db, TiposConPromocion, datos.servicios, cancellationToken);
        if (!datos.EsAlta)
        {
            var id = datos.id;
            datos.usos = await _db.DetallesReservacion.AsNoTracking()
                .Where(d => d.id_promocion == id).Select(d => d.id_reservacion).Distinct().CountAsync(cancellationToken);
        }
    }

    // =============================================================================================
    // Alta, edición y estado
    // =============================================================================================

    public async Task<ResultadoOperacion> GuardarAsync(ulong? id, PromocionFormViewModel datos, ulong idUsuario, CancellationToken cancellationToken = default)
    {
        var errores = new List<(string campo, string mensaje)>();
        Promociones? actual = null;

        if (id is ulong idPromo)
        {
            actual = await _db.Promociones.AsNoTracking().FirstOrDefaultAsync(p => p.id == idPromo, cancellationToken);
            if (actual is null) return ResultadoOperacion.Falla("La promoción ya no existe.");
            datos.codigo = actual.codigo;   // el código no cambia al editar (las compras lo muestran)
        }
        else
        {
            datos.codigo = datos.codigo.Trim().ToUpperInvariant();
            var codigo = datos.codigo;
            if (await _db.Promociones.AsNoTracking().AnyAsync(p => p.codigo == codigo, cancellationToken))
                errores.Add((nameof(datos.codigo), "Ya existe una promoción con ese código."));
        }

        if (datos.tipo_descuento == "PORCENTAJE" && datos.valor > 100)
            errores.Add((nameof(datos.valor), "Un porcentaje no puede pasar de 100."));
        if (datos.fecha_inicio is DateTime inicio && datos.fecha_fin is DateTime fin && fin <= inicio)
            errores.Add((nameof(datos.fecha_fin), "El fin de vigencia debe ser posterior al inicio."));

        var elegidos = datos.servicios.Distinct().ToList();
        if (elegidos.Count == 0)
            errores.Add((nameof(datos.servicios), "Marca al menos un servicio: la promoción solo aplica a los servicios elegidos."));
        else
        {
            // Solo servicios activos (o que ya estaban ligados): nunca lo que diga el navegador.
            var yaLigados = id is ulong idp
                ? await _db.PromocionesServicios.AsNoTracking().Where(ps => ps.id_promocion == idp).Select(ps => ps.id_servicio).ToListAsync(cancellationToken)
                : new List<ulong>();
            var validos = (await GestorBd.CandidatosAsync(_db, TiposConPromocion, yaLigados, cancellationToken)).Select(c => c.id).ToHashSet();
            if (elegidos.Any(s => !validos.Contains(s)))
                errores.Add((nameof(datos.servicios), "Algún servicio marcado ya no existe o está dado de baja."));
        }

        if (errores.Count > 0) return new ResultadoOperacion(false, "Revisa los datos marcados.", errores);

        var nombre = GestorBd.Limpiar(datos.nombre);
        var descripcion = GestorBd.Limpiar(datos.descripcion);
        var fechaInicio = datos.fecha_inicio!.Value;
        var fechaFin = datos.fecha_fin!.Value;

        try
        {
            await GestorBd.EnTransaccionAsync(_db, async () =>
            {
                ulong idGuardado;
                if (id is null)
                {
                    var promo = new Promociones
                    {
                        codigo = datos.codigo,
                        nombre = nombre,
                        descripcion = descripcion,
                        tipo_descuento = datos.tipo_descuento,
                        valor = datos.valor,
                        fecha_inicio = fechaInicio,
                        fecha_fin = fechaFin,
                        minimo_compra = datos.minimo_compra,
                        activo = datos.activo
                    };
                    _db.Promociones.Add(promo);
                    await _db.SaveChangesAsync(cancellationToken);
                    idGuardado = promo.id;
                }
                else
                {
                    idGuardado = id.Value;
                    await _db.Promociones.Where(p => p.id == idGuardado)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(p => p.nombre, nombre)
                            .SetProperty(p => p.descripcion, descripcion)
                            .SetProperty(p => p.tipo_descuento, datos.tipo_descuento)
                            .SetProperty(p => p.valor, datos.valor)
                            .SetProperty(p => p.fecha_inicio, fechaInicio)
                            .SetProperty(p => p.fecha_fin, fechaFin)
                            .SetProperty(p => p.minimo_compra, datos.minimo_compra)
                            .SetProperty(p => p.activo, datos.activo), cancellationToken);
                    await _db.PromocionesServicios.Where(ps => ps.id_promocion == idGuardado).ExecuteDeleteAsync(cancellationToken);
                }

                foreach (var idServicio in elegidos)
                    _db.PromocionesServicios.Add(new PromocionServicio { id_promocion = idGuardado, id_servicio = idServicio });

                var texto = datos.tipo_descuento == "PORCENTAJE" ? $"{datos.valor:0.##} %" : $"${datos.valor:N2}";
                _db.RegistrarBitacora(idUsuario, id is null ? "CREAR" : "ACTUALIZAR", "promociones", idGuardado,
                    $"Promoción {datos.codigo}: {texto}, del {fechaInicio:dd/MM/yyyy} al {fechaFin:dd/MM/yyyy}, " +
                    $"{elegidos.Count} servicio(s){(datos.activo ? "" : ", inactiva")}.");
                await _db.SaveChangesAsync(cancellationToken);
            }, cancellationToken);
        }
        catch (Exception ex) when (GestorBd.EsErrorDeBd(ex))
        {
            _logger.LogError(ex, "No se pudo guardar la promoción {Codigo}.", datos.codigo);
            return ResultadoOperacion.Falla("No se pudo guardar la promoción. Intenta de nuevo.");
        }

        return ResultadoOperacion.Ok(id is null
            ? $"Se creó la promoción {datos.codigo}."
            : $"Se guardaron los cambios de {datos.codigo}. Las compras anteriores conservan su descuento.");
    }

    public async Task<ResultadoOperacion> CambiarEstadoAsync(ulong id, bool activo, ulong idUsuario, CancellationToken cancellationToken = default)
    {
        var promo = await _db.Promociones.AsNoTracking().FirstOrDefaultAsync(p => p.id == id, cancellationToken);
        if (promo is null) return ResultadoOperacion.Falla("La promoción no existe.");
        if (promo.activo == activo)
            return ResultadoOperacion.Ok($"{promo.codigo} ya estaba {(activo ? "activa" : "inactiva")}.");

        await GestorBd.EnTransaccionAsync(_db, async () =>
        {
            await _db.Promociones.Where(p => p.id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.activo, activo), cancellationToken);
            _db.RegistrarBitacora(idUsuario, activo ? "REACTIVAR" : "BAJA", "promociones", id,
                $"Promoción {promo.codigo} {(activo ? "activada" : "desactivada")}.");
            await _db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        return ResultadoOperacion.Ok(activo
            ? $"{promo.codigo} se activó."
            : $"{promo.codigo} se desactivó: ya no se puede usar. Las compras anteriores no cambian.");
    }

    /// <summary>Compras (reservaciones distintas) que usaron cada promoción.</summary>
    private async Task<Dictionary<ulong, int>> UsosAsync(CancellationToken cancellationToken)
    {
        var filas = await _db.DetallesReservacion.AsNoTracking()
            .Where(d => d.id_promocion != null)
            .Select(d => new { id = d.id_promocion!.Value, d.id_reservacion })
            .Distinct()
            .ToListAsync(cancellationToken);
        return filas.GroupBy(f => f.id).ToDictionary(g => g.Key, g => g.Count());
    }
}
