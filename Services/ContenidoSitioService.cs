using FlyVibeMex.Data;
using FlyVibeMex.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FlyVibeMex.Services;

/// <summary>Contenido del sitio ya resuelto (lo guardado por el SA o, si no hay, el texto por defecto).</summary>
public sealed class ContenidoPublico
{
    private readonly Dictionary<string, ContenidoSitio> _guardado;

    public ContenidoPublico(Dictionary<string, ContenidoSitio> guardado)
    {
        _guardado = guardado;
    }

    /// <summary>Texto de una clave (texto plano: en la vista se pinta con Razor, que lo codifica, y white-space: pre-line).</summary>
    public string Texto(string clave) =>
        _guardado.TryGetValue(clave, out var c) && c.texto is not null ? c.texto : ContenidoSitioClaves.Buscar(clave)?.Defecto ?? string.Empty;

    /// <summary>Imagen de una clave: la que subió el SA o la de por defecto.</summary>
    public string Imagen(string clave) =>
        _guardado.TryGetValue(clave, out var c) && c.imagen_ruta is not null ? c.imagen_ruta : ContenidoSitioClaves.Buscar(clave)?.Defecto ?? string.Empty;
}

/// <summary>
/// Contenido editable del sitio (tabla <c>contenido_sitio</c>, T4 fase D). El sitio público lo lee con
/// <see cref="ObtenerAsync"/> (en caché 10 minutos; cada cambio del SA la borra) y el panel SA lo edita.
/// </summary>
public interface IContenidoSitioService
{
    Task<ContenidoPublico> ObtenerAsync(CancellationToken cancellationToken = default);

    Task<ContenidoSeccionViewModel> SeccionAsync(string? seccion, CancellationToken cancellationToken = default);

    Task<ResultadoOperacion> GuardarTextoAsync(string? clave, string? texto, ulong idSA, CancellationToken cancellationToken = default);

    Task<ResultadoOperacion> GuardarImagenAsync(string? clave, IFormFile? imagen, ulong idSA, CancellationToken cancellationToken = default);

    /// <summary>Borra lo personalizado: vuelve el texto (o la imagen) por defecto.</summary>
    Task<ResultadoOperacion> RestaurarAsync(string? clave, ulong idSA, CancellationToken cancellationToken = default);
}

/// <summary>Implementación de <see cref="IContenidoSitioService"/> con EF Core, caché en memoria y bitácora.</summary>
public sealed class ContenidoSitioService : IContenidoSitioService
{
    private const string LlaveCache = "contenido_sitio";

    private readonly FlyVibeMexDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IAlmacenImagenes _imagenes;
    private readonly ILogger<ContenidoSitioService> _logger;

    public ContenidoSitioService(FlyVibeMexDbContext db, IMemoryCache cache, IAlmacenImagenes imagenes, ILogger<ContenidoSitioService> logger)
    {
        _db = db;
        _cache = cache;
        _imagenes = imagenes;
        _logger = logger;
    }

