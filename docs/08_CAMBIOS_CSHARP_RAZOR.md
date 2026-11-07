# FlyVibeMex · Código C# y Razor (después de correr `sql/09_...sql`)

Cada bloque dice **en qué archivo va**. Nada se reescribe completo: solo se agrega o se cambia lo indicado.
Convenciones del proyecto respetadas: propiedades `snake_case`, ids `ulong`, escritura por `ProcedimientosBd` (SPs), lectura por EF.

## 0 · Equivalencia de roles (tu proyecto usa estos nombres)

| Tu mensaje | Rol real en `Roles.cs` | Constante |
|---|---|---|
| admin | `ADMINISTRADOR` | `Roles.Administrador` |
| sa | `SA` | `Roles.SA` |
| gestor de ventas | panel `/Admin/GestorVentas` (ADMINISTRADOR + SA) | `Roles.PanelGestor` |
| ticket man | `SOPORTE` | `Roles.Soporte` |
| (cliente) | `CLIENTE` | `Roles.Cliente` |

**`Models/Auth/Roles.cs`** — agrega dentro de la clase:
```csharp
/// <summary>Moderar reseñas (/Admin/Resenas): soporte y administración.</summary>
public const string PanelResenas = Soporte + "," + Administrador + "," + SA;
```

---

## 1 · Modelos (una clase por archivo en `Models/`)

```csharp
// Models/tipos_habitacion.cs
namespace FlyVibeMex.Models;
public class TiposHabitacion
{
    public short id { get; set; }
    public string codigo { get; set; } = string.Empty;     // SGL, DBL, TPL, QPL
    public string nombre { get; set; } = string.Empty;     // Single...
    public string nombre_es { get; set; } = string.Empty;  // Sencilla...
    public byte capacidad { get; set; }
    public string? descripcion { get; set; }
    public bool activo { get; set; }
}

// Models/tipos_huesped.cs
namespace FlyVibeMex.Models;
public class TiposHuesped
{
    public short id { get; set; }
    public string codigo { get; set; } = string.Empty;
    public string nombre { get; set; } = string.Empty;
    public string? descripcion { get; set; }
    public bool activo { get; set; }
}

// Models/hospedaje_detalle.cs
namespace FlyVibeMex.Models;
public class HospedajeDetalle
{
    public ulong id_servicio { get; set; }
    public short id_tipo_habitacion { get; set; }
    public bool tiene_alberca { get; set; }
    public decimal tamano_m2 { get; set; }
}

// Models/detalle_reservacion_huesped.cs
namespace FlyVibeMex.Models;
public class DetalleReservacionHuesped
{
    public ulong id_detalle { get; set; }
    public short id_tipo_huesped { get; set; }
    public short cantidad { get; set; }
}

// Models/favoritos.cs
namespace FlyVibeMex.Models;
public class Favoritos
{
    public ulong id_usuarioglobal { get; set; }
    public ulong id_servicio { get; set; }
    public DateTime creado_en { get; set; }
}

// Models/resenas.cs
namespace FlyVibeMex.Models;
public class Resenas
{
    public ulong id { get; set; }
    public ulong id_usuarioglobal { get; set; }
    public ulong id_servicio { get; set; }
    public byte calificacion { get; set; }
    public string? titulo { get; set; }
    public string comentario { get; set; } = string.Empty;
    public string estado { get; set; } = "VISIBLE";
    public DateTime creada_en { get; set; }
    public DateTime actualizada_en { get; set; }
}
```

**`Data/FlyVibeMexDbContext.cs`** — DbSets (junto a los demás) y mapeo en `OnModelCreating`:
```csharp
public DbSet<TiposHabitacion> TiposHabitacion => Set<TiposHabitacion>();
public DbSet<TiposHuesped> TiposHuesped => Set<TiposHuesped>();
public DbSet<HospedajeDetalle> HospedajeDetalles => Set<HospedajeDetalle>();
public DbSet<DetalleReservacionHuesped> DetalleReservacionHuespedes => Set<DetalleReservacionHuesped>();
public DbSet<Favoritos> Favoritos => Set<Favoritos>();
public DbSet<Resenas> Resenas => Set<Resenas>();
```
```csharp
modelBuilder.Entity<TiposHabitacion>(e => { e.ToTable("tipos_habitacion"); e.HasKey(x => x.id); });
modelBuilder.Entity<TiposHuesped>(e => { e.ToTable("tipos_huesped"); e.HasKey(x => x.id); });
modelBuilder.Entity<HospedajeDetalle>(e =>
{
    e.ToTable("hospedaje_detalle");
    e.HasKey(x => x.id_servicio);
    e.Property(x => x.id_servicio).ValueGeneratedNever();
    e.Property(x => x.tamano_m2).HasPrecision(6, 2);
});
modelBuilder.Entity<DetalleReservacionHuesped>(e =>
{
    e.ToTable("detalle_reservacion_huesped");
    e.HasKey(x => new { x.id_detalle, x.id_tipo_huesped });
});
modelBuilder.Entity<Favoritos>(e =>
{
    e.ToTable("favoritos");
    e.HasKey(x => new { x.id_usuarioglobal, x.id_servicio });
});
modelBuilder.Entity<Resenas>(e =>
{
    e.ToTable("resenas");
    e.HasKey(x => x.id);
    e.Property(x => x.titulo).HasMaxLength(100);
    e.Property(x => x.comentario).HasMaxLength(1000);
    e.Property(x => x.estado).HasMaxLength(10);
});
```

