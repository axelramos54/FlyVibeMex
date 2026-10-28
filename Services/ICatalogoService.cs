using FlyVibeMex.Models;

namespace FlyVibeMex.Services;

/// <summary>
/// Catálogo público: listas de destinos, búsqueda por sección, detalle y cotización de un servicio.
/// Todas las consultas son LINQ (parametrizadas). Solo se muestran servicios activos de proveedores activos,
/// en ubicaciones activas, y con el subtipo correspondiente.
/// </summary>
public interface ICatalogoService
{
    /// <summary>
    /// Llena <see cref="BusquedaCatalogoViewModel.Destinos"/>, <c>Origenes</c> y <c>Resultados</c> de la sección
    /// <c>busqueda.tipo</c>. Si <paramref name="aplicarFechas"/> es <c>false</c> (no hay fechas o no son válidas),
    /// muestra lo disponible en los próximos 30 días con precio "desde".
    /// </summary>
    Task LlenarSeccionAsync(BusquedaCatalogoViewModel busqueda, bool aplicarFechas, CancellationToken cancellationToken = default);

    /// <summary>Tipo (VUELO, HOSPEDAJE…) de un servicio activo del catálogo, o <c>null</c> si no existe / no se vende.</summary>
    Task<string?> TipoDeServicioAsync(ulong idServicio, CancellationToken cancellationToken = default);

    /// <summary>
    /// Datos para la página de detalle. <c>busqueda.tipo</c> debe venir asignado (ver <see cref="TipoDeServicioAsync"/>).
    /// Si <paramref name="cotizar"/> es <c>true</c> y hay fechas (o es un vuelo), calcula precio y cupo.
    /// </summary>
    Task<DetalleServicioViewModel?> DetalleAsync(ulong idServicio, BusquedaCatalogoViewModel busqueda, bool cotizar,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Precio y cupo de un servicio para la búsqueda indicada (lo usará la reserva en la fase C, que SIEMPRE debe
    /// recalcular en el servidor y nunca confiar en un precio enviado por el navegador).
    /// Devuelve <c>null</c> si el servicio no existe o no está a la venta.
    /// </summary>
    Task<CotizacionServicio?> CotizarAsync(ulong idServicio, BusquedaCatalogoViewModel busqueda, CancellationToken cancellationToken = default);
}
