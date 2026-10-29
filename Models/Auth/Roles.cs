using System.Security.Claims;

namespace FlyVibeMex.Models;

/// <summary>
/// Nombres de rol tal como están en <c>roles_usuarios.nombre</c>. Se usan en <c>[Authorize(Roles = ...)]</c>
/// y en <c>User.IsInRole(...)</c>. Escribirlos SIEMPRE con estas constantes (sin "strings mágicos").
/// </summary>
/// <remarks>
/// Quién hace qué (sql/00_db_prueba_conexion.sql y sql/07_roles_panel_bitacora.sql):
/// <list type="bullet">
/// <item>CLIENTE: busca, reserva, paga, cancela y abre tickets en el sitio público.</item>
/// <item>SOPORTE: solo atiende tickets (/Admin/Soporte).</item>
/// <item>AGENTE: gestiona reservaciones y reembolsos.</item>
/// <item>ADMINISTRADOR: todo lo anterior + Gestor de ventas (proveedores, servicios, paquetes, promociones).</item>
/// <item>SA: todo lo del ADMINISTRADOR + usuarios y roles, catálogos base, bitácora y contenido del sitio (/Admin/SA).</item>
/// </list>
/// </remarks>
public static class Roles
{
    public const string Cliente = "CLIENTE";
    public const string Soporte = "SOPORTE";
    public const string Agente = "AGENTE";
    public const string Administrador = "ADMINISTRADOR";
    public const string SA = "SA";

    // Para [Authorize(Roles = ...)] se separan con coma (basta con tener UNO de los roles).

    /// <summary>Pantalla de inicio del panel (/Admin): cualquier rol interno.</summary>
    public const string PanelInterno = Soporte + "," + Agente + "," + Administrador + "," + SA;

    /// <summary>Panel de tickets (/Admin/Soporte).</summary>
    public const string PanelSoporte = Soporte + "," + Administrador + "," + SA;

    /// <summary>Reservaciones y reembolsos del panel.</summary>
    public const string PanelReservaciones = Agente + "," + Administrador + "," + SA;

    /// <summary>Gestor de ventas (/Admin/GestorVentas): altas y bajas del catálogo.</summary>
    public const string PanelGestor = Administrador + "," + SA;

    /// <summary>Panel del SA (/Admin/SA).</summary>
    public const string PanelSA = SA;

    /// <summary>¿El nombre de rol es de personal de FlyVibeMex (no cliente)?</summary>
    public static bool EsInterno(string? rol) => rol is Soporte or Agente or Administrador or SA;

    /// <summary>¿El usuario de la cookie es personal de FlyVibeMex?</summary>
    public static bool EsInterno(ClaimsPrincipal usuario) =>
        usuario.IsInRole(Soporte) || usuario.IsInRole(Agente) || usuario.IsInRole(Administrador) || usuario.IsInRole(SA);

    /// <summary>¿Puede entrar a una sección? Mismo criterio que [Authorize(Roles = "A,B")]: basta uno de los roles.</summary>
    public static bool Puede(ClaimsPrincipal usuario, string rolesSeparadosPorComa) =>
        rolesSeparadosPorComa.Split(',').Any(usuario.IsInRole);
}
