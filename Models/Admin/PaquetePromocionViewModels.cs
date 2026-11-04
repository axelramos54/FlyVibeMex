using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

// ViewModels del Gestor de ventas para paquetes y promociones.
// Igual que en GestorViewModels.cs: solo llevan los campos que el formulario puede enviar y se validan en el servidor.
namespace FlyVibeMex.Models
{
    // ========================== SERVICIOS PARA ELEGIR ==========================

    /// <summary>
    /// Un servicio que se puede incluir en un paquete o ligar a una promoción (lista con casillas y filtros).
    /// </summary>
    public class CandidatoServicio
    {
        public ulong id { get; set; }
        public string nombre { get; set; } = string.Empty;
        public string tipo_servicio { get; set; } = string.Empty;
        public ulong id_proveedor { get; set; }
        public string proveedor { get; set; } = string.Empty;
        /// <summary>Ruta y salida del vuelo, ciudad del hospedaje, etc.</summary>
        public string? detalle { get; set; }
        /// <summary>Ciudad donde se usa el servicio (para deducir el destino del paquete).</summary>
        public ulong? id_ciudad { get; set; }
        public bool activo { get; set; }
    }

    // ========================== PAQUETES ==========================

    /// <summary>Un servicio incluido en el paquete (tabla paquete_servicio). Se envía una fila por candidato.</summary>
    public class PaqueteComponenteForm
    {
        public ulong id_servicio { get; set; }

        /// <summary>Casilla marcada = el servicio forma parte del paquete.</summary>
        public bool incluido { get; set; }

        [Display(Name = "Cantidad")]
        [Range(typeof(decimal), "0", "99999999.99", ParseLimitsInInvariantCulture = true, ErrorMessage = "Precio no válido.")]
        public decimal cantidad { get; set; } = 1;

        [Display(Name = "Orden")]
        [Range(0, 999, ErrorMessage = "El orden debe estar entre 0 y 999.")]
        public ushort? orden { get; set; }
    }

    /// <summary>
    /// servicios (tipo PAQUETE) + paquete + paquete_servicio (+ disponibilidad inicial).
    /// El paquete tiene precio (por persona, en la disponibilidad) y cupo propios: los servicios incluidos son su
    /// contenido y NO descuentan su propio cupo.
    /// </summary>
    public class PaqueteFormViewModel : ServicioConInventarioFormBase
    {
        public override string TipoServicio => "PAQUETE";

        [Display(Name = "Duración (días)")]
        [Range(1, 365, ErrorMessage = "La duración debe estar entre 1 y 365 días.")]
        public int duracion_dias { get; set; } = 3;

        [Display(Name = "Mínimo de personas")]
        [Range(1, 500, ErrorMessage = "Debe estar entre 1 y 500.")]
        public int min_personas { get; set; } = 1;

        [Display(Name = "Máximo de personas")]
        [Range(1, 500, ErrorMessage = "Debe estar entre 1 y 500.")]
        public int max_personas { get; set; } = 6;

        /// <summary>
        /// Opcional: si no se indica, el catálogo usa la ciudad del hospedaje incluido o el destino del vuelo incluido.
        /// Es obligatorio cuando los servicios están en ciudades distintas o no hay hospedaje ni vuelo.
        /// </summary>
        [Display(Name = "Destino")]
        public ulong? id_ubicacion_destino { get; set; }

        /// <summary>Una fila por candidato (en el mismo orden que <see cref="Candidatos"/>); cuentan las marcadas.</summary>
        public List<PaqueteComponenteForm> componentes { get; set; } = new();

        /// <summary>Servicios que se pueden incluir (los llena el servidor).</summary>
        [BindNever, ValidateNever]
        public List<CandidatoServicio> Candidatos { get; set; } = new();
    }

    public class PaqueteListadoItem
    {
        public Servicios Servicio { get; set; } = new();
        public string proveedor { get; set; } = string.Empty;
        public int duracion_dias { get; set; }
        public int min_personas { get; set; }
        public int max_personas { get; set; }
        public string? destino { get; set; }
        /// <summary>true = el destino lo eligió el gestor; false = se deduce de sus servicios.</summary>
        public bool destino_explicito { get; set; }
        public List<string> componentes { get; set; } = new();
        public int dias_con_cupo { get; set; }
    }

    /// <summary>Pantalla "Gestionar paquetes".</summary>
    public class ListaPaquetesViewModel
    {
        public string? Nombre { get; set; }
        public bool IncluirInactivos { get; set; }
        public List<PaqueteListadoItem> Resultados { get; set; } = new();
    }

