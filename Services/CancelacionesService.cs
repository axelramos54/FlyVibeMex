using FlyVibeMex.Data;
using FlyVibeMex.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FlyVibeMex.Services;

/// <summary>Implementación de <see cref="ICancelacionesService"/> con EF Core (consultas LINQ) y bitácora.</summary>
/// <remarks>
/// Misma lógica que los procedimientos dbo.pa_cancelar_reservacion y dbo.pa_resolver_reembolso
/// (sql/02_procedimientos_almacenados.sql), que la aplicación no llama.
/// </remarks>
public sealed class CancelacionesService : ICancelacionesService
{
    private const string EstadoCancelada = "CANCELADA";
    private const string PagoAprobado = "APROBADO";
    private const string PagoReembolsado = "REEMBOLSADO";
    private const string ReembolsoSolicitado = "SOLICITADO";
    private const string ReembolsoAprobado = "APROBADO";
    private const string ReembolsoProcesado = "PROCESADO";
    private const string ReembolsoRechazado = "RECHAZADO";

    private readonly FlyVibeMexDbContext _db;
    private readonly ILogger<CancelacionesService> _logger;

    public CancelacionesService(FlyVibeMexDbContext db, ILogger<CancelacionesService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // =============================================================================================
    // Opciones (la misma regla para el cliente y para el personal)
    // =============================================================================================

    public async Task<OpcionesCancelacion?> OpcionesAsync(ulong idReservacion, CancellationToken cancellationToken = default)
    {
        var cabecera = await (
                from r in _db.Reservaciones.AsNoTracking()
                join e in _db.EstadosReservacion.AsNoTracking() on r.id_estado_reservacion equals e.id
                where r.id == idReservacion
                select new { r.moneda, e.es_final })
            .FirstOrDefaultAsync(cancellationToken);
        if (cabecera is null) return null;

        var lineas = await _db.DetallesReservacion.AsNoTracking()
            .Where(d => d.id_reservacion == idReservacion)
            .OrderBy(d => d.id)
            .Select(d => new { d.fecha_inicio, d.permite_cancelacion, d.horas_reembolso_total, d.porcentaje_reembolso_tardio, d.politica_cancelacion })
            .ToListAsync(cancellationToken);
        if (lineas.Count == 0)
            return new OpcionesCancelacion { moneda = cabecera.moneda, motivo_no_cancelar = "La reservación no tiene detalle.", motivo_no_reembolsar = "La reservación no tiene detalle." };

        var pagos = await (
                from p in _db.Pagos.AsNoTracking()
                join e in _db.EstadosPago.AsNoTracking() on p.id_estado_pago equals e.id
                where p.id_reservacion == idReservacion && e.nombre == PagoAprobado
                orderby p.id descending
                select p.monto)
            .Take(1)
            .ToListAsync(cancellationToken);

        // La política es la de la primera línea (en esta versión cada reservación tiene un solo servicio).
        var primera = lineas[0];
        return ReglasCancelacion.Calcular(
            cabecera.es_final, lineas.Min(l => l.fecha_inicio), primera.permite_cancelacion, primera.horas_reembolso_total,
            primera.porcentaje_reembolso_tardio, primera.politica_cancelacion,
            pagos.Count > 0 ? pagos[0] : null, cabecera.moneda, DateTime.Now);
    }

    // =============================================================================================
    // Cancelación del personal (panel de Reservaciones)
    // =============================================================================================

    public async Task<ResultadoOperacion> CancelarComoPersonalAsync(ulong idReservacion, ulong idUsuario, string? modo, string? motivo,
        CancellationToken cancellationToken = default)
    {
        motivo = GestorBd.Limpiar(motivo);
        if (motivo is null || motivo.Length < 10 || motivo.Length > 250)
            return ResultadoOperacion.Falla("Escribe el motivo de la cancelación (10 a 250 caracteres).");
        modo = modo?.Trim().ToUpperInvariant();
        if (modo is not ("SIN" or "POLITICA" or "CORTESIA"))
            return ResultadoOperacion.Falla("Elige qué pasa con el dinero.");

        // Se recalcula todo aquí (nunca se confía en lo que mostró el navegador).
        var opciones = await OpcionesAsync(idReservacion, cancellationToken);
        if (opciones is null) return ResultadoOperacion.Falla("La reservación no existe.");
        if (!opciones.puede_cancelar)
            return ResultadoOperacion.Falla(opciones.motivo_no_cancelar ?? "Esta reservación no se puede cancelar.");

        var reembolso = ModoReembolso.Ninguno;
        decimal monto = 0;
        string? detalle = null;
        if (modo == "POLITICA")
        {
            if (!opciones.puede_reembolsar)
                return ResultadoOperacion.Falla(opciones.motivo_no_reembolsar ?? "Según la política de la compra no hay reembolso.");
            reembolso = ModoReembolso.Procesar;
            monto = opciones.monto_reembolso;
            detalle = $"Solicitado por el personal: {opciones.porcentaje:0.##} % según la política de la compra.";
        }
        else if (modo == "CORTESIA")
        {
            if (opciones.monto_pagado is not decimal pagado || pagado <= 0)
                return ResultadoOperacion.Falla("No hay un pago aprobado que reembolsar.");
            reembolso = ModoReembolso.Procesar;
            monto = pagado;
            detalle = $"Solicitado por el personal: 100 % de cortesía (la política daba {opciones.porcentaje:0.##} %).";
        }

        var resultado = await EjecutarAsync(
            new OrdenCancelacion(idReservacion, idUsuario, null, true, motivo, reembolso, monto, detalle), cancellationToken);

        return resultado switch
        {
            ResultadoCancelacion.Ok when reembolso == ModoReembolso.Procesar =>
                ResultadoOperacion.Ok($"Reservación cancelada y reembolso de ${monto:N2} {opciones.moneda} procesado (pago REEMBOLSADO)."),
            ResultadoCancelacion.Ok => ResultadoOperacion.Ok("Reservación cancelada sin reembolso."),
            ResultadoCancelacion.Cambio => ResultadoOperacion.Falla("La reservación o su pago cambiaron mientras la cancelabas. Revisa su estado."),
            _ => ResultadoOperacion.Falla("No se pudo cancelar la reservación. Intenta de nuevo.")
        };
    }

    // =============================================================================================
    // Ejecución (una transacción)
    // =============================================================================================

    public async Task<ResultadoCancelacion> EjecutarAsync(OrdenCancelacion orden, CancellationToken cancellationToken = default)
    {
        try
        {
            var estrategia = _db.Database.CreateExecutionStrategy();
            await estrategia.ExecuteAsync(() => EjecutarEnTransaccionAsync(orden, cancellationToken));
        }
        catch (CambioConcurrenteException)
        {
            return ResultadoCancelacion.Cambio;
        }
        catch (Exception ex) when (ex is DbUpdateException or SqlException or RetryLimitExceededException or InvalidOperationException)
        {
            _logger.LogError(ex, "No se pudo cancelar la reservación {IdReservacion}.", orden.IdReservacion);
            return ResultadoCancelacion.ErrorBd;
        }

        _logger.LogInformation("Reservación {IdReservacion} cancelada por {IdUsuario} (personal: {Personal}, reembolso: {Reembolso}).",
            orden.IdReservacion, orden.IdUsuario, orden.PorPersonal, orden.Reembolso);
        return ResultadoCancelacion.Ok;
    }

    /// <summary>Pasos de la cancelación (dentro de la execution strategy: si se reintenta, se repite todo).</summary>
    private async Task EjecutarEnTransaccionAsync(OrdenCancelacion orden, CancellationToken cancellationToken)
    {
        _db.ChangeTracker.Clear();
        await using var transaccion = await _db.Database.BeginTransactionAsync(cancellationToken);
        var ahora = DateTime.Now;
        var idReservacion = orden.IdReservacion;

        var idCancelada = await IdEstadoReservacionAsync(EstadoCancelada, cancellationToken);

        // a) Estado actual (debe seguir sin ser final; si se pide, del dueño) y UPDATE condicional: si otra petición
        //    la canceló al mismo tiempo no se actualiza ninguna fila y se deshace todo.
        var reservas = _db.Reservaciones.AsNoTracking().Where(r => r.id == idReservacion);
        if (orden.IdDueno is ulong dueno) reservas = reservas.Where(r => r.id_usuarioglobal == dueno);
        var actual = await (
                from r in reservas
                join e in _db.EstadosReservacion.AsNoTracking() on r.id_estado_reservacion equals e.id
                select new { r.id_estado_reservacion, r.codigo_reserva, r.moneda, e.es_final })
            .FirstOrDefaultAsync(cancellationToken);
        if (actual is null || actual.es_final) throw new CambioConcurrenteException();

        var estadoAnterior = actual.id_estado_reservacion;
        var filas = await _db.Reservaciones
            .Where(r => r.id == idReservacion && r.id_estado_reservacion == estadoAnterior)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.id_estado_reservacion, idCancelada)
                .SetProperty(r => r.actualizada_en, ahora), cancellationToken);
        if (filas != 1) throw new CambioConcurrenteException();

