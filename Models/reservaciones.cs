// NOTA: [NAMESPACE] Esta clase está en el namespace `fvm`; el estándar acordado es `FlyVibeMex.Models`.
// Views/_ViewImports.cshtml solo importa FlyVibeMex.Models, así que esta clase no es visible en vistas ni
// controladores sin un `using fvm;` adicional. No se cambia aquí para no alterar la compilación actual.
namespace fvm
{
    /// <summary>
    /// Encabezado de la operación de reserva de un usuario. Identifica el estado actual y conserva los totales
    /// <b>históricos</b> una vez creada, sus importes no se recalculan aunque cambie el catálogo.
    /// </summary>
    
    /// <remarks>
    /// Cada cambio de <c>id_estado_reservacion</c> debe registrarse además en la bitácora de estados (R6).
    /// Las líneas de la reserva están en <c>DetallesReservacion</c>.
    
    /// </remarks>
    // NOTA: falta `actualizada_en` (fecha de última actualización), prevista en el diseño.
    public class Reservaciones
    {
        /// <summary>Llave primaria (identificador único autoincremental).</summary>
        public ulong id {get; set;}

        /// <summary>Llave foránea → <c>Usuarios.id</c>. Usuario propietario de la reserva.</summary>
        public ulong id_usuario {get; set;}

        /// <summary>Llave foránea → <c>EstadosReservacion.id</c>. Estado actual.</summary>
        public ushort id_estado_reservacion {get; set;}

        /// <summary>Código público y único de la reserva (el que ve el client en la confirmación).</summary>
        public string codigo_reserva {get; set;} = string.Empty;

        /// <summary>Moneda de los importes (ISO, por defecto MXN).</summary>
        public string moneda {get; set;} = string.Empty;

        /// <summary>Suma histórica antes de descuentos.</summary>
        public decimal subtotal {get; set;}

        /// <summary>Descuento total aplicado.</summary>
        public decimal descuento_total {get; set;}

        /// <summary>Total histórico de la reserva (calculado en servidor, nunca recibido del navegador).</summary>
        public decimal total {get; set;}
        
        /// <summary>Fecha y hora de alta del registro (campo de control).</summary>
        public DateTime creada_en {get; set;}
    }
}