---

## 2 · Servicio de favoritos y reseñas

```csharp
// Services/IComunidadService.cs
namespace FlyVibeMex.Services;

public record ResenaFila(ulong id, int calificacion, string? titulo, string comentario, string estado, DateTime creada_en, string autor);
public record FavoritoFila(ulong id_servicio, string tipo_servicio, string nombre, string? imagen_ruta, decimal precio_base, string moneda, bool activo);

public interface IComunidadService
{
    Task<bool> AlternarFavoritoAsync(ulong idUsuario, ulong idServicio, CancellationToken ct = default);
    Task<List<FavoritoFila>> FavoritosAsync(ulong idUsuario, CancellationToken ct = default);
    Task<HashSet<ulong>> IdsFavoritosAsync(ulong idUsuario, CancellationToken ct = default);
    Task<ulong> CrearResenaAsync(ulong idUsuario, ulong idServicio, int calificacion, string? titulo, string comentario, CancellationToken ct = default);
    Task<(List<ResenaFila> resenas, decimal promedio, int total)> ResenasAsync(ulong idServicio, bool incluirOcultas, CancellationToken ct = default);
    Task ModerarResenaAsync(ulong idUsuario, ulong idResena, bool ocultar, CancellationToken ct = default);
}
```
```csharp
// Services/ComunidadService.cs
using System.Data;
using FlyVibeMex.Data;
using Microsoft.EntityFrameworkCore;
using static FlyVibeMex.Services.ProcedimientosBd;

namespace FlyVibeMex.Services;

/// <summary>Favoritos y reseñas. Escribe con procedimientos (validan rol y reglas); los errores 514xx suben como
/// <see cref="ReglaProcedimientoException"/> con mensaje listo para mostrar.</summary>
public class ComunidadService : IComunidadService
{
    private readonly FlyVibeMexDbContext _db;
    public ComunidadService(FlyVibeMexDbContext db) => _db = db;

    public async Task<bool> AlternarFavoritoAsync(ulong idUsuario, ulong idServicio, CancellationToken ct = default)
    {
        var salida = Salida("@es_favorito", SqlDbType.Bit);
        await EjecutarAsync(_db, "dbo.pa_alternar_favorito",
            new[] { Par("@id_usuario", (long)idUsuario), Par("@id_servicio", (long)idServicio), salida }, ct);
        return salida.Value is bool b && b;
    }

    public Task<List<FavoritoFila>> FavoritosAsync(ulong idUsuario, CancellationToken ct = default) =>
        LeerAsync(_db, "dbo.pa_listar_favoritos", new[] { Par("@id_usuario", (long)idUsuario) },
            r => new FavoritoFila((ulong)r.GetInt64(0), r.GetString(1), r.GetString(2),
                                  r.IsDBNull(3) ? null : r.GetString(3), r.GetDecimal(4), r.GetString(5), r.GetBoolean(6)), ct);

    // Para pintar el corazón lleno en las tarjetas: una sola consulta por página.
    public async Task<HashSet<ulong>> IdsFavoritosAsync(ulong idUsuario, CancellationToken ct = default) =>
        (await _db.Favoritos.AsNoTracking().Where(f => f.id_usuarioglobal == idUsuario)
            .Select(f => f.id_servicio).ToListAsync(ct)).ToHashSet();

    public async Task<ulong> CrearResenaAsync(ulong idUsuario, ulong idServicio, int calificacion, string? titulo, string comentario, CancellationToken ct = default)
    {
        var salida = Salida("@id_resena", SqlDbType.BigInt);
        await EjecutarAsync(_db, "dbo.pa_crear_resena", new[]
        {
            Par("@id_usuario", (long)idUsuario), Par("@id_servicio", (long)idServicio),
            Par("@calificacion", (byte)calificacion), Par("@titulo", titulo), Par("@comentario", comentario), salida
        }, ct);
        return salida.Value is long id ? (ulong)id : 0;
    }

    public async Task<(List<ResenaFila>, decimal, int)> ResenasAsync(ulong idServicio, bool incluirOcultas, CancellationToken ct = default)
    {
        var sets = await LeerVariosAsync<object>(_db, "dbo.pa_listar_resenas",
            new[] { Par("@id_servicio", (long)idServicio), Par("@incluir_ocultas", incluirOcultas) },
            new Func<Microsoft.Data.SqlClient.SqlDataReader, object>[]
            {
                r => new ResenaFila((ulong)r.GetInt64(0), r.GetByte(1), r.IsDBNull(2) ? null : r.GetString(2),
                                    r.GetString(3), r.GetString(4), r.GetDateTime(5), r.GetString(6)),
                r => (r.GetDecimal(0), r.GetInt32(1))
            }, ct);
        var resenas = sets[0].Cast<ResenaFila>().ToList();
        var (prom, total) = sets.Count > 1 && sets[1].Count > 0 ? ((decimal, int))sets[1][0] : (0m, 0);
        return (resenas, prom, total);
    }

    public Task ModerarResenaAsync(ulong idUsuario, ulong idResena, bool ocultar, CancellationToken ct = default) =>
        EjecutarAsync(_db, "dbo.pa_moderar_resena", new[]
            { Par("@id_usuario", (long)idUsuario), Par("@id_resena", (long)idResena), Par("@estado", ocultar ? "OCULTA" : "VISIBLE") }, ct);
}
```
**`Program.cs`** (junto a `AddScoped<ICatalogoService, CatalogoService>()`):
```csharp
builder.Services.AddScoped<IComunidadService, ComunidadService>();
```

