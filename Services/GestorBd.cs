using FlyVibeMex.Data;
using FlyVibeMex.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FlyVibeMex.Services;

/// <summary>Ayudantes de base de datos que comparten los servicios del Gestor de ventas (paquetes y promociones).</summary>
public static class GestorBd
{
    /// <summary>
    /// Ejecuta <paramref name="trabajo"/> en una transacción dentro de la execution strategy (la conexión tiene reintentos
    /// activos): si se reintenta, se repite TODO el bloque desde cero.
    /// </summary>
    public static async Task EnTransaccionAsync(FlyVibeMexDbContext db, Func<Task> trabajo, CancellationToken cancellationToken)
    {
        var estrategia = db.Database.CreateExecutionStrategy();
        await estrategia.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaccion = await db.Database.BeginTransactionAsync(cancellationToken);
            await trabajo();
            await transaccion.CommitAsync(cancellationToken);
        });
    }

    public static bool EsErrorDeBd(Exception ex) =>
        ex is DbUpdateException or SqlException or RetryLimitExceededException or InvalidOperationException;

    public static string? Limpiar(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    /// <summary>
    /// Servicios de los tipos indicados para elegirlos con casillas (incluir en un paquete o ligar a una promoción),
    /// con su proveedor, un detalle legible y la ciudad donde se usan. Solo activos (servicio y proveedor), salvo los
    /// de <paramref name="tambien"/> (los que ya estaban elegidos), para poder quitarlos aunque estén dados de baja.
    /// </summary>
    public static async Task<List<CandidatoServicio>> CandidatosAsync(FlyVibeMexDbContext db, string[] tipos,
        ICollection<ulong> tambien, CancellationToken cancellationToken)
    {
        var filas = await (
                from s in db.Servicios.AsNoTracking()
                join p in db.Proveedores.AsNoTracking() on s.id_proveedor equals p.id
                orderby s.tipo_servicio, s.nombre, s.id
                select new { s.id, s.nombre, s.tipo_servicio, s.id_proveedor, proveedor = p.nombre, p.id_ubicacion, activo = s.activo && p.activo })
            .ToListAsync(cancellationToken);
        // El filtro por tipo se hace en memoria (son pocos servicios) para no depender de OPENJSON en SQL Server.
        filas = filas.Where(f => tipos.Contains(f.tipo_servicio) && (f.activo || tambien.Contains(f.id))).ToList();
        if (filas.Count == 0) return new List<CandidatoServicio>();

        var ciudades = await db.Ubicaciones.AsNoTracking().ToDictionaryAsync(u => u.id, u => u.municipio_ciudad, cancellationToken);
        var vuelos = await db.Vuelos.AsNoTracking()
            .Select(v => new { v.id_servicio, v.id_origen, v.id_destino, v.codigo_vuelo, v.fecha_salida })
            .ToDictionaryAsync(v => v.id_servicio, cancellationToken);
        var hospedajes = await db.Hospedajes.AsNoTracking()
            .Select(h => new { h.id_servicio, h.id_ubicacion, h.tipo_habitacion })
            .ToDictionaryAsync(h => h.id_servicio, cancellationToken);
        var vehiculos = await db.Vehiculos.AsNoTracking()
            .Select(v => new { v.id_servicio, v.id_ubicacion_retiro, v.marca, v.modelo })
            .ToDictionaryAsync(v => v.id_servicio, cancellationToken);
        var paquetes = await db.Paquetes.AsNoTracking()
            .Select(p => new { p.id_servicio, p.duracion_dias, p.id_ubicacion_destino })
            .ToDictionaryAsync(p => p.id_servicio, cancellationToken);

        string Ciudad(ulong? id) => id is ulong x ? ciudades.GetValueOrDefault(x, "?") : "?";

        return filas.Select(f =>
        {
            var c = new CandidatoServicio
            {
                id = f.id,
                nombre = f.nombre,
                tipo_servicio = f.tipo_servicio,
                id_proveedor = f.id_proveedor,
                proveedor = f.proveedor,
                activo = f.activo
            };
            switch (f.tipo_servicio)
            {
                case "VUELO" when vuelos.TryGetValue(f.id, out var v):
                    c.id_ciudad = v.id_destino;
                    c.detalle = $"{v.codigo_vuelo} · {Ciudad(v.id_origen)} → {Ciudad(v.id_destino)} · {v.fecha_salida:dd/MM/yyyy HH:mm}";
                    break;
                case "HOSPEDAJE" when hospedajes.TryGetValue(f.id, out var h):
                    c.id_ciudad = h.id_ubicacion;
                    c.detalle = $"{h.tipo_habitacion} · {Ciudad(h.id_ubicacion)}";
                    break;
                case "VEHICULO" when vehiculos.TryGetValue(f.id, out var ve):
                    c.id_ciudad = ve.id_ubicacion_retiro;
                    c.detalle = $"{ve.marca} {ve.modelo} · {Ciudad(ve.id_ubicacion_retiro)}";
                    break;
                case "EVENTO":
                    c.id_ciudad = f.id_ubicacion;
                    c.detalle = f.id_ubicacion is null ? "Proveedor sin ubicación" : Ciudad(f.id_ubicacion);
                    break;
                case "PAQUETE" when paquetes.TryGetValue(f.id, out var pq):
                    c.id_ciudad = pq.id_ubicacion_destino;
                    c.detalle = $"{pq.duracion_dias} día(s){(pq.id_ubicacion_destino is null ? "" : " · " + Ciudad(pq.id_ubicacion_destino))}";
                    break;
            }
            return c;
        }).ToList();
    }
}
