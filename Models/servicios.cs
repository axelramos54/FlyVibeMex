namespace FlyVibeMex.Models
{
    /// <summary>
    /// <b>Supertipo</b> de todo lo reservable. Concentra los datos comunes: proveedor, nombre,
    /// descripción, precio base y moneda. Cada fila tiene un subtipo 1:1 según <c>tipo_servicio</c>:
    /// VUELO → (clase Vuelos, pendiente), HOSPEDAJE → <c>Hospedajes</c>, VEHICULO → <c>Vehiculos</c>,
    /// PAQUETE → <c>Paquetes</c>. En el diseño, cada subtipo usa <c>id_servicio</c> como su llave primaria y foránea.
    /// </summary>
    
    /// <remarks>
    /// El precio que se cobra en una fecha concreta sale de <c>DisponibilidadServicio.precio_fecha</c> (R3);
    /// <c>precio_base</c> es solo el precio de referencia antes de promociones.
    /// </remarks>
    
    // NOTA: falta la clase del subtipo VUELO (Vuelos: id_servicio, id_origen, id_destino, codigo_vuelo,
    // fecha_salida, fecha_llegada, clase), que sí aparece en el diagrama entidad-relación.
    public class Servicios
    {
        /// <summary>Llave primaria (identificador único autoincremental).</summary>
        public ulong id {get; set;}

        /// <summary>Llave foránea → <c>Proveedores.id</c>. Proveedor responsable del servicio.</summary>
        public ulong id_proveedor {get; set;}

        /// <summary>Discriminador del subtipo: VUELO, HOSPEDAJE, VEHICULO o PAQUETE.</summary>
        public string tipo_servicio {get; set;} = string.Empty;
        public string nombre {get; set;} = string.Empty;

        /// <summary>Descripción comercial y operativa.</summary>
        // NOTA: opcional en el diseño.
        public string? descripcion {get; set;}

        /// <summary>Precio de referencia antes de promociones (dinero: siempre decimal).</summary>
        public decimal precio_base {get; set;}

        /// <summary>Código ISO de moneda de 3 letras; por defecto MXN.</summary>
        public string moneda {get; set;} = string.Empty;

        /// <summary>Borrado lógico <c>false</c> oculta el registro sin eliminarlo.</summary>
        public bool activo {get; set;}

        /// <summary>Fecha y hora de alta del registro (campo de control).</summary>
        public DateTime creado_en {get; set;}
        
        /// <summary>Fecha y hora de la última actualización (campo de control).</summary>
        public DateTime actualizado_en {get; set;}

        // ---------- Política de cancelación: la define el gestor; al comprar se copia a DetallesReservacion ----------

        /// <summary><c>false</c> = el servicio no admite cancelación (tarifa no reembolsable).</summary>
        public bool permite_cancelacion {get; set;} = true;

        /// <summary>Horas de anticipación al inicio para obtener el 100 % de reembolso (0 = hasta el inicio).</summary>
        public int horas_reembolso_total {get; set;} = 48;

        /// <summary>Porcentaje (0-100) que se reembolsa si se cancela después de ese plazo y antes del inicio.</summary>
        public decimal porcentaje_reembolso_tardio {get; set;} = 50;

        /// <summary>Texto de la política que ve el cliente antes de pagar (opcional).</summary>
        public string? politica_cancelacion {get; set;}
    }
}