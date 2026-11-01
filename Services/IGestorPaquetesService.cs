using FlyVibeMex.Models;

namespace FlyVibeMex.Services;

/// <summary>
/// Paquetes del Gestor de ventas: servicios (tipo PAQUETE) + paquete + paquete_servicio + disponibilidad.
/// El paquete tiene precio (por persona) y cupo propios; sus servicios incluidos son contenido y no descuentan su cupo.
/// Su disponibilidad se ajusta en la misma pantalla que la de los demás servicios.
/// </summary>
public interface IGestorPaquetesService
{
    Task<ListaPaquetesViewModel> ListaAsync(string? nombre, bool incluirInactivos, CancellationToken cancellationToken = default);

    /// <summary>Formulario de alta vacío (con la lista de servicios para incluir y el inventario inicial sugerido).</summary>
    Task<PaqueteFormViewModel> NuevoAsync(ulong? idProveedor, CancellationToken cancellationToken = default);

    /// <summary>Formulario lleno para "Actualizar". null si no existe o no es un paquete.</summary>
    Task<PaqueteFormViewModel?> FormularioAsync(ulong id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Vuelve a llenar la lista de servicios del formulario (después de un POST con errores) y acomoda las filas de
    /// <c>componentes</c> en el mismo orden, conservando lo que el gestor marcó.
    /// </summary>
    Task PrepararAsync(PaqueteFormViewModel datos, CancellationToken cancellationToken = default);

    Task<ResultadoOperacion> CrearAsync(PaqueteFormViewModel datos, ulong idUsuario, CancellationToken cancellationToken = default);

    Task<ResultadoOperacion> ActualizarAsync(ulong id, PaqueteFormViewModel datos, ulong idUsuario, CancellationToken cancellationToken = default);
}
