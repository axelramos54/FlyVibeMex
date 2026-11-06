/* ============================================================================================
 * script_mensajes.js — T7.5. Lo carga Views/Shared/_Mensajes.cshtml solo cuando hay mensajes.
 * Oculta solos los mensajes de éxito (atributo data-ocultar-ms, 6000 ms). Si el usuario pasa el mouse o enfoca
 * el mensaje, se pausa la cuenta para que alcance a leerlo; al salir, se cierra 3 s después.
 * Avisos y errores no tienen data-ocultar-ms: se quedan hasta que el usuario los cierra con la ✕.
 * ============================================================================================ */
(function () {
    'use strict';

    function cerrar(alerta) {
        if (window.bootstrap && window.bootstrap.Alert) {
            window.bootstrap.Alert.getOrCreateInstance(alerta).close();   // con la animación de Bootstrap
        } else {
            alerta.remove();                                               // página sin el JS de Bootstrap
        }
    }

    document.querySelectorAll('#mensajes_sitio [data-ocultar-ms]').forEach(function (alerta) {
        var espera = parseInt(alerta.getAttribute('data-ocultar-ms'), 10) || 6000;
        var temporizador = setTimeout(function () { cerrar(alerta); }, espera);

        function pausar() { clearTimeout(temporizador); }
        function reanudar() {
            clearTimeout(temporizador);
            temporizador = setTimeout(function () { cerrar(alerta); }, 3000);
        }

        alerta.addEventListener('mouseenter', pausar);
        alerta.addEventListener('focusin', pausar);
        alerta.addEventListener('mouseleave', reanudar);
        alerta.addEventListener('focusout', reanudar);
    });
})();
