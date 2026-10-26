namespace FlyVibeMex.Models
{
    /// <summary>
    /// <b>Subtipo VUELO</b> de <c>Servicios</c>: datos específicos de un vuelo (ruta, horario y clase).
    /// Igual que los demás subtipos, se identifica por <c>id_servicio</c> (llave primaria y foránea hacia
    /// <c>Servicios.id</c>, relación 1:1). Columnas tomadas del diagrama entidad-relación (contexto, sección 4.2).
    /// </summary>
    /// <remarks>
    /// Reglas: <c>id_origen</c> distinto de <c>id_destino</c> y <c>fecha_llegada</c> posterior a <c>fecha_salida</c>
    /// (ambas validadas con CHECK en <c>sql/01_esquema.sql</c>). Nombre, proveedor y precio base viven en <c>Servicios</c>.
    /// </remarks>
    // Completada el 25-10-2026 (Paso 0 del doc 06): antes la clase estaba vacía y no se podía mapear.
    public class Vuelos
    {
        /// <summary>Llave primaria y foránea → <c>Servicios.id</c> (servicio de tipo VUELO).</summary>
        public ulong id_servicio {get; set;}

        /// <summary>Llave foránea → <c>Ubicaciones.id</c>. Ciudad de salida.</summary>
        public ulong id_origen {get; set;}

        /// <summary>Llave foránea → <c>Ubicaciones.id</c>. Ciudad de llegada.</summary>
        public ulong id_destino {get; set;}

        /// <summary>Código del vuelo de la aerolínea (ej. AM123).</summary>
        public string codigo_vuelo {get; set;} = string.Empty;

        /// <summary>Fecha y hora de salida.</summary>
        public DateTime fecha_salida {get; set;}

        /// <summary>Fecha y hora de llegada (posterior a la salida).</summary>
        public DateTime fecha_llegada {get; set;}

        /// <summary>TURISTA, PREMIUM o PRIMERA.</summary>
        public string clase {get; set;} = string.Empty;
    }
}