---

## 3 · Controladores

```csharp
// Controllers/FavoritosController.cs
using System.Security.Claims;
using FlyVibeMex.Models;
using FlyVibeMex.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlyVibeMex.Controllers;

[Authorize(Roles = Roles.Cliente)]
public class FavoritosController : Controller
{
    private readonly IComunidadService _comunidad;
    public FavoritosController(IComunidadService comunidad) => _comunidad = comunidad;

    private ulong IdUsuario => ulong.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // GET /Favoritos  (la lista; vista en Views/Favoritos/Index.cshtml)
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(await _comunidad.FavoritosAsync(IdUsuario, ct));

    // POST /Favoritos/Alternar  (lo llama fetch desde el corazón; devuelve JSON)
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Alternar(ulong id, CancellationToken ct)
    {
        try { return Json(new { ok = true, favorito = await _comunidad.AlternarFavoritoAsync(IdUsuario, id, ct) }); }
        catch (ReglaProcedimientoException ex) { return BadRequest(new { ok = false, mensaje = ex.Message }); }
    }
}
```
```csharp
// Controllers/ResenasController.cs  — crear (CLIENTE)
[Authorize(Roles = Roles.Cliente)]
public class ResenasController : Controller
{
    private readonly IComunidadService _comunidad;
    public ResenasController(IComunidadService comunidad) => _comunidad = comunidad;

    public class ResenaForm
    {
        public ulong id_servicio { get; set; }
        [System.ComponentModel.DataAnnotations.Range(1, 5, ErrorMessage = "Elige de 1 a 5 estrellas.")]
        public int calificacion { get; set; }
        [System.ComponentModel.DataAnnotations.StringLength(100)] public string? titulo { get; set; }
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Escribe un comentario.")]
        [System.ComponentModel.DataAnnotations.StringLength(1000)] public string comentario { get; set; } = string.Empty;
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Crear(ResenaForm f, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = ModelState.Values.SelectMany(v => v.Errors).First().ErrorMessage;
            return RedirectToAction("Detalle", "Catalogo", new { id = f.id_servicio });
        }
        try
        {
            var id = ulong.Parse(User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier)!);
            await _comunidad.CrearResenaAsync(id, f.id_servicio, f.calificacion, f.titulo, f.comentario, ct);
            TempData["Exito"] = "¡Gracias por tu reseña!";
        }
        catch (ReglaProcedimientoException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction("Detalle", "Catalogo", new { id = f.id_servicio });
    }
}
```
```csharp
// Controllers/AdminResenasController.cs  — moderación (SOPORTE, ADMINISTRADOR, SA)
[Authorize(Roles = Roles.PanelResenas)]
[Route("Admin/Resenas")]
public class AdminResenasController : Controller
{
    private readonly IComunidadService _comunidad;
    public AdminResenasController(IComunidadService comunidad) => _comunidad = comunidad;

    [HttpGet("{idServicio}")]
    public async Task<IActionResult> Index(ulong idServicio, CancellationToken ct)
    {
        var (resenas, promedio, total) = await _comunidad.ResenasAsync(idServicio, incluirOcultas: true, ct);
        ViewBag.IdServicio = idServicio; ViewBag.Promedio = promedio; ViewBag.Total = total;
        return View("~/Views/Admin/Resenas/Index.cshtml", resenas);
    }

    [HttpPost("Moderar"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Moderar(ulong idServicio, ulong idResena, bool ocultar, CancellationToken ct)
    {
        try { await _comunidad.ModerarResenaAsync(ulong.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), idResena, ocultar, ct); }
        catch (ReglaProcedimientoException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index), new { idServicio });
    }
}
```

