/**
 * ============================================================================================
 * script_seleccion_servicios.js — Lista de servicios con casillas del Gestor de ventas.
 * Vistas: Views/Admin/GestorVentas/PaqueteForm.cshtml (servicios incluidos) y PromocionForm.cshtml
 * (servicios a los que aplica la promoción).
 *
 * Marcado esperado (todo dentro de un elemento con data-seleccion):
 *   <select data-filtro="tipo">  <select data-filtro="proveedor">  <input data-filtro="texto">
 *   <input type="checkbox" data-filtro="marcados">            → mostrar solo los marcados
 *   <button data-accion="marcar-visibles">  <button data-accion="quitar-visibles">   (opcionales)
 *   <span data-contador>                                       → "3 marcados · 12 visibles"
 *   <tr data-tipo="HOSPEDAJE" data-proveedor="5" data-texto="hotel playa azul cancún"> … <input type="checkbox" data-marca> …
 * Solo filtra lo que se VE: las filas ocultas se siguen enviando con el formulario (el servidor decide).
 * ============================================================================================
 */
(function () {
    'use strict';

    document.querySelectorAll('[data-seleccion]').forEach(contenedor => {
        const filas = Array.from(contenedor.querySelectorAll('tr[data-tipo]'));
        const tipo = contenedor.querySelector('[data-filtro="tipo"]');
        const proveedor = contenedor.querySelector('[data-filtro="proveedor"]');
        const texto = contenedor.querySelector('[data-filtro="texto"]');
        const marcados = contenedor.querySelector('[data-filtro="marcados"]');
        const contador = contenedor.querySelector('[data-contador]');

        const casilla = fila => fila.querySelector('input[type="checkbox"][data-marca]');

        function aplicar() {
            const t = tipo ? tipo.value : '';
            const p = proveedor ? proveedor.value : '';
            const q = texto ? texto.value.trim().toLowerCase() : '';
            const soloMarcados = marcados ? marcados.checked : false;
            let visibles = 0;

            filas.forEach(fila => {
                const ok = (!t || fila.dataset.tipo === t)
                    && (!p || fila.dataset.proveedor === p)
                    && (!q || (fila.dataset.texto || '').includes(q))
                    && (!soloMarcados || casilla(fila)?.checked);
                fila.classList.toggle('d-none', !ok);
                if (ok) visibles++;
            });

            if (contador) {
                const total = filas.filter(f => casilla(f)?.checked).length;
                contador.textContent = `${total} marcado(s) · ${visibles} visible(s)`;
            }
        }

        function marcarVisibles(valor) {
            filas.filter(f => !f.classList.contains('d-none')).forEach(f => {
                const c = casilla(f);
                if (c) c.checked = valor;
            });
            aplicar();
        }

        [tipo, proveedor, marcados].forEach(el => el?.addEventListener('change', aplicar));
        texto?.addEventListener('input', aplicar);
        filas.forEach(f => casilla(f)?.addEventListener('change', aplicar));
        contenedor.querySelector('[data-accion="marcar-visibles"]')?.addEventListener('click', () => marcarVisibles(true));
        contenedor.querySelector('[data-accion="quitar-visibles"]')?.addEventListener('click', () => marcarVisibles(false));

        aplicar();
    });
})();
