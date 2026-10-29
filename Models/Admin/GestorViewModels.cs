using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;

// ViewModels del panel Gestor de ventas.
// Un ViewModel es una clase SOLO con los campos que el formulario puede enviar.
// Así nadie puede "colar" campos como id, activo o creado_en agregando inputs a mano,
// y las validaciones ([Required], [StringLength], ...) se revisan en el servidor.
namespace FlyVibeMex.Models
{
    // ========================== PROVEEDORES ==========================

    /// <summary>Formulario de alta / actualización de proveedor (tabla proveedores).</summary>
    public class ProveedorFormViewModel
    {
        // En "actualizar" el id se toma de la URL, no del formulario.
        public ulong id { get; set; }

        [Display(Name = "Ubicación")]
        public ulong? id_ubicacion { get; set; }

        [Display(Name = "Tipo de proveedor")]
        [Required(ErrorMessage = "Selecciona el tipo de proveedor.")]
        [RegularExpression("^(AEROLINEA|HOTEL|RENTADORA|OTRO)$", ErrorMessage = "Tipo de proveedor no válido.")]
        public string tipo_proveedor { get; set; } = string.Empty;

        [Display(Name = "Nombre")]
        [Required(ErrorMessage = "El nombre es obligatorio.")]
        [StringLength(120, ErrorMessage = "Máximo 120 caracteres.")]
        public string nombre { get; set; } = string.Empty;

        [Display(Name = "Teléfono")]
        [Required(ErrorMessage = "El teléfono es obligatorio.")]
        [StringLength(20, ErrorMessage = "Máximo 20 caracteres.")]
        [RegularExpression(@"^[0-9+()\-\s]{7,20}$", ErrorMessage = "Usa solo números, espacios, +, - o paréntesis.")]
        public string telefono { get; set; } = string.Empty;

        [Display(Name = "Correo")]
        [Required(ErrorMessage = "El correo es obligatorio.")]
        [StringLength(150, ErrorMessage = "Máximo 150 caracteres.")]
        [EmailAddress(ErrorMessage = "Correo no válido.")]
        public string correo { get; set; } = string.Empty;

        [Display(Name = "Proveedor activo")]
        public bool activo { get; set; } = true;

        /// <summary>Valores permitidos por el CHECK de la tabla.</summary>
        public static List<SelectListItem> OpcionesTipo() => new()
        {
            new("Aerolínea", "AEROLINEA"),
            new("Hotel", "HOTEL"),
            new("Rentadora", "RENTADORA"),
            new("Otro", "OTRO")
        };

        public static ProveedorFormViewModel Desde(Proveedores p) => new()
        {
            id = p.id,
            id_ubicacion = p.id_ubicacion == 0 ? null : p.id_ubicacion,
            tipo_proveedor = p.tipo_proveedor,
            nombre = p.nombre,
            telefono = p.telefono,
            correo = p.correo,
            activo = p.activo
        };
    }

    /// <summary>Pantalla "Gestionar proveedores": búsqueda por nombre y resultados.</summary>
    public class GestionarProveedoresViewModel
    {
        public string? Nombre { get; set; }
        public bool Buscado { get; set; }
        public List<Proveedores> Resultados { get; set; } = new();
    }

    // ========================== TIPOS DE SERVICIO DEL GESTOR ==========================

    /// <summary>
    /// Tipos que se dan de alta con el formulario de servicio (el paquete tiene su propia pantalla).
    /// En la URL van en minúsculas: <c>/Admin/GestorVentas/Servicios/Alta?tipo=hospedaje</c>.
    /// </summary>
    public static class TiposGestor
    {
        public static readonly string[] Slugs = { "vuelo", "hospedaje", "vehiculo", "evento" };

        /// <summary>Devuelve el tipo solo si está en la lista permitida; si no, null.</summary>
        public static string? Normalizar(string? tipo)
        {
            var t = tipo?.Trim().ToLowerInvariant();
            return t is not null && Slugs.Contains(t) ? t : null;
        }