**`Controllers/CatalogoController.cs`** — en `Detalle`, antes de `return View(vm)`, cargar reseñas y favorito:
```csharp
var esStaff = Roles.Puede(User, Roles.PanelResenas);
var (resenas, promedio, total) = await _comunidad.ResenasAsync(id, incluirOcultas: false, cancellationToken);
ViewBag.Resenas = resenas; ViewBag.Promedio = promedio; ViewBag.TotalResenas = total;
ViewBag.EsFavorito = User.IsInRole(Roles.Cliente) &&
    (await _comunidad.IdsFavoritosAsync(ulong.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), cancellationToken)).Contains(id);
```
(inyecta `IComunidadService _comunidad` en el constructor, igual que `_catalogo`).

---

## 4 · Filtros avanzados (costo, tamaño, alberca) + tipo de habitación

Hoy el catálogo calcula precio y cupo **en memoria** (`CatalogoService`), así que los filtros siguen ese patrón: no hace falta un SP de búsqueda nuevo.

**`Models/Catalogo/CatalogoViewModels.cs`** — en `BusquedaCatalogoViewModel`, junto a `orden`:
```csharp
[Display(Name = "Precio mínimo"), Range(0, 1000000)] public decimal? precio_min { get; set; }
[Display(Name = "Precio máximo"), Range(0, 1000000)] public decimal? precio_max { get; set; }
[Display(Name = "Tamaño mínimo (m²)"), Range(0, 1000)] public decimal? tamano_min { get; set; }
[Display(Name = "Con alberca")] public bool solo_alberca { get; set; }
[Display(Name = "Tipo de habitación")] public short? id_tipo_habitacion { get; set; }
[BindNever, ValidateNever] public List<SelectListItem> TiposHabitacion { get; set; } = new();
```
y en `Validar(...)` (al final):
```csharp
if (precio_min is not null && precio_max is not null && precio_min > precio_max)
    yield return (nameof(precio_max), "El precio máximo debe ser mayor o igual al mínimo.");
```
En `TarjetaServicioViewModel` agrega:
```csharp
public decimal? tamano_m2 { get; set; }
public bool tiene_alberca { get; set; }
public string? tipo_habitacion { get; set; }
public bool es_favorito { get; set; }
```

**`Services/CatalogoService.cs`**

1) En la clase privada `Candidato`:
```csharp
public decimal? tamano_m2 { get; set; }
public bool tiene_alberca { get; set; }
public short? id_tipo_habitacion { get; set; }
```
2) Reemplaza el `from h in ... select new {...}` de `CompletarHospedajesAsync` por (LEFT JOIN al detalle):
```csharp
var hospedajes = await (
        from h in _db.Hospedajes.AsNoTracking()
        join s in servicios on h.id_servicio equals s.id
        join d in _db.HospedajeDetalles.AsNoTracking() on h.id_servicio equals d.id_servicio into dd
        from d in dd.DefaultIfEmpty()
        select new
        {
            h.id_servicio, h.id_ubicacion, h.capacidad_huespedes, h.hora_entrada, h.hora_salida,
            h.tipo_habitacion, h.categoria,
            tamano = d == null ? (decimal?)null : d.tamano_m2,
            alberca = d != null && d.tiene_alberca,
            tipo_id = d == null ? (short?)null : d.id_tipo_habitacion
        })
    .ToDictionaryAsync(h => h.id_servicio, cancellationToken);
```
y dentro del `foreach` agrega:
```csharp
c.tamano_m2 = hosp.tamano; c.tiene_alberca = hosp.alberca; c.id_tipo_habitacion = hosp.tipo_id;
```
3) En `LlenarSeccionAsync`, al armar `filtrados` (antes de `b.BusquedaConFechas = ...`) — solo aplica a hoteles:
```csharp
if (b.tipo == TipoCatalogo.Hospedaje)
{
    filtrados = filtrados
        .Where(c => b.tamano_min is null || (c.tamano_m2 ?? 0) >= b.tamano_min)
        .Where(c => !b.solo_alberca || c.tiene_alberca)
        .Where(c => b.id_tipo_habitacion is null || c.id_tipo_habitacion == b.id_tipo_habitacion)
        .ToList();
    b.TiposHabitacion = await _db.TiposHabitacion.AsNoTracking().Where(t => t.activo).OrderBy(t => t.capacidad)
        .Select(t => new SelectListItem($"{t.nombre_es} ({t.codigo}) · {t.capacidad} pers.", t.id.ToString())).ToListAsync(cancellationToken);
}
```
4) Al crear cada `tarjeta` (donde se asignan nombre, imagen, etc.) agrega: `tamano_m2 = c.tamano_m2, tiene_alberca = c.tiene_alberca, tipo_habitacion = c.tipo_habitacion,`.
5) Filtro de costo (usa el mismo precio que ordena): justo antes de `b.Resultados = Ordenar(tarjetas, b.orden);`
```csharp
static decimal? PrecioDe(TarjetaServicioViewModel t) => t.total ?? t.precio_unitario;
tarjetas = tarjetas
    .Where(t => b.precio_min is null || (PrecioDe(t) is decimal p && p >= b.precio_min))
    .Where(t => b.precio_max is null || (PrecioDe(t) is decimal p2 && p2 <= b.precio_max))
    .ToList();
```
6) Favoritos en las tarjetas: en `HomeController.Hoteles` (y las otras secciones), después de `LlenarSeccionAsync`:
```csharp
if (User.IsInRole(Roles.Cliente))
{
    var favs = await _comunidad.IdsFavoritosAsync(ulong.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), ct);
    foreach (var t in busqueda.Resultados) t.es_favorito = favs.Contains(t.id);
}
```

