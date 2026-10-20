/**
 * ============================================================================================
 * script.js — Lógica GLOBAL del shell de FlyVibe Mex (cargado desde Views/Home/Index.cshtml).
 * Implementa una navegación tipo SPA "casera": en lugar de recargar la página, cada sección se pide al
 * servidor como vista parcial (HTML) y se inserta dentro de <main id="contenido_main">.
 * Es el único archivo JavaScript del proyecto que se ejecuta realmente.
 * ============================================================================================
 */




/**
 * Función de prueba sin uso: ninguna vista la invoca.
 * NOTA: error en el mensaje ("secion" → "sesión"). Candidata a eliminarse o a convertirse en la lógica real
 * de inicio de sesión.
 */
function iniciar(){
    console.log("Iniciando secion");
}

/**
 * Carga inicial: cuando el navegador termina de construir el DOM del shell (evento DOMContentLoaded),
 * se pide la sección de Inicio ('/Home/Default') para que <main> no quede vacío.
 */
// Cargar una página por defecto cuando abra el sitio
document.addEventListener('DOMContentLoaded', () => {
    cargarPagina('/Home/Default');
});

/**
 * Carga una vista parcial del servidor y la muestra dentro de <main id="contenido_main">.
 *
 * Flujo:
 *   1. fetch(url) hace una petición GET a una acción de HomeController (p. ej. '/Home/Hoteles').
 *   2. ASP.NET ejecuta la acción, que devuelve la vista .cshtml con Layout = null (solo un fragmento HTML).
 *   3. Si la respuesta no es 2xx se lanza un Error y se muestra un aviso en <main>.
 *   4. Si todo va bien, el texto HTML recibido reemplaza el contenido de <main> mediante innerHTML.
 *
 * Efectos y limitaciones a tener en cuenta:
 *   - Historial: no se usa history.pushState, por lo que la URL no cambia, el botón Atrás no regresa a la
 *     sección anterior y no se puede compartir o recargar un enlace directo a una sección.
 *   - CSS por sección (no se acumula): cada vista trae su propio <link rel="stylesheet">. Al insertarse, el navegador descarga
 *     y aplica la hoja; al cambiar de sección el <link> se elimina junto con el HTML anterior, pero las reglas
 *     globales (como *, body) de cada hoja pueden afectar al shell mientras la sección está visible.
 *   - Scripts: por especificación HTML5, los elementos <script> insertados con innerHTML NO se ejecutan.
 *     Por eso los script_<Letra>.js enlazados en cada vista nunca corren. Alternativas: cargarlos desde
 *     Index.cshtml, o recorrer los <script> del HTML recibido y crearlos con document.createElement('script').
 *   - Seguridad: innerHTML confía en el HTML recibido. Solo debe usarse con contenido propio del servidor.
 *
 * @param {string} url Ruta relativa de la acción MVC a cargar (p. ej. '/Home/Viajes').
 * @returns {void} No devuelve nada; el resultado se refleja en el DOM (la promesa de fetch no se expone).
 */
function cargarPagina(url) {
    const mainContainer = document.getElementById('contenido_main');

    // 1) Petición GET al controlador; devuelve una promesa con la respuesta HTTP.
    fetch(url)
        .then(response => {
            if (!response.ok) {
                throw new Error('No se pudo cargar la página: ' + response.statusText);
            }
            return response.text();
        })
        .then(html => {
            // Inyecta el contenido del otro archivo en el <main>
            // 4) Inserta el fragmento HTML. Los <script> que contenga NO se ejecutarán (ver JSDoc).
            mainContainer.innerHTML = html;
        })
        // 3) Errores de red o respuestas no exitosas: se registran en consola y se avisa al usuario.
        .catch(error => {
            console.error('Error:', error);
            mainContainer.innerHTML = '<p class="alert alert-danger m-3">Error al cargar el contenido.</p>';
        });
}
