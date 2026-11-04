namespace FlyVibeMex.Models
{
    /// <summary>
    /// Ubicación reutilizable: domicilio de proveedores, ubicación de hospedajes, origen/destino de vuelos
    /// y punto de retiro de vehículos. Evita repetir direcciones en cada entidad.
    /// </summary>
    // NOTA: faltan campos del diseño para una dirección completa: colonia, calle, numero_exterior (opcional),
    // numero_interior (opcional) y referencias.
    public class Ubicaciones
    {
        /// <summary>Llave primaria (identificador único autoincremental).</summary>
        public ulong id { get; set; }

        /// <summary>País (por defecto, México).</summary>
        public string pais { get; set; } = string.Empty;
        public string estado { get; set; } = string.Empty;
        public string municipio_ciudad { get; set; } = string.Empty;
        public string codigo_postal { get; set; } = string.Empty;

        /// <summary>Borrado lógico <c>false</c> oculta el registro sin eliminarlo.</summary>
        public bool activo {get; set;}

        // NOTA: `creado` y `actualizado` son fechas de control pero están declaradas como string;
        // deberían ser DateTime (y llamarse creado_en / actualizado_en como en el resto de entidades). // Solucionado 21-10-2026 Por Yamil
        public DateTime creado_en {get; set;}
        public DateTime actualizado_en { get; set; }
    }
}