---

## 5 · Tipos de habitación y de huésped en la reserva

**Cómo encaja con tu modelo:** cada hospedaje ya es *una modalidad de habitación* (SGL, DBL…), así que el tipo de habitación se **elige al buscar/elegir la oferta** (dropdown de la sección 4) y en el resumen se muestra fijo. Lo que sí se captura al reservar son los **tipos de huésped**.

**`Models/Reservas/ReservaViewModels.cs`** — en `PagoFormViewModel`:
```csharp
/// <summary>id de tipos_huesped → cantidad. Llega como huespedes[1]=2&huespedes[3]=1.</summary>
public Dictionary<short, int> huespedes { get; set; } = new();
```
**`Models/Reservas/ReservaViewModels.cs`** — en `ResumenReservaViewModel` (el modelo de `Resumen.cshtml`) agrega:
```csharp
public List<TiposHuesped> TiposHuesped { get; set; } = new();
```
Validación en el servidor (en `ReservacionService`, antes de cobrar):
```csharp
var necesidades = pago.huespedes.Where(x => x.Value > 0).ToDictionary(x => x.Key, x => x.Value);
if (necesidades.Values.Sum() > pago.personas)
    return /* tu forma de devolver error */ "Los huéspedes con necesidades específicas no pueden ser más que las personas.";
```
**`Services/ReservacionService.cs`** — en el bloque (d), guarda la entidad en una variable y después de `SaveChangesAsync` llama al SP (se une a la transacción abierta):
```csharp
var detalle = new DetallesReservacion { /* ...lo mismo que ya tienes... */ };
_db.DetallesReservacion.Add(detalle);
// (tu SaveChangesAsync existente)
await _db.SaveChangesAsync(cancellationToken);

if (necesidades.Count > 0)
    await ProcedimientosBd.EjecutarAsync(_db, "dbo.pa_registrar_huespedes_detalle", new[]
    {
        ProcedimientosBd.Par("@id_usuario", (long)idUsuario),
        ProcedimientosBd.Par("@id_detalle", (long)detalle.id),
        ProcedimientosBd.Par("@huespedes", string.Join(",", necesidades.Select(x => $"{x.Key}:{x.Value}")))
    }, cancellationToken);
```
**Cargar la lista** en `ReservasController.Resumen` (GET y al re-mostrar el POST con errores):
```csharp
modelo.TiposHuesped = await _db.TiposHuesped.AsNoTracking().Where(t => t.activo).OrderBy(t => t.id).ToListAsync(ct);
```

---

## 6 · Razor

Cada bloque trae su **📍 ubicación** (líneas aproximadas de tu proyecto actual) y si **AGREGA** (no borra nada) o **SUSTITUYE**.
Todo es AGREGAR salvo donde se indique lo contrario.

