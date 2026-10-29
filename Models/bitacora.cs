namespace FlyVibeMex.Models
{
    /// <summary>
    /// Bitácora del panel (tabla <c>bitacora</c>): quién cambió qué y cuándo. La escriben los servicios del panel
    /// con <c>FlyVibeMexDbContext.RegistrarBitacora(...)</c> en la MISMA transacción que el cambio, y la consulta el SA.
    /// </summary>
    public class Bitacora
    {
        public ulong id { get; set; }

        public DateTime fecha { get; set; }

        /// <summary>Usuario interno que hizo el cambio (NULL = proceso del sistema).</summary>
        public ulong? id_usuario { get; set; }

        /// <summary>CREAR, ACTUALIZAR, BAJA, REACTIVAR, DISPONIBILIDAD…</summary>
        public string accion { get; set; } = string.Empty;

        /// <summary>Tabla afectada: proveedores, servicios, disponibilidad_servicio…</summary>
        public string entidad { get; set; } = string.Empty;

        public ulong? id_entidad { get; set; }

        /// <summary>Resumen legible del cambio (máx. 500).</summary>
        public string? detalle { get; set; }
    }
}