        var conReembolso = orden.Reembolso != ModoReembolso.Ninguno;
        _db.HistorialEstadosReservacion.Add(new HistorialEstadoReservacion
        {
            id_reservacion = idReservacion,
            id_estado_reservacion = idCancelada,
            id_usuario_cambio = orden.IdUsuario,
            fecha_cambio = ahora,
            motivo = orden.PorPersonal
                ? $"A nombre del cliente: {orden.Motivo}"
                : orden.Motivo ?? (conReembolso ? "Cancelada por el cliente con reembolso" : "Cancelada por el cliente")
        });

        // b) Devolver el cupo (R3) de cada servicio de la reservación. Nunca por encima de cantidad_total.
        var detalles = await (
                from d in _db.DetallesReservacion.AsNoTracking()
                join s in _db.Servicios.AsNoTracking() on d.id_servicio equals s.id
                where d.id_reservacion == idReservacion
                select new { d.id_servicio, s.tipo_servicio, d.fecha_inicio, d.fecha_fin, d.cantidad })
            .ToListAsync(cancellationToken);

        foreach (var d in detalles)
        {
            var (desde, hasta) = FechasDeInventario(d.tipo_servicio, d.fecha_inicio, d.fecha_fin);
            var cantidad = (int)Math.Ceiling(d.cantidad);
            var idServicio = d.id_servicio;
            await _db.DisponibilidadServicios
                .Where(x => x.id_servicio == idServicio && x.fecha >= desde && x.fecha <= hasta
                            && x.cantidad_disponible + cantidad <= x.cantidad_total)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.cantidad_disponible, x => x.cantidad_disponible + cantidad)
                    .SetProperty(x => x.actualizado_en, ahora), cancellationToken);
            // Si una fecha no tiene fila (p. ej. compras de demostración fuera de la ventana del seed) se ignora.
        }

        // c) Reembolso del pago aprobado (R8) + sus historiales.
        if (conReembolso)
        {
            var idsPago = await (
                    from p in _db.Pagos.AsNoTracking()
                    join e in _db.EstadosPago.AsNoTracking() on p.id_estado_pago equals e.id
                    where p.id_reservacion == idReservacion && e.nombre == PagoAprobado
                    orderby p.id descending
                    select p.id)
                .Take(1)
                .ToListAsync(cancellationToken);
            if (idsPago.Count == 0) throw new CambioConcurrenteException();

            var procesar = orden.Reembolso == ModoReembolso.Procesar;
            var referencia = procesar ? NuevaReferencia() : null;
            var reembolso = new Reembolsos
            {
                id_pago = idsPago[0],
                monto = orden.MontoReembolso,
                motivo = orden.Motivo ?? "Cancelación",
                estado = procesar ? ReembolsoProcesado : ReembolsoSolicitado,
                fecha_solicitud = ahora,
                fecha_resolucion = procesar ? ahora : null,
                referencia_externa = referencia
            };
            _db.Reembolsos.Add(reembolso);
            await _db.SaveChangesAsync(cancellationToken);

            _db.HistorialEstadosReembolso.Add(new HistorialEstadoReembolso
            {
                id_reembolso = reembolso.id,
                id_usuario_cambio = orden.IdUsuario,
                estado = ReembolsoSolicitado,
                fecha_cambio = ahora,
                detalle = orden.DetalleReembolso ?? "Solicitado al cancelar"
            });

            if (procesar)
            {
                _db.HistorialEstadosReembolso.Add(new HistorialEstadoReembolso
                {
                    id_reembolso = reembolso.id,
                    id_usuario_cambio = orden.IdUsuario,
                    estado = ReembolsoAprobado,
                    fecha_cambio = ahora.AddSeconds(1),
                    detalle = "Aprobado al cancelar a nombre del cliente"
                });
                _db.HistorialEstadosReembolso.Add(new HistorialEstadoReembolso
                {
                    id_reembolso = reembolso.id,
                    id_usuario_cambio = orden.IdUsuario,
                    estado = ReembolsoProcesado,
                    fecha_cambio = ahora.AddSeconds(2),
                    detalle = $"Devolución simulada {referencia}"
                });
                await MarcarPagoReembolsadoAsync(idsPago[0], orden.MontoReembolso, actual.moneda, ahora.AddSeconds(2), cancellationToken);
            }
        }

        // d) Bitácora del panel (solo cuando cancela el personal).
        if (orden.PorPersonal)
        {
            var dinero = orden.Reembolso switch
            {
                ModoReembolso.Procesar => $" con reembolso procesado de ${orden.MontoReembolso:N2} {actual.moneda}",
                ModoReembolso.Solicitar => $" con reembolso solicitado de ${orden.MontoReembolso:N2} {actual.moneda}",
                _ => " sin reembolso"
            };
            _db.RegistrarBitacora(orden.IdUsuario, "CANCELAR", "reservaciones", idReservacion,
                $"Cancelación de {actual.codigo_reserva} a nombre del cliente{dinero}: {orden.Motivo}");
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaccion.CommitAsync(cancellationToken);
    }

    // =============================================================================================
    // Resolver un reembolso SOLICITADO
    // =============================================================================================

    /// <summary>
    /// Un agente aprueba o rechaza un reembolso que está en <c>SOLICITADO</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Al aprobar se escriben DOS filas de historial</b>, no una: la máquina de estados del reembolso es
    /// <c>SOLICITADO → APROBADO / RECHAZADO → PROCESADO</c> (R8), y como aquí la pasarela es simulada los dos
    /// últimos pasos ocurren en el mismo instante. Para que al ordenar por <c>fecha_cambio</c> queden en el
    /// orden correcto, al segundo se le suma 1 segundo: sin eso el historial podría leerse al revés.
    /// </para>
    /// <para>
    /// <b>La protección contra dos agentes a la vez está en el UPDATE condicional</b>
    /// (<c>estado == SOLICITADO</c>), no en la lectura de arriba. Esa lectura solo sirve para dar un mensaje
    /// claro; si entre ella y la transacción otro agente resuelve el mismo reembolso, el UPDATE afecta 0 filas
    /// y <c>CambioConcurrenteException</c> deshace todo.
    /// </para>
    /// <para>
    /// <b><c>referenciaGuardar</c> es una copia local a propósito:</b> con reintentos activos
    /// (<c>EnableRetryOnFailure</c>) el cuerpo de la transacción puede ejecutarse más de una vez, así que la
    /// referencia se vuelve a generar en cada intento y el valor que se guarda tiene que ser el de ESE intento.
    /// </para>
    /// </remarks>
    public async Task<ResultadoOperacion> ResolverReembolsoAsync(ulong idReembolso, bool aprobar, string? motivo, ulong idUsuario,
        CancellationToken cancellationToken = default)
    {
        // ---- Paso 1: validar el motivo. Rechazar SIEMPRE exige uno, porque el cliente lo va a leer ----
        motivo = GestorBd.Limpiar(motivo);
        if (!aprobar && (motivo is null || motivo.Length < 10))
            return ResultadoOperacion.Falla("Para rechazar escribe el motivo (10 a 250 caracteres): es lo que verá el cliente.");
        if (motivo is { Length: > 250 })
            return ResultadoOperacion.Falla("El motivo puede tener máximo 250 caracteres.");

        // ---- Paso 2: leer el reembolso con su pago y su reservación (para el monto, la moneda y el código) ----
        var datos = await (
                from rb in _db.Reembolsos.AsNoTracking()
                join p in _db.Pagos.AsNoTracking() on rb.id_pago equals p.id
                join r in _db.Reservaciones.AsNoTracking() on p.id_reservacion equals r.id
                where rb.id == idReembolso
                select new { rb.estado, rb.monto, rb.id_pago, p.moneda, r.codigo_reserva })
            .FirstOrDefaultAsync(cancellationToken);
        if (datos is null) return ResultadoOperacion.Falla("El reembolso no existe.");
        if (datos.estado != ReembolsoSolicitado)
            return ResultadoOperacion.Falla($"Este reembolso ya fue resuelto ({datos.estado}).");

        // ---- Paso 3: resolver, todo dentro de una transacción ----
        // `referencia` se declara AQUÍ, fuera, porque el mensaje de éxito se arma después del commit.
        string? referencia = null;
        try
        {
            await GestorBd.EnTransaccionAsync(_db, async () =>
            {
                var ahora = DateTime.Now;
                referencia = aprobar ? NuevaReferencia() : null;
                var nuevoEstado = aprobar ? ReembolsoProcesado : ReembolsoRechazado;
                var referenciaGuardar = referencia;

                // 3.a) UPDATE condicional: es el candado real contra dos agentes resolviendo a la vez.
                var filas = await _db.Reembolsos
                    .Where(rb => rb.id == idReembolso && rb.estado == ReembolsoSolicitado)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(rb => rb.estado, nuevoEstado)
                        .SetProperty(rb => rb.fecha_resolucion, ahora)
                        .SetProperty(rb => rb.referencia_externa, referenciaGuardar), cancellationToken);
                if (filas != 1) throw new CambioConcurrenteException();

                // 3.b) Bitácora del reembolso (R6) y, si se aprueba, el pago pasa a REEMBOLSADO.
                if (aprobar)
                {
                    _db.HistorialEstadosReembolso.Add(new HistorialEstadoReembolso
                    {
                        id_reembolso = idReembolso,
                        id_usuario_cambio = idUsuario,
                        estado = ReembolsoAprobado,
                        fecha_cambio = ahora,
                        detalle = motivo ?? "Aprobado por el personal"
                    });
                    _db.HistorialEstadosReembolso.Add(new HistorialEstadoReembolso
                    {
                        id_reembolso = idReembolso,
                        id_usuario_cambio = idUsuario,
                        estado = ReembolsoProcesado,
                        fecha_cambio = ahora.AddSeconds(1),
                        detalle = $"Devolución simulada {referenciaGuardar}"
                    });
                    await MarcarPagoReembolsadoAsync(datos.id_pago, datos.monto, datos.moneda, ahora.AddSeconds(1), cancellationToken);
                }
                else
                {
                    _db.HistorialEstadosReembolso.Add(new HistorialEstadoReembolso
                    {
                        id_reembolso = idReembolso,
                        id_usuario_cambio = idUsuario,
                        estado = ReembolsoRechazado,
                        fecha_cambio = ahora,
                        detalle = motivo!
                    });
                }

                _db.RegistrarBitacora(idUsuario, aprobar ? "APROBAR" : "RECHAZAR", "reembolsos", idReembolso,
                    aprobar
                        ? $"Reembolso de {datos.codigo_reserva} por ${datos.monto:N2} {datos.moneda} aprobado y procesado ({referenciaGuardar})."
                        : $"Reembolso de {datos.codigo_reserva} por ${datos.monto:N2} {datos.moneda} rechazado: {motivo}");
                await _db.SaveChangesAsync(cancellationToken);
            }, cancellationToken);
        }
        // ---- Paso 4: traducir los dos fallos posibles a un mensaje que el agente entienda ----
        catch (CambioConcurrenteException)
        {
            return ResultadoOperacion.Falla("Otra persona resolvió este reembolso (o cambió el pago) al mismo tiempo. Revisa su estado.");
        }
        catch (Exception ex) when (GestorBd.EsErrorDeBd(ex))
        {
            _logger.LogError(ex, "No se pudo resolver el reembolso {IdReembolso}.", idReembolso);
            return ResultadoOperacion.Falla("No se pudo guardar. Intenta de nuevo.");
        }

        _logger.LogInformation("Usuario {IdUsuario}: reembolso {IdReembolso} {Accion}.", idUsuario, idReembolso, aprobar ? "aprobado" : "rechazado");
        return aprobar
            ? ResultadoOperacion.Ok($"Reembolso de {datos.codigo_reserva} aprobado: ${datos.monto:N2} {datos.moneda} devueltos (ref. {referencia}).")
            : ResultadoOperacion.Ok($"Reembolso de {datos.codigo_reserva} rechazado. El cliente verá el motivo en Mis compras.");
    }

    // =============================================================================================
    // Ayudantes
    // =============================================================================================

    /// <summary>
    /// Pago APROBADO → REEMBOLSADO (también si el reembolso es parcial: el monto real queda en el reembolso y en el
    /// historial del pago). UPDATE condicional: si el pago ya no está aprobado se deshace todo.
    /// </summary>
    private async Task MarcarPagoReembolsadoAsync(ulong idPago, decimal monto, string moneda, DateTime fecha, CancellationToken cancellationToken)
    {
        var idAprobado = await IdEstadoPagoAsync(PagoAprobado, cancellationToken);
        var idReembolsado = await IdEstadoPagoAsync(PagoReembolsado, cancellationToken);

        var filas = await _db.Pagos
            .Where(p => p.id == idPago && p.id_estado_pago == idAprobado)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.id_estado_pago, idReembolsado), cancellationToken);
        if (filas != 1) throw new CambioConcurrenteException();

        _db.HistorialEstadosPago.Add(new HistorialEstadoPago
        {
            id_pago = idPago,
            id_estado_pago = idReembolsado,
            fecha_cambio = fecha,
            detalle = $"Reembolso de ${monto:N2} {moneda}"
        });
    }

    /// <summary>Referencia de la devolución simulada (igual que el procedimiento: RSIM- + 16 caracteres).</summary>
    private static string NuevaReferencia() => "RSIM-" + Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();

    /// <summary>
    /// Días de disponibilidad que ocupa un servicio comprado: hospedaje y vehículo de la fecha de inicio a la víspera
    /// de la fecha de fin (noches / días de renta); vuelo, tour y paquete, solo el día de inicio.
    /// </summary>
    private static (DateOnly desde, DateOnly hasta) FechasDeInventario(string tipo, DateTime inicio, DateTime fin)
    {
        var desde = DateOnly.FromDateTime(inicio);
        if (TipoCatalogo.UsaRango(tipo))
        {
            var ultimo = DateOnly.FromDateTime(fin).AddDays(-1);
            return (desde, ultimo < desde ? desde : ultimo);
        }
        return (desde, desde);
    }

    private async Task<ushort> IdEstadoReservacionAsync(string nombre, CancellationToken cancellationToken)
    {
        var ids = await _db.EstadosReservacion.AsNoTracking().Where(e => e.nombre == nombre).Select(e => e.id).Take(1).ToListAsync(cancellationToken);
        return ids.Count > 0
            ? ids[0]
            : throw new InvalidOperationException($"Falta el estado de reservación '{nombre}'. Ejecuta sql/01_crear_base_datos.sql.");
    }

    private async Task<ushort> IdEstadoPagoAsync(string nombre, CancellationToken cancellationToken)
    {
        var ids = await _db.EstadosPago.AsNoTracking().Where(e => e.nombre == nombre).Select(e => e.id).Take(1).ToListAsync(cancellationToken);
        return ids.Count > 0
            ? ids[0]
            : throw new InvalidOperationException($"Falta el estado de pago '{nombre}'. Ejecuta sql/01_crear_base_datos.sql.");
    }

    /// <summary>La reservación, el reembolso o el pago ya no están como cuando se validaron (otra petición los cambió).</summary>
    private sealed class CambioConcurrenteException : Exception
    {
        public CambioConcurrenteException() : base("El registro cambió durante la operación.") { }
    }
}