### 6.1 `Views/Home/Hoteles.cshtml`
📍 **AGREGAR entre la línea 63 y la 64**: después del `</div>` del bloque "Ordenar por" (empieza en la 60) y **antes** de `<button type="submit" class="btn btn-search-submit">Buscar hoteles</button>`.
```cshtml
<div class="form-group mb-3">
    <label asp-for="id_tipo_habitacion">Tipo de habitación</label>
    <select asp-for="id_tipo_habitacion" asp-items="Model.TiposHabitacion" class="form-select">
        <option value="">Todas</option>
    </select>
</div>
<div class="row g-2 mb-3">
    <div class="col-6"><label asp-for="precio_min">Precio mín.</label><input asp-for="precio_min" type="number" min="0" step="50" class="form-control" /></div>
    <div class="col-6"><label asp-for="precio_max">Precio máx.</label><input asp-for="precio_max" type="number" min="0" step="50" class="form-control" /></div>
</div>
<div class="form-group mb-3">
    <label asp-for="tamano_min">Tamaño mínimo (m²)</label>
    <input asp-for="tamano_min" type="number" min="0" step="1" class="form-control" />
</div>
<div class="form-check mb-3">
    <input asp-for="solo_alberca" type="checkbox" class="form-check-input" />
    <label asp-for="solo_alberca" class="form-check-label">Solo con alberca</label>
</div>
```

### 6.2 `Views/Home/_ResultadosCatalogo.cshtml`
📍 **AGREGAR entre la línea 68 y la 69**: después de `<p class="card-text">@r.descripcion</p>` y **antes** de `<div class="card-footer-box">`. La variable del `foreach` (línea 42) es `r`.
```cshtml
@if (r.tipo == TipoCatalogo.Hospedaje)
{
    <p class="small text-muted mb-1">
        @r.tipo_habitacion · @r.tamano_m2?.ToString("0.#") m²
        @if (r.tiene_alberca) { <span class="badge text-bg-info">Alberca</span> }
    </p>
}
@if (User.IsInRole(Roles.Cliente))
{
    <button type="button" class="btn btn-sm btn-outline-danger btn-favorito mb-2" data-id="@r.id"
            aria-pressed="@(r.es_favorito ? "true" : "false")">@(r.es_favorito ? "♥" : "♡")</button>
}
```

### 6.3 Script del corazón
📍 **Archivo NUEVO** `wwwroot/assets/js/script_favoritos.js`:
```javascript
document.addEventListener('click', async (e) => {
  const btn = e.target.closest('.btn-favorito'); if (!btn) return;
  const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
  const r = await fetch('/Favoritos/Alternar', {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'RequestVerificationToken': token ?? '' },
    body: 'id=' + encodeURIComponent(btn.dataset.id)
  });
  if (!r.ok) { alert((await r.json()).mensaje ?? 'No se pudo guardar.'); return; }
  const { favorito } = await r.json();
  btn.textContent = favorito ? '♥' : '♡';
  btn.setAttribute('aria-pressed', favorito);
});
```
📍 **`Views/Shared/_Layout.cshtml`, AGREGAR 2 cosas:**
1. Después de `<partial name="_Mensajes" />` (línea ~72): `@Html.AntiForgeryToken()`
2. **Justo antes** de `@await RenderSectionAsync("Scripts", required: false)` (línea ~123):
```cshtml
<script src="~/assets/js/script_favoritos.js" asp-append-version="true"></script>
```

### 6.4 `Views/Reservas/Resumen.cshtml`
📍 **AGREGAR entre la línea 168 y la 170**: después de `<input type="hidden" name="total_confirmado" ...>` (línea 168) y **antes** del `<div class="mb-3">` de "Método de pago" (línea 170), **dentro** del `<form asp-action="Pagar">` (empieza en la 160). Solo para hoteles. El modelo de esta vista es `ResumenReservaViewModel`, por eso se usa `Model.Solicitud`.
```cshtml
@if (srv.tipo_servicio == TipoCatalogo.Hospedaje)
{
    <fieldset class="mb-3">
        <legend class="h6">¿Algún huésped requiere atención especial?</legend>
        @foreach (var t in Model.TiposHuesped)
        {
            <div class="d-flex align-items-center gap-2 mb-1">
                <input type="number" name="huespedes[@t.id]" value="@(s.huespedes.GetValueOrDefault(t.id))"
                       min="0" max="@s.personas" class="form-control form-control-sm" style="width:5rem" />
                <label class="mb-0">@t.nombre <small class="text-muted">— @t.descripcion</small></label>
            </div>
        }
    </fieldset>
}
```
(El tipo de habitación ya se ve en el nombre del servicio, línea 69; no hace falta repetirlo. Si `srv.tipo_servicio` no existe con ese nombre en tu `Servicio` del resumen, usa la propiedad equivalente que trae el tipo.)

