// =================================================================================================
// script_widget.js — avisos del widget hacia la página que lo contiene (GoldStay). Fase 4.
//
// Solo lo carga _LayoutWidget.cshtml, así que en el sitio normal este archivo no se descarga.
//
// Cómo funciona:
//   1. _LayoutWidget escribe en la página un <script type="application/json" id="widget-config">
//      con la lista de orígenes permitidos (la misma de appsettings: "Integracion:OrigenesPermitidos").
//   2. Una vista que quiera avisar algo incluye el parcial _EventoWidget, que escribe otro bloque JSON
//      con el evento. Al cargar la página, este archivo lo lee y lo manda.
//   3. También queda disponible window.FlyVibeWidget.avisar(tipo, datos) para avisos desde otro script.
//
// LO IMPORTANTE DE SEGURIDAD (las dos mitades de postMessage):
//   · AQUÍ: nunca se usa postMessage(mensaje, "*"). El segundo parámetro es el origen AL QUE se permite
//     entregar el mensaje; con "*" cualquier página que lograra incrustar este iframe podría leer el
//     contenido del aviso. Se manda una vez por cada origen configurado y punto.
//   · ALLÁ (en GoldStay): su listener TIENE que comparar event.origin con el dominio de FlyVibeMex antes
//     de hacer caso al mensaje. Sin esa comprobación, cualquier pestaña o iframe podría mandarle un
//     "reserva.confirmada" falso.
//
// Y lo que NO hay que hacer con esto: un postMessage es un dato que llega del navegador, no una prueba.
// Cualquiera con la consola abierta puede fabricar uno. Si GoldStay va a cobrar, mover dinero o marcar algo
// como pagado, el aviso sirve para REFRESCAR LA PANTALLA; la confirmación real tiene que pedirla su servidor
// al servidor de FlyVibeMex con el código de reserva.
// =================================================================================================

(function () {
    "use strict";

    /** Lee y parsea un bloque <script type="application/json"> por id. Devuelve null si no existe o no es válido. */
    function leerJson(id) {
        var elemento = document.getElementById(id);
        if (!elemento) {
            return null;
        }
        try {
            return JSON.parse(elemento.textContent);
        } catch (error) {
            console.error("[widget] JSON inválido en #" + id, error);
            return null;
        }
    }

    var configuracion = leerJson("widget-config") || {};
    var origenes = Array.isArray(configuracion.origenes) ? configuracion.origenes : [];

    /** ¿La página está realmente dentro de un iframe? Fuera de uno no hay a quién avisar. */
    function estaEmbebido() {
        try {
            return window.parent && window.parent !== window;
        } catch (error) {
            // Acceder a window.parent entre orígenes distintos puede lanzar: si lanza, hay un padre.
            return true;
        }
    }

    /**
     * Manda un aviso al contenedor.
     * @param {string} tipo  Nombre del evento, p. ej. "reserva.confirmada".
     * @param {object} datos Campos del evento (lo mínimo necesario).
     */
    function avisar(tipo, datos) {
        if (!tipo) {
            return;
        }
        if (!estaEmbebido()) {
            return;   // Página abierta directamente: no es un widget.
        }
        if (origenes.length === 0) {
            console.warn("[widget] Sin orígenes configurados: el aviso '" + tipo + "' no se manda.");
            return;
        }

        var mensaje = {
            fuente: "flyvibemex",        // para que el listener distinga estos mensajes de los de otros scripts
            tipo: tipo,
            datos: datos || {}
        };

        origenes.forEach(function (origen) {
            try {
                // Segundo parámetro = origen destino concreto. NUNCA "*" (ver la nota de arriba).
                window.parent.postMessage(mensaje, origen);
            } catch (error) {
                console.error("[widget] No se pudo avisar a " + origen, error);
            }
        });
    }

    // Avisos declarados por la vista con el parcial _EventoWidget.
    var evento = leerJson("widget-evento");
    if (evento && evento.tipo) {
        avisar(evento.tipo, evento.datos);
    }

    // ---------------------------------------------------------------------------------------------
    // Alto del contenido: el anfitrión no puede medir un iframe de otro origen, así que se lo decimos.
    // Sin esto, el catálogo queda cortado o con una barra de desplazamiento dentro del iframe.
    // Se manda al cargar y cuando el contenido cambia de tamaño (ResizeObserver), con un pequeño
    // "debounce" para no inundar al anfitrión durante una animación.
    // ---------------------------------------------------------------------------------------------
    var ultimaAltura = 0;
    var temporizador = null;

    function medirYAvisar() {
        var altura = Math.ceil(document.documentElement.scrollHeight);

        // Solo si cambió de verdad (más de 8 px): evita un ida y vuelta por cada píxel.
        if (Math.abs(altura - ultimaAltura) < 8) {
            return;
        }

        ultimaAltura = altura;
        avisar("flyvibe-altura", { altura: altura });
    }

    function programarMedicion() {
        if (temporizador) {
            window.clearTimeout(temporizador);
        }
        temporizador = window.setTimeout(medirYAvisar, 100);
    }

    if (estaEmbebido()) {
        window.addEventListener("load", medirYAvisar);

        if (typeof ResizeObserver === "function") {
            new ResizeObserver(programarMedicion).observe(document.documentElement);
        } else {
            window.addEventListener("resize", programarMedicion);
        }
    }

    // Disponible para avisos desde otros scripts de la página.
    window.FlyVibeWidget = { avisar: avisar, medir: medirYAvisar };
})();
