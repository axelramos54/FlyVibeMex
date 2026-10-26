// DTOs del inicio de sesión.
// Regla: los controladores y las vistas NUNCA reciben la entidad Usuarios completa (trae password_hash).
// Solo reciben estos objetos con los datos públicos necesarios para la sesión.
namespace FlyVibeMex.Models
{
    /// <summary>
    /// Datos públicos de la sesión de un usuario autenticado. NO contiene <c>password_hash</c>,
    /// teléfono ni ningún otro dato sensible.
    /// </summary>
    /// <param name="id">Identificador del usuario (<c>usuarios.id</c>).</param>
    /// <param name="nombre">Nombre para mostrar en la interfaz.</param>
    /// <param name="correo">Correo normalizado con el que inició sesión.</param>
    /// <param name="id_rol">Rol del usuario (<c>roles_usuarios.id</c>) para la autorización.</param>
    public sealed record UsuarioSesion(ulong id, string nombre, string correo, ulong id_rol, string rol);

    /// <summary>Resultado posible de un intento de inicio de sesión.</summary>
    public enum EstadoAutenticacion
    {
        /// <summary>Correo y contraseña válidos y usuario activo.</summary>
        Exitoso,

        /// <summary>Correo inexistente, usuario inactivo o contraseña incorrecta (no se distingue cuál).</summary>
        CredencialesInvalidas,

        /// <summary>No se pudo consultar la base de datos (el detalle solo queda en el log del servidor).</summary>
        ErrorServicio
    }

    /// <summary>
    /// Respuesta de <c>IAuthService</c>. El <see cref="Mensaje"/> es genérico a propósito para no revelar
    /// si un correo existe (evita la enumeración de usuarios) ni detalles de la base de datos.
    /// </summary>
    public sealed record ResultadoAutenticacion(EstadoAutenticacion Estado, UsuarioSesion? Usuario, string Mensaje)
    {
        public const string MensajeCredencialesInvalidas = "Credenciales inválidas.";
        public const string MensajeErrorServicio = "No fue posible iniciar sesión en este momento. Intenta de nuevo más tarde.";

        public bool Exitoso => Estado == EstadoAutenticacion.Exitoso && Usuario is not null;

        public static ResultadoAutenticacion Correcto(UsuarioSesion usuario) =>
            new(EstadoAutenticacion.Exitoso, usuario, string.Empty);

        public static ResultadoAutenticacion Invalido() =>
            new(EstadoAutenticacion.CredencialesInvalidas, null, MensajeCredencialesInvalidas);

        public static ResultadoAutenticacion FalloServicio() =>
            new(EstadoAutenticacion.ErrorServicio, null, MensajeErrorServicio);
    }
}