        /// <summary>slug → valor de servicios.tipo_servicio (VUELO, HOSPEDAJE, VEHICULO, EVENTO).</summary>
        public static string TipoServicio(string slug) => slug.ToUpperInvariant();

        /// <summary>servicios.tipo_servicio → slug (null si es PAQUETE u otro).</summary>
        public static string? Slug(string? tipoServicio) => Normalizar(tipoServicio);

        public static string Nombre(string slug) => slug switch
        {
            "vuelo" => "Vuelo",
            "hospedaje" => "Hospedaje",
            "vehiculo" => "Vehículo",
            "evento" => "Tour / evento",
            _ => slug
        };

        public static string Descripcion(string slug) => slug switch
        {
            "vuelo" => "Una salida o varias (diario o ciertos días) con origen, destino, horario, clase y asientos.",
            "hospedaje" => "Habitación o alojamiento con ubicación, horarios, capacidad e imagen; el cupo es por noche.",
            "vehiculo" => "Auto en renta con punto de retiro, marca, modelo, capacidad e imagen; el cupo es por día.",
            "evento" => "Tour o actividad en la ciudad del proveedor; el cupo es de lugares por fecha.",
            _ => string.Empty
        };

        /// <summary>Formulario vacío del tipo indicado (null si el tipo no es válido).</summary>
        public static ServicioFormBase? NuevoFormulario(string? slug) => Normalizar(slug) switch
        {
            "vuelo" => new VueloFormViewModel(),
            "hospedaje" => new HospedajeFormViewModel(),
            "vehiculo" => new VehiculoFormViewModel(),
            "evento" => new EventoFormViewModel(),
            _ => null
        };
    }

    // ========================== FORMULARIOS DE SERVICIO ==========================

    /// <summary>
    /// Campos comunes de la tabla servicios. Cada tipo agrega los de su subtipo (vuelos, hospedajes, vehiculos)
    /// y se guardan juntos en UNA transacción (regla R1: no hay servicio sin su subtipo).
    /// </summary>
    public abstract class ServicioFormBase : IPoliticaCancelacionForm
    {
        /// <summary>0 = alta. En "actualizar" lo pone el controlador a partir de la URL (nunca del formulario).</summary>
        [BindNever]
        public ulong id { get; set; }

        [Display(Name = "Proveedor")]
        [Required(ErrorMessage = "Selecciona el proveedor.")]
        public ulong? id_proveedor { get; set; }

        [Display(Name = "Nombre del servicio")]
        [Required(ErrorMessage = "El nombre es obligatorio.")]
        [StringLength(150, ErrorMessage = "Máximo 150 caracteres.")]
        public string nombre { get; set; } = string.Empty;

        [Display(Name = "Descripción")]
        [StringLength(2000, ErrorMessage = "Máximo 2000 caracteres.")]
        public string? descripcion { get; set; }

        [Display(Name = "Precio base")]
        [Range(typeof(decimal), "0", "99999999.99", ErrorMessage = "El precio debe estar entre 0 y 99,999,999.99.")]
        public decimal precio_base { get; set; }

        [Display(Name = "Moneda")]
        [Required(ErrorMessage = "Selecciona la moneda.")]
        [RegularExpression("^(MXN|USD|EUR)$", ErrorMessage = "Moneda no válida.")]
        public string moneda { get; set; } = "MXN";

        /// <summary>Solo se muestra al actualizar (un alta siempre queda activa).</summary>
        [Display(Name = "Servicio activo")]
        public bool activo { get; set; } = true;

        // ---------- Política de cancelación ----------

        [Display(Name = "Permite cancelación")]
        public bool permite_cancelacion { get; set; } = true;

        [Display(Name = "Horas antes del inicio para reembolso del 100 %")]
        [Range(0, PoliticaCancelacion.HorasMaximas, ErrorMessage = "Debe estar entre 0 y 8760 horas (1 año).")]
        public int horas_reembolso_total { get; set; } = 48;

