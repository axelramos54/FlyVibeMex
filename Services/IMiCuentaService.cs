using System.Security.Claims;
using FlyVibeMex.Models;

namespace FlyVibeMex.Services;

/// <summary>
/// Cuenta del usuario: de quién son los datos que se muestran ("Ver como cliente" para internos), perfil,
/// contraseña y tarjetas guardadas.
/// </summary>
public interface IMiCuentaService
{
    /// <summary>
    /// Resuelve el contexto: el usuario de la cookie y, si es interno y pide <paramref name="idCliente"/>, la cuenta
    /// de ese cliente en solo lectura. Devuelve <c>null</c> si la cookie no trae un id válido.
    /// Un cliente que mande <c>?cliente=</c> siempre ve SU propia cuenta (el parámetro se ignora).
    /// </summary>
    Task<ContextoCuenta?> ContextoAsync(ClaimsPrincipal usuario, ulong? idCliente, CancellationToken cancellationToken = default);

    /// <summary>Id de un CLIENTE por su correo (para "Ver como cliente"); <c>null</c> si no existe.</summary>
    Task<ulong?> BuscarClientePorCorreoAsync(string? correo, CancellationToken cancellationToken = default);

    Task<MiPerfilViewModel?> PerfilAsync(ContextoCuenta contexto, CancellationToken cancellationToken = default);

    /// <summary>Actualiza nombre, apellidos y teléfono de la cuenta PROPIA (el correo y el rol no se cambian aquí).</summary>
    Task<ResultadoOperacion> ActualizarPerfilAsync(ulong idUsuario, PerfilFormViewModel perfil, CancellationToken cancellationToken = default);

    /// <summary>Cambia la contraseña de la cuenta propia verificando primero la actual.</summary>
    Task<ResultadoOperacion> CambiarPasswordAsync(ulong idUsuario, CambioPasswordViewModel datos, CancellationToken cancellationToken = default);

    Task<MisTarjetasViewModel> TarjetasAsync(ContextoCuenta contexto, CancellationToken cancellationToken = default);

    /// <summary>Alta simulada (token falso + marca + últimos 4). No cobra nada y el CVV no se guarda.</summary>
    Task<ResultadoOperacion> AgregarTarjetaAsync(ulong idUsuario, TarjetaNuevaViewModel tarjeta, CancellationToken cancellationToken = default);

    Task<ResultadoOperacion> CambiarAliasAsync(ulong idUsuario, ulong idTarjeta, string? alias, CancellationToken cancellationToken = default);

    /// <summary>Baja lógica (activo = 0). Los pagos hechos con la tarjeta no cambian.</summary>
    Task<ResultadoOperacion> EliminarTarjetaAsync(ulong idUsuario, ulong idTarjeta, CancellationToken cancellationToken = default);
}
