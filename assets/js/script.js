



function iniciar(){
    console.log("Iniciando secion");
}

// Cargar una página por defecto cuando abra el sitio
document.addEventListener('DOMContentLoaded', () => {
    cargarPagina('/pages/index_D.html');
});

function cargarPagina(url) {
    const mainContainer = document.getElementById('contenido_main');

    fetch(url)
        .then(response => {
            if (!response.ok) {
                throw new Error('No se pudo cargar la página: ' + response.statusText);
            }
            return response.text();
        })
        .then(html => {
            // Inyecta el contenido del otro archivo en el <main>
            mainContainer.innerHTML = html;
        })
        .catch(error => {
            console.error('Error:', error);
            mainContainer.innerHTML = '<p class="alert alert-danger m-3">Error al cargar el contenido.</p>';
        });
}