### 6.5 `Views/Catalogo/Detalle.cshtml`
📍 **AGREGAR al final del archivo, justo ANTES del último `</div>`** (línea ~240, el que cierra el contenedor principal; es decir, después del `</div>` de la línea 239).
```cshtml
<section class="mt-5" id="resenas">
    <h2 class="h4">Reseñas <small class="text-muted">@(((decimal)ViewBag.Promedio).ToString("0.0")) ★ · @ViewBag.TotalResenas</small></h2>

    @if (User.IsInRole(Roles.Cliente))
    {
        <form asp-controller="Resenas" asp-action="Crear" method="post" class="card card-body mb-4">
            <input type="hidden" name="id_servicio" value="@Model.id" />
            <label class="form-label">Calificación</label>
            <select name="calificacion" class="form-select mb-2" required>
                @for (var i = 5; i >= 1; i--) { <option value="@i">@(new string('★', i)) (@i)</option> }
            </select>
            <input name="titulo" class="form-control mb-2" maxlength="100" placeholder="Título (opcional)" />
            <textarea name="comentario" class="form-control mb-2" maxlength="1000" rows="3" required placeholder="Cuéntanos tu experiencia"></textarea>
            <button class="btn btn-primary align-self-start">Publicar reseña</button>
        </form>
    }

    @foreach (var r in (List<ResenaFila>)ViewBag.Resenas)
    {
        <article class="border-bottom py-3">
            <strong>@r.autor</strong> <span class="text-warning">@(new string('★', r.calificacion))</span>
            <small class="text-muted">@r.creada_en.ToString("dd/MM/yyyy")</small>
            @if (!string.IsNullOrWhiteSpace(r.titulo)) { <h3 class="h6 mt-1">@r.titulo</h3> }
            <p class="mb-0">@r.comentario</p>
        </article>
    }
    @if (Roles.Puede(User, Roles.PanelResenas))
    {
        <a class="btn btn-sm btn-outline-secondary mt-3" href="/Admin/Resenas/@Model.id">Moderar reseñas</a>
    }
</section>
```
Los mensajes (`TempData["Exito"]` / `["Error"]`) ya los pinta `_Mensajes` del layout. Razor escapa el HTML: no uses `Html.Raw` con comentarios de usuarios.

### 6.6 `Views/Admin/Resenas/Index.cshtml`
📍 **Archivo NUEVO** (crea la carpeta `Resenas`; hereda `_AdminLayout` de `Views/Admin/_ViewStart.cshtml`).
```cshtml
@model List<ResenaFila>
<h1 class="h4">Reseñas del servicio #@ViewBag.IdServicio — @(((decimal)ViewBag.Promedio).ToString("0.0")) ★ (@ViewBag.Total visibles)</h1>
<table class="table align-middle">
    <thead><tr><th>Autor</th><th>★</th><th>Comentario</th><th>Estado</th><th></th></tr></thead>
    <tbody>
    @foreach (var r in Model)
    {
        <tr class="@(r.estado == "OCULTA" ? "table-secondary" : "")">
            <td>@r.autor<br /><small>@r.creada_en.ToString("dd/MM/yyyy")</small></td>
            <td>@r.calificacion</td>
            <td>@r.titulo @r.comentario</td>
            <td>@r.estado</td>
            <td>
                <form asp-controller="AdminResenas" asp-action="Moderar" method="post">
                    <input type="hidden" name="idServicio" value="@ViewBag.IdServicio" />
                    <input type="hidden" name="idResena" value="@r.id" />
                    <input type="hidden" name="ocultar" value="@(r.estado == "VISIBLE" ? "true" : "false")" />
                    <button class="btn btn-sm btn-outline-danger">@(r.estado == "VISIBLE" ? "Ocultar" : "Mostrar")</button>
                </form>
            </td>
        </tr>
    }
    </tbody>
</table>
```

### 6.7 Gestor de ventas: `Views/Admin/GestorVentas/_FormHospedaje.cshtml`
**No sustituyas nada** (el campo de texto `tipo_habitacion`, líneas 18-22, y `capacidad_huespedes`, líneas 30-35, siguen siendo obligatorios en tu `HospedajeFormViewModel`; el procedimiento los sobrescribe con los del tipo elegido).

