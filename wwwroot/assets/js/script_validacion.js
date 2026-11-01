/**
 * ============================================================================================
 * script_validacion.js — Comodidades de los formularios de todo el sitio (T6.1).
 * Lo cargan Views/Shared/_ValidationScriptsPartial.cshtml (después de jQuery y jquery-validation).
 *
 * 1. Mensajes de jquery-validation en español (los de las DataAnnotations ya vienen en español en data-val-*).
 * 2. Estilos de Bootstrap: el campo con error se marca con .is-invalid y se quita al corregirlo; también los
 *    campos que el SERVIDOR marcó con error (clase input-validation-error al volver a mostrar el formulario).
 * 3. Al enviar con errores, el cursor va al primer campo con problema (focusInvalid de jquery-validation).
 * 4. Doble envío: en un formulario POST válido, los botones se desactivan y dicen "Procesando…".
 *    Para excluir un formulario: <form data-sin-bloqueo>. Al volver con "Atrás" se reactivan.
 * 5. Correcciones de compatibilidad con las DataAnnotations: [RegularExpression] con \p{L} (bandera "u") y
 *    [Range(typeof(bool), "true", "true")] en casillas.
 * Si jQuery no cargó (sin internet / CDN caído), solo funciona el punto 4 y el servidor valida todo.
 * ============================================================================================
 */
(function () {
    'use strict';

    // ---------------------------------------------------------------- 1-3. jquery-validation
    var $ = window.jQuery;
    if ($ && $.validator) {
        $.extend($.validator.messages, {
            required: 'Este campo es obligatorio.',
            remote: 'Revisa este campo.',
            email: 'Escribe un correo válido.',
            url: 'Escribe una dirección web válida.',
            date: 'Escribe una fecha válida.',
            dateISO: 'Escribe una fecha válida (AAAA-MM-DD).',
            number: 'Escribe un número válido.',
            digits: 'Escribe solo dígitos.',
            creditcard: 'Escribe un número de tarjeta válido.',
            equalTo: 'Escribe el mismo valor otra vez.',
            extension: 'El archivo no tiene una extensión permitida.',
            maxlength: $.validator.format('Escribe máximo {0} caracteres.'),
            minlength: $.validator.format('Escribe al menos {0} caracteres.'),
            rangelength: $.validator.format('Escribe entre {0} y {1} caracteres.'),
            range: $.validator.format('Escribe un valor entre {0} y {1}.'),
            max: $.validator.format('Escribe un valor menor o igual a {0}.'),
            min: $.validator.format('Escribe un valor mayor o igual a {0}.'),
            step: $.validator.format('Escribe un múltiplo de {0}.')
        });

        // Antes de que unobtrusive lea los formularios (lo hace al terminar de cargar la página).
        $.validator.setDefaults({
            focusInvalid: true,
            highlight: function (elemento) {
                $(elemento).addClass('is-invalid').removeClass('is-valid');
            },
            unhighlight: function (elemento) {
                $(elemento).removeClass('is-invalid');
            }
        });

        // Corrección (01-11-2026): unobtrusive arma la expresión de [RegularExpression] SIN la bandera Unicode "u",
        // así que en el navegador \p{L} ("cualquier letra, con acentos y ñ") se leía como una "p" literal: "Axel"
        // fallaba en Crear cuenta, Mi perfil y Alta de usuarios del SA, aunque el servidor sí lo aceptaba.
        // Mismo criterio que unobtrusive: el patrón debe cubrir TODO el valor.
        $.validator.addMethod('regex', function (valor, elemento, patron) {
            if (this.optional(elemento)) return true;
            var expresion;
            try {
                expresion = new RegExp(patron, 'u');      // \p{L}, \p{N}… (navegadores actuales)
            } catch (e) {
                expresion = new RegExp(patron);           // patrón que no es válido en modo Unicode
            }
            var coincidencia = expresion.exec(valor);
            return coincidencia !== null && coincidencia.index === 0 && coincidencia[0].length === valor.length;
        });

        // Corrección (01-11-2026): [Range(typeof(bool), "true", "true")] (casilla "Acepto los términos") llega como
        // data-val-range-min="True" / data-val-range-max="True" y jquery-validation convierte los límites a número
        // (Number("True") = NaN), así que la casilla marcada siempre daba error. En una casilla, la regla significa
        // "debe estar marcada" (o desmarcada si el rango es false); el valor se lee del atributo original.
        var rangoOriginal = $.validator.methods.range;
        $.validator.addMethod('range', function (valor, elemento, limites) {
            if (elemento.type === 'checkbox') {
                var minimo = elemento.getAttribute('data-val-range-min');
                var debeEstarMarcada = String(minimo !== null ? minimo : limites[0]).toLowerCase() === 'true';
                return elemento.checked === debeEstarMarcada;
            }
            return rangoOriginal.call(this, valor, elemento, limites);
        });
    }

    // Campos que ya vienen marcados por el servidor (POST con errores).
    function marcarErroresDelServidor() {
        document.querySelectorAll('.input-validation-error').forEach(function (el) {
            el.classList.add('is-invalid');
        });
        // Si el servidor regresó errores, el primer campo con problema recibe el foco.
        var primero = document.querySelector('form .input-validation-error:not([type=hidden])');
        if (primero && typeof primero.focus === 'function') primero.focus();
    }

    // ---------------------------------------------------------------- 4. Doble envío
    var SELECTOR_BOTONES = 'button:not([type]), button[type="submit"], input[type="submit"]';

    function bloquear(form) {
        // Después del evento: los datos del botón pulsado (name/value) ya se tomaron para el envío.
        setTimeout(function () {
            form.querySelectorAll(SELECTOR_BOTONES).forEach(function (boton) {
                if (boton.disabled) return;
                boton.dataset.textoOriginal = boton.tagName === 'INPUT' ? boton.value : boton.innerHTML;
                boton.disabled = true;
                boton.dataset.bloqueado = 'true';
                if (boton.tagName === 'INPUT') boton.value = 'Procesando…';
                else boton.innerHTML = '<span class="spinner-border spinner-border-sm me-1" aria-hidden="true"></span>Procesando…';
            });
        }, 0);
    }

    function desbloquearTodo() {
        document.querySelectorAll('[data-bloqueado="true"]').forEach(function (boton) {
            boton.disabled = false;
            boton.dataset.bloqueado = '';
            if (boton.dataset.textoOriginal !== undefined) {
                if (boton.tagName === 'INPUT') boton.value = boton.dataset.textoOriginal;
                else boton.innerHTML = boton.dataset.textoOriginal;
            }
        });
    }

    document.addEventListener('submit', function (e) {
        var form = e.target;
        if (!(form instanceof HTMLFormElement)) return;
        if ((form.getAttribute('method') || 'get').toLowerCase() !== 'post') return;   // búsquedas (GET): sin bloqueo
        if (form.hasAttribute('data-sin-bloqueo')) return;
        if (e.defaultPrevented) return;
        // Si jquery-validation encontró errores, no se envía: no se bloquea.
        if ($ && $.fn.valid && $(form).data('validator') && !$(form).valid()) return;
        bloquear(form);
    });

    // Al volver con "Atrás" el navegador puede mostrar la página guardada con los botones desactivados.
    window.addEventListener('pageshow', function (e) {
        if (e.persisted) desbloquearTodo();
    });

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', marcarErroresDelServidor);
    else marcarErroresDelServidor();
})();
