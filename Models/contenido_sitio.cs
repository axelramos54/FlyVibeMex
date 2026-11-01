namespace FlyVibeMex.Models
{
    /// <summary>
    /// Texto o imagen del sitio que el SA edita sin tocar código (tabla <c>contenido_sitio</c>, T4 fase D).
    /// Las claves válidas y su texto por defecto están en <see cref="ContenidoSitioClaves"/>.
    /// </summary>
    public class ContenidoSitio
    {
        /// <summary>Clave del contenido, p. ej. <c>inicio.bienvenida.titulo</c> (llave primaria).</summary>
        public string clave {get; set;} = string.Empty;

        /// <summary>Texto plano (se muestran los saltos de línea; nunca se interpreta como HTML).</summary>
        public string? texto {get; set;}

        /// <summary>Ruta pública de la imagen subida (/uploads/contenido/...), si la clave lleva imagen.</summary>
        public string? imagen_ruta {get; set;}

        public DateTime actualizado_en {get; set;}

        /// <summary>SA que hizo el último cambio.</summary>
        public ulong? id_usuario {get; set;}
    }
}
