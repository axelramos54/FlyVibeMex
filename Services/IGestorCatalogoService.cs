using FlyVibeMex.Models;

namespace FlyVibeMex.Services;

/// <summary>
/// Datos del Gestor de ventas: proveedores, servicios con su subtipo (vuelo, hospedaje, vehículo, evento)
/// y su disponibilidad por fecha. Todo lo que modifica datos deja un registro en la bitácora del panel.
/// </summary>
/// <remarks>
/// Los métodos que guardan devuelven <see cref="ResultadoOperacion"/>: si falla una validación, <c>Errores</c> trae
/// (campo, mensaje) con el nombre del campo del formulario para pasarlo a ModelState.
/// </remarks>
public interface IGestorCatalogoService
{
    // ----- Ubicaciones y proveedores -----

    /// <summary>Ubicaciones activas, ordenadas por ciudad (para los &lt;select&gt;).</summary>
    Task<List<Ubicaciones>> UbicacionesAsync(CancellationToken cancellationToken = default);

    Task<List<Proveedores>> ProveedoresAsync(string? nombre, string? tipo, bool incluirInactivos, int maximo = 200,
        CancellationToken cancellationToken = default);

    Task<Proveedores?> ProveedorAsync(ulong id, CancellationToken cancellationToken = default);

    /// <summary>Alta (<paramref name="id"/> null) o actualización de un proveedor. Correo y teléfono no se repiten.</summary>
    Task<ResultadoOperacion> GuardarProveedorAsync(ulong? id, ProveedorFormViewModel datos, ulong idUsuario,
        CancellationToken cancellationToken = default);

    /// <summary>Baja lógica (activo = 0) o reactivación. Nunca DELETE.</summary>
    Task<ResultadoOperacion> CambiarEstadoProveedorAsync(ulong id, bool activo, ulong idUsuario,
        CancellationToken cancellationToken = default);

    // ----- Servicios -----

    /// <summary>Listado con filtros + nombre del proveedor, detalle del subtipo y días con cupo de cada servicio.</summary>
    Task<ListaServiciosViewModel> ListaServiciosAsync(string? nombre, string? tipo, ulong? idProveedor, bool incluirInactivos,
        CancellationToken cancellationToken = default);

    /// <summary>Servicios de un proveedor de un tipo (VUELO, HOSPEDAJE…), para la pantalla de alta.</summary>
    Task<List<Servicios>> ServiciosDeProveedorAsync(ulong idProveedor, string tipoServicio, CancellationToken cancellationToken = default);

    Task<Servicios?> ServicioAsync(ulong id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Formulario lleno con los datos del servicio y su subtipo, para "Actualizar". null si no existe o es un PAQUETE
    /// (los paquetes tienen su propia pantalla).
    /// </summary>
    Task<ServicioFormBase?> FormularioAsync(ulong id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Alta de servicio + subtipo + disponibilidad inicial en UNA transacción. Un vuelo con "repetir" crea una salida por fecha.
    /// </summary>
    Task<ResultadoOperacion> CrearServicioAsync(ServicioFormBase datos, ulong idUsuario, CancellationToken cancellationToken = default);

    /// <summary>Actualiza todo menos el tipo (datos generales, política, subtipo e imagen). Las compras pasadas no cambian (R4).</summary>
    Task<ResultadoOperacion> ActualizarServicioAsync(ulong id, ServicioFormBase datos, ulong idUsuario,
        CancellationToken cancellationToken = default);

    Task<ResultadoOperacion> CambiarEstadoServicioAsync(ulong id, bool activo, ulong idUsuario, CancellationToken cancellationToken = default);

    // ----- Disponibilidad -----

    /// <summary>Días de disponibilidad del servicio a partir de <paramref name="desde"/> (60 días). null si no existe.</summary>
    Task<DisponibilidadViewModel?> DisponibilidadAsync(ulong idServicio, DateOnly? desde, CancellationToken cancellationToken = default);

    /// <summary>
    /// Agrega o ajusta el cupo (y opcionalmente el precio) de cada día del rango. Nunca duplica una fecha y nunca baja
    /// el cupo por debajo de lo ya vendido.
    /// </summary>
    Task<ResultadoOperacion> GuardarDisponibilidadAsync(ulong idServicio, DisponibilidadFormViewModel datos, ulong idUsuario,
        CancellationToken cancellationToken = default);
}
