using System.Text;
using FlyVibeMex.Models;
using Microsoft.Extensions.Caching.Memory;

namespace FlyVibeMex.Bot;

/// <summary>
/// Texto libre fuera de un flujo: un saludo muestra el menú; cualquier otra cosa se busca en el catálogo
/// ("cancún", "hotel playa", "renta de auto en monterrey").
/// </summary>
/// <remarks>
/// La búsqueda usa los MISMOS resultados que las secciones del sitio (<see cref="Services.ICatalogoService.LlenarSeccionAsync"/>:
/// activos, proveedor activo, cupo de los próximos 30 días) y los filtra en memoria por nombre, ubicación y tipo,
/// sin importar acentos ni mayúsculas. Esos resultados se guardan 60 segundos para no repetir las 5 consultas
/// en cada búsqueda. No hay SQL nuevo ni se registra el texto buscado.
/// </remarks>
public sealed partial class ManejadorActualizaciones
{
    private const int MaximoResultadosBusqueda = 8;
    private const string LlaveCacheBusqueda = "tg:catalogo-busqueda";
    private static readonly TimeSpan VigenciaCacheBusqueda = TimeSpan.FromSeconds(60);

    /// <summary>Servicio del catálogo con su texto de búsqueda ya normalizado.</summary>
    private sealed record ServicioBuscable(TarjetaServicioViewModel Tarjeta, string Texto);

    private static readonly HashSet<string> Saludos = new(StringComparer.Ordinal)
    {
        "hola", "holi", "hey", "hi", "hello", "buenas", "buen dia", "buenos dias", "buenas tardes", "buenas noches",
        "que tal", "hola que tal", "gracias", "muchas gracias", "ok", "okay", "va", "inicio", "menu", "empezar", "start"
    };

    private static readonly HashSet<string> PalabrasVacias = new(StringComparer.Ordinal)
    {
        "a", "al", "de", "del", "el", "la", "los", "las", "lo", "un", "una", "unos", "unas", "en", "y", "o", "para", "por",
        "con", "que", "me", "mi", "mis", "hay", "algo", "quiero", "quisiera", "busco", "buscar", "ver", "tienen", "tienes",
        "renta", "rentar", "reservar", "precio", "precios", "barato", "baratos", "favor"
    };

    /// <summary>Palabras extra por tipo, para que "auto", "avión" o "excursión" también encuentren su sección.</summary>
    private static string SinonimosTipo(string tipo) => tipo switch
    {
        TipoCatalogo.Vuelo => "vuelo vuelos avion aviones boleto boletos",
        TipoCatalogo.Hospedaje => "hotel hoteles hospedaje hospedajes habitacion cuarto",
        TipoCatalogo.Vehiculo => "vehiculo vehiculos auto autos carro carros coche coches camioneta",
        TipoCatalogo.Evento => "tour tours evento eventos excursion excursiones actividad actividades",
        _ => "paquete paquetes"
    };

    private async Task TextoLibreAsync(ContextoBot ctx, string texto, CancellationToken ct)
    {
        var normal = FormatoBot.SinAcentos(texto).Trim(' ', '.', ',', ';', '!', '¡', '?', '¿', ':');
        if (normal == "ayuda")
        {
            await AyudaAsync(ctx, ct);
            return;
        }
        if (Saludos.Contains(normal))
        {
            await MenuAsync(ctx, ct);
            return;
        }

        var palabras = normal
            .Split(new[] { ' ', ',', '.', ';', ':', '-', '/', '¿', '?', '¡', '!', '"', '\'' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.Length >= 2 && !PalabrasVacias.Contains(p))
            .Distinct()
            .Take(6)
            .ToList();
        if (palabras.Sum(p => p.Length) < 3)
        {
            await MostrarAsync(ctx,
                "Escríbeme al menos 3 letras de un destino o un servicio, por ejemplo «Cancún» o «hotel playa».",
                TecladoMenu(ctx), ct);
            return;
        }

        var catalogo = await CatalogoParaBuscarAsync(ct);
        var encontrados = catalogo.Where(s => palabras.All(p => s.Texto.Contains(p, StringComparison.Ordinal)))
            .Take(MaximoResultadosBusqueda + 1)
            .ToList();
        var hayMas = encontrados.Count > MaximoResultadosBusqueda;
        if (hayMas) encontrados.RemoveAt(encontrados.Count - 1);

        var buscado = FormatoBot.HtmlRecortado(texto, 40);
        var sb = new StringBuilder().Linea(FormatoBot.Ruta(RutaExplorar, "Búsqueda")).Linea();
        var teclado = new Teclado();

        if (encontrados.Count == 0)
        {
            sb.Linea($"No encontré nada con «{buscado}» 🤔")
              .Linea()
              .Linea("Prueba con una ciudad («Cancún»), un tipo de servicio («hotel», «vuelo», «tour») o explora por sección.")
              .Linea("Si lo que necesitas es ayuda con una reservación, entra a Soporte.");
            teclado.Fila(("🔎 Explorar", "exp"), ("💬 Soporte", "sop"));
            teclado.Navegacion();
            await MostrarAsync(ctx, sb.ToString(), teclado, ct);
            return;
        }

        sb.Linea($"<b>Resultados para «{buscado}»</b> ({encontrados.Count}{(hayMas ? "+" : "")})");
        var n = 0;
        foreach (var s in encontrados)
        {
            n++;
            sb.Linea();
            LineasServicio(sb, n, s.Tarjeta, mostrarSeccion: true);
            teclado.Boton($"{n}. {FormatoBot.Recortar(s.Tarjeta.nombre, 40)}", $"srv:{s.Tarjeta.id}:0:1");
        }
        if (hayMas)
            sb.Linea().Linea("<i>Hay más resultados: escribe algo más específico (por ejemplo «hotel Cancún») o explora por sección.</i>");

        teclado.Boton("🔎 Explorar por sección", "exp").Navegacion();
        await MostrarAsync(ctx, sb.ToString(), teclado, ct);
    }

    /// <summary>Lo que se puede buscar: las 5 secciones del catálogo (guardado 60 s).</summary>
    private async Task<List<ServicioBuscable>> CatalogoParaBuscarAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(LlaveCacheBusqueda, out List<ServicioBuscable>? guardado) && guardado is not null)
            return guardado;

        var lista = new List<ServicioBuscable>();
        foreach (var tipo in TiposCatalogo)   // en orden, una por una (el DbContext no admite consultas en paralelo)
        {
            var busqueda = new BusquedaCatalogoViewModel { tipo = tipo };
            await _catalogo.LlenarSeccionAsync(busqueda, aplicarFechas: false, ct);
            foreach (var r in busqueda.Resultados)
            {
                var texto = FormatoBot.SinAcentos($"{r.nombre} {r.ubicacion} {r.descripcion} {SinonimosTipo(r.tipo)}");
                lista.Add(new ServicioBuscable(r, texto));
            }
        }

        _cache.Set(LlaveCacheBusqueda, lista, VigenciaCacheBusqueda);
        return lista;
    }
}
