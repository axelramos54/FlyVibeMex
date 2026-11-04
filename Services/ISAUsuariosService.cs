using FlyVibeMex.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace FlyVibeMex.Services;

/// <summary>
/// Usuarios y roles del panel SA (<c>/Admin/SA/Usuarios</c> y <c>/Admin/SA/Roles</c>).
/// </summary>
/// <remarks>
/// Reglas (también en pa_cambiar_rol_usuario y pa_cambiar_estado_usuario):
/// el SA no puede cambiarse el rol, desactivarse ni restablecerse la contraseña a sí mismo; siempre debe quedar al menos
/// un SA activo; las cuentas nunca se borran (se desactivan); el correo no se cambia. Las contraseñas temporales se
/// generan aquí, se devuelven UNA vez para mostrarlas y en la BD solo queda su hash.
/// </remarks>
public interface ISAUsuariosService
{
    /// <summary>Cuentas que coinciden con el filtro (texto, rol y estado), para el panel del SA.</summary>
    Task<ListaUsuariosViewModel> ListaAsync(FiltroUsuarios filtro, CancellationToken cancellationToken = default);

    /// <summary>Roles activos para un &lt;select&gt;; <paramref name="soloInternos"/> = sin CLIENTE (alta interna).</summary>
    Task<List<SelectListItem>> OpcionesRolAsync(bool soloInternos, string? elegido, CancellationToken cancellationToken = default);

    /// <summary>Crea una cuenta interna; si sale bien, devuelve la contraseña temporal (para mostrarla una vez).</summary>
    Task<(ResultadoOperacion resultado, ContrasenaTemporalViewModel? temporal)> CrearInternoAsync(UsuarioAltaViewModel datos, ulong idSA,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pantalla de edición de una cuenta. Recibe <paramref name="idSA"/> para saber si es la suya propia y
    /// deshabilitar lo que un SA no puede hacerse a sí mismo. <c>null</c> si la cuenta no existe.
    /// </summary>
    Task<UsuarioEdicionViewModel?> EdicionAsync(ulong idCuenta, ulong idSA, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cambia nombre, apellidos y teléfono de una cuenta. El <b>correo no se cambia</b> (es la identidad del
    /// login) y el teléfono no puede repetirse en otra cuenta.
    /// </summary>
    Task<ResultadoOperacion> ActualizarDatosAsync(ulong idCuenta, UsuarioDatosForm datos, ulong idSA, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cambia el rol de una cuenta. <b>Un SA no puede cambiar su propio rol</b>: si pudiera, podría quitarse el
    /// acceso al panel y dejar al equipo sin nadie que lo devuelva.
    /// </summary>
    Task<ResultadoOperacion> CambiarRolAsync(ulong idCuenta, string? rol, ulong idSA, CancellationToken cancellationToken = default);

    /// <summary>
    /// Activa o desactiva una cuenta (<b>R11</b>). Dos candados: un SA no se desactiva a sí mismo y <b>siempre
    /// queda al menos un SA activo</b>, comprobado DENTRO de la transacción para que dos SA no se desactiven a la vez.
    /// </summary>
    Task<ResultadoOperacion> CambiarEstadoAsync(ulong idCuenta, bool activo, ulong idSA, CancellationToken cancellationToken = default);

    /// <summary>
    /// Genera una contraseña temporal para una cuenta. Se devuelve para mostrarla <b>una sola vez</b>: solo se
    /// guarda su hash (<b>R10</b>), así que si no se copia en ese momento hay que generar otra.
    /// </summary>
    Task<(ResultadoOperacion resultado, ContrasenaTemporalViewModel? temporal)> RestablecerContrasenaAsync(ulong idCuenta, ulong idSA,
        CancellationToken cancellationToken = default);

    /// <summary>Los roles con cuántas cuentas tiene cada uno. Los nombres están fijos en <c>Models/Auth/Roles.cs</c>.</summary>
    Task<List<RolListadoItem>> RolesAsync(CancellationToken cancellationToken = default);

    /// <summary>Solo la descripción: los nombres de rol están fijos en el código (Models/Auth/Roles.cs).</summary>
    Task<ResultadoOperacion> ActualizarDescripcionRolAsync(ulong idRol, string? descripcion, ulong idSA, CancellationToken cancellationToken = default);
}
