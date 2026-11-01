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
    Task<ListaUsuariosViewModel> ListaAsync(FiltroUsuarios filtro, CancellationToken cancellationToken = default);

    /// <summary>Roles activos para un &lt;select&gt;; <paramref name="soloInternos"/> = sin CLIENTE (alta interna).</summary>
    Task<List<SelectListItem>> OpcionesRolAsync(bool soloInternos, string? elegido, CancellationToken cancellationToken = default);

    /// <summary>Crea una cuenta interna; si sale bien, devuelve la contraseña temporal (para mostrarla una vez).</summary>
    Task<(ResultadoOperacion resultado, ContrasenaTemporalViewModel? temporal)> CrearInternoAsync(UsuarioAltaViewModel datos, ulong idSA,
        CancellationToken cancellationToken = default);

    Task<UsuarioEdicionViewModel?> EdicionAsync(ulong idCuenta, ulong idSA, CancellationToken cancellationToken = default);

    Task<ResultadoOperacion> ActualizarDatosAsync(ulong idCuenta, UsuarioDatosForm datos, ulong idSA, CancellationToken cancellationToken = default);

    Task<ResultadoOperacion> CambiarRolAsync(ulong idCuenta, string? rol, ulong idSA, CancellationToken cancellationToken = default);

    Task<ResultadoOperacion> CambiarEstadoAsync(ulong idCuenta, bool activo, ulong idSA, CancellationToken cancellationToken = default);

    Task<(ResultadoOperacion resultado, ContrasenaTemporalViewModel? temporal)> RestablecerContrasenaAsync(ulong idCuenta, ulong idSA,
        CancellationToken cancellationToken = default);

    Task<List<RolListadoItem>> RolesAsync(CancellationToken cancellationToken = default);

    /// <summary>Solo la descripción: los nombres de rol están fijos en el código (Models/Auth/Roles.cs).</summary>
    Task<ResultadoOperacion> ActualizarDescripcionRolAsync(ulong idRol, string? descripcion, ulong idSA, CancellationToken cancellationToken = default);
}
