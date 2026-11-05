// =================================================================================================
// widget-flyvibe.js — receptor de los avisos que manda el widget de GoldStay incrustado en FlyVibeMex.
// Lo carga Views/Shared/_WidgetFlyVibe.cshtml, así que solo se descarga en las páginas que llevan el widget.
//
// GoldStay manda dos mensajes a window.parent:
//   { tipo: "goldstay-altura", altura: <px> }     al cargar y cada vez que cambia el alto de su contenido
//   { status: "exito", reservaId: <número> }      al completar la reserva
//
// ============================  LAS TRES COMPROBACIONES  ============================
//   1. event.origin EXACTAMENTE igual al dominio de GoldStay. Comparación con ===, nunca con indexOf ni
//      startsWith: "https://goldstayprb.somee.com.atacante.net" pasaría esas dos.
//   2. event.source === iframe.contentWindow: el mensaje viene de NUESTRO iframe y no de otra ventana,
//      pestaña o anuncio de la misma página.
//   3. La altura se valida como número y con un tope. Sin eso, un "altura: 99999999" deja la página
//      inservible (y un valor negativo o un texto rompería el estilo).
//
// Y LO MÁS IMPORTANTE: el mensaje "exito" NO es un comprobante. Lo manda el navegador del cliente y
// cualquiera con la consola abierta puede fabricar uno. Sirve para AVISAR EN PANTALLA. Si algún día
// FlyVibeMex tiene que cobrar, conciliar o guardar algo por esa reserva, su SERVIDOR tiene que confirmar el
// folio contra el servidor de GoldStay. Nunca se mueve dinero desde un listener de postMessage.
// ==================================================================================================

(function () {
    "use strict";

    /** Tope de alto razonable para el widget (px). Protege contra un valor absurdo o malicioso. */
    var ALTURA_MAXIMA = 2000;
    var ALTURA_MINIMA = 200;

    var elementoConfig = document.getElementById("goldstay-config");
    var origenEsperado = null;

    if (elementoConfig) {
        try {
            origenEsperado = (JSON.parse(elementoConfig.textContent) || {}).origen;
        } catch (error) {
            console.error("[goldstay] configuración inválida", error);
            return;
        }
    }

    if (!origenEsperado) {
        console.error("[goldstay] sin origen configurado: no se escuchan mensajes.");
        return;
    }

    window.addEventListener("message", function (evento) {
        // (1) Origen exacto.
        if (evento.origin !== origenEsperado) {
            return;
        }

        var iframe = document.getElementById("goldstayWidget");

        // (2) Que venga de nuestro iframe.
        if (!iframe || evento.source !== iframe.contentWindow) {
            return;
        }

        var datos = evento.data;
        if (!datos || typeof datos !== "object") {
            return;
        }

        // --- Ajuste de alto ------------------------------------------------------------------
        if (datos.tipo === "goldstay-altura") {
            // (3) Número válido y dentro de un rango sensato.
            var altura = Number(datos.altura);
            if (!Number.isFinite(altura)) {
                return;
            }
            altura = Math.min(Math.max(altura, ALTURA_MINIMA), ALTURA_MAXIMA);
            iframe.style.height = altura + "px";
            return;
        }

        // --- Reserva completada -------------------------------------------------------------
        if (datos.status === "exito") {
            var folio = Number(datos.reservaId);
            if (!Number.isFinite(folio)) {
                return;
            }

            // Aviso en pantalla. Si algún día hay que registrar o cobrar algo, eso lo confirma el servidor.
            console.info("[goldstay] reserva confirmada, folio", folio);

            // Evento propio, para que cualquier script de la página reaccione sin tocar este archivo.
            document.dispatchEvent(new CustomEvent("goldstay:reserva-confirmada", {
                detail: { reservaId: folio }
            }));

            mostrarAviso("¡Tu reserva de hospedaje en GoldStay quedó confirmada! Folio " + folio + ".");
        }
    });

    /**
     * Muestra el aviso usando los estilos del sitio (Bootstrap). Si no encuentra dónde ponerlo, cae a alert()
     * para que el mensaje no se pierda.
     */
    function mostrarAviso(texto) {
        var contenedor = document.querySelector(".goldstay-widget");

        if (!contenedor) {
            window.alert(texto);
            return;
        }

        var aviso = document.createElement("div");
        aviso.className = "alert alert-success mt-3";
        aviso.setAttribute("role", "status");
        aviso.textContent = texto;   // textContent, no innerHTML: el texto trae un dato del iframe.
        contenedor.insertAdjacentElement("afterend", aviso);
        aviso.scrollIntoView({ behavior: "smooth", block: "nearest" });
    }
})();
