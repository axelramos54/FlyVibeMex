namespace FlyVibeMex.Models;

/// <summary>
/// Páginas de error propias (T6.2): Views/Estado/NoEncontrado, AccesoDenegado, SolicitudInvalida y Error.
/// </summary>
public class EstadoViewModel
{
    /// <summary>Código HTTP (400, 403, 404 o 500).</summary>
    public int Codigo { get; set; }

    /// <summary>
    /// La dirección original era del panel (/Admin…) y quien navega es personal: la página usa el layout del panel.
    /// Si no, el del sitio público (un visitante nunca ve el layout del panel).
    /// </summary>
    public bool EsPanel { get; set; }

    /// <summary>Dirección que se pidió (solo para el log; no se muestra completa al usuario).</summary>
    public string? RutaOriginal { get; set; }

    /// <summary>Referencia para buscar el error en el log del servidor (solo en el 500).</summary>
    public string? Referencia { get; set; }

    /// <summary>Layout que usa la vista según la zona.</summary>
    public string Layout => EsPanel ? "~/Views/Admin/_AdminLayout.cshtml" : "~/Views/Shared/_Layout.cshtml";
}
