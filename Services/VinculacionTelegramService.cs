using System.Security.Cryptography;
using FlyVibeMex.Data;
using FlyVibeMex.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FlyVibeMex.Services;

/// <summary>Código de un solo uso para vincular la cuenta con el bot (se muestra una sola vez en Mi cuenta › Telegram).</summary>
/// <param name="Codigo">8 caracteres sin guion (lo que va en el enlace t.me/...?start=v_CODIGO).</param>
/// <param name="CodigoVisible">El mismo código con guion para leerlo fácil: "K7P2-9QXM".</param>
/// <param name="ExpiraEn">Hora local en que deja de servir.</param>
public sealed record CodigoVinculacionTelegram(string Codigo, string CodigoVisible, DateTime ExpiraEn);

/// <summary>Cliente que escribe desde un chat vinculado (lo usa el bot en cada mensaje).</summary>
public sealed record ClienteTelegram(ulong IdUsuario, string Nombre, long ChatId);

public enum ResultadoVinculacion
{
    Vinculado,
    /// <summary>El código no existe, ya se usó o venció (misma respuesta en los tres casos).</summary>
    CodigoInvalido,
    /// <summary>La cuenta ya no está activa o no es de cliente.</summary>
    CuentaNoValida,
    ErrorBd
}

/// <summary>
/// Vinculación de cuentas de CLIENTE con su chat privado del bot de Telegram (docs/06).
/// </summary>
/// <remarks>
/// <para>Flujo: el cliente, con su sesión iniciada en la web, genera un código en Mi cuenta › Telegram
/// (<see cref="GenerarCodigo"/>) y lo manda al bot (<c>/vincular CODIGO</c> o el enlace <c>t.me/bot?start=v_CODIGO</c>).
/// El bot llama a <see cref="VincularAsync"/>.</para>
/// <para>Seguridad: el código es aleatorio (<see cref="RandomNumberGenerator"/>, 32^8 combinaciones), sirve UNA vez,
/// vence a los 10 minutos y solo vive en la memoria de la aplicación (nunca en la BD ni en los logs). El bot y la web
/// corren en el mismo proceso, así que comparten esa memoria. Si la app se reinicia, el cliente genera otro código.
/// Además, el bot limita los intentos de /vincular por chat.</para>
/// </remarks>
public interface IVinculacionTelegramService
{
    /// <summary>Vínculo activo de la cuenta (para Mi cuenta › Telegram); <c>null</c> si no está vinculada.</summary>
    Task<DateTime?> VinculadoDesdeAsync(ulong idUsuario, CancellationToken cancellationToken = default);

    /// <summary>Genera un código nuevo para la cuenta; el anterior (si había) deja de servir.</summary>
    CodigoVinculacionTelegram GenerarCodigo(ulong idUsuario);

    /// <summary>
    /// Usa el código (una sola vez) y vincula esa cuenta con <paramref name="chatId"/>. Desactiva, en la misma
    /// transacción, los vínculos anteriores de la cuenta y del chat. Devuelve el nombre del cliente si se vinculó.
    /// </summary>
    Task<(ResultadoVinculacion Resultado, string? Nombre)> VincularAsync(string? codigo, long chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cliente del chat: vínculo activo + cuenta activa + rol CLIENTE activo. <c>null</c> si falta cualquiera
    /// (así un cliente desactivado o cambiado de rol por el SA pierde el acceso desde el bot al instante).
    /// </summary>
    Task<ClienteTelegram?> ClientePorChatAsync(long chatId, CancellationToken cancellationToken = default);

    /// <summary>Chat del cliente para avisarle (respuesta de un agente, ticket cerrado); <c>null</c> si no está vinculado.</summary>
    Task<long?> ChatDeClienteAsync(ulong idUsuario, CancellationToken cancellationToken = default);

    /// <summary>Desvincula desde el bot (/desvincular). Devuelve <c>true</c> si había un vínculo activo.</summary>
    Task<bool> DesvincularChatAsync(long chatId, CancellationToken cancellationToken = default);

    /// <summary>Desvincula desde la web (Mi cuenta › Telegram).</summary>
    Task<ResultadoOperacion> DesvincularCuentaAsync(ulong idUsuario, CancellationToken cancellationToken = default);
}

/// <summary>Implementación de <see cref="IVinculacionTelegramService"/> con EF Core (LINQ) y <see cref="IMemoryCache"/>.</summary>
public sealed class VinculacionTelegramService : IVinculacionTelegramService
{
    /// <summary>Sin 0/O ni 1/I para que no se confundan al copiarlo.</summary>
    private const string Alfabeto = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int LongitudCodigo = 8;
    public static readonly TimeSpan VigenciaCodigo = TimeSpan.FromMinutes(10);

