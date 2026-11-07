namespace FlyVibeMex.Models;
public class TiposHuesped
{
    public short id { get; set; }
    public string codigo { get; set; } = string.Empty;
    public string nombre { get; set; } = string.Empty;
    public string? descripcion { get; set; }
    public bool activo { get; set; }
}