📍 **1) AGREGAR en la línea 6**, después de `var ubicaciones = ...;` (dentro del bloque `@{ }`):
```cshtml
var tipos = ViewData["TiposHabitacion"] as List<SelectListItem> ?? new List<SelectListItem>();
```
📍 **2) AGREGAR entre la línea 47 y la 48**: después del `</div>` de `hora_salida` y **antes** del `</div>` que cierra el `row g-3` (línea 48):
```cshtml
    <div class="col-md-6">
        <label asp-for="id_tipo_habitacion" class="form-label"></label>
        <select asp-for="id_tipo_habitacion" asp-items="tipos" class="form-select" required>
            <option value="">— Selecciona —</option>
        </select>
        <span asp-validation-for="id_tipo_habitacion" class="text-danger small"></span>
    </div>
    <div class="col-md-3">
        <label asp-for="tamano_m2" class="form-label"></label>
        <input asp-for="tamano_m2" type="number" min="1" step="0.5" class="form-control" required />
        <span asp-validation-for="tamano_m2" class="text-danger small"></span>
    </div>
    <div class="col-md-3 d-flex align-items-end">
        <div class="form-check">
            <input asp-for="tiene_alberca" type="checkbox" class="form-check-input" />
            <label asp-for="tiene_alberca" class="form-check-label">Tiene alberca</label>
        </div>
    </div>
```
📍 **3) `Models/Admin/GestorViewModels.cs`, línea ~246**, dentro de `HospedajeFormViewModel` (AGREGAR propiedades):
```csharp
[Display(Name = "Tipo de habitación (catálogo)"), Required(ErrorMessage = "Elige un tipo.")]
public short? id_tipo_habitacion { get; set; }
[Display(Name = "Tamaño (m²)"), Range(1, 1000, ErrorMessage = "Entre 1 y 1000.")]
public decimal tamano_m2 { get; set; }
[Display(Name = "Tiene alberca")]
public bool tiene_alberca { get; set; }
```
Y en `GestorController`, donde ya llenas `ViewData["Ubicaciones"]` para este formulario, **AGREGAR** (misma lógica en alta y edición):
```csharp
ViewData["TiposHabitacion"] = await _db.TiposHabitacion.AsNoTracking().Where(t => t.activo).OrderBy(t => t.capacidad)
    .Select(t => new SelectListItem($"{t.nombre_es} ({t.codigo}) · {t.capacidad} pers.", t.id.ToString())).ToListAsync(ct);
```
La llamada a `pa_guardar_hospedaje_detalle` va **después** del alta/edición del hospedaje (ya está en el bloque de C# de la sección 5 de la versión anterior; el código es este):
```csharp
await ProcedimientosBd.EjecutarAsync(_db, "dbo.pa_guardar_hospedaje_detalle", new[]
{
    ProcedimientosBd.Par("@id_usuario", (long)IdUsuario),      // el helper que ya usa GestorController (línea ~186)
    ProcedimientosBd.Par("@id_servicio", (long)idServicio),
    ProcedimientosBd.Par("@id_tipo_habitacion", form.id_tipo_habitacion),
    ProcedimientosBd.Par("@tiene_alberca", form.tiene_alberca),
    ProcedimientosBd.Par("@tamano_m2", form.tamano_m2)
}, ct);
```

---

## 7 · Qué ve cada rol

| Función | CLIENTE | SOPORTE (ticket man) | ADMINISTRADOR / gestor | SA |
|---|---|---|---|---|
| Filtros, favoritos (♥), crear reseña | ✔ | solo filtros | solo filtros | solo filtros |
| Moderar reseñas (`/Admin/Resenas/{id}`) | ✘ | ✔ | ✔ | ✔ |
| Editar tipo/tamaño/alberca del hotel | ✘ | ✘ | ✔ (`Roles.PanelGestor`) | ✔ |
| Catálogos `tipos_habitacion` / `tipos_huesped` | ✘ | ✘ | ✘ | ✔ (agrégalos a `SAController.Catalogos.cs`) |

Atajos en Razor (sin strings mágicos):
```cshtml
@if (User.IsInRole(Roles.Cliente)) { /* ♥ y formulario de reseña */ }
@if (Roles.Puede(User, Roles.PanelResenas)) { /* enlace Moderar */ }
@if (Roles.Puede(User, Roles.PanelGestor)) { /* campos de hotel */ }
```
Menú del panel: en `Views/Admin/_AdminLayout.cshtml`, el enlace a reseñas va dentro de `@if (Roles.Puede(User, Roles.PanelResenas))`.

---

## 8 · No vitales (guía breve)

- **Recordarme:** ya está medio hecho: `HomeController` usa `IsPersistent = datos.recordarme`. Solo verifica que `_FormSesion.cshtml` tenga `<input type="checkbox" asp-for="recordarme">` y que en `Program.cs` la cookie tenga `ExpireTimeSpan` (p. ej. 14 días) y `SlidingExpiration = true`.
- **Calendario de disponibilidad:** ya tienes los datos en `disponibilidad_servicio` (cupo y `precio_fecha` por día). Haz un endpoint `GET /Catalogo/Disponibilidad/{id}?mes=2026-12` que devuelva `[{fecha, disponible, precio}]` y píntalo en el Detalle con una cuadrícula de 7 columnas (o la librería flatpickr con `enable`/`disable`). Días con `disponible = 0` en gris.
- **Lista de favoritos (UI):** `Views/Favoritos/Index.cshtml` con `@model List<FavoritoFila>`: tarjetas con imagen, nombre, precio_base y un botón `.btn-favorito` para quitar; enlace desde `_CuentaNav.cshtml`.
- **Favoritos de la otra integración:** `favoritos` solo guarda servicios locales (FK a `servicios`). Para guardar viajes del otro equipo habría que añadir `origen` + `id_externo` (texto) y quitar esa FK; dime si lo quieres.
