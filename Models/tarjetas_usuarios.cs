// NOTA: [NAMESPACE] Esta clase está en el namespace `fvm.models`; el estándar acordado es `FlyVibeMex.Models`.
// Views/_ViewImports.cshtml solo importa FlyVibeMex.Models, así que esta clase no es visible en vistas ni
// controladores sin un `using fvm.models;` adicional. No se cambia aquí para no alterar la compilación actual.
namespace fvm.models
{
    /// <summary>
    /// Referencia <b>segura</b> a una tarjeta guardada por el usuario. Solo almacena el token emitido
    /// por el proveedor de pagos y datos no sensibles para mostrarla (marca, últimos 4 dígitos, expiración, alias).
    /// </summary>
    // NOTA: NUNCA agregar a esta clase, a un ViewModel, a logs ni a JavaScript el número completo de
    // la tarjeta (PAN) ni el código de seguridad (CVV). La captura de esos datos corresponde al gateway de pagos.
    public class TarjetasUsuarios
    {
        /// <summary>Llave primaria (identificador único autoincremental).</summary>
        public ulong id {get; set;}

        /// <summary>Llave foránea → <c>Usuarios.id</c>. Propietario de la tarjeta.</summary>
        public ulong id_usuario {get; set;}

        /// <summary>Llave foránea → <c>InstitucionesFinancieras.id</c>. Institución emisora, si se conoce.</summary>
        // NOTA: opcional en el diseño.
        public ulong id_institucion {get; set;}

        /// <summary>Token único del proveedor de pagos; NO es el número real de la tarjeta.</summary>
        public string token_referencia {get; set;} = string.Empty;

        /// <summary>Marca de la tarjeta (Visa, Mastercard…).</summary>
        public string marca {get; set;} = string.Empty;

        /// <summary>Últimos cuatro dígitos, solo para mostrar.</summary>
        public string ultimos4 {get; set;} = string.Empty;

        /// <summary>Mes de expiración (1–12).</summary>
        public byte mes_expiracion {get; set;}

        /// <summary>Año de expiración.</summary>
        public ushort anio_expiracion {get; set;}

        /// <summary>Nombre amigable asignado por el usuario.</summary>
        // NOTA: opcional en el diseño.
        public string alias {get; set;} = string.Empty;

        /// <summary>Borrado lógico <c>false</c> oculta el registro sin eliminarlo.</summary>
        public bool activo {get; set;}

        /// <summary>Fecha y hora de alta del registro (campo de control).</summary>
        public DateTime creado_en {get; set;}
    }
}