    private const string LlaveCodigo = "telegram:codigo:";            // + código   → id del usuario
    private const string LlaveCodigoDeUsuario = "telegram:usuario:";  // + id       → su código vigente

    /// <summary>Candado para que "leer y borrar" el código sea atómico (un código no se puede usar dos veces).</summary>
    private static readonly object Candado = new();

    private readonly FlyVibeMexDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<VinculacionTelegramService> _logger;

    public VinculacionTelegramService(FlyVibeMexDbContext db, IMemoryCache cache, ILogger<VinculacionTelegramService> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    public async Task<DateTime?> VinculadoDesdeAsync(ulong idUsuario, CancellationToken cancellationToken = default)
    {
        var fechas = await _db.TelegramVinculos.AsNoTracking()
            .Where(v => v.id_usuarioglobal == idUsuario && v.activo)
            .Select(v => v.vinculado_en)
            .Take(1)
            .ToListAsync(cancellationToken);
        return fechas.Count == 0 ? null : fechas[0];
    }

    public CodigoVinculacionTelegram GenerarCodigo(ulong idUsuario)
    {
        var codigo = RandomNumberGenerator.GetString(Alfabeto, LongitudCodigo);
        var expira = DateTime.Now.Add(VigenciaCodigo);
        var opciones = new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = VigenciaCodigo };

        lock (Candado)
        {
            // El código anterior de esta cuenta deja de servir.
            if (_cache.TryGetValue(LlaveCodigoDeUsuario + idUsuario, out string? anterior) && anterior is not null)
                _cache.Remove(LlaveCodigo + anterior);

            _cache.Set(LlaveCodigo + codigo, idUsuario, opciones);
            _cache.Set(LlaveCodigoDeUsuario + idUsuario, codigo, opciones);
        }

        _logger.LogInformation("Telegram: el cliente {IdUsuario} generó un código de vinculación.", idUsuario);
        return new CodigoVinculacionTelegram(codigo, $"{codigo[..4]}-{codigo[4..]}", expira);
    }

    public async Task<(ResultadoVinculacion Resultado, string? Nombre)> VincularAsync(string? codigo, long chatId,
        CancellationToken cancellationToken = default)
    {
        if (chatId <= 0) return (ResultadoVinculacion.CodigoInvalido, null);   // solo chats privados

        var idUsuario = Consumir(codigo);
        if (idUsuario is null) return (ResultadoVinculacion.CodigoInvalido, null);

        var nombre = await (
                from u in _db.Usuarios.AsNoTracking()
                join r in _db.RolesUsuarios.AsNoTracking() on u.id_rol equals r.id
                where u.id == idUsuario && u.activo && r.activo && r.nombre == Roles.Cliente
                select u.nombre)
            .FirstOrDefaultAsync(cancellationToken);
        if (nombre is null) return (ResultadoVinculacion.CuentaNoValida, null);

        var id = idUsuario.Value;
        try
        {
            await GestorBd.EnTransaccionAsync(_db, async () =>
            {
                var ahora = DateTime.Now;
                // Una cuenta = un chat, y un chat = una cuenta: se desactiva lo anterior de los dos lados.
                await _db.TelegramVinculos
                    .Where(v => v.activo && (v.id_usuarioglobal == id || v.chat_id == chatId))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(v => v.activo, false)
                        .SetProperty(v => v.desvinculado_en, (DateTime?)ahora), cancellationToken);

                _db.TelegramVinculos.Add(new TelegramVinculos
                {
                    id_usuarioglobal = id,
                    chat_id = chatId,
                    activo = true,
                    vinculado_en = ahora
                });
                await _db.SaveChangesAsync(cancellationToken);
            }, cancellationToken);
        }
        catch (Exception ex) when (GestorBd.EsErrorDeBd(ex))
        {
            _logger.LogError(ex, "Telegram: no se pudo vincular la cuenta {IdUsuario}.", id);
            return (ResultadoVinculacion.ErrorBd, null);
        }

