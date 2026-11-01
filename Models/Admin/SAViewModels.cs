using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace FlyVibeMex.Models
{
    // =============================================================================================
    // Panel del SA (T4 fase D): usuarios y roles, catálogos base, bitácora y contenido del sitio.
    // Lo usan SAController y los servicios SAUsuariosService, SACatalogosService, SABitacoraService y
    // ContenidoSitioService.
    // =============================================================================================

    /// <summary>Inicio del panel SA: contadores y últimas acciones de la bitácora.</summary>
    public class InicioSAViewModel
    {
        public List<(string rol, int activas, int inactivas)> CuentasPorRol { get; set; } = new();
        public int CuentasInactivas { get; set; }
        public int CambiosHoy { get; set; }
        public List<BitacoraItem> Ultimos { get; set; } = new();
    }

    // ---------------------------------------------------------------------------------------------
    // Usuarios
    // ---------------------------------------------------------------------------------------------

    public class FiltroUsuarios
    {
        /// <summary>Nombre, apellido, correo o teléfono.</summary>
        public string? texto { get; set; }
        public string? rol { get; set; }
        /// <summary>activos | inactivos | todos.</summary>
        public string? estado { get; set; }
        public int pagina { get; set; } = 1;
    }

    public class UsuarioListadoItem
    {
        public ulong id { get; set; }
        public string nombre { get; set; } = string.Empty;
        public string correo { get; set; } = string.Empty;
        public string telefono { get; set; } = string.Empty;
        public string rol { get; set; } = string.Empty;
        public bool activo { get; set; }
        public DateTime creado_en { get; set; }
    }

    public class ListaUsuariosViewModel
    {
        public const int PorPagina = 50;

        public FiltroUsuarios Filtro { get; set; } = new();
        public int Total { get; set; }
        public List<UsuarioListadoItem> Resultados { get; set; } = new();
        public List<SelectListItem> Roles { get; set; } = new();

        public int Paginas => Math.Max(1, (int)Math.Ceiling(Total / (double)PorPagina));

        public static readonly (string valor, string texto)[] Estados =
        {
            ("activos", "Activas"), ("inactivos", "Inactivas"), ("todos", "Todas")
        };

        public static string NormalizarEstado(string? estado) => estado is "inactivos" or "todos" ? estado : "activos";
    }

    /// <summary>Datos personales de una cuenta (alta interna y edición). El correo solo se captura en el alta.</summary>
    public class UsuarioDatosForm
    {
        private const string SoloLetras = @"^[\p{L}][\p{L} '\.\-]*$";

        [Display(Name = "Nombre(s)")]
        [Required(ErrorMessage = "El nombre es obligatorio.")]
        [StringLength(80, ErrorMessage = "Máximo 80 caracteres.")]
        [RegularExpression(SoloLetras, ErrorMessage = "Usa solo letras y espacios.")]
        public string nombre { get; set; } = string.Empty;

        [Display(Name = "Apellido paterno")]
        [Required(ErrorMessage = "El apellido paterno es obligatorio.")]
        [StringLength(60, ErrorMessage = "Máximo 60 caracteres.")]
        [RegularExpression(SoloLetras, ErrorMessage = "Usa solo letras y espacios.")]
        public string apellido_paterno { get; set; } = string.Empty;

        [Display(Name = "Apellido materno")]
        [Required(ErrorMessage = "El apellido materno es obligatorio.")]
        [StringLength(60, ErrorMessage = "Máximo 60 caracteres.")]
        [RegularExpression(SoloLetras, ErrorMessage = "Usa solo letras y espacios.")]
        public string apellido_materno { get; set; } = string.Empty;

        [Display(Name = "Teléfono")]
        [Required(ErrorMessage = "El teléfono es obligatorio.")]
        [StringLength(20, ErrorMessage = "Máximo 20 caracteres.")]
        [RegularExpression(@"^[0-9+()\-\s]{10,20}$", ErrorMessage = "Escribe al menos 10 dígitos (solo números, espacios, +, - o paréntesis).")]
        public string telefono { get; set; } = string.Empty;
    }

    /// <summary>Alta de una cuenta INTERNA (SOPORTE, AGENTE, ADMINISTRADOR o SA). La contraseña la genera el sistema.</summary>
    public class UsuarioAltaViewModel : UsuarioDatosForm
    {
        [Display(Name = "Correo")]
        [Required(ErrorMessage = "El correo es obligatorio.")]
        [StringLength(150, ErrorMessage = "Máximo 150 caracteres.")]
        [EmailAddress(ErrorMessage = "Correo no válido.")]
        public string correo { get; set; } = string.Empty;

        [Display(Name = "Rol")]
        [Required(ErrorMessage = "Elige el rol.")]
        public string rol { get; set; } = FlyVibeMex.Models.Roles.Soporte;
    }

    /// <summary>Pantalla /Admin/SA/Usuarios/{id}: datos, rol, estado y contraseña de una cuenta.</summary>
    public class UsuarioEdicionViewModel
    {
        [BindNever] public ulong id { get; set; }
        public UsuarioDatosForm Datos { get; set; } = new();
        [BindNever] public string correo { get; set; } = string.Empty;
        [BindNever] public string rol { get; set; } = string.Empty;
        [BindNever] public bool activo { get; set; }
        [BindNever] public DateTime creado_en { get; set; }
        /// <summary>Es la cuenta del SA que está viendo (no puede cambiarse el rol ni desactivarse).</summary>
        [BindNever] public bool EsPropia { get; set; }
        [BindNever] public int reservaciones { get; set; }
        [BindNever] public int tickets { get; set; }
        [BindNever] public List<SelectListItem> Roles { get; set; } = new();
        [BindNever] public List<BitacoraItem> Movimientos { get; set; } = new();
    }

    /// <summary>Contraseña temporal recién generada: se muestra UNA sola vez y nunca se guarda en texto plano.</summary>
    public class ContrasenaTemporalViewModel
    {
        public ulong id { get; set; }
        public string nombre { get; set; } = string.Empty;
        public string correo { get; set; } = string.Empty;
        public string rol { get; set; } = string.Empty;
        public string contrasena { get; set; } = string.Empty;
        /// <summary>true = cuenta nueva; false = contraseña restablecida.</summary>
        public bool EsAlta { get; set; }
    }

    public class RolListadoItem
    {
        public ulong id { get; set; }
        public string nombre { get; set; } = string.Empty;
        public string? descripcion { get; set; }
        public int activas { get; set; }
        public int inactivas { get; set; }
    }

    // ---------------------------------------------------------------------------------------------
    // Catálogos base: ubicaciones, métodos de pago y bancos
    // ---------------------------------------------------------------------------------------------

    public class UbicacionFormViewModel
    {
        [BindNever] public ulong id { get; set; }

        [Display(Name = "País")]
        [Required(ErrorMessage = "El país es obligatorio.")]
        [StringLength(60, ErrorMessage = "Máximo 60 caracteres.")]
        public string pais { get; set; } = "México";

        [Display(Name = "Estado")]
        [Required(ErrorMessage = "El estado es obligatorio.")]
        [StringLength(60, ErrorMessage = "Máximo 60 caracteres.")]
        public string estado { get; set; } = string.Empty;

        [Display(Name = "Municipio o ciudad")]
        [Required(ErrorMessage = "La ciudad es obligatoria.")]
        [StringLength(100, ErrorMessage = "Máximo 100 caracteres.")]
        public string municipio_ciudad { get; set; } = string.Empty;

        [Display(Name = "Código postal")]
        [Required(ErrorMessage = "El código postal es obligatorio.")]
        [RegularExpression(@"^[0-9A-Za-z\- ]{4,10}$", ErrorMessage = "De 4 a 10 caracteres (números o letras).")]
        public string codigo_postal { get; set; } = string.Empty;

        public bool EsAlta => id == 0;
    }

    public class UbicacionListadoItem
    {
        public ulong id { get; set; }
        public string pais { get; set; } = string.Empty;
        public string estado { get; set; } = string.Empty;
        public string municipio_ciudad { get; set; } = string.Empty;
        public string codigo_postal { get; set; } = string.Empty;
        public bool activo { get; set; }
        /// <summary>Proveedores, servicios activos (vuelos, hospedajes, vehículos y tours) y paquetes que la usan.</summary>
        public int proveedores { get; set; }
        public int servicios { get; set; }
        public int paquetes { get; set; }
    }

    public class ListaUbicacionesViewModel
    {
        public string? Texto { get; set; }
        public bool IncluirInactivas { get; set; }
        public List<UbicacionListadoItem> Resultados { get; set; } = new();
    }

    /// <summary>Confirmación antes de desactivar una ubicación: qué dejará de aparecer en el catálogo.</summary>
    public class BajaUbicacionViewModel
    {
        public UbicacionListadoItem Ubicacion { get; set; } = new();
        /// <summary>Hasta 20 servicios activos que dejarán de aparecer (nombre y tipo).</summary>
        public List<string> Ejemplos { get; set; } = new();
    }

    public class MetodoPagoFormViewModel
    {
        [BindNever] public ushort id { get; set; }

        [Display(Name = "Nombre visible")]
        [Required(ErrorMessage = "El nombre es obligatorio.")]
        [StringLength(40, ErrorMessage = "Máximo 40 caracteres.")]
        public string nombre { get; set; } = string.Empty;

        [Display(Name = "Descripción")]
        [StringLength(150, ErrorMessage = "Máximo 150 caracteres.")]
        public string? descripcion { get; set; }
    }

    public class MetodoPagoListadoItem
    {
        public ushort id { get; set; }
        public string nombre { get; set; } = string.Empty;
        public string? descripcion { get; set; }
        public bool activo { get; set; }
        public int pagos { get; set; }
    }

    public class InstitucionFormViewModel
    {
        [BindNever] public ulong id { get; set; }

        [Display(Name = "Nombre del banco")]
        [Required(ErrorMessage = "El nombre es obligatorio.")]
        [StringLength(120, ErrorMessage = "Máximo 120 caracteres.")]
        public string nombre { get; set; } = string.Empty;

        [Display(Name = "Clave (ABM)")]
        [RegularExpression(@"^[0-9A-Za-z]{1,10}$", ErrorMessage = "Hasta 10 letras o números.")]
        public string? clave_institucion { get; set; }

        [Display(Name = "Código internacional (SWIFT/BIC)")]
        [RegularExpression(@"^[A-Za-z0-9]{8,15}$", ErrorMessage = "De 8 a 15 letras o números.")]
        public string? codigo_internacional { get; set; }

        [Display(Name = "Teléfono")]
        [RegularExpression(@"^[0-9+()\-\s]{7,20}$", ErrorMessage = "De 7 a 20 caracteres (números, espacios, +, - o paréntesis).")]
        public string? telefono { get; set; }

        public bool EsAlta => id == 0;
    }

    public class InstitucionListadoItem
    {
        public ulong id { get; set; }
        public string nombre { get; set; } = string.Empty;
        public string? clave_institucion { get; set; }
        public string? codigo_internacional { get; set; }
        public string? telefono { get; set; }
        public bool activo { get; set; }
        public int tarjetas { get; set; }
    }

    // ---------------------------------------------------------------------------------------------
    // Bitácora
    // ---------------------------------------------------------------------------------------------

    public class FiltroBitacora
    {
        public DateTime? desde { get; set; }
        public DateTime? hasta { get; set; }
        public ulong? usuario { get; set; }
        public string? entidad { get; set; }
        public string? accion { get; set; }
        public string? texto { get; set; }
        public int pagina { get; set; } = 1;
    }

    public class BitacoraItem
    {
        public DateTime fecha { get; set; }
        public ulong? id_usuario { get; set; }
        public string usuario { get; set; } = "Sistema";
        public string? rol { get; set; }
        public string accion { get; set; } = string.Empty;
        public string entidad { get; set; } = string.Empty;
        public ulong? id_entidad { get; set; }
        public string? detalle { get; set; }
    }

    public class ListaBitacoraViewModel
    {
        public const int PorPagina = 50;

        public FiltroBitacora Filtro { get; set; } = new();
        public int Total { get; set; }
        public List<BitacoraItem> Resultados { get; set; } = new();
        public List<SelectListItem> Usuarios { get; set; } = new();
        public List<SelectListItem> Entidades { get; set; } = new();
        public List<SelectListItem> Acciones { get; set; } = new();

        public int Paginas => Math.Max(1, (int)Math.Ceiling(Total / (double)PorPagina));
    }

    // ---------------------------------------------------------------------------------------------
    // Contenido del sitio
    // ---------------------------------------------------------------------------------------------

    /// <summary>Un campo editable en /Admin/SA/Contenido.</summary>
    public class ContenidoCampoViewModel
    {
        public DefinicionContenido Definicion { get; set; } = null!;
        /// <summary>Texto guardado (null = se usa el de por defecto).</summary>
        public string? texto { get; set; }
        public string? imagen_ruta { get; set; }
        public DateTime? actualizado_en { get; set; }
        public string? actualizado_por { get; set; }

        public bool Personalizado => texto is not null || imagen_ruta is not null;
        public string TextoActual => texto ?? Definicion.Defecto;
        public string ImagenActual => imagen_ruta ?? Definicion.Defecto;
    }

    public class ContenidoSeccionViewModel
    {
        public string Seccion { get; set; } = string.Empty;
        public List<ContenidoCampoViewModel> Campos { get; set; } = new();

        public static readonly (string valor, string texto)[] Secciones =
        {
            (ContenidoSitioClaves.SeccionInicio, "Inicio"),
            (ContenidoSitioClaves.SeccionPoliticas, "Políticas"),
            (ContenidoSitioClaves.SeccionPie, "Pie de página")
        };
    }
}