        [Display(Name = "% de reembolso después de ese plazo")]
        [Range(typeof(decimal), "0", "100", ErrorMessage = "Debe estar entre 0 y 100.")]
        public decimal porcentaje_reembolso_tardio { get; set; } = 50;

        [Display(Name = "Texto de la política para el cliente")]
        [StringLength(500, ErrorMessage = "Máximo 500 caracteres.")]
        public string? politica_cancelacion { get; set; }

        /// <summary>Valor de servicios.tipo_servicio de este formulario.</summary>
        public abstract string TipoServicio { get; }

        /// <summary>Tipo en minúsculas para la URL (vuelo, hospedaje, vehiculo, evento).</summary>
        public string Slug => TipoServicio.ToLowerInvariant();

        public bool EsAlta => id == 0;

        public static List<SelectListItem> OpcionesMoneda() => new()
        {
            new("MXN - Peso mexicano", "MXN"),
            new("USD - Dólar", "USD"),
            new("EUR - Euro", "EUR")
        };
    }

    /// <summary>
    /// Tipos cuyo cupo es por día (hospedaje: noches; vehículo: días de renta; evento: lugares por fecha).
    /// Al dar de alta se captura un rango inicial; después se ajusta en Servicios › Disponibilidad.
    /// </summary>
    public abstract class ServicioConInventarioFormBase : ServicioFormBase
    {
        [Display(Name = "Disponible desde")]
        [DataType(DataType.Date)]
        public DateTime? inventario_desde { get; set; }

        [Display(Name = "Hasta (incluido)")]
        [DataType(DataType.Date)]
        public DateTime? inventario_hasta { get; set; }

        [Display(Name = "Cupo por día")]
        [Range(1, 100000, ErrorMessage = "El cupo debe estar entre 1 y 100,000.")]
        public int? inventario_cupo { get; set; }

        [Display(Name = "Precio por día")]
        [Range(typeof(decimal), "0", "99999999.99", ErrorMessage = "Precio no válido.")]
        public decimal? inventario_precio { get; set; }
    }

    /// <summary>servicios + hospedajes (+ disponibilidad inicial)</summary>
    public class HospedajeFormViewModel : ServicioConInventarioFormBase
    {
        public override string TipoServicio => "HOSPEDAJE";

        [Display(Name = "Ubicación")]
        [Required(ErrorMessage = "Selecciona la ubicación.")]
        public ulong? id_ubicacion { get; set; }

        [Display(Name = "Tipo de habitación")]
        [Required(ErrorMessage = "Indica el tipo (sencilla, doble, suite, cabaña...).")]
        [StringLength(60, ErrorMessage = "Máximo 60 caracteres.")]
        public string tipo_habitacion { get; set; } = string.Empty;

        [Display(Name = "Categoría")]
        [StringLength(40, ErrorMessage = "Máximo 40 caracteres.")]
        public string? categoria { get; set; }

        [Display(Name = "Capacidad de huéspedes")]
        [Range(1, 1000, ErrorMessage = "Debe ser entre 1 y 1000.")]
        public int capacidad_huespedes { get; set; } = 1;

        [Display(Name = "Hora de entrada")]
        [Required(ErrorMessage = "Indica la hora de entrada.")]
        public TimeOnly? hora_entrada { get; set; }

        [Display(Name = "Hora de salida")]
        [Required(ErrorMessage = "Indica la hora de salida.")]
        public TimeOnly? hora_salida { get; set; }

        [Display(Name = "Imagen")]
        public IFormFile? imagen { get; set; }

        [Display(Name = "Quitar la imagen actual")]
        public bool quitar_imagen { get; set; }

        /// <summary>Ruta guardada (solo para mostrarla al actualizar).</summary>
        [BindNever]
        public string? imagen_actual { get; set; }
    }

    /// <summary>servicios + vehiculos (+ disponibilidad inicial)</summary>
    public class VehiculoFormViewModel : ServicioConInventarioFormBase
    {
        public override string TipoServicio => "VEHICULO";

