/**
 * ============================================================================================
 * script_password.js — campos de contraseña del registro y del login (Módulo 1).
 * Lo cargan Views/Crearusuario/Index.cshtml y Views/Home/IniciarSesion.cshtml, DESPUÉS de
 * _ValidationScriptsPartial (el punto 3 usa el validador que arma jquery-validation-unobtrusive).
 * Estilos: wwwroot/assets/css/campo_password.css.
 *
 * 1. Botón "Ver" (<button data-ver-password aria-controls="id-del-campo">): alterna type="password" / "text".
 *    Al enviar el formulario el campo vuelve a type="password" (los gestores de contraseñas lo esperan así).
 * 2. Requisitos en vivo (<ul id="requisitos_password" data-campo="id-del-campo">): mismas reglas que
 *    CrearUsuarioViewModel.password. El DOM se toca SOLO cuando un requisito cambia de estado.
 * 3. Campos con data-validar-al-salir: jquery-validation no los revisa en cada tecla, solo al salir del campo
 *    (y al enviar). Lo usa el correo del registro: su regla [Remote] consulta al servidor, que tiene límite por IP.
 * Sin JavaScript todo sigue funcionando: el campo es un password normal y la lista se lee como texto de ayuda.
 * ============================================================================================
 */
(function () {
    'use strict';

    // ---------------------------------------------------------------- 1. Botón "Ver"
    function campoDe(boton) {
        var id = boton.getAttribute('aria-controls');
        return id ? document.getElementById(id) : null;
    }

    function mostrar(campo, boton, visible) {
        campo.type = visible ? 'text' : 'password';
        var texto = boton.querySelector('[data-texto-ver]');
        if (texto) texto.textContent = visible ? 'Ocultar' : 'Ver';
    }

    var botones = document.querySelectorAll('[data-ver-password]');
    Array.prototype.forEach.call(botones, function (boton) {
        var campo = campoDe(boton);
        if (!campo) return;

        boton.addEventListener('click', function () {
            mostrar(campo, boton, campo.type === 'password');
            campo.focus();
        });

        // Antes de enviar, se vuelve a ocultar. Fase de captura: corre antes que los manejadores de envío.
        if (campo.form) {
            campo.form.addEventListener('submit', function () {
                if (campo.type !== 'password') mostrar(campo, boton, false);
            }, true);
        }
    });

    // ---------------------------------------------------------------- 2. Requisitos en vivo
    var lista = document.getElementById('requisitos_password');
    var campoPassword = lista ? document.getElementById(lista.getAttribute('data-campo')) : null;

    if (lista && campoPassword) {
        // \p{L} = cualquier letra (acentos, ñ), igual que [RegularExpression] del servidor. Si el navegador no
        // entiende la bandera "u", se usa un rango latino equivalente para el español.
        var letra;
        try {
            letra = new RegExp('\\p{L}', 'u');
        } catch (e) {
            letra = /[A-Za-zÀ-ÖØ-öø-ÿ]/;
        }

        var reglas = {
            longitud: function (valor) { return valor.length >= 8 && valor.length <= 128; },
            letra: function (valor) { return letra.test(valor); },
            numero: function (valor) { return /\d/.test(valor); }
        };

        var requisitos = Array.prototype.slice.call(lista.querySelectorAll('[data-requisito]'));

        var actualizar = function () {
            var valor = campoPassword.value;
            requisitos.forEach(function (elemento) {
                var regla = reglas[elemento.getAttribute('data-requisito')];
                if (!regla) return;
                var cumple = regla(valor);
                // Sin cambio de estado no se toca el DOM: la lista es aria-live y se anunciaría en cada tecla.
                if ((elemento.getAttribute('data-cumple') === 'true') === cumple) return;
                elemento.setAttribute('data-cumple', cumple ? 'true' : 'false');
                elemento.classList.toggle('cumplido', cumple);
                var estado = elemento.querySelector('.estado-requisito');
                if (estado) estado.textContent = cumple ? 'Cumplido: ' : 'Pendiente: ';
            });
        };

        campoPassword.addEventListener('input', actualizar);
        actualizar();   // por si el navegador rellenó el campo (botón Atrás)
    }

    // ---------------------------------------------------------------- 3. Validar al salir del campo
    var $ = window.jQuery;
    if ($ && $.validator && document.querySelector('[data-validar-al-salir]')) {
        // jQuery ejecuta los "ready" en orden: unobtrusive registró el suyo antes (su script carga antes que
        // este), así que aquí el validador del formulario ya existe.
        $(function () {
            $('form').each(function () {
                var validador = $(this).data('validator');
                if (!validador) return;
                var alTeclear = validador.settings.onkeyup;
                validador.settings.onkeyup = function (elemento, evento) {
                    if (elemento.hasAttribute('data-validar-al-salir')) return;
                    if (typeof alTeclear === 'function') alTeclear.call(this, elemento, evento);
                };
            });
        });
    }
})();
