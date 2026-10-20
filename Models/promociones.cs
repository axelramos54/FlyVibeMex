// NOTA: [NAMESPACE] Esta clase está en el namespace `fvm`; el estándar acordado es `FlyVibeMex.Models`.
// Views/_ViewImports.cshtml solo importa FlyVibeMex.Models, así que esta clase no es visible en vistas ni
// controladores sin un `using fvm;` adicional. No se cambia aquí para no alterar la compilación actual.
namespace fvm
{
    /// <summary>
    /// Promoción o descuento con vigencia controlada. Se aplica a servicios concretos mediante
    /// <c>PromocionServicio</c> y, al reservar, queda registrada en <c>DetallesReservacion.id_promocion</c>.
    /// </summary>
    // NOTA: faltan `nombre` (nombre visible de la promoción) y `minimo_compra` (monto mínimo requerido),
    // que forman parte del diseño.
    public class Promociones
    {
        /// <summary>Llave primaria (identificador único autoincremental).</summary>
        public ulong id {get; set;}

        /// <summary>Código promocional único que captura el cliente.</summary>
        public string codigo {get; set;} = string.Empty;

        /// <summary>Descripción y condiciones resumidas.</summary>
        // NOTA: opcional en el diseño.
        public string descripcion {get; set;} = string.Empty;

        /// <summary>PORCENTAJE o MONTO.</summary>
        public string tipo_descuento {get; set;} = string.Empty;

        /// <summary>Porcentaje o monto del descuento, según <c>tipo_descuento</c>.</summary>
        public decimal valor {get; set;}

        /// <summary>Inicio de vigencia.</summary>
        public DateTime fecha_inicio {get; set;}

        /// <summary>Fin de vigencia (debe ser posterior a fecha_inicio).</summary>
        public DateTime fecha_fin {get; set;}

        /// <summary>Borrado lógico <c>false</c> oculta el registro sin eliminarlo.</summary>
        public bool activo {get; set;}
    }
}