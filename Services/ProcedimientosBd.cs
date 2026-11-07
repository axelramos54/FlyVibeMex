// ███ ESTE ARCHIVO VA EN EL PROYECTO DE FLYVIBEMEX ███
// Ruta: Services/ProcedimientosBd.cs

using System.Data;
using FlyVibeMex.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;   // GetDbTransaction()

namespace FlyVibeMex.Services;

/// <summary>
/// Error de NEGOCIO lanzado por un procedimiento almacenado: los <c>THROW 51xxx</c> de sql/02, con mensajes
/// en español ya pensados para mostrarse al usuario ("Sin cupo suficiente el 12/11/2026.", "El ticket ya
/// está cerrado."…).
///
/// Se separa de los errores técnicos a propósito: un 51xxx NO es una falla del sistema, es el procedimiento
/// diciendo que la operación no procede. Los servicios lo convierten en <c>ResultadoOperacion.Falla</c> con
/// ese mismo texto; cualquier otro error de SQL Server se registra en el log y al usuario solo le llega un
/// mensaje genérico.
/// </summary>
public sealed class ReglaProcedimientoException : Exception
{
    /// <summary>Número del THROW (51001, 51128…). Útil si algún día hay que distinguir casos concretos.</summary>
    public int Numero { get; }

    public ReglaProcedimientoException(int numero, string mensaje) : base(mensaje) => Numero = numero;
}

/// <summary>
/// Llama a los procedimientos almacenados de sql/02 desde los servicios.
///
/// ============================  POR QUÉ ASÍ Y NO CON FromSqlRaw  ============================
/// Se usa <see cref="SqlCommand"/> con <c>CommandType.StoredProcedure</c>: el nombre del procedimiento y los
/// parámetros viajan por canales distintos, así que no existe forma de concatenar texto en la consulta. Es la
/// manera más segura y cumple la regla del DbContext (prohibido FromSqlRaw/ExecuteSqlRaw con interpolación).
///
/// OTRAS DECISIONES:
///   · Se usa la execution strategy del DbContext porque la conexión tiene reintentos activos
///     (EnableRetryOnFailure en Program.cs). Sin esto, un fallo transitorio de red tira la operación.
///   · NO se abre transacción desde C#: cada procedimiento maneja la suya (BEGIN TRANSACTION … COMMIT) y,
///     desde el 05-11-2026, su propio TRY/CATCH que deshace y registra en dbo.LogError. Abrir otra aquí
///     anidaría transacciones sin ganar nada.
///   · Los errores 51xxx se traducen a <see cref="ReglaProcedimientoException"/>; el resto sube tal cual para
///     que el servicio lo registre como falla técnica.
/// ============================================================================================
/// </summary>
public static class ProcedimientosBd
{
    /// <summary>Rango de los THROW de negocio de este proyecto (ver sql/02).</summary>
    private const int NumeroNegocioMinimo = 51000;
    private const int NumeroNegocioMaximo = 51999;

    // =============================================================================================
    // Construcción de parámetros
    // =============================================================================================

    /// <summary>Parámetro de entrada. Un valor nulo se manda como NULL, no como cadena vacía.</summary>
    public static SqlParameter Par(string nombre, object? valor) =>
        new(nombre, valor ?? DBNull.Value);

    /// <summary>Parámetro de salida (los OUTPUT de los procedimientos, p. ej. @codigo_reserva).</summary>
    public static SqlParameter Salida(string nombre, SqlDbType tipo, int tamano = 0)
    {
        var p = tamano > 0 ? new SqlParameter(nombre, tipo, tamano) : new SqlParameter(nombre, tipo);
        p.Direction = ParameterDirection.Output;
        return p;
    }

    // =============================================================================================
    // Ejecución
    // =============================================================================================

    /// <summary>
    /// Ejecuta un procedimiento que no necesita devolver filas (altas, bajas, cambios de estado…).
    /// Si el procedimiento trae un SELECT final de confirmación, simplemente se ignora.
    /// </summary>
    public static Task EjecutarAsync(
        FlyVibeMexDbContext db, string procedimiento, SqlParameter[] parametros, CancellationToken cancellationToken) =>
        ConCommandAsync(db, procedimiento, parametros, async cmd =>
        {
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            return 0;
        }, cancellationToken);

