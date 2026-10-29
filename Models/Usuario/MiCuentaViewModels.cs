using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace FlyVibeMex.Models
{
    // =============================================================================================
    // Área del cliente: perfil, reservaciones, compras, tarjetas y soporte.
    // Lo usan Controllers/MiusuarioController.cs, Controllers/SolicitarController.cs y los servicios
    // MiCuentaService, MisViajesService y SoporteClienteService.
    // =============================================================================================

    /// <summary>
    /// De quién son los datos que se muestran y qué puede hacer quien navega.
    /// </summary>
    /// <remarks>
    /// - Cliente: ve y modifica SU cuenta.
    /// - Interno (AGENTE / SOPORTE / ADMINISTRADOR): solo lectura. Puede elegir un cliente ("Ver como cliente",
    ///   <c>?cliente=ID</c>) para comprobar que lo que añadió / modificó / eliminó ya se ve del lado del cliente.
    ///   Las acciones (cancelar, tarjetas, tickets) además están bloqueadas en el servidor con [Authorize(Roles = CLIENTE)].
    /// </remarks>
    public class ContextoCuenta
    {
        /// <summary>Usuario de la cookie (quien navega).</summary>
        public ulong IdUsuario { get; set; }

        /// <summary>Cuenta cuyos datos se muestran (la propia o la del cliente elegido).</summary>
        public ulong IdCuenta { get; set; }

        public bool EsInterno { get; set; }

        /// <summary>Cliente elegido en "Ver como cliente" (null = la propia cuenta).</summary>
        public ulong? IdClienteVisto { get; set; }
        public string? NombreClienteVisto { get; set; }
        public string? CorreoClienteVisto { get; set; }

        /// <summary>Los internos nunca ejecutan acciones de cliente.</summary>
        public bool SoloLectura => EsInterno;
        public bool ViendoOtroCliente => IdClienteVisto is not null;
    }

    // ---------------------------------------------------------------------------------------------
    // Perfil y contraseña
    // ---------------------------------------------------------------------------------------------

    public class PerfilFormViewModel
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

    public class CambioPasswordViewModel
    {
        [Display(Name = "Contraseña actual")]
        [Required(ErrorMessage = "Escribe tu contraseña actual.")]
        [StringLength(128)]
        [DataType(DataType.Password)]
        public string password_actual { get; set; } = string.Empty;

        [Display(Name = "Contraseña nueva")]
        [Required(ErrorMessage = "Escribe la contraseña nueva.")]
        [StringLength(128, MinimumLength = 8, ErrorMessage = "La contraseña debe tener entre 8 y 128 caracteres.")]
        [RegularExpression(@"^(?=.*\p{L})(?=.*\d).+$", ErrorMessage = "La contraseña debe tener al menos una letra y un número.")]
        [DataType(DataType.Password)]
        public string password_nueva { get; set; } = string.Empty;

        [Display(Name = "Confirmar contraseña nueva")]
        [Required(ErrorMessage = "Confirma la contraseña nueva.")]
        [Compare(nameof(password_nueva), ErrorMessage = "Las contraseñas no coinciden.")]
        [DataType(DataType.Password)]
        public string confirmar_password { get; set; } = string.Empty;
    }

    /// <summary>Pantalla /Miusuario (Mi perfil).</summary>
    public class MiPerfilViewModel
    {
        public ContextoCuenta Contexto { get; set; } = new();
        public PerfilFormViewModel Perfil { get; set; } = new();
        public CambioPasswordViewModel Password { get; set; } = new();
        public string correo { get; set; } = string.Empty;
        public string rol { get; set; } = string.Empty;
        public DateTime creado_en { get; set; }
        public int reservaciones_proximas { get; set; }
        public int tickets_abiertos { get; set; }
    }

    // ---------------------------------------------------------------------------------------------
    // Reservaciones (Mis reservaciones y detalle)
    // ---------------------------------------------------------------------------------------------

    public enum GrupoReservacion { Proximo, Pasado, Cancelado }

    /// <summary>Qué se puede hacer con una reservación según la política GUARDADA en la compra (regla R4).</summary>
    public class OpcionesCancelacion
    {
        public bool puede_cancelar { get; set; }
        public string? motivo_no_cancelar { get; set; }
        /// <summary>Porcentaje de reembolso que aplica hoy (100 o el % tardío).</summary>
        public decimal porcentaje { get; set; }
        public decimal monto_reembolso { get; set; }
        public bool puede_reembolsar { get; set; }
        public string? motivo_no_reembolsar { get; set; }
        public string politica { get; set; } = string.Empty;
        public string moneda { get; set; } = "MXN";

        /// <summary>Texto para el modal: "Se te reembolsarán $1,200.00 MXN (100 %)".</summary>
        public string TextoReembolso => monto_reembolso > 0
            ? $"Se te reembolsarán ${monto_reembolso:N2} {moneda} ({porcentaje:0.##} % según la política de tu compra)."
            : "Según la política de tu compra, esta cancelación no tiene reembolso.";
    }

    public class ReservacionTarjetaViewModel
    {
        public ulong id { get; set; }
        public string codigo_reserva { get; set; } = string.Empty;
        public ulong id_servicio { get; set; }
        public string servicio { get; set; } = string.Empty;
        public string tipo { get; set; } = string.Empty;
        public string descripcion { get; set; } = string.Empty;
        public string? imagen_ruta { get; set; }
        public DateTime inicio { get; set; }
        public DateTime fin { get; set; }
        public decimal total { get; set; }
        public string moneda { get; set; } = "MXN";
        public string estado { get; set; } = string.Empty;
        public GrupoReservacion grupo { get; set; }
        public OpcionesCancelacion cancelacion { get; set; } = new();
        /// <summary>Estado del reembolso más reciente (SOLICITADO, APROBADO…), si lo hay.</summary>
        public string? estado_reembolso { get; set; }
        public decimal? monto_reembolso_solicitado { get; set; }
    }

    public class MisReservacionesViewModel
    {
        public ContextoCuenta Contexto { get; set; } = new();
        public List<ReservacionTarjetaViewModel> Proximas { get; set; } = new();
        public List<ReservacionTarjetaViewModel> Pasadas { get; set; } = new();
        public List<ReservacionTarjetaViewModel> Canceladas { get; set; } = new();
    }

    public record HistorialEstadoItem(DateTime fecha, string estado, string? motivo, string quien);

    public class DetalleReservacionViewModel
    {
        public ContextoCuenta Contexto { get; set; } = new();
        public ConfirmacionReservaViewModel Comprobante { get; set; } = new();
        public ReservacionTarjetaViewModel Reservacion { get; set; } = new();
        public List<HistorialEstadoItem> Historial { get; set; } = new();
        public List<ReembolsoListadoViewModel> Reembolsos { get; set; } = new();
    }

    // ---------------------------------------------------------------------------------------------
    // Compras (pagos y reembolsos)
    // ---------------------------------------------------------------------------------------------

    public class PagoListadoViewModel
    {
        public DateTime fecha { get; set; }
        public string codigo_reserva { get; set; } = string.Empty;
        public string servicio { get; set; } = string.Empty;
        public decimal monto { get; set; }
        public string moneda { get; set; } = "MXN";
        public string metodo { get; set; } = string.Empty;
        public string tarjeta { get; set; } = string.Empty;
        public string estado { get; set; } = string.Empty;
        public string? referencia { get; set; }
    }

    public class ReembolsoListadoViewModel
    {
        public DateTime fecha_solicitud { get; set; }
        public string codigo_reserva { get; set; } = string.Empty;
        public decimal monto { get; set; }
        public string moneda { get; set; } = "MXN";
        public string estado { get; set; } = string.Empty;
        public string motivo { get; set; } = string.Empty;
        public DateTime? fecha_resolucion { get; set; }
    }

    public class MisComprasViewModel
    {
        public ContextoCuenta Contexto { get; set; } = new();
        public List<PagoListadoViewModel> Pagos { get; set; } = new();
        public List<ReembolsoListadoViewModel> Reembolsos { get; set; } = new();
    }

    // ---------------------------------------------------------------------------------------------
    // Tarjetas
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Alta de tarjeta (simulada). El número y el CVV solo se validan en memoria: nunca se guardan ni se regresan
    /// al navegador (regla R5). Por eso la vista NO usa asp-for en esos dos campos.
    /// </summary>
    public class TarjetaNuevaViewModel
    {
        public string? numero_tarjeta { get; set; }
        public byte? mes_expiracion { get; set; }
        public ushort? anio_expiracion { get; set; }
        public string? cvv { get; set; }

        [StringLength(40, ErrorMessage = "Máximo 40 caracteres.")]
        public string? alias { get; set; }
    }

    public class TarjetaListadoViewModel
    {
        public ulong id { get; set; }
        public string marca { get; set; } = string.Empty;
        public string ultimos4 { get; set; } = string.Empty;
        public byte mes_expiracion { get; set; }
        public ushort anio_expiracion { get; set; }
        public string? alias { get; set; }
        public bool vigente { get; set; }
        public int pagos { get; set; }
    }

    public class MisTarjetasViewModel
    {
        public ContextoCuenta Contexto { get; set; } = new();
        public List<TarjetaListadoViewModel> Tarjetas { get; set; } = new();
        public TarjetaNuevaViewModel Nueva { get; set; } = new();
    }

    // ---------------------------------------------------------------------------------------------
    // Soporte del cliente
    // ---------------------------------------------------------------------------------------------

    public class NuevaSolicitudViewModel
    {
        [Display(Name = "Tipo")]
        [Required(ErrorMessage = "Elige el tipo de solicitud.")]
        [RegularExpression("^(ASESORIA|PROBLEMA)$", ErrorMessage = "Tipo no válido.")]
        public string tipo { get; set; } = "ASESORIA";

        [Display(Name = "Asunto")]
        [Required(ErrorMessage = "Escribe el asunto.")]
        [StringLength(120, MinimumLength = 5, ErrorMessage = "El asunto debe tener entre 5 y 120 caracteres.")]
        public string asunto { get; set; } = string.Empty;

        [Display(Name = "Mensaje")]
        [Required(ErrorMessage = "Escribe tu mensaje.")]
        [StringLength(2000, MinimumLength = 10, ErrorMessage = "El mensaje debe tener entre 10 y 2000 caracteres.")]
        public string mensaje { get; set; } = string.Empty;

        /// <summary>Reservación relacionada (opcional). Debe ser del cliente: se valida en el servidor.</summary>
        [Display(Name = "Reservación relacionada")]
        public ulong? id_reservacion { get; set; }

        public static List<SelectListItem> OpcionesTipo() => new()
        {
            new("Asesoría (dudas, cotizaciones, recomendaciones)", "ASESORIA"),
            new("Problema con una reservación o pago", "PROBLEMA")
        };
    }

    public class TicketListadoViewModel
    {
        public ulong id { get; set; }
        public string tipo { get; set; } = string.Empty;
        public string asunto { get; set; } = string.Empty;
        public string mensaje { get; set; } = string.Empty;
        public string estado { get; set; } = string.Empty;
        public string? agente { get; set; }
        public string? codigo_reserva { get; set; }
        public DateTime creada_en { get; set; }
        public DateTime actualizada_en { get; set; }
    }

    public class MisSolicitudesViewModel
    {
        public ContextoCuenta Contexto { get; set; } = new();
        public NuevaSolicitudViewModel Nueva { get; set; } = new();
        public List<TicketListadoViewModel> Tickets { get; set; } = new();
        public List<SelectListItem> Reservaciones { get; set; } = new();
    }

    /// <summary>Resultado simple de una acción (cancelar, guardar tarjeta…).</summary>
    public record ResultadoOperacion(bool Exitoso, string Mensaje, List<(string campo, string mensaje)>? Errores = null)
    {
        public static ResultadoOperacion Ok(string mensaje) => new(true, mensaje);
        public static ResultadoOperacion Falla(string mensaje) => new(false, mensaje);
    }
}
