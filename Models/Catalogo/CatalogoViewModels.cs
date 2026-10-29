using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace FlyVibeMex.Models
{
    // =============================================================================================
    // Catálogo público: búsqueda por sección, tarjetas de resultados, detalle y cotización.
    // Lo usan HomeController (secciones), CatalogoController (detalle) y Services/CatalogoService.cs.
    // =============================================================================================

    /// <summary>Tipos de servicio que se buscan y reservan en el sitio público, con sus textos por tipo.</summary>
    public static class TipoCatalogo
    {
        public const string Vuelo = "VUELO";
        public const string Hospedaje = "HOSPEDAJE";
        public const string Vehiculo = "VEHICULO";
        public const string Paquete = "PAQUETE";
        public const string Evento = "EVENTO";   // la sección se llama "Tours"

        public static bool EsValido(string? tipo) => tipo is Vuelo or Hospedaje or Vehiculo or Paquete or Evento;

        /// <summary>Hospedaje y Vehículo se buscan por rango (desde - hasta); los demás por una sola fecha.</summary>
        public static bool UsaRango(string tipo) => tipo is Hospedaje or Vehiculo;

        /// <summary>Unidad del precio: "noche", "día", "asiento" o "persona".</summary>
        public static string Unidad(string tipo) => tipo switch
        {
            Hospedaje => "noche",
            Vehiculo => "día",
            Vuelo => "asiento",
            _ => "persona"
        };

        /// <summary>Nombre de la sección del menú.</summary>
        public static string NombreSeccion(string tipo) => tipo switch
        {
            Vuelo => "Vuelos",
            Hospedaje => "Hoteles",
            Vehiculo => "Vehículos",
            Paquete => "Paquetes",
            _ => "Tours"
        };

        /// <summary>Acción de HomeController que muestra la sección (para los enlaces "volver").</summary>
        public static string AccionSeccion(string tipo) => tipo switch
        {
            Vuelo => "Viajes",
            Hospedaje => "Hoteles",
            Vehiculo => "vehiculos",
            Paquete => "Paquetes",
            _ => "Tours"
        };

        /// <summary>Nombre en singular (lo usa la lista de servicios incluidos en un paquete).</summary>
        public static string NombreTipo(string tipo) => tipo switch
        {
            Vuelo => "Vuelo",
            Hospedaje => "Hospedaje",
            Vehiculo => "Vehículo",
            Paquete => "Paquete",
            Evento => "Tour",
            _ => tipo
        };

        public static string EtiquetaDesde(string tipo) => tipo switch
        {
            Hospedaje => "Fecha de entrada",
            Vehiculo => "Fecha de retiro",
            Vuelo => "Fecha de salida (±3 días)",
            Paquete => "Fecha de inicio",
            _ => "Fecha"
        };

        public static string EtiquetaHasta(string tipo) => tipo == Hospedaje ? "Fecha de salida" : "Fecha de devolución";

        public static string EtiquetaPersonas(string tipo) => tipo switch
        {
            Hospedaje => "Huéspedes",
            Vuelo or Vehiculo => "Pasajeros",
            _ => "Personas"
        };
    }

    /// <summary>Orden de los resultados (selector "Ordenar por").</summary>
    public enum OrdenCatalogo
    {
        [Display(Name = "Menor precio")] PrecioAsc,
        [Display(Name = "Mayor precio")] PrecioDesc,
        [Display(Name = "Nombre")] Nombre
    }

    /// <summary>
    /// Modelo de las 5 secciones del catálogo (Vuelos, Hoteles, Vehículos, Tours y Paquetes) y del detalle.
    /// Los campos en minúsculas vienen del formulario GET (?destino=2&amp;desde=2026-11-02...); los que tienen
    /// [BindNever] los llena el servidor. Así los &lt;input asp-for="desde"&gt; generan name="desde" (URL corta).
    /// </summary>
    public class BusquedaCatalogoViewModel
    {
        public const int MaxPersonas = 20;
        public const int MaxHabitaciones = 10;
        public const int MaxDiasRango = 30;          // estancia / renta más larga que se puede buscar
        public const int DiasFlexiblesVuelo = 3;     // los vuelos se buscan ±3 días alrededor de la fecha
        public const int DiasSinBusqueda = 30;       // sin fechas se muestra lo disponible en los próximos 30 días
        public const int MaxDiasAnticipacion = 365;

        [Display(Name = "Destino")]
        public ulong? destino { get; set; }

        /// <summary>Solo Vuelos.</summary>
        [Display(Name = "Origen")]
        public ulong? origen { get; set; }

        [Display(Name = "Fecha")]
        [DataType(DataType.Date)]
        public DateOnly? desde { get; set; }

        /// <summary>Solo Hoteles (salida) y Vehículos (devolución).</summary>
        [Display(Name = "Fecha final")]
        [DataType(DataType.Date)]
        public DateOnly? hasta { get; set; }

        [Display(Name = "Personas")]
        [Range(1, MaxPersonas, ErrorMessage = "Debe ser entre 1 y 20.")]
        public int personas { get; set; } = 1;

        /// <summary>Solo Hoteles.</summary>
        [Display(Name = "Habitaciones")]
        [Range(1, MaxHabitaciones, ErrorMessage = "Debe ser entre 1 y 10.")]
        public int habitaciones { get; set; } = 1;

        [Display(Name = "Ordenar por")]
        public OrdenCatalogo orden { get; set; } = OrdenCatalogo.PrecioAsc;

        // ---------------- Lo llena el servidor ----------------

        /// <summary>VUELO, HOSPEDAJE, VEHICULO, PAQUETE o EVENTO (lo asigna la acción, nunca el formulario).</summary>
        // [ValidateNever]: MVC trata los string no anulables como [Required] aunque tengan [BindNever]; sin esto,
        // "tipo" (vacío al enlazar el formulario) haría que ModelState.IsValid fuera siempre false.
        [BindNever, ValidateNever] public string tipo { get; set; } = string.Empty;
        [BindNever, ValidateNever] public List<SelectListItem> Destinos { get; set; } = new();
        [BindNever, ValidateNever] public List<SelectListItem> Origenes { get; set; } = new();
        [BindNever, ValidateNever] public List<TarjetaServicioViewModel> Resultados { get; set; } = new();

        /// <summary><c>true</c> si se buscó con fechas válidas (lo asigna CatalogoService).</summary>
        [BindNever, ValidateNever] public bool BusquedaConFechas { get; set; }

        /// <summary>
        /// Reglas que dependen del tipo (las DataAnnotations no lo conocen). La acción agrega cada error a ModelState.
        /// </summary>
        public IEnumerable<(string campo, string mensaje)> Validar(DateOnly hoy)
        {
            var conRango = TipoCatalogo.UsaRango(tipo);

            if (tipo == TipoCatalogo.Vuelo && origen is not null && origen == destino)
                yield return (nameof(destino), "El destino debe ser distinto del origen.");

            if (desde is null)
            {
                if (conRango && hasta is not null)
                    yield return (nameof(desde), "Indica también la fecha inicial.");
                yield break;
            }

            var inicio = desde.Value;
            if (inicio < hoy)
                yield return (nameof(desde), "La fecha no puede ser en el pasado.");
            if (inicio > hoy.AddDays(MaxDiasAnticipacion))
                yield return (nameof(desde), "Solo se puede buscar hasta un año adelante.");

            if (conRango)
            {
                if (hasta is null)
                    yield return (nameof(hasta), $"Indica la {TipoCatalogo.EtiquetaHasta(tipo).ToLowerInvariant()}.");
                else if (hasta.Value <= inicio)
                    yield return (nameof(hasta), "Debe ser posterior a la fecha inicial.");
                else if (hasta.Value.DayNumber - inicio.DayNumber > MaxDiasRango)
                    yield return (nameof(hasta), $"El máximo es de {MaxDiasRango} días.");
            }
        }

        /// <summary>
        /// Parámetros que viajan de la tarjeta al detalle (y del detalle de regreso a la sección) para no perder la
        /// búsqueda. Uso en Razor: <c>asp-all-route-data="Model.ParametrosRuta()"</c>.
        /// </summary>
        public Dictionary<string, string> ParametrosRuta()
        {
            var parametros = new Dictionary<string, string>();
            if (desde is not null && tipo != TipoCatalogo.Vuelo) parametros["desde"] = desde.Value.ToString("yyyy-MM-dd");
            if (hasta is not null && TipoCatalogo.UsaRango(tipo)) parametros["hasta"] = hasta.Value.ToString("yyyy-MM-dd");
            parametros["personas"] = personas.ToString();
            if (tipo == TipoCatalogo.Hospedaje) parametros["habitaciones"] = habitaciones.ToString();
            return parametros;
        }

        /// <summary>
        /// Igual que <see cref="ParametrosRuta()"/> pero incluye el <c>id</c> del servicio (enlaces a
        /// <c>/Catalogo/Detalle/{id}</c> y <c>/Reservas/Resumen?id=</c>).
        /// </summary>
        /// <remarks>
        /// IMPORTANTE: no combinar <c>asp-route-id</c> con <c>asp-all-route-data</c> en la misma etiqueta:
        /// <c>asp-all-route-data</c> reemplaza el diccionario completo y el id se pierde (la URL sale sin id → 404).
        /// </remarks>
        public Dictionary<string, string> ParametrosRuta(ulong idServicio)
        {
            var parametros = ParametrosRuta();
            parametros["id"] = idServicio.ToString();
            return parametros;
        }
    }

    /// <summary>Una tarjeta de resultados.</summary>
    public class TarjetaServicioViewModel
    {
        public ulong id { get; set; }
        public string tipo { get; set; } = string.Empty;
        public string nombre { get; set; } = string.Empty;
        public string? descripcion { get; set; }
        public string? imagen_ruta { get; set; }
        public string moneda { get; set; } = "MXN";

        /// <summary>Texto de la etiqueta de la imagen: ciudad, o "Origen → Destino" en vuelos.</summary>
        public string ubicacion { get; set; } = string.Empty;

        /// <summary>Precio más bajo por noche / día / asiento / persona ("desde").</summary>
        public decimal? precio_unitario { get; set; }

        /// <summary>Total de la búsqueda (solo si se buscó con fechas y hay cupo).</summary>
        public decimal? total { get; set; }

        /// <summary>Ej.: "3 noches · 1 habitación · 2 huéspedes".</summary>
        public string? resumen_total { get; set; }

        public bool agotado { get; set; }

        /// <summary>Vuelos: "QZ301 · sale lun 02/11 07:30".</summary>
        public string? fecha_texto { get; set; }

        /// <summary>Vuelos: sale justo el día buscado (los demás son de ±3 días).</summary>
        public bool fecha_buscada { get; set; }

        public string Unidad => TipoCatalogo.Unidad(tipo);
    }

    // ---------------------------------------------------------------------------------------------
    // Cotización: precio y cupo de un servicio para unas fechas. La usa el detalle y la usará la reserva (fase C).
    // Convención (igual que detalle_reservacion):
    //   cantidad        = unidades que se apartan POR DÍA (habitaciones, autos, asientos o personas)
    //   precio_unitario = precio de UNA unidad por todo el periodo (suma de precio_fecha de cada noche / día)
    //   subtotal        = precio_unitario * cantidad
    // ---------------------------------------------------------------------------------------------

    public enum EstadoCotizacion
    {
        /// <summary>Hay cupo en todas las fechas: se puede reservar.</summary>
        Disponible,
        /// <summary>Falta cupo (o no hay fechas cargadas) en al menos un día: la tarjeta sale como "Agotado".</summary>
        SinCupo,
        /// <summary>El servicio no sirve para esa búsqueda (capacidad, personas del paquete, vuelo ya salió): no se muestra.</summary>
        NoAplica
    }

    /// <summary>Precio y cupo de un día dentro de la cotización.</summary>
    public record DiaCotizado(DateOnly fecha, decimal precio, int disponible, bool existe, bool alcanza);

    public class CotizacionServicio
    {
        public EstadoCotizacion estado { get; set; } = EstadoCotizacion.Disponible;
        public string? motivo { get; set; }
        public DateTime inicio { get; set; }
        public DateTime fin { get; set; }
        public int cantidad { get; set; }
        /// <summary>Noches, días de renta, o 1.</summary>
        public int unidades_periodo { get; set; }
        public decimal precio_unitario { get; set; }
        public decimal subtotal => precio_unitario * cantidad;
        public string moneda { get; set; } = "MXN";
        public string resumen { get; set; } = string.Empty;
        public List<DiaCotizado> dias { get; set; } = new();

        public bool Disponible => estado == EstadoCotizacion.Disponible;

        public CotizacionServicio MarcarNoAplica(string mensaje)
        {
            estado = EstadoCotizacion.NoAplica;
            motivo = mensaje;
            return this;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Detalle (/Catalogo/Detalle/{id})
    // ---------------------------------------------------------------------------------------------

    public record DatoServicio(string etiqueta, string valor);
    public record IncluidoPaquete(string nombre, string tipo_servicio, decimal cantidad);
    public record PromocionVisible(string codigo, string? nombre, string? descripcion, string tipo_descuento, decimal valor, decimal minimo_compra);

    public class DetalleServicioViewModel
    {
        public ulong id { get; set; }
        public string tipo { get; set; } = string.Empty;
        public string nombre { get; set; } = string.Empty;
        public string? descripcion { get; set; }
        public string? imagen_ruta { get; set; }
        public string moneda { get; set; } = "MXN";
        public string proveedor { get; set; } = string.Empty;
        public string ubicacion { get; set; } = string.Empty;

        /// <summary>Atributos propios del tipo (habitación, horarios, marca, código de vuelo…).</summary>
        public List<DatoServicio> Datos { get; set; } = new();

        /// <summary>Texto de la política de cancelación (el del gestor, o uno automático).</summary>
        public string politica { get; set; } = string.Empty;

        public List<IncluidoPaquete> Incluidos { get; set; } = new();
        public List<PromocionVisible> Promociones { get; set; } = new();

        /// <summary>Disponibilidad de los próximos 30 días (para elegir fecha).</summary>
        public List<DiaCotizado> ProximasFechas { get; set; } = new();

        public BusquedaCatalogoViewModel Busqueda { get; set; } = new();

        /// <summary>Precio y cupo para las fechas elegidas (null si aún no se eligieron).</summary>
        public CotizacionServicio? Cotizacion { get; set; }
    }
}
