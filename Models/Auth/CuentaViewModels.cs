using System.ComponentModel.DataAnnotations;

// ViewModels de la cuenta del cliente: iniciar sesión y crear nueva cuenta.
// Regla R10: la contraseña solo viaja en el formulario y se convierte en hash en el servidor;
// nunca se guarda, se registra en logs ni se vuelve a mandar a la vista.
namespace FlyVibeMex.Models
{
    /// <summary>Formulario "Iniciar sesión" (Views/Home/_FormSesion.cshtml).</summary>
    public class LoginViewModel
    {
        [Display(Name = "Correo")]
        [Required(ErrorMessage = "Escribe tu correo.")]
        [StringLength(150, ErrorMessage = "Máximo 150 caracteres.")]
        [EmailAddress(ErrorMessage = "Correo no válido.")]
        public string correo { get; set; } = string.Empty;

        [Display(Name = "Contraseña")]
        [Required(ErrorMessage = "Escribe tu contraseña.")]
        [StringLength(128, ErrorMessage = "Máximo 128 caracteres.")]
        [DataType(DataType.Password)]
        public string password { get; set; } = string.Empty;

        [Display(Name = "Mantener la sesión iniciada")]
        public bool recordarme { get; set; }

        /// <summary>Página a la que se regresa después de iniciar sesión (solo se acepta si es local).</summary>
        public string? returnUrl { get; set; }
    }

    /// <summary>Formulario "Crear nueva cuenta" (Views/Crearusuario/Index.cshtml) → tabla UsuariosGlobales.</summary>
    public class CrearUsuarioViewModel
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

        // En la tabla es NOT NULL; se pide igual que el paterno.
        [Display(Name = "Apellido materno")]
        [Required(ErrorMessage = "El apellido materno es obligatorio.")]
        [StringLength(60, ErrorMessage = "Máximo 60 caracteres.")]
        [RegularExpression(SoloLetras, ErrorMessage = "Usa solo letras y espacios.")]
        public string apellido_materno { get; set; } = string.Empty;

        [Display(Name = "Correo")]
        [Required(ErrorMessage = "El correo es obligatorio.")]
        [StringLength(150, ErrorMessage = "Máximo 150 caracteres.")]
        [EmailAddress(ErrorMessage = "Correo no válido.")]
        public string correo { get; set; } = string.Empty;

        [Display(Name = "Teléfono")]
        [Required(ErrorMessage = "El teléfono es obligatorio.")]
        [StringLength(20, ErrorMessage = "Máximo 20 caracteres.")]
        [RegularExpression(@"^[0-9+()\-\s]{10,20}$", ErrorMessage = "Escribe al menos 10 dígitos (solo números, espacios, +, - o paréntesis).")]
        public string telefono { get; set; } = string.Empty;

        [Display(Name = "Contraseña")]
        [Required(ErrorMessage = "La contraseña es obligatoria.")]
        [StringLength(128, MinimumLength = 8, ErrorMessage = "La contraseña debe tener entre 8 y 128 caracteres.")]
        [RegularExpression(@"^(?=.*\p{L})(?=.*\d).+$", ErrorMessage = "La contraseña debe tener al menos una letra y un número.")]
        [DataType(DataType.Password)]
        public string password { get; set; } = string.Empty;

        [Display(Name = "Confirmar contraseña")]
        [Required(ErrorMessage = "Confirma tu contraseña.")]
        [Compare(nameof(password), ErrorMessage = "Las contraseñas no coinciden.")]
        [DataType(DataType.Password)]
        public string confirmar_password { get; set; } = string.Empty;

        [Display(Name = "Acepto los términos y la política de privacidad")]
        [Range(typeof(bool), "true", "true", ErrorMessage = "Debes aceptar los términos para crear tu cuenta.")]
        public bool acepto_terminos { get; set; }
    }
}
