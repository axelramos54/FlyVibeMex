using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

// ViewModels del panel Gestor de ventas para las pantallas de listado de proveedores
// y el alta / baja / actualización de servicios.
// Igual que en GestorViewModels.cs: solo llevan los campos que el formulario puede enviar
// (nadie puede "colar" id, creado_en o actualizado_en) y se validan en el servidor.
namespace FlyVibeMex.Models
{
    // ========================== LISTADO DE PROVEEDORES ==========================

    /// <summary>Pantalla "Proveedores": todos los proveedores con filtros (GET, solo consulta).</summary>
    public class ListaProveedoresViewModel
    {
        public string? Nombre { get; set; }
        public string? Tipo { get; set; }                 // AEROLINEA | HOTEL | RENTADORA | OTRO | null = todos
        public bool IncluirInactivos { get; set; }
        public List<Proveedores> Resultados { get; set; } = new();

        /// <summary>Devuelve el tipo solo si es uno de los permitidos por el CHECK de la tabla.</summary>
        public static string? NormalizarTipo(string? tipo)
        {
            var t = tipo?.Trim().ToUpperInvariant();
            return t is "AEROLINEA" or "HOTEL" or "RENTADORA" or "OTRO" ? t : null;
        }
    }

    // ========================== SERVICIOS ==========================

    /// <summary>
    /// Formulario de alta / actualización de un servicio (tabla <c>servicios</c>).
    /// Solo maneja los datos comunes del supertipo; los datos propios de hospedaje o vehículo
    /// se capturan en la pantalla "Asociar" del proveedor.
    /// </summary>
    public class ServicioFormViewModel
    {
        // En "actualizar" el id se toma de la URL, no del formulario.
        public ulong id { get; set; }

        [Display(Name = "Proveedor")]
        [Required(ErrorMessage = "Selecciona el proveedor.")]
        public ulong? id_proveedor { get; set; }

        [Display(Name = "Tipo de servicio")]
        [Required(ErrorMessage = "Selecciona el tipo de servicio.")]
        [RegularExpression("^(VUELO|HOSPEDAJE|VEHICULO|PAQUETE|EVENTO)$", ErrorMessage = "Tipo de servicio no válido.")]
        public string tipo_servicio { get; set; } = string.Empty;

        [Display(Name = "Nombre del servicio")]
        [Required(ErrorMessage = "El nombre es obligatorio.")]
        [StringLength(150, ErrorMessage = "Máximo 150 caracteres.")]
        public string nombre { get; set; } = string.Empty;

        [Display(Name = "Descripción")]
        [StringLength(2000, ErrorMessage = "Máximo 2000 caracteres.")]
        public string? descripcion { get; set; }

        // servicios.precio_base es decimal(10,2) → máximo 99,999,999.99
        [Display(Name = "Precio base")]
        [Range(typeof(decimal), "0", "99999999.99", ErrorMessage = "El precio debe estar entre 0 y 99,999,999.99.")]
        public decimal precio_base { get; set; }

        [Display(Name = "Moneda")]
        [Required(ErrorMessage = "Selecciona la moneda.")]
        [RegularExpression("^(MXN|USD|EUR)$", ErrorMessage = "Moneda no válida.")]
        public string moneda { get; set; } = "MXN";

        [Display(Name = "Servicio activo")]
        public bool activo { get; set; } = true;

        /// <summary>Tipos de servicio (discriminador del supertipo, regla R1 + EVENTO que ya usa "Asociar").</summary>
        public static List<SelectListItem> OpcionesTipo() => new()
        {
            new("Vuelo", "VUELO"),
            new("Hospedaje", "HOSPEDAJE"),
            new("Vehículo", "VEHICULO"),
            new("Paquete", "PAQUETE"),
            new("Evento", "EVENTO")
        };

        public static string NombreTipo(string? tipo) => tipo switch
        {
            "VUELO" => "Vuelo",
            "HOSPEDAJE" => "Hospedaje",
            "VEHICULO" => "Vehículo",
            "PAQUETE" => "Paquete",
            "EVENTO" => "Evento",
            _ => tipo ?? string.Empty
        };

        /// <summary>Devuelve el tipo solo si es uno de los permitidos; si no, null.</summary>
        public static string? NormalizarTipo(string? tipo)
        {
            var t = tipo?.Trim().ToUpperInvariant();
            return t is "VUELO" or "HOSPEDAJE" or "VEHICULO" or "PAQUETE" or "EVENTO" ? t : null;
        }

        public static ServicioFormViewModel Desde(Servicios s) => new()
        {
            id = s.id,
            id_proveedor = s.id_proveedor == 0 ? null : s.id_proveedor,
            tipo_servicio = s.tipo_servicio,
            nombre = s.nombre,
            descripcion = s.descripcion,
            precio_base = s.precio_base,
            moneda = string.IsNullOrWhiteSpace(s.moneda) ? "MXN" : s.moneda,
            activo = s.activo
        };
    }

    /// <summary>Pantalla "Servicios": listado con filtros (GET, solo consulta).</summary>
    public class ListaServiciosViewModel
    {
        public string? Nombre { get; set; }
        public string? Tipo { get; set; }
        public ulong? IdProveedor { get; set; }
        public bool IncluirInactivos { get; set; }
        public List<Servicios> Resultados { get; set; } = new();

        /// <summary>id_proveedor → nombre del proveedor, para mostrarlo en la tabla.</summary>
        public Dictionary<ulong, string> NombresProveedor { get; set; } = new();

        public string NombreProveedor(ulong id) =>
            NombresProveedor.TryGetValue(id, out var nombre) ? nombre : $"Proveedor #{id}";
    }

    /// <summary>Ficha de un servicio para la pantalla "Dar de baja servicio".</summary>
    public class ServicioDetalleViewModel
    {
        public Servicios Servicio { get; set; } = new();
        public string NombreProveedor { get; set; } = string.Empty;
    }
}