    // ========================== PROMOCIONES ==========================

    /// <summary>Alta / edición de una promoción (promociones + promocion_servicio).</summary>
    public class PromocionFormViewModel
    {
        /// <summary>0 = alta. Al editar lo pone el controlador a partir de la URL.</summary>
        [BindNever]
        public ulong id { get; set; }

        [Display(Name = "Código")]
        [Required(ErrorMessage = "El código es obligatorio.")]
        [RegularExpression("^[A-Za-z0-9]{4,30}$", ErrorMessage = "De 4 a 30 letras o números, sin espacios (p. ej. VERANO10).")]
        public string codigo { get; set; } = string.Empty;

        [Display(Name = "Nombre")]
        [StringLength(120, ErrorMessage = "Máximo 120 caracteres.")]
        public string? nombre { get; set; }

        [Display(Name = "Descripción")]
        [StringLength(500, ErrorMessage = "Máximo 500 caracteres.")]
        public string? descripcion { get; set; }

        [Display(Name = "Tipo de descuento")]
        [Required(ErrorMessage = "Elige el tipo de descuento.")]
        [RegularExpression("^(PORCENTAJE|MONTO)$", ErrorMessage = "Tipo no válido.")]
        public string tipo_descuento { get; set; } = "PORCENTAJE";

        [Display(Name = "Valor")]
        [Range(typeof(decimal), "0", "99999999.99", ParseLimitsInInvariantCulture = true, ErrorMessage = "Precio no válido.")]
        public decimal valor { get; set; } = 10;

        [Display(Name = "Inicio de vigencia")]
        [Required(ErrorMessage = "Indica desde cuándo es válida.")]
        public DateTime? fecha_inicio { get; set; }

        [Display(Name = "Fin de vigencia")]
        [Required(ErrorMessage = "Indica hasta cuándo es válida.")]
        public DateTime? fecha_fin { get; set; }

        [Display(Name = "Compra mínima")]
        [Range(typeof(decimal), "0", "99999999.99", ParseLimitsInInvariantCulture = true, ErrorMessage = "Precio no válido.")]        public decimal minimo_compra { get; set; }

        [Display(Name = "Promoción activa")]
        public bool activo { get; set; } = true;

        /// <summary>Servicios a los que aplica (casillas).</summary>
        public List<ulong> servicios { get; set; } = new();

        [BindNever, ValidateNever]
        public List<CandidatoServicio> Candidatos { get; set; } = new();

        /// <summary>Cuántas compras la han usado.</summary>
        [BindNever]
        public int usos { get; set; }

        public bool EsAlta => id == 0;

        public static List<SelectListItem> OpcionesTipo() => new()
        {
            new("Porcentaje sobre el subtotal (%)", "PORCENTAJE"),
            new("Monto fijo ($)", "MONTO")
        };
    }

    public class PromocionListadoItem
    {
        public ulong id { get; set; }
        public string codigo { get; set; } = string.Empty;
        public string? nombre { get; set; }
        public string tipo_descuento { get; set; } = string.Empty;
        public decimal valor { get; set; }
        public decimal minimo_compra { get; set; }
        public DateTime fecha_inicio { get; set; }
        public DateTime fecha_fin { get; set; }
        public bool activo { get; set; }
        public int servicios { get; set; }
        public int usos { get; set; }

        /// <summary>Vigente, Próxima, Vencida o Inactiva (a la hora actual).</summary>
        public string Estado(DateTime ahora) =>
            !activo ? "Inactiva" : fecha_fin < ahora ? "Vencida" : fecha_inicio > ahora ? "Próxima" : "Vigente";

        public string TextoDescuento => tipo_descuento == "PORCENTAJE" ? $"{valor:0.##} %" : $"${valor:N2}";
    }

    /// <summary>Pantalla "Gestionar promociones".</summary>
    public class ListaPromocionesViewModel
    {
        public string? Codigo { get; set; }
        /// <summary>vigentes | proximas | vencidas | inactivas | todas</summary>
        public string Estado { get; set; } = "vigentes";
        public List<PromocionListadoItem> Resultados { get; set; } = new();

        public static readonly (string valor, string texto)[] Estados =
        {
            ("vigentes", "Vigentes"), ("proximas", "Próximas"), ("vencidas", "Vencidas"), ("inactivas", "Inactivas"), ("todas", "Todas")
        };

        public static string NormalizarEstado(string? estado)
        {
            var e = estado?.Trim().ToLowerInvariant();
            return Estados.Any(x => x.valor == e) ? e! : "vigentes";
        }
    }
}
