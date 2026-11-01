using System.Diagnostics;
using FlyVibeMex.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FlyVibeMex.Services;

/// <summary>Respuesta de <see cref="IDiagnosticoBdService.DiagnosticarAsync"/>: código HTTP y cuerpo JSON.</summary>
public sealed record ResultadoDiagnosticoBd(int CodigoHttp, object Cuerpo);

/// <summary>Resultado de la verificación de una tabla.</summary>
public sealed record VerificacionTabla(string tabla, bool ok, int? filas, string? error);

/// <summary>
/// Diagnóstico de la conexión con SQL Server para <c>GET /Home/TestDb</c> (solo Development). T8.1: antes vivía en
/// HomeController. Devuelve el modo activo (Test / Real), si hubo conexión, una consulta LINQ de prueba y la
/// verificación de las 30 tablas. Nunca devuelve servidor, usuario, contraseña, hashes ni datos personales.
/// </summary>
public interface IDiagnosticoBdService
{
    Task<ResultadoDiagnosticoBd> DiagnosticarAsync(CancellationToken cancellationToken = default);
}

/// <summary>Implementación de <see cref="IDiagnosticoBdService"/> con EF Core.</summary>
/// <remarks>
/// El DbContext llega como <c>Lazy&lt;FlyVibeMexDbContext&gt;</c> (Services/ServicioDiferido.cs): si la cadena de
/// conexión no está configurada, el servicio responde "sin configurar" sin llegar a crear el DbContext (que lanzaría).
/// </remarks>
public sealed class DiagnosticoBdService : IDiagnosticoBdService
{
    private readonly ConfiguracionBaseDatos _configuracionBd;
    private readonly Lazy<FlyVibeMexDbContext> _db;
    private readonly ILogger<DiagnosticoBdService> _logger;

    public DiagnosticoBdService(ConfiguracionBaseDatos configuracionBd, Lazy<FlyVibeMexDbContext> db,
        ILogger<DiagnosticoBdService> logger)
    {
        _configuracionBd = configuracionBd;
        _db = db;
        _logger = logger;
    }

    public async Task<ResultadoDiagnosticoBd> DiagnosticarAsync(CancellationToken cancellationToken = default)
    {
        var modo = _configuracionBd.Modo;
        var cadena = _configuracionBd.NombreCadena;
        var baseDeDatos = _configuracionBd.NombreBaseDeDatos();

        if (!_configuracionBd.EstaConfigurada)
        {
            return new ResultadoDiagnosticoBd(StatusCodes.Status503ServiceUnavailable, new
            {
                modo,
                cadena,
                configurada = false,
                conectado = false,
                mensaje = _configuracionBd.MensajeSinConfigurar()
            });
        }

        var cronometro = Stopwatch.StartNew();
        try
        {
            var db = _db.Value;

            if (!await db.Database.CanConnectAsync(cancellationToken))
            {
                return new ResultadoDiagnosticoBd(StatusCodes.Status503ServiceUnavailable, new
                {
                    modo,
                    cadena,
                    baseDeDatos,
                    configurada = true,
                    conectado = false,
                    mensaje = $"No se pudo abrir la conexión. Revisa que SQL Server esté iniciado, el nombre del servidor " +
                              $"en tu user-secret y que exista la base \"{baseDeDatos}\" (sql/01_crear_base_datos.sql)."
                });
            }

            // Consulta LINQ de prueba (parametrizada, sin seguimiento de cambios, sin columnas sensibles).
            var rolesActivos = await db.RolesUsuarios
                .AsNoTracking()
                .Where(r => r.activo)
                .OrderBy(r => r.id)
                .Select(r => r.nombre)
                .ToListAsync(cancellationToken);

            var usuariosActivos = await db.Usuarios.AsNoTracking().CountAsync(u => u.activo, cancellationToken);
            var usuariosInactivos = await db.Usuarios.AsNoTracking().CountAsync(u => !u.activo, cancellationToken);

            // Verificación del esquema completo: lee 1 fila de cada tabla con TODAS sus columnas.
            // Si un nombre o tipo de columna no coincide con el modelo, aquí aparece la tabla y el motivo.
            var tablas = await VerificarTablasAsync(db, cancellationToken);
            var tablasConError = tablas.Count(t => !t.ok);

            cronometro.Stop();
            return new ResultadoDiagnosticoBd(StatusCodes.Status200OK, new
            {
                modo,
                cadena,
                baseDeDatos,
                configurada = true,
                conectado = true,
                tiempoMs = cronometro.ElapsedMilliseconds,
                consultaPrueba = new { rolesActivos, usuariosActivos, usuariosInactivos },
                esquemaCompleto = tablasConError == 0,
                tablas,
                mensaje = tablasConError == 0
                    ? "Conexión correcta con Entity Framework Core y las 30 tablas coinciden con los modelos."
                    : $"Conexión correcta, pero {tablasConError} tabla(s) no coinciden con los modelos. " +
                      "Vuelve a crear la base de prueba con sql/01_crear_base_datos.sql (y después 02 y 03)."
            });
        }
        catch (Exception ex) when (ex is SqlException || ex.InnerException is SqlException)
        {
            var errorSql = ex as SqlException ?? (SqlException)ex.InnerException!;
            _logger.LogError(ex, "TestDb: error de SQL Server (número {NumeroSql}).", errorSql.Number);

            return new ResultadoDiagnosticoBd(StatusCodes.Status503ServiceUnavailable, new
            {
                modo,
                cadena,
                baseDeDatos,
                configurada = true,
                conectado = false,
                codigoSql = errorSql.Number,
                mensaje = SugerenciaPorCodigoSql(errorSql.Number)
            });
        }
    }

