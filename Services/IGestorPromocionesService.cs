using FlyVibeMex.Models;

namespace FlyVibeMex.Services;

/// <summary>
/// Promociones del Gestor de ventas: promociones + promocion_servicio. El cliente escribe el código en el resumen de
/// compra (Services/ReservacionService.cs) y solo aplica a los servicios ligados aquí. Nunca se borran: se desactivan.
/// </summary>
public interface IGestorPromocionesService
{
    /// <summary>Listado filtrado por código y estado (vigentes, proximas, vencidas, inactivas o todas).</summary>
    Task<ListaPromocionesViewModel> ListaAsync(string? codigo, string estado, CancellationToken cancellationToken = default);

    /// <summary>Formulario de alta vacío con la lista de servicios para elegir.</summary>
    Task<PromocionFormViewModel> NuevaAsync(CancellationToken cancellationToken = default);

    /// <summary>Formulario lleno para editar (con cuántas compras la han usado). null si no existe.</summary>
    Task<PromocionFormViewModel?> FormularioAsync(ulong id, CancellationToken cancellationToken = default);

    /// <summary>Vuelve a llenar la lista de servicios (después de un POST con errores).</summary>
    Task PrepararAsync(PromocionFormViewModel datos, CancellationToken cancellationToken = default);

    /// <summary>Alta (<paramref name="id"/> null) o edición. Al editar, el código no cambia.</summary>
    Task<ResultadoOperacion> GuardarAsync(ulong? id, PromocionFormViewModel datos, ulong idUsuario, CancellationToken cancellationToken = default);

    /// <summary>
    /// Activa o desactiva una promoción (<b>R11</b>). Las promociones <b>nunca se borran</b>: las compras que
    /// usaron el código lo siguen mostrando.
    /// </summary>
    Task<ResultadoOperacion> CambiarEstadoAsync(ulong id, bool activo, ulong idUsuario, CancellationToken cancellationToken = default);
}
