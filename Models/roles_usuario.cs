namespace FlyVibeMex.Models
{
    /// <summary>
    /// Catálogo de roles que determina el tipo de acceso al sistema: cliente, agente y administrador.
    /// Un <c>Usuarios</c> pertenece a un rol; el rol decidirá qué módulos puede ver (p. ej. Panel interno
    /// solo para AGENTE/ADMINISTRADOR) cuando se implemente la autorización.
    /// </summary>
    public class RolesUsuario
    {
        /// <summary>Llave primaria (identificador único autoincremental).</summary>
        public ulong id {get; set;}

        /// <summary>Nombre único del rol (p. ej. CLIENTE, AGENTE, ADMINISTRADOR).</summary>
        public string nombre {get; set;} = string.Empty;

        /// <summary>Descripción funcional del rol.</summary>
        // NOTA: en el diseño este campo es opcional.
        public string descripcion {get; set;} = string.Empty;

        /// <summary>Borrado lógico <c>false</c> oculta el registro sin eliminarlo.</summary>
        public bool activo {get; set;}
    }
}