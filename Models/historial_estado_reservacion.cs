namespace FlyVibeMex.Models
{
  
    public class HistorialEstadoReservacion
    {
        
        public ulong id {get; set;}

        
        public ulong id_reservacion {get; set;}

        // ushort (no ulong): debe coincidir con EstadosReservacion.id (smallint) para poder ser llave foránea. // Corregido 25-10-2026 (Paso 0)
        public ushort id_estado_reservacion {get; set;}

        public ulong? id_usuario_cambio {get; set;}

        public DateTime fecha_cambio {get; set;}
        public string? motivo {get; set;}
    }
}