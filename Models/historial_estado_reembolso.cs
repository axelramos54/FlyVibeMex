// FIXME: [NAMESPACE] Esta clase está en el namespace `fvm.models`; el estándar acordado es `FlyVibeMex.Models`.
// Views/_ViewImports.cshtml solo importa FlyVibeMex.Models, así que esta clase no es visible en vistas ni
// controladores sin un `using fvm.models;` adicional. No se cambia aquí para no alterar la compilación actual.
namespace fvm.models
{
    /// <summary>
    /// Bitácora de transiciones de estado de un reembolso, sin perder información previa.
    /// Solo inserción; nunca actualizar ni eliminar.
    /// </summary>
    public class HistorialEstadoReembolso
    {
        /// <summary>Llave primaria (identificador único autoincremental).</summary>
        public ulong id {get; set;}

        /// <summary>Llave foránea → <c>Reembolsos.id</c>. Reembolso afectado.</summary>
        public ulong id_reembolso {get; set;}

        /// <summary>Llave foránea → <c>Usuarios.id</c>. Agente/usuario que hizo el cambio; vacío si fue automático.</summary>
        // NOTA: debe poder ser nulo (cambio automático).
        public ulong id_usuario_cambio {get; set;}

        /// <summary>Estado registrado.</summary>
        public string estado {get; set;} = string.Empty;

        /// <summary>Fecha del cambio.</summary>
        public DateTime fecha_cambio {get; set;}

        /// <summary>Observación.</summary>
        public string detalle {get; set;} = string.Empty;
    }
}