        _logger.LogInformation("Telegram: la cuenta {IdUsuario} quedó vinculada a un chat.", id);
        return (ResultadoVinculacion.Vinculado, nombre);
    }

    public async Task<ClienteTelegram?> ClientePorChatAsync(long chatId, CancellationToken cancellationToken = default)
    {
        if (chatId <= 0) return null;

        var fila = await (
                from v in _db.TelegramVinculos.AsNoTracking()
                join u in _db.Usuarios.AsNoTracking() on v.id_usuarioglobal equals u.id
                join r in _db.RolesUsuarios.AsNoTracking() on u.id_rol equals r.id
                where v.chat_id == chatId && v.activo && u.activo && r.activo && r.nombre == Roles.Cliente
                select new { u.id, u.nombre })
            .FirstOrDefaultAsync(cancellationToken);

        return fila is null ? null : new ClienteTelegram(fila.id, fila.nombre, chatId);
    }

    public async Task<long?> ChatDeClienteAsync(ulong idUsuario, CancellationToken cancellationToken = default)
    {
        var chats = await _db.TelegramVinculos.AsNoTracking()
            .Where(v => v.id_usuarioglobal == idUsuario && v.activo)
            .Select(v => v.chat_id)
            .Take(1)
            .ToListAsync(cancellationToken);
        return chats.Count == 0 ? null : chats[0];
    }

    public async Task<bool> DesvincularChatAsync(long chatId, CancellationToken cancellationToken = default)
    {
        var ahora = DateTime.Now;
        var filas = await _db.TelegramVinculos
            .Where(v => v.chat_id == chatId && v.activo)
            .ExecuteUpdateAsync(s => s
                .SetProperty(v => v.activo, false)
                .SetProperty(v => v.desvinculado_en, (DateTime?)ahora), cancellationToken);
        if (filas > 0) _logger.LogInformation("Telegram: un chat se desvinculó desde el bot.");
        return filas > 0;
    }

    public async Task<ResultadoOperacion> DesvincularCuentaAsync(ulong idUsuario, CancellationToken cancellationToken = default)
    {
        try
        {
            var ahora = DateTime.Now;
            var filas = await _db.TelegramVinculos
                .Where(v => v.id_usuarioglobal == idUsuario && v.activo)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(v => v.activo, false)
                    .SetProperty(v => v.desvinculado_en, (DateTime?)ahora), cancellationToken);

            _logger.LogInformation("Telegram: el cliente {IdUsuario} desvinculó su cuenta desde la web.", idUsuario);
            return filas > 0
                ? ResultadoOperacion.Ok("Listo: tu cuenta ya no está vinculada con Telegram. El bot ya no podrá ver tus reservaciones ni tus tickets.")
                : ResultadoOperacion.Falla("Tu cuenta no estaba vinculada con Telegram.");
        }
        catch (Exception ex) when (GestorBd.EsErrorDeBd(ex))
        {
            _logger.LogError(ex, "Telegram: no se pudo desvincular la cuenta {IdUsuario}.", idUsuario);
            return ResultadoOperacion.Falla("No pudimos desvincular tu cuenta. Intenta de nuevo más tarde.");
        }
    }

    /// <summary>Normaliza ("k7p2-9qxm", "v_K7P29QXM" → "K7P29QXM"), valida y BORRA el código. Devuelve el id de la cuenta.</summary>
    private ulong? Consumir(string? codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo) || codigo.Length > 40) return null;

        var limpio = new string(codigo.Trim().ToUpperInvariant().Where(c => c is not ('-' or ' ')).ToArray());
        if (limpio.StartsWith("V_", StringComparison.Ordinal)) limpio = limpio[2..];
        if (limpio.Length != LongitudCodigo || limpio.Any(c => !Alfabeto.Contains(c))) return null;

        lock (Candado)
        {
            if (!_cache.TryGetValue(LlaveCodigo + limpio, out ulong idUsuario)) return null;
            _cache.Remove(LlaveCodigo + limpio);
            _cache.Remove(LlaveCodigoDeUsuario + idUsuario);
            return idUsuario;
        }
    }
}
