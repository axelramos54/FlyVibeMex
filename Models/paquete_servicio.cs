namespace FlyVibeMex.Models
{
    /// <summary>
    /// Tabla puente (muchos a muchos) que indica qué servicios forman parte de cada paquete,
    /// con la cantidad incluida y el orden de presentación/ejecución.
    /// </summary>
    // NOTA: el diseño define la clave compuesta (id_paquete, id_servicio_incluido), donde id_paquete apunta
    // al paquete. Aquí existen `id` e `id_servicio`, y no queda claro cuál de los dos identifica al paquete.
    public class PaqueteServicio
    {
        // NOTA: identificador no previsto en el diseño (ver nota de la clase). // Solucionado 21-10-2026 Por Yamil
        // public ulong id {get; set;}

        /// <summary>Servicio relacionado (según el diseño debería llamarse id_paquete y apuntar al paquete).</summary>
        public ulong id_servicio {get; set;}

        /// <summary>Llave foránea → <c>Servicios.id</c>. Servicio incluido dentro del paquete.</summary>
        public ulong id_servicio_incluido {get; set;}

        /// <summary>Cantidad o unidades incluidas (p. ej. noches, días de renta).</summary>
        public decimal cantidad {get; set;}

        /// <summary>Orden de presentación o ejecución.</summary>
        // NOTA: opcional en el diseño. // Solucionado 21-10-2026 Por Yamil
        public ushort? orden {get; set;} 
    }
}