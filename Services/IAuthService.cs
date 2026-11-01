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

    /// <summary>
    /// Crea la cuenta de un CLIENTE desde el formulario de registro (T8.1; antes vivía en CrearusuarioController).
    /// Revisa que el correo y el teléfono no estén registrados, busca el rol de cliente (nunca viene del formulario)
    /// y guarda solo el hash de la contraseña. Si falla, <see cref="ResultadoOperacion.Errores"/> trae el campo con
    /// el problema (correo / telefono) o, sin errores por campo, un mensaje general. Nunca lanza errores de BD.
    /// </summary>
    Task<ResultadoOperacion> RegistrarClienteAsync(CrearUsuarioViewModel datos, CancellationToken cancellationToken = default);
}
