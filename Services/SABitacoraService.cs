using FlyVibeMex.Data;
using FlyVibeMex.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace FlyVibeMex.Services;

/// <summary>Bitácora del panel e inicio del SA (consultas de solo lectura).</summary>
public interface ISABitacoraService
{
    Task<ListaBitacoraViewModel> ListaAsync(FiltroBitacora filtro, CancellationToken cancellationToken = default);

    /// <summary>Contadores del inicio del panel SA y las últimas 10 acciones.</summary>
    Task<InicioSAViewModel> InicioAsync(CancellationToken cancellationToken = default);
}

/// <summary>Implementación de <see cref="ISABitacoraService"/> con EF Core.</summary>
public sealed class SABitacoraService : ISABitacoraService
{
    private readonly FlyVibeMexDbContext _db;

    public SABitacoraService(FlyVibeMexDbContext db)
    {
        _db = db;
    }

    public async Task<ListaBitacoraViewModel> ListaAsync(FiltroBitacora filtro, CancellationToken cancellationToken = default)
    {
        filtro.entidad = Recortar(filtro.entidad, 40);
        filtro.accion = Recortar(filtro.accion, 30);
        filtro.texto = Recortar(filtro.texto, 100);

        var q = _db.Bitacora.AsNoTracking().AsQueryable();
        if (filtro.desde is DateTime desde)
        {
            var inicio = desde.Date;
            q = q.Where(b => b.fecha >= inicio);
        }
        if (filtro.hasta is DateTime hasta)
        {
            var fin = hasta.Date.AddDays(1);
            q = q.Where(b => b.fecha < fin);
        }
        if (filtro.usuario is ulong idUsuario) q = q.Where(b => b.id_usuario == idUsuario);
        if (filtro.entidad is string entidad) q = q.Where(b => b.entidad == entidad);
        if (filtro.accion is string accion) q = q.Where(b => b.accion == accion);
        if (filtro.texto is string texto) q = q.Where(b => b.detalle != null && b.detalle.Contains(texto));

        var total = await q.CountAsync(cancellationToken);
        var paginas = Math.Max(1, (int)Math.Ceiling(total / (double)ListaBitacoraViewModel.PorPagina));
        filtro.pagina = Math.Clamp(filtro.pagina, 1, paginas);

        var pagina = q.OrderByDescending(b => b.fecha).ThenByDescending(b => b.id)
            .Skip((filtro.pagina - 1) * ListaBitacoraViewModel.PorPagina)
            .Take(ListaBitacoraViewModel.PorPagina);

        // Opciones de los filtros: solo lo que existe en la bitácora.
        var entidades = await _db.Bitacora.AsNoTracking().Select(b => b.entidad).Distinct().OrderBy(e => e).ToListAsync(cancellationToken);
        var acciones = await _db.Bitacora.AsNoTracking().Select(b => b.accion).Distinct().OrderBy(a => a).ToListAsync(cancellationToken);
        var autores = await (
                from u in _db.Usuarios.AsNoTracking()
                where _db.Bitacora.Any(b => b.id_usuario == u.id)
                orderby u.nombre, u.apellido_paterno
                select new { u.id, nombre = u.nombre + " " + u.apellido_paterno, u.correo })
            .ToListAsync(cancellationToken);

        return new ListaBitacoraViewModel
        {
            Filtro = filtro,
            Total = total,
            Resultados = await ItemsAsync(_db, pagina, cancellationToken),
            Entidades = entidades.Select(e => new SelectListItem(e, e, e == filtro.entidad)).ToList(),
            Acciones = acciones.Select(a => new SelectListItem(a, a, a == filtro.accion)).ToList(),
            Usuarios = autores.Select(a => new SelectListItem($"{a.nombre} ({a.correo})", a.id.ToString(), a.id == filtro.usuario)).ToList()
        };
    }

    public async Task<InicioSAViewModel> InicioAsync(CancellationToken cancellationToken = default)
    {
        var roles = await _db.RolesUsuarios.AsNoTracking().OrderBy(r => r.id).Select(r => new { r.id, r.nombre }).ToListAsync(cancellationToken);
        var cuentas = await _db.Usuarios.AsNoTracking()
            .GroupBy(u => new { u.id_rol, u.activo })
            .Select(g => new { g.Key.id_rol, g.Key.activo, n = g.Count() })
            .ToListAsync(cancellationToken);
        var hoy = DateTime.Today;

        return new InicioSAViewModel
        {
            CuentasPorRol = roles.Select(r => (
                    r.nombre,
                    cuentas.Where(c => c.id_rol == r.id && c.activo).Sum(c => c.n),
                    cuentas.Where(c => c.id_rol == r.id && !c.activo).Sum(c => c.n)))
                .ToList(),
            CuentasInactivas = cuentas.Where(c => !c.activo).Sum(c => c.n),
            CambiosHoy = await _db.Bitacora.AsNoTracking().CountAsync(b => b.fecha >= hoy, cancellationToken),
            Ultimos = await ItemsAsync(_db,
                _db.Bitacora.AsNoTracking().OrderByDescending(b => b.fecha).ThenByDescending(b => b.id).Take(10),
                cancellationToken)
        };
    }

    /// <summary>
    /// Convierte registros de la bitácora en filas para las vistas (con el nombre y el rol de quien hizo el cambio).
    /// La consulta que recibe ya trae su orden y su límite; el orden se respeta en memoria.
    /// </summary>
    public static async Task<List<BitacoraItem>> ItemsAsync(FlyVibeMexDbContext db, IQueryable<Bitacora> registros,
        CancellationToken cancellationToken)
    {
        var filas = await registros.ToListAsync(cancellationToken);
        if (filas.Count == 0) return new List<BitacoraItem>();

        var autores = new Dictionary<ulong, (string nombre, string rol)>();
        foreach (var id in filas.Where(f => f.id_usuario is not null).Select(f => f.id_usuario!.Value).Distinct())
        {
            var autor = await (
                    from u in db.Usuarios.AsNoTracking()
                    join r in db.RolesUsuarios.AsNoTracking() on u.id_rol equals r.id
                    where u.id == id
                    select new { nombre = u.nombre + " " + u.apellido_paterno, rol = r.nombre })
                .FirstOrDefaultAsync(cancellationToken);
            if (autor is not null) autores[id] = (autor.nombre, autor.rol);
        }

        return filas.Select(f =>
        {
            var conAutor = f.id_usuario is ulong a && autores.ContainsKey(a);
            return new BitacoraItem
            {
                fecha = f.fecha,
                id_usuario = f.id_usuario,
                usuario = conAutor ? autores[f.id_usuario!.Value].nombre : (f.id_usuario is null ? "Sistema" : $"Usuario {f.id_usuario}"),
                rol = conAutor ? autores[f.id_usuario!.Value].rol : null,
                accion = f.accion,
                entidad = f.entidad,
                id_entidad = f.id_entidad,
                detalle = f.detalle
            };
        }).ToList();
    }

    private static string? Recortar(string? texto, int maximo)
    {
        texto = GestorBd.Limpiar(texto);
        return texto is { Length: > 0 } && texto.Length > maximo ? texto[..maximo] : texto;
    }
}
