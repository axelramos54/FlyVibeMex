namespace FlyVibeMex.Models
{
    /// <summary>Un texto o imagen editable del sitio: su clave, dónde aparece y su valor por defecto.</summary>
    /// <param name="Clave">Llave en la tabla contenido_sitio.</param>
    /// <param name="Seccion">inicio | politicas | pie (pestaña de /Admin/SA/Contenido).</param>
    /// <param name="Etiqueta">Cómo se llama en el panel.</param>
    /// <param name="Defecto">Texto (o URL de la imagen) que se muestra si no hay fila en la tabla.</param>
    /// <param name="Maximo">Longitud máxima del texto.</param>
    /// <param name="Multilinea">true = área de texto (se respetan los saltos de línea).</param>
    /// <param name="EsImagen">true = se sube una imagen en lugar de texto.</param>
    public sealed record DefinicionContenido(string Clave, string Seccion, string Etiqueta, string Defecto,
        int Maximo = 200, bool Multilinea = false, bool EsImagen = false);

    /// <summary>
    /// Catálogo ÚNICO de contenido editable (T4 fase D). Las vistas leen estas claves con
    /// <c>IContenidoSitioService</c>; agregar un texto editable nuevo = agregar aquí su definición y usar su clave.
    /// Los textos por defecto son los que tenía el sitio antes de la fase D (nada cambia con la tabla vacía).
    /// </summary>
    public static class ContenidoSitioClaves
    {
        public const string SeccionInicio = "inicio";
        public const string SeccionPoliticas = "politicas";
        public const string SeccionPie = "pie";

        // ---- Inicio ----
        public const string BienvenidaTitulo = "inicio.bienvenida.titulo";
        public const string BienvenidaTexto = "inicio.bienvenida.texto";
        public const string PromoTitulo = "inicio.promo.titulo";
        public const string PromoTexto = "inicio.promo.texto";
        public const string ContactoTitulo = "inicio.contacto.titulo";
        public const string ContactoTexto = "inicio.contacto.texto";

        /// <summary>Las 4 tarjetas de categorías del Inicio (prefijo de sus claves .titulo, .texto e .imagen).</summary>
        public static readonly string[] Tarjetas = { "vuelos", "paquetes", "hospedaje", "tours" };
        public static string TarjetaTitulo(string tarjeta) => $"inicio.tarjeta.{tarjeta}.titulo";
        public static string TarjetaTexto(string tarjeta) => $"inicio.tarjeta.{tarjeta}.texto";
        public static string TarjetaImagen(string tarjeta) => $"inicio.tarjeta.{tarjeta}.imagen";

        // ---- Políticas ----
        public const string PoliticasIntro = "politicas.intro";
        public const string PoliticasTerminos = "politicas.terminos";
        public const string PoliticasPrivacidad = "politicas.privacidad";
        public const string PoliticasCancelaciones = "politicas.cancelaciones";

        // ---- Pie de página ----
        public const string PieTexto = "pie.texto";

        public static readonly IReadOnlyList<DefinicionContenido> Todas = new List<DefinicionContenido>
        {
            new(BienvenidaTitulo, SeccionInicio, "Bienvenida · título", "Organiza tu viaje", 80),
            new(BienvenidaTexto, SeccionInicio, "Bienvenida · texto",
                "Menú de bienvenida\nEmpieza a disfrutar con FlyVibe Mex el viaje de tus sueños.", 400, Multilinea: true),

            new(TarjetaTitulo("vuelos"), SeccionInicio, "Tarjeta Vuelos · título", "Conoce nuestros vuelos", 60),
            new(TarjetaTexto("vuelos"), SeccionInicio, "Tarjeta Vuelos · texto",
                "Vuela a los mejores destinos de México y el mundo con tarifas exclusivas.", 200, Multilinea: true),
            new(TarjetaImagen("vuelos"), SeccionInicio, "Tarjeta Vuelos · imagen",
                "https://images.unsplash.com/photo-1436491865332-7a61a109cc05?auto=format&fit=crop&w=600&q=80", EsImagen: true),

            new(TarjetaTitulo("paquetes"), SeccionInicio, "Tarjeta Paquetes · título", "Paquetes para ti", 60),
            new(TarjetaTexto("paquetes"), SeccionInicio, "Tarjeta Paquetes · texto",
                "Ahorra combinando Vuelo + Hotel con todo incluido para tus vacaciones.", 200, Multilinea: true),
            new(TarjetaImagen("paquetes"), SeccionInicio, "Tarjeta Paquetes · imagen",
                "https://images.unsplash.com/photo-1507525428034-b723cf961d3e?auto=format&fit=crop&w=600&q=80", EsImagen: true),

            new(TarjetaTitulo("hospedaje"), SeccionInicio, "Tarjeta Hospedaje · título", "Duerme con comodidad", 60),
            new(TarjetaTexto("hospedaje"), SeccionInicio, "Tarjeta Hospedaje · texto",
                "Encuentra desde hoteles boutique hasta resorts de lujo frente al mar.", 200, Multilinea: true),
            new(TarjetaImagen("hospedaje"), SeccionInicio, "Tarjeta Hospedaje · imagen",
                "https://images.unsplash.com/photo-1566073771259-6a8506099945?auto=format&fit=crop&w=600&q=80", EsImagen: true),

            new(TarjetaTitulo("tours"), SeccionInicio, "Tarjeta Tours · título", "Tours y Experiencias", 60),
            new(TarjetaTexto("tours"), SeccionInicio, "Tarjeta Tours · texto",
                "Excursiones guiadas, actividades de aventura y visitas culturales inolvidables.", 200, Multilinea: true),
            new(TarjetaImagen("tours"), SeccionInicio, "Tarjeta Tours · imagen",
                "https://images.unsplash.com/photo-1512813195386-6cf811ad3542?auto=format&fit=crop&w=600&q=80", EsImagen: true),

            new(PromoTitulo, SeccionInicio, "Banner de promociones · título", "La mejor promoción es la que no dejas pasar", 100),
            new(PromoTexto, SeccionInicio, "Banner de promociones · texto",
                "Aprovecha hasta 12 meses sin intereses en paquetes seleccionados por temporada.", 300, Multilinea: true),
            new(ContactoTitulo, SeccionInicio, "Contacto · título", "¿Tienes alguna duda sobre tu viaje?", 100),
            new(ContactoTexto, SeccionInicio, "Contacto · texto",
                "Contáctanos directamente o genera una solicitud de asistencia y nuestro equipo te atenderá de inmediato.", 300, Multilinea: true),

            new(PoliticasIntro, SeccionPoliticas, "Introducción",
                "En FlyVibeMex queremos que viajes tranquilo. Aquí encontrarás las reglas con las que funciona el sitio, " +
                "cómo cuidamos tus datos y qué pasa si necesitas cancelar.", 800, Multilinea: true),
            new(PoliticasTerminos, SeccionPoliticas, "Términos y condiciones",
                "1. Al crear una cuenta aceptas usar el sitio solo para reservar servicios de viaje para ti o tus acompañantes.\n" +
                "2. Los precios se muestran en pesos mexicanos (MXN) e incluyen el precio por noche, día o persona según el servicio.\n" +
                "3. Una reservación queda confirmada cuando el pago se aprueba; recibirás un código de reservación (FV-…).\n" +
                "4. Los códigos de promoción tienen vigencia, servicios y compra mínima; solo se aplica uno por reservación.\n" +
                "5. FlyVibeMex actúa como intermediario: cada proveedor (aerolínea, hotel, rentadora u operador de tours) es " +
                "responsable de prestar su servicio.\n" +
                "6. Este sitio es un proyecto escolar: los pagos son simulados y no se hace ningún cargo real.", 4000, Multilinea: true),
            new(PoliticasPrivacidad, SeccionPoliticas, "Aviso de privacidad",
                "Usamos tu nombre, correo y teléfono para administrar tu cuenta, tus reservaciones y tus solicitudes de soporte.\n" +
                "Tu contraseña se guarda cifrada (hash): nadie del personal puede verla.\n" +
                "De tus tarjetas solo guardamos la marca, los últimos 4 dígitos, el vencimiento y, si lo eliges, el banco; " +
                "nunca el número completo ni el CVV.\n" +
                "No vendemos ni compartimos tus datos con terceros fuera del proveedor del servicio que reservas.\n" +
                "Para corregir tus datos usa Mi perfil; para cualquier otra solicitud, abre un ticket en Soporte.", 4000, Multilinea: true),
            new(PoliticasCancelaciones, SeccionPoliticas, "Cancelaciones y reembolsos",
                "Cada servicio tiene su propia política de cancelación y se guarda en tu compra: aunque el proveedor la cambie " +
                "después, se respeta la que aceptaste.\n" +
                "Si cancelas con la anticipación que indica la política, el reembolso es del 100 %; si cancelas después, se " +
                "reembolsa el porcentaje indicado. Una vez iniciado el servicio ya no se puede cancelar.\n" +
                "Puedes cancelar desde Mis reservaciones. Los reembolsos los revisa un agente y se devuelven a la misma tarjeta.\n" +
                "Si un reembolso se rechaza, verás el motivo en Mis compras y puedes abrir un ticket para aclararlo.", 4000, Multilinea: true),

            new(PieTexto, SeccionPie, "Texto del pie de página", "FlyVibeMex · Proyecto integrador", 150)
        };

        private static readonly Dictionary<string, DefinicionContenido> PorClave = Todas.ToDictionary(d => d.Clave);

        /// <summary>Definición de una clave; <c>null</c> si la clave no existe (nunca se acepta una clave inventada).</summary>
        public static DefinicionContenido? Buscar(string? clave) =>
            clave is not null && PorClave.TryGetValue(clave, out var d) ? d : null;
    }
}