    public async Task<ContenidoPublico> ObtenerAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(LlaveCache, out ContenidoPublico? enCache) && enCache is not null) return enCache;

        Dictionary<string, ContenidoSitio> guardado;
        try
        {
            guardado = await _db.ContenidoSitio.AsNoTracking().ToDictionaryAsync(c => c.clave, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sin BD (o sin la tabla) el sitio sigue funcionando con los textos por defecto. No se guarda en caché.
            _logger.LogWarning(ex, "No se pudo leer contenido_sitio; se usan los textos por defecto.");
            return new ContenidoPublico(new Dictionary<string, ContenidoSitio>());
        }

        var contenido = new ContenidoPublico(guardado);
        _cache.Set(LlaveCache, contenido, TimeSpan.FromMinutes(10));
        return contenido;
    }

    public async Task<ContenidoSeccionViewModel> SeccionAsync(string? seccion, CancellationToken cancellationToken = default)
    {
        seccion = ContenidoSeccionViewModel.Secciones.Any(s => s.valor == seccion) ? seccion! : ContenidoSitioClaves.SeccionInicio;

        var guardado = await _db.ContenidoSitio.AsNoTracking().ToDictionaryAsync(c => c.clave, cancellationToken);
        var autores = new Dictionary<ulong, string>();
        foreach (var id in guardado.Values.Where(c => c.id_usuario is not null).Select(c => c.id_usuario!.Value).Distinct())
            autores[id] = await ConversacionSoporte.NombreAsync(_db, id, cancellationToken) ?? $"Usuario {id}";

        return new ContenidoSeccionViewModel
        {
            Seccion = seccion,
            Campos = ContenidoSitioClaves.Todas.Where(d => d.Seccion == seccion).Select(d =>
            {
                guardado.TryGetValue(d.Clave, out var c);
                return new ContenidoCampoViewModel
                {
                    Definicion = d,
                    texto = c?.texto,
                    imagen_ruta = c?.imagen_ruta,
                    actualizado_en = c?.actualizado_en,
                    actualizado_por = c?.id_usuario is ulong a ? autores.GetValueOrDefault(a) : null
                };
            }).ToList()
        };
    }

    public async Task<ResultadoOperacion> GuardarTextoAsync(string? clave, string? texto, ulong idSA, CancellationToken cancellationToken = default)
    {
        var definicion = ContenidoSitioClaves.Buscar(clave);
        if (definicion is null || definicion.EsImagen) return ResultadoOperacion.Falla("Ese campo no existe.");

        // Saltos de línea de Windows → \n; sin espacios al inicio/fin. Nunca se interpreta como HTML.
        texto = texto?.Replace("\r\n", "\n").Trim();
        if (string.IsNullOrEmpty(texto))
            return ResultadoOperacion.Falla($"\"{definicion.Etiqueta}\" no puede quedar vacío. Usa \"Restaurar el original\" si quieres el texto por defecto.");
        if (!definicion.Multilinea) texto = texto.Replace("\n", " ");
        if (texto.Length > definicion.Maximo)
            return ResultadoOperacion.Falla($"\"{definicion.Etiqueta}\" puede tener máximo {definicion.Maximo} caracteres (tiene {texto.Length}).");

        // T7.3: el contacto del footer se vuelve enlace (mailto: / tel:), así que debe tener un formato válido.
        if (definicion.Clave == ContenidoSitioClaves.PieCorreo
            && !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(texto))
            return ResultadoOperacion.Falla("Escribe un correo válido, por ejemplo contacto@flyvibemex.com.");
        if (definicion.Clave == ContenidoSitioClaves.PieTelefono
            && ContenidoSitioClaves.SoloDigitosTelefono(texto).TrimStart('+').Length is < 8 or > 15)
            return ResultadoOperacion.Falla("Escribe un teléfono de 8 a 15 dígitos (puedes usar espacios, guiones o un + al inicio).");

        // Si es igual al de por defecto, no hace falta guardarlo: se restaura.
        if (texto == definicion.Defecto) return await RestaurarAsync(clave, idSA, cancellationToken);

        var textoFinal = texto;
        try
        {
            await GestorBd.EnTransaccionAsync(_db, async () =>
            {
                var fila = await _db.ContenidoSitio.FirstOrDefaultAsync(c => c.clave == definicion.Clave, cancellationToken);
                if (fila is null)
                {
                    fila = new ContenidoSitio { clave = definicion.Clave };
                    _db.ContenidoSitio.Add(fila);
                }
                fila.texto = textoFinal;
                fila.actualizado_en = DateTime.Now;
                fila.id_usuario = idSA;
                _db.RegistrarBitacora(idSA, "ACTUALIZAR", "contenido_sitio", null,
                    $"{definicion.Clave}: {(textoFinal.Length > 300 ? textoFinal[..300] + "…" : textoFinal)}");
                await _db.SaveChangesAsync(cancellationToken);
            }, cancellationToken);
        }
        catch (Exception ex) when (GestorBd.EsErrorDeBd(ex))
        {
            _logger.LogError(ex, "No se pudo guardar el contenido {Clave}.", definicion.Clave);
            return ResultadoOperacion.Falla("No se pudo guardar el texto. Intenta de nuevo.");
        }

        _cache.Remove(LlaveCache);
        return ResultadoOperacion.Ok($"Se guardó \"{definicion.Etiqueta}\". Ya se ve en el sitio.");
    }

    public async Task<ResultadoOperacion> GuardarImagenAsync(string? clave, IFormFile? imagen, ulong idSA, CancellationToken cancellationToken = default)
    {
        var definicion = ContenidoSitioClaves.Buscar(clave);
        if (definicion is null || !definicion.EsImagen) return ResultadoOperacion.Falla("Ese campo no existe.");
        if (imagen is null || imagen.Length == 0) return ResultadoOperacion.Falla("Elige una imagen.");
        var error = _imagenes.Validar(imagen);
        if (error is not null) return ResultadoOperacion.Falla(error);

        string? nueva = null;
        string? anterior = null;
        try
        {
            nueva = await _imagenes.GuardarAsync(imagen, CarpetaImagen.Contenido, cancellationToken);
            await GestorBd.EnTransaccionAsync(_db, async () =>
            {
                var fila = await _db.ContenidoSitio.FirstOrDefaultAsync(c => c.clave == definicion.Clave, cancellationToken);
                if (fila is null)
                {
                    fila = new ContenidoSitio { clave = definicion.Clave };
                    _db.ContenidoSitio.Add(fila);
                }
                anterior = fila.imagen_ruta;
                fila.imagen_ruta = nueva;
                fila.actualizado_en = DateTime.Now;
                fila.id_usuario = idSA;
                _db.RegistrarBitacora(idSA, "ACTUALIZAR", "contenido_sitio", null, $"{definicion.Clave}: imagen {nueva}");
                await _db.SaveChangesAsync(cancellationToken);
            }, cancellationToken);
        }
        catch (Exception ex) when (GestorBd.EsErrorDeBd(ex) || ex is IOException)
        {
            _imagenes.Eliminar(nueva);
            _logger.LogError(ex, "No se pudo guardar la imagen de {Clave}.", definicion.Clave);
            return ResultadoOperacion.Falla("No se pudo guardar la imagen. Intenta de nuevo.");
        }

        if (anterior is not null && anterior != nueva) _imagenes.Eliminar(anterior);
        _cache.Remove(LlaveCache);
        return ResultadoOperacion.Ok($"Se cambió \"{definicion.Etiqueta}\". Ya se ve en el sitio.");
    }

    public async Task<ResultadoOperacion> RestaurarAsync(string? clave, ulong idSA, CancellationToken cancellationToken = default)
    {
        var definicion = ContenidoSitioClaves.Buscar(clave);
        if (definicion is null) return ResultadoOperacion.Falla("Ese campo no existe.");

        string? imagen = null;
        var habia = false;
        try
        {
            await GestorBd.EnTransaccionAsync(_db, async () =>
            {
                var fila = await _db.ContenidoSitio.AsNoTracking().FirstOrDefaultAsync(c => c.clave == definicion.Clave, cancellationToken);
                habia = fila is not null;
                if (fila is null) return;
                imagen = fila.imagen_ruta;
                await _db.ContenidoSitio.Where(c => c.clave == definicion.Clave).ExecuteDeleteAsync(cancellationToken);
                _db.RegistrarBitacora(idSA, "RESTAURAR", "contenido_sitio", null, $"{definicion.Clave}: vuelve el contenido original.");
                await _db.SaveChangesAsync(cancellationToken);
            }, cancellationToken);
        }
        catch (Exception ex) when (GestorBd.EsErrorDeBd(ex))
        {
            _logger.LogError(ex, "No se pudo restaurar el contenido {Clave}.", definicion.Clave);
            return ResultadoOperacion.Falla("No se pudo restaurar el contenido. Intenta de nuevo.");
        }

        _imagenes.Eliminar(imagen);
        _cache.Remove(LlaveCache);
        return ResultadoOperacion.Ok(habia
            ? $"\"{definicion.Etiqueta}\" volvió al original."
            : $"\"{definicion.Etiqueta}\" ya tenía el contenido original.");
    }
}
