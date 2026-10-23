namespace FlyVibeMex.Models
{
  
    public class HistorialEstadoReservacion
    {
        
        public ulong id {get; set;}

        
        public ulong id_reservacion {get; set;}

        public ulong id_estado_reservacion {get; set;}

        public ulong? id_usuario_cambio {get; set;}

        public DateTime fecha_cambio {get; set;}
        public string? motivo {get; set;}
    }
}