    /// <summary>
    /// Lee una fila (todas las columnas) y cuenta las filas de cada tabla mapeada en <see cref="FlyVibeMexDbContext"/>.
    /// Solo se usa en Development (GET /Home/TestDb); no devuelve contenido de las filas, solo el conteo
    /// y, si falla, el mensaje de SQL Server/EF (p. ej. <c>Invalid column name 'tipo_proveedor'</c>).
    /// </summary>
    private static async Task<List<VerificacionTabla>> VerificarTablasAsync(FlyVibeMexDbContext db, CancellationToken ct)
    {
        var pruebas = new (string Tabla, Func<Task<int>> Prueba)[]
        {
            ("roles_usuarios", Probar(db.RolesUsuarios)),
            ("usuarios", Probar(db.Usuarios)),
            ("ubicaciones", Probar(db.Ubicaciones)),
            ("proveedores", Probar(db.Proveedores)),
            ("servicios", Probar(db.Servicios)),
            ("vuelos", Probar(db.Vuelos)),
            ("hospedajes", Probar(db.Hospedajes)),
            ("vehiculos", Probar(db.Vehiculos)),
            ("paquete", Probar(db.Paquetes)),
            ("paquete_servicio", Probar(db.PaquetesServicios)),
            ("disponibilidad_servicio", Probar(db.DisponibilidadServicios)),
            ("promociones", Probar(db.Promociones)),
            ("promocion_servicio", Probar(db.PromocionesServicios)),
            ("estados_reservacion", Probar(db.EstadosReservacion)),
            ("reservaciones", Probar(db.Reservaciones)),
            ("detalle_reservacion", Probar(db.DetallesReservacion)),
            ("historial_estado_reservacion", Probar(db.HistorialEstadosReservacion)),
            ("metodos_pago", Probar(db.MetodosPago)),
            ("instituciones_financieras", Probar(db.InstitucionesFinancieras)),
            ("tarjetas_usuarios", Probar(db.TarjetasUsuarios)),
            ("estados_pago", Probar(db.EstadosPago)),
            ("pagos", Probar(db.Pagos)),
            ("historial_estado_pago", Probar(db.HistorialEstadosPago)),
            ("reembolsos", Probar(db.Reembolsos)),
            ("historial_estado_reembolso", Probar(db.HistorialEstadosReembolso)),
            ("solicitudes_soporte", Probar(db.SolicitudesSoporte)),
            ("mensajes_soporte", Probar(db.MensajesSoporte)),
            ("bitacora", Probar(db.Bitacora)),
            ("contenido_sitio", Probar(db.ContenidoSitio)),
            ("telegram_vinculos", Probar(db.TelegramVinculos)),
        };

        var resultado = new List<VerificacionTabla>(pruebas.Length);
        foreach (var (tabla, probar) in pruebas)
        {
            try
            {
                resultado.Add(new VerificacionTabla(tabla, true, await probar(), null));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Exception causa = ex.InnerException is SqlException interna ? interna : ex;
                resultado.Add(new VerificacionTabla(tabla, false, null, $"{causa.GetType().Name}: {causa.Message}"));
            }
        }
        return resultado;

        // Materializa una fila (así EF lee y convierte cada columna) y después cuenta.
        Func<Task<int>> Probar<T>(DbSet<T> conjunto) where T : class => async () =>
        {
            await conjunto.AsNoTracking().Take(1).ToListAsync(ct);
            return await conjunto.CountAsync(ct);
        };
    }

    /// <summary>Traduce los códigos de error de SQL Server más comunes a una sugerencia (sin datos sensibles).</summary>
    private static string SugerenciaPorCodigoSql(int numero) => numero switch
    {
        4060 => "La base de datos no existe o tu usuario no tiene acceso. Ejecuta sql/01_crear_base_datos.sql en SSMS.",
        18456 => "SQL Server rechazó el inicio de sesión. Revisa usuario/contraseña o usa Trusted_Connection=True.",
        208 => "Faltan tablas en la base de datos. Ejecuta en SSMS sql/01_crear_base_datos.sql.",
        2 or 53 or -1 or -2 => "No se encontró el servidor o tardó demasiado. Revisa el nombre (localhost, .\\SQLEXPRESS o (localdb)\\MSSQLLocalDB) y que el servicio de SQL Server esté iniciado.",
        _ => "Error de SQL Server. Revisa la consola de la aplicación para ver el detalle."
    };
}
