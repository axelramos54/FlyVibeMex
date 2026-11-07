namespace FlyVibeMex.Models;
public class Resenas
{
    public ulong id { get; set; }
    public ulong id_usuarioglobal { get; set; }
    public ulong id_servicio { get; set; }
    public byte calificacion { get; set; }
    public string? titulo { get; set; }
    public string comentario { get; set; } = string.Empty;
    public string estado { get; set; } = "VISIBLE";
    public DateTime creada_en { get; set; }
    public DateTime actualizada_en { get; set; }
}