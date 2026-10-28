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

    // ========================== ASOCIAR ==========================

    /// <summary>Pantalla "Asociar": proveedor + tipo elegido + formulario de ese tipo.</summary>
    public class AsociarViewModel
    {
        public Proveedores Proveedor { get; set; } = new();
        public string? Tipo { get; set; }                       // hospedaje | vehiculo | evento
        public ServicioFormBase? Formulario { get; set; }
        public List<Servicios> ServiciosAsociados { get; set; } = new();

        public static readonly string[] TiposValidos = { "hospedaje", "vehiculo", "evento" };

        /// <summary>Devuelve el tipo solo si está en la lista permitida; si no, null.</summary>
        public static string? Normalizar(string? tipo)
        {
            var t = tipo?.Trim().ToLowerInvariant();
            return t is not null && TiposValidos.Contains(t) ? t : null;
        }

        public static string Nombre(string tipo) => tipo switch
        {
            "hospedaje" => "Hospedajes",
            "vehiculo" => "Vehículos",
            "evento" => "Eventos",
            _ => tipo
        };

        /// <summary>Valor que se guardará en servicios.tipo_servicio.</summary>
        public static string TipoServicio(string tipo) => tipo switch
        {
            "hospedaje" => "HOSPEDAJE",
            "vehiculo" => "VEHICULO",
            "evento" => "EVENTO",
            _ => "OTRO"
        };

        public static List<SelectListItem> OpcionesTipo() => new()
        {
            new("Hospedajes", "hospedaje"),
            new("Vehículos", "vehiculo"),
            new("Eventos", "evento")
        };
    }

    /// <summary>Campos comunes de la tabla servicios (todo lo que se asocia a un proveedor es un servicio).</summary>
    public abstract class ServicioFormBase : IPoliticaCancelacionForm
    {
        // Lo pone el controlador a partir de la URL. [BindNever] = nunca se toma del formulario.
        [BindNever]
        public ulong id_proveedor { get; set; }

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

        public static List<SelectListItem> OpcionesMoneda() => new()
        {
            new("MXN - Peso mexicano", "MXN"),
            new("USD - Dólar", "USD"),
            new("EUR - Euro", "EUR")
        };
    }

    /// <summary>servicios + hospedajes</summary>
    public class HospedajeFormViewModel : ServicioFormBase
    {
        [Display(Name = "Ubicación")]
        [Required(ErrorMessage = "Selecciona la ubicación.")]
        public ulong? id_ubicacion { get; set; }

        [Display(Name = "Tipo de habitación")]
        [Required(ErrorMessage = "Indica el tipo (hotel, cabaña, departamento...).")]
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
    }

    /// <summary>servicios + vehiculos</summary>
    public class VehiculoFormViewModel : ServicioFormBase
    {
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
    }

    /// <summary>
    /// servicios + disponibilidad_servicio.
    /// No existe una tabla "eventos": por ahora un evento se guarda como servicio (tipo_servicio = 'EVENTO')
    /// con su fecha y cupo en disponibilidad_servicio.
    /// </summary>
    public class EventoFormViewModel : ServicioFormBase
    {
        [Display(Name = "Fecha del evento")]
        [Required(ErrorMessage = "La fecha es obligatoria.")]
        [DataType(DataType.Date)]
        public DateTime? fecha { get; set; }

        [Display(Name = "Cupo total")]
        [Range(1, 1000000, ErrorMessage = "El cupo debe ser mayor a 0.")]
        public int cantidad_total { get; set; } = 1;

        [Display(Name = "Precio para esa fecha")]
        [Range(typeof(decimal), "0", "9999999999.99", ErrorMessage = "Precio no válido.")]
        public decimal? precio_fecha { get; set; }
    }
}
