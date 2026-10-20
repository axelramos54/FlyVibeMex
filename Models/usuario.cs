// NOTA: namespace correcto (FlyVibeMex.Models), visible en vistas gracias a Views/_ViewImports.cshtml.
namespace FlyVibeMex.Models
{
    /// <summary>
    /// Cuenta de una persona que usa FlyVibe Mex: clientes/viajeros, agentes de la agencia y administradores.
    /// Reglas: el correo identifica al usuario para iniciar sesión; la contraseña se guarda SOLO como hash
    /// (regla R10) y nunca debe enviarse a una vista ni registrarse en logs; borrado lógico con <c>activo</c>.
    /// </summary>
    // FIXME: el diseño incluye `actualizado_en` (fecha de última actualización); falta en esta clase.
    // NOTA: la clase está en plural (Usuarios) y el archivo en singular (usuario.cs).
    public class Usuarios
    {
        /// <summary>Llave primaria (identificador único autoincremental).</summary>
        public ulong id {get; set;}
        
        /// <summary>Llave foránea → <c>RolesUsuario.id</c>. Rol asignado al usuario.</summary>
        public ulong id_rol {get; set;}
        
        /// <summary>Nombre y apellidos del usuario. Se muestran en la interfaz y se usan para personalizar correos.</summary>
        public string nombre {get; set;} = string.Empty;
        public string apellido_paterno {get; set;} = string.Empty;
        public string apellido_materno {get; set;} = string.Empty;

        /// <summary>Correo único; credencial de inicio de sesión.</summary>
        public string correo {get; set;} = string.Empty;

        /// <summary>Teléfono de contacto (único).</summary>
        public string telefono {get; set;} = string.Empty;

        /// <summary>Hash seguro de la contraseña. NUNCA almacenar ni comparar la contraseña en texto plano.</summary>
        public string password_hash {get; set;} = string.Empty;

        /// <summary>Borrado lógico <c>false</c> oculta el registro sin eliminarlo.</summary>
        public bool activo {get; set;}

        /// <summary>Fecha y hora de alta del registro (campo de control).</summary>
        public DateTime creado_en {get; set;}

        /// <summary>Foto de perfil (opcional).</summary>
        // NOTA: campo agregado por el equipo; no aparece en el diccionario de datos del PDF.
        public byte[]? imagen {get; set;} //imagen
    }
}