/**
 * ============================================================================================
 * script_R.js — Sección R: cancelar reservación / cancelar y reembolsar.
 * Vista asociada: Views/Miusuario/_ModalesCancelacion.cshtml (la usan Mis reservaciones y Mis compras).
 * Estas páginas se cargan completas (no con cargarPagina), por eso este <script> sí se ejecuta.
 *
 * Botones de las tarjetas (cualquier elemento con estos atributos; de preferencia <button type="button">):
 *   data-accion="cancelar" | "reembolsar"   data-id="<id de la reservación>"   data-nombre="<texto a mostrar>"
 *   y, opcionales, data-reembolso="<texto del reembolso>"  data-politica="<política de la compra>".
 *   Cualquier elemento del modal con data-campo="x" recibe el valor de data-x.
 *
 * Flujo de cada modal:
 *   Paso 1 → un clic en "Sí, continuar".
 *   Paso 2 → mantener presionado el botón [data-sostener] durante DURACION_MS. Al completarse se envía el formulario.
 *            En "reembolsar" el botón se habilita solo cuando el motivo tiene entre 10 y 250 caracteres.
 * ============================================================================================
 */
(function () {
    'use strict';

    /** Tiempo que hay que mantener presionado el botón (ms). */
    const DURACION_MS = 1500;
    const MOTIVO_MIN = 10;
    const MOTIVO_MAX = 250;

    const MODALES = {
        cancelar: 'modal_cancelar',
        reembolsar: 'modal_reembolsar'
    };

    document.addEventListener('DOMContentLoaded', () => {
        if (typeof bootstrap === 'undefined') {
            console.error('script_R.js: falta el JS de Bootstrap (bootstrap.bundle.min.js).');
            return;
        }

        Object.values(MODALES).forEach(idModal => {
            const modal = document.getElementById(idModal);
            if (!modal) return;

            // Botones "Sí, continuar" y "Atrás"
            modal.querySelectorAll('[data-ir-paso]').forEach(boton => {
                boton.addEventListener('click', () => mostrarPaso(modal, boton.dataset.irPaso));
            });

            // Botón de clic sostenido → envía el formulario del modal
            const sostener = modal.querySelector('[data-sostener]');
            const formulario = modal.querySelector('form');
            if (sostener && formulario) {
                prepararBotonSostenido(sostener, () => {
                    if (idModal === MODALES.reembolsar && !motivoValido(formulario)) return false;
                    formulario.submit();
                    return true;
                });
            }

            // Al cerrar el modal se regresa todo a su estado inicial
            modal.addEventListener('hidden.bs.modal', () => reiniciarModal(modal));
        });

        prepararMotivo();

        // Clic en los botones de las tarjetas (delegación: funciona aunque las tarjetas se agreguen después)
        document.addEventListener('click', evento => {
            const disparador = evento.target.closest('[data-accion="cancelar"], [data-accion="reembolsar"]');
            if (!disparador) return;
            evento.preventDefault(); // por si es un <a href="#">

            const modal = document.getElementById(MODALES[disparador.dataset.accion]);
            if (!modal) return;

            reiniciarModal(modal);
            // Cada [data-campo="x"] del modal toma el data-x del botón (id, nombre, reembolso, politica…).
            // En <input> va a value; en el resto, a textContent (nunca HTML).
            modal.querySelectorAll('[data-campo]').forEach(campo => {
                const valor = disparador.dataset[campo.dataset.campo] || '';
                if (campo.tagName === 'INPUT') {
                    campo.value = valor;
                } else {
                    campo.textContent = valor || (campo.dataset.campo === 'nombre' ? 'esta reservación' : '');
                }
            });

            bootstrap.Modal.getOrCreateInstance(modal).show();
        });
    });

    /** Muestra solo los elementos del paso indicado (data-paso="1" | "2"). */
    function mostrarPaso(modal, paso) {
        modal.querySelectorAll('[data-paso]').forEach(el => {
            el.classList.toggle('d-none', el.dataset.paso !== String(paso));
        });
        if (String(paso) === '2') {
            const motivo = modal.querySelector('textarea[name="motivo"]');
            (motivo || modal.querySelector('[data-sostener]'))?.focus();
        }
    }

    function reiniciarModal(modal) {
        mostrarPaso(modal, 1);
        const formulario = modal.querySelector('form');
        const motivo = modal.querySelector('textarea[name="motivo"]');
        if (motivo) {
            motivo.value = '';
            motivo.dispatchEvent(new Event('input'));
        }
        const sostener = modal.querySelector('[data-sostener]');
        if (sostener && sostener._reiniciar) sostener._reiniciar();
        if (formulario) formulario.dataset.enviado = '';
    }

    // ----------------------------------------------------------------------------------------
    // Motivo del reembolso: contador y habilitar el botón solo con un motivo válido
    // ----------------------------------------------------------------------------------------
    function motivoValido(formulario) {
        const motivo = formulario.querySelector('textarea[name="motivo"]');
        if (!motivo) return true;
        const largo = motivo.value.trim().length;
        return largo >= MOTIVO_MIN && largo <= MOTIVO_MAX;
    }

    function prepararMotivo() {
        const motivo = document.getElementById('motivo_reembolso');
        const contador = document.getElementById('contador_motivo');
        const ayuda = document.getElementById('ayuda_motivo');
        const boton = document.getElementById('btn_sostener_reembolsar');
        if (!motivo || !boton) return;

        motivo.addEventListener('input', () => {
            const largo = motivo.value.trim().length;
            const valido = largo >= MOTIVO_MIN && largo <= MOTIVO_MAX;

            if (contador) contador.textContent = `${motivo.value.length} / ${MOTIVO_MAX}`;
            if (ayuda) {
                ayuda.textContent = valido ? 'Listo. Ahora mantén presionado el botón.'
                                           : `Mínimo ${MOTIVO_MIN} caracteres (faltan ${Math.max(0, MOTIVO_MIN - largo)}).`;
                ayuda.classList.toggle('text-success', valido);
                ayuda.classList.toggle('text-muted', !valido);
            }
            boton.disabled = !valido;
            if (!valido && boton._reiniciar) boton._reiniciar();
        });
    }

    // ----------------------------------------------------------------------------------------
    // Botón de clic sostenido
    // ----------------------------------------------------------------------------------------
    /**
     * Convierte un botón en "mantener presionado para confirmar".
     * Funciona con mouse, dedo (pantallas táctiles) y teclado (mantener Espacio o Enter).
     * La barra de progreso se dibuja con la variable CSS --progreso (0 a 1), ver style_R.css.
     * @param {HTMLButtonElement} boton
     * @param {() => boolean|void} alCompletar  se llama al llegar al 100 %; si devuelve false, el botón se reinicia.
     */
    function prepararBotonSostenido(boton, alCompletar) {
        const texto = boton.querySelector('[data-texto]');
        const textoOriginal = texto ? texto.textContent : boton.textContent;
        let inicio = null;
        let animacion = null;
        let completado = false;

        function pintar(progreso) {
            boton.style.setProperty('--progreso', progreso.toFixed(3));
        }

        function cuadro(ahora) {
            if (inicio === null) inicio = ahora;
            const progreso = Math.min((ahora - inicio) / DURACION_MS, 1);
            pintar(progreso);

            if (progreso < 1) {
                animacion = requestAnimationFrame(cuadro);
                return;
            }

            completado = true;
            animacion = null;
            boton.classList.add('sostener-completo');
            if (texto) texto.textContent = 'Procesando…';
            boton.setAttribute('aria-busy', 'true');

            if (alCompletar() === false) reiniciar();
        }

        function empezar() {
            if (boton.disabled || completado || animacion !== null) return;
            inicio = null;
            boton.classList.add('sostener-activo');
            if (texto) texto.textContent = 'Sigue presionando…';
            animacion = requestAnimationFrame(cuadro);
        }

        function soltar() {
            if (completado) return;
            if (animacion !== null) cancelAnimationFrame(animacion);
            animacion = null;
            inicio = null;
            boton.classList.remove('sostener-activo');
            pintar(0);
            if (texto) texto.textContent = textoOriginal;
        }

        function reiniciar() {
            completado = false;
            boton.classList.remove('sostener-completo');
            boton.removeAttribute('aria-busy');
            soltar();
        }

        // Mouse y pantallas táctiles
        boton.addEventListener('pointerdown', e => {
            if (e.button !== 0) return;           // solo botón principal
            boton.setPointerCapture?.(e.pointerId);
            empezar();
        });
        ['pointerup', 'pointercancel', 'lostpointercapture'].forEach(tipo => boton.addEventListener(tipo, soltar));
        boton.addEventListener('contextmenu', e => e.preventDefault()); // evita el menú al mantener presionado en celular

        // Teclado: mantener presionado Espacio o Enter
        boton.addEventListener('keydown', e => {
            if ((e.key === ' ' || e.key === 'Enter') && !e.repeat) {
                e.preventDefault();
                empezar();
            }
        });
        boton.addEventListener('keyup', e => {
            if (e.key === ' ' || e.key === 'Enter') soltar();
        });
        boton.addEventListener('blur', soltar);

        // Un clic normal no hace nada (hay que sostener)
        boton.addEventListener('click', e => e.preventDefault());

        boton._reiniciar = reiniciar;
        pintar(0);
    }
})();