    /// <summary>
    /// Ejecuta un procedimiento y devuelve las filas de su PRIMER conjunto de resultados, convertidas con
    /// <paramref name="mapear"/>. Para los procedimientos que devuelven varios conjuntos (como
    /// pa_reporte_ventas) está <see cref="LeerVariosAsync{T}"/>.
    /// </summary>
    public static Task<List<T>> LeerAsync<T>(
        FlyVibeMexDbContext db, string procedimiento, SqlParameter[] parametros,
        Func<SqlDataReader, T> mapear, CancellationToken cancellationToken) =>
        ConCommandAsync(db, procedimiento, parametros, async cmd =>
        {
            var filas = new List<T>();
            await using var lector = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await lector.ReadAsync(cancellationToken))
            {
                filas.Add(mapear(lector));
            }
            return filas;
        }, cancellationToken);

    /// <summary>
    /// Ejecuta un procedimiento que devuelve VARIOS conjuntos de resultados y los lee en orden.
    /// Se pasa un convertidor por conjunto; sobran o faltan conjuntos sin error (la lista resultante tiene
    /// tantas entradas como conjuntos haya devuelto el procedimiento).
    /// </summary>
    public static Task<List<List<T>>> LeerVariosAsync<T>(
        FlyVibeMexDbContext db, string procedimiento, SqlParameter[] parametros,
        IReadOnlyList<Func<SqlDataReader, T>> mapeadores, CancellationToken cancellationToken) =>
        ConCommandAsync(db, procedimiento, parametros, async cmd =>
        {
            var conjuntos = new List<List<T>>();
            await using var lector = await cmd.ExecuteReaderAsync(cancellationToken);
            var indice = 0;
            do
            {
                var filas = new List<T>();
                if (indice < mapeadores.Count)
                {
                    var mapear = mapeadores[indice];
                    while (await lector.ReadAsync(cancellationToken))
                    {
                        filas.Add(mapear(lector));
                    }
                }
                conjuntos.Add(filas);
                indice++;
            }
            while (await lector.NextResultAsync(cancellationToken));
            return conjuntos;
        }, cancellationToken);

    // =============================================================================================
    // Plomería común
    // =============================================================================================

    /// <summary>
    /// Abre la conexión del DbContext (si hiciera falta), arma el comando y lo ejecuta dentro de la execution
    /// strategy. Traduce los THROW 51xxx a <see cref="ReglaProcedimientoException"/>.
    /// </summary>
    private static async Task<TResultado> ConCommandAsync<TResultado>(
        FlyVibeMexDbContext db, string procedimiento, SqlParameter[] parametros,
        Func<SqlCommand, Task<TResultado>> trabajo, CancellationToken cancellationToken)
    {
        var estrategia = db.Database.CreateExecutionStrategy();

        try
        {
            return await estrategia.ExecuteAsync(async () =>
            {
                var conexion = (SqlConnection)db.Database.GetDbConnection();
                var abiertaAqui = conexion.State != ConnectionState.Open;
                if (abiertaAqui)
                {
                    await conexion.OpenAsync(cancellationToken);
                }

                try
                {
                    await using var cmd = conexion.CreateCommand();
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.CommandText = procedimiento;
                    cmd.CommandTimeout = db.Database.GetCommandTimeout() ?? 30;

                    // Si el DbContext ya está dentro de una transacción (no es el caso habitual, pero puede
                    // pasar si alguien llama desde GestorBd.EnTransaccionAsync), el comando se enlista en ella.
                    var transaccion = db.Database.CurrentTransaction?.GetDbTransaction();
                    if (transaccion is SqlTransaction st)
                    {
                        cmd.Transaction = st;
                    }

                    // Los SqlParameter no se pueden reutilizar entre comandos: si la execution strategy
                    // reintenta, hay que volver a agregarlos a un comando nuevo. Por eso se limpia la
                    // colección y se agregan los MISMOS objetos, que así quedan listos para el reintento.
                    cmd.Parameters.Clear();
                    foreach (var p in parametros)
                    {
                        cmd.Parameters.Add(p);
                    }

                    var resultado = await trabajo(cmd);

                    // Soltar los parámetros del comando antes de desecharlo: si no, un reintento los
                    // encontraría todavía "pertenecientes" a otro comando y SqlClient se queja.
                    cmd.Parameters.Clear();
                    return resultado;
                }
                finally
                {
                    if (abiertaAqui && conexion.State == ConnectionState.Open)
                    {
                        await conexion.CloseAsync();
                    }
                }
            });
        }
        catch (SqlException ex) when (ex.Number >= NumeroNegocioMinimo && ex.Number <= NumeroNegocioMaximo)
        {
            // Regla de negocio del procedimiento: el mensaje ya está escrito para el usuario.
            // El error TAMBIÉN quedó registrado en dbo.LogError por el CATCH del propio procedimiento.
            throw new ReglaProcedimientoException(ex.Number, ex.Message);
        }
    }
}
