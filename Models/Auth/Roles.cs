namespace FlyVibeMex.Models;

/// <summary>
/// Nombres de rol tal como están en <c>roles_usuarios.nombre</c>. Se usan en <c>[Authorize(Roles = ...)]</c>
/// y en <c>User.IsInRole(...)</c>. Escribirlos SIEMPRE con estas constantes (sin "strings mágicos").
/// </summary>
/// <remarks>
/// Roles que hoy existen en la BD (sql/00_db_prueba_conexion.sql): CLIENTE, AGENTE y ADMINISTRADOR.
/// SOPORTE todavía NO existe en la tabla; si el equipo decide usarlo, insertarlo con:
///   INSERT INTO roles_usuarios (nombre, descripcion, activo) VALUES ('SOPORTE', 'Atiende tickets de soporte', 1);
/// Mientras tanto, el panel de soporte también deja entrar a AGENTE y ADMINISTRADOR (ver <see cref="PanelSoporte"/>).
/// </remarks>
public static class Roles
{
    public const string Cliente = "CLIENTE";
    public const string Agente = "AGENTE";
    public const string Soporte = "SOPORTE";
    public const string Administrador = "ADMINISTRADOR";

    // Para [Authorize(Roles = ...)] se separan con coma (basta con tener UNO de los roles).

    /// <summary>Pantalla de inicio del panel (/Admin).</summary>
    public const string PanelInterno = Agente + "," + Soporte + "," + Administrador;

    /// <summary>Panel de tickets (/Admin/Soporte).</summary>
    public const string PanelSoporte = Agente + "," + Soporte + "," + Administrador;
}