        [Display(Name = "Ubicación de retiro")]
        [Required(ErrorMessage = "Selecciona la ubicación de retiro.")]
        public ulong? id_ubicacion_retiro { get; set; }

        [Display(Name = "Categoría")]
        [Required(ErrorMessage = "La categoría es obligatoria.")]
        [StringLength(50, ErrorMessage = "Máximo 50 caracteres.")]
        public string categoria { get; set; } = string.Empty;

        [Display(Name = "Marca")]
        [Required(ErrorMessage = "La marca es obligatoria.")]
        [StringLength(50, ErrorMessage = "Máximo 50 caracteres.")]
        public string marca { get; set; } = string.Empty;

        [Display(Name = "Modelo")]
        [Required(ErrorMessage = "El modelo es obligatorio.")]
        [StringLength(60, ErrorMessage = "Máximo 60 caracteres.")]
        public string modelo { get; set; } = string.Empty;

        [Display(Name = "Capacidad de pasajeros")]
        [Range(1, 255, ErrorMessage = "Debe ser entre 1 y 255.")]
        public byte capacidad_pasajeros { get; set; } = 4;

        [Display(Name = "Imagen")]
        public IFormFile? imagen { get; set; }

        [Display(Name = "Quitar la imagen actual")]
        public bool quitar_imagen { get; set; }

        [BindNever]
        public string? imagen_actual { get; set; }
    }

    /// <summary>
    /// servicios (tipo EVENTO) + disponibilidad_servicio. No existe tabla "eventos": la ciudad del tour es la
    /// ubicación de su proveedor y las fechas / lugares están en disponibilidad_servicio.
    /// </summary>
    public class EventoFormViewModel : ServicioConInventarioFormBase
    {
        public override string TipoServicio => "EVENTO";
    }

    /// <summary>
    /// servicios + vuelos + disponibilidad_servicio. En la BD cada vuelo es UNA salida (vuelos.fecha_salida);
    /// con "repetir" se crea una salida por fecha, cada una con sus asientos.
    /// </summary>
    public class VueloFormViewModel : ServicioFormBase
    {
        public override string TipoServicio => "VUELO";

        /// <summary>Máximo de salidas que se crean de una vez.</summary>
        public const int MaximoSalidas = 120;

        [Display(Name = "Origen")]
        [Required(ErrorMessage = "Selecciona el origen.")]
        public ulong? id_origen { get; set; }

        [Display(Name = "Destino")]
        [Required(ErrorMessage = "Selecciona el destino.")]
        public ulong? id_destino { get; set; }

        [Display(Name = "Código de vuelo")]
        [Required(ErrorMessage = "El código es obligatorio.")]
        [RegularExpression("^[A-Za-z0-9]{2,10}$", ErrorMessage = "De 2 a 10 letras o números, sin espacios (p. ej. QZ123).")]
        public string codigo_vuelo { get; set; } = string.Empty;

        [Display(Name = "Salida")]
        [Required(ErrorMessage = "Indica la fecha y hora de salida.")]
        public DateTime? fecha_salida { get; set; }

        [Display(Name = "Llegada")]
        [Required(ErrorMessage = "Indica la fecha y hora de llegada.")]
        public DateTime? fecha_llegada { get; set; }

        [Display(Name = "Clase")]
        [Required(ErrorMessage = "Selecciona la clase.")]
        [RegularExpression("^(TURISTA|PREMIUM|PRIMERA)$", ErrorMessage = "Clase no válida.")]
        public string clase { get; set; } = "TURISTA";

        // ---------- Solo en el alta ----------

        [Display(Name = "Asientos")]
        [Range(1, 1000, ErrorMessage = "Debe ser entre 1 y 1000.")]
        public int asientos { get; set; } = 150;

        [Display(Name = "Precio por asiento")]
        [Range(typeof(decimal), "0", "99999999.99", ErrorMessage = "Precio no válido.")]
        public decimal? precio_asiento { get; set; }

