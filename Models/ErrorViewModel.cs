namespace FlyVibeMex.Models;

/// <summary>
/// ViewModel de la vista <c>Views/Shared/Error.cshtml</c>. Transporta el identificador de la petición
/// que falló para mostrarlo al usuario y facilitar el rastreo en los logs.
/// </summary>
public class ErrorViewModel
{
    /// <summary>Identificador de la petición (Activity.Current.Id o HttpContext.TraceIdentifier).</summary>
    public string? RequestId { get; set; }

    /// <summary>Indica si hay un <c>RequestId</c> que mostrar.</summary>
    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
}
