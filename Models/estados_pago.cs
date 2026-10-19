// NOTA: [NAMESPACE] Esta clase está en el namespace `fvm.models`; el estándar acordado es `FlyVibeMex.Models`.
// Views/_ViewImports.cshtml solo importa FlyVibeMex.Models, así que esta clase no es visible en vistas ni
// controladores sin un `using fvm.models;` adicional. No se cambia aquí para no alterar la compilación actual.
namespace fvm.models
{
    /// <summary>
    /// Catálogo de estados válidos de una transacción de pago
    /// </summary>
    public class EstadosPago
    {
        /// <summary>Llave primaria (identificador único autoincremental).</summary>
        public ushort id {get; set;}

        /// <summary>Nombre único del estado.</summary>
        public string nombre {get; set;} = string.Empty;

        /// <summary>Descripción del estado.</summary>
        public string descripcion {get; set;} = string.Empty;

        /// <summary>Indica si el estado concluye el flujo del pago.</summary>
        // NOTA: declarado como byte; en EstadosReservacion el mismo concepto es bool. Unificar a bool.
        public byte es_final {get; set;}
    }
}