        [Display(Name = "Repetir")]
        [RegularExpression("^(NO|DIARIO|SEMANAL)$", ErrorMessage = "Opción no válida.")]
        public string repetir { get; set; } = "NO";

        [Display(Name = "Repetir hasta (incluido)")]
        [DataType(DataType.Date)]
        public DateTime? repetir_hasta { get; set; }

        /// <summary>Días de la semana para "SEMANAL" (0 = domingo … 6 = sábado, como DayOfWeek).</summary>
        [Display(Name = "Días de la semana")]
        public List<int> dias_semana { get; set; } = new();

        public static List<SelectListItem> OpcionesClase() => new()
        {
            new("Turista", "TURISTA"),
            new("Premium", "PREMIUM"),
            new("Primera", "PRIMERA")
        };

        public static List<SelectListItem> OpcionesRepetir() => new()
        {
            new("Solo esta salida", "NO"),
            new("Todos los días", "DIARIO"),
            new("Ciertos días de la semana", "SEMANAL")
        };

        /// <summary>Lunes primero (como en México), con el valor de DayOfWeek.</summary>
        public static readonly (int valor, string nombre)[] DiasSemana =
        {
            (1, "Lun"), (2, "Mar"), (3, "Mié"), (4, "Jue"), (5, "Vie"), (6, "Sáb"), (0, "Dom")
        };
    }

    /// <summary>Pantalla de alta / actualización de un servicio (formulario del tipo elegido).</summary>
    public class FormServicioViewModel
    {
        /// <summary>null = todavía no se elige el tipo (se muestra el selector de tipo).</summary>
        public ServicioFormBase? Formulario { get; set; }

        /// <summary>Proveedor preseleccionado (viene de Proveedores › Asociar).</summary>
        public ulong? IdProveedor { get; set; }
        public string? NombreProveedor { get; set; }

        /// <summary>Servicios del mismo tipo que ya tiene el proveedor (informativo).</summary>
        public List<Servicios> ServiciosDelProveedor { get; set; } = new();
    }

    // ========================== DISPONIBILIDAD ==========================

    /// <summary>Formulario para agregar o ajustar el cupo y el precio de un rango de fechas.</summary>
    public class DisponibilidadFormViewModel
    {
        [Display(Name = "Desde")]
        [Required(ErrorMessage = "Indica la fecha inicial.")]
        [DataType(DataType.Date)]
        public DateTime? desde { get; set; }

        [Display(Name = "Hasta (incluido)")]
        [DataType(DataType.Date)]
        public DateTime? hasta { get; set; }

        [Display(Name = "Cupo por día")]
        [Required(ErrorMessage = "Indica el cupo (0 = cerrar esas fechas).")]
        [Range(0, 100000, ErrorMessage = "El cupo debe estar entre 0 y 100,000.")]
        public int? cupo { get; set; }

        [Display(Name = "Precio por día")]
        [Range(typeof(decimal), "0", "99999999.99", ErrorMessage = "Precio no válido.")]
        public decimal? precio { get; set; }
    }

    /// <summary>Un día de disponibilidad_servicio.</summary>
    public record DiaDisponibilidad(DateOnly fecha, int total, int disponible, decimal precio)
    {
        public int vendidos => total - disponible;
    }

    /// <summary>Pantalla Servicios › Disponibilidad.</summary>
    public class DisponibilidadViewModel
    {
        public Servicios Servicio { get; set; } = new();
        public string NombreProveedor { get; set; } = string.Empty;

        /// <summary>Vuelo: solo existe su fecha de salida (no se agregan otras fechas).</summary>
        public DateOnly? FechaFija { get; set; }

        /// <summary>Periodo que se muestra en la tabla.</summary>
        public DateOnly Desde { get; set; }
        public DateOnly Hasta { get; set; }
        public List<DiaDisponibilidad> Dias { get; set; } = new();

        public DisponibilidadFormViewModel Formulario { get; set; } = new();

        public const int DiasPorPagina = 60;
        public const int MaximoDiasPorCambio = 366;
    }
}
