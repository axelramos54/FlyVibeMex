using FlyVibeMex.Models;

namespace FlyVibeMex.Services;

/// <summary>
/// Valida los tokens de inicio de sesión transparente (SSO) que manda GoldStay para abrir el widget con la
/// sesión del cliente ya iniciada.
/// </summary>
public interface ISsoIntegracionService
{
    /// <summary>
    /// Verifica la firma y el contenido del token y, si todo está bien, devuelve el usuario LOCAL que le
    /// corresponde (buscado por correo en la tabla <c>usuarios</c>).
    ///
    /// Reusa <see cref="ResultadoAutenticacion"/> para que el mensaje hacia el navegador sea igual de genérico
    /// que en el inicio de sesión normal: el motivo exacto del rechazo queda solo en el log del servidor.
    /// Nunca lanza excepciones de base de datos ni de criptografía al llamador.
    /// </summary>
    Task<ResultadoAutenticacion> ValidarTokenAsync(string? token, CancellationToken cancellationToken = default);
}
