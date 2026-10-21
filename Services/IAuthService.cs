using FlyVibeMex.Models;

namespace FlyVibeMex.Services;

/// <summary>
/// Autenticación de usuarios de FlyVibe Mex contra la tabla <c>usuarios</c>.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Verifica correo y contraseña. Devuelve <see cref="ResultadoAutenticacion"/> con un
    /// <see cref="UsuarioSesion"/> (sin <c>password_hash</c>) si las credenciales son válidas y el usuario está activo.
    /// Nunca lanza excepciones de base de datos al llamador: las registra en el log y devuelve
    /// <see cref="EstadoAutenticacion.ErrorServicio"/>.
    /// </summary>
    Task<ResultadoAutenticacion> ValidarCredencialesAsync(string? correo, string? password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Genera el hash seguro de una contraseña (para registro o cambio de contraseña).
    /// La contraseña en texto plano no debe guardarse ni registrarse en ningún lado.
    /// </summary>
    string GenerarHash(string password);

    /// <summary>Normaliza un correo antes de buscarlo o guardarlo (sin espacios y en minúsculas).</summary>
    string NormalizarCorreo(string correo);
}
