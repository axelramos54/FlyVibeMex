# 02 · Guía de arquitectura MVC y frontend

> **Para quién es este documento:** cualquier integrante que vaya a modificar el código. Explica cómo está organizado FlyVibe Mex **hoy**, cómo viaja una petición desde el clic hasta la pantalla y qué convenciones seguimos para no romper nada.
>
> Sustituye y actualiza a `docs/FlyVibeMex – Documento explicativo del proyecto.docx` (10/10/2026) y a `docs/documento explicativos.txt`.

## Índice
1. [Panorama en 30 segundos](#1-panorama-en-30-segundos)
2. [Estructura de directorios](#2-estructura-de-directorios)
3. [Ciclo de vida en Program.cs](#3-ciclo-de-vida-en-programcs)
4. [Cómo se aplica MVC en el proyecto](#4-cómo-se-aplica-mvc-en-el-proyecto)
5. [Navegación tipo SPA paso a paso](#5-navegación-tipo-spa-paso-a-paso)
6. [Catálogo de controladores](#6-catálogo-de-controladores)
7. [Catálogo de vistas](#7-catálogo-de-vistas)
8. [Convenciones del frontend](#8-convenciones-del-frontend)
9. [Receta: agregar una sección nueva](#9-receta-agregar-una-sección-nueva)
10. [Limitaciones conocidas](#10-limitaciones-conocidas)
11. [Correcciones respecto al documento anterior](#11-correcciones-respecto-al-documento-anterior)

---

## 1. Panorama en 30 segundos

- Es una aplicación **ASP.NET Core MVC sobre .NET 10**, sin paquetes NuGet adicionales.
- La página principal (`Views/Home/Index.cshtml`) se carga **una sola vez** y funciona como "cascarón" (*shell*).
- Al hacer clic en el menú, JavaScript pide al servidor la sección correspondiente y la **inserta dentro de `<main>`** sin recargar la página.
- Cada sección es una **vista parcial** (`Layout = null`) con su propio CSS y JS, identificados por una **letra**.
- Todo el contenido visible (hoteles, vuelos, precios) está **escrito a mano** en las vistas: todavía no hay lógica de negocio, datos dinámicos ni inicio de sesión.

## 2. Estructura de directorios

```
FlyVibeMex/                          ← raíz del repositorio (aquí está FlyVibeMex.csproj)
├── FlyVibeMex.csproj                ← proyecto web, net10.0, Nullable e ImplicitUsings activados
├── Program.cs                       ← arranque: servicios, middlewares y ruta por defecto
├── appsettings.json                 ← configuración (solo logging por ahora)
├── appsettings.Development.json     ← configuración del entorno Development
├── Properties/
│   └── launchSettings.json          ← perfiles http (5106) y https (7045)
│
├── Controllers/
│   ├── HomeController.cs            ← ÚNICO controlador en uso: shell + una acción por sección
│   └── DefaultController.cs         ← copia obsoleta; sus rutas fallan (ver §6)
│
├── Models/                          ← 24 clases de entidad (solo propiedades) + ErrorViewModel
│                                       (detalle en 03_CATALOGO_MODELOS_CSHARP.md)
│
├── Views/
│   ├── _ViewImports.cshtml          ← usings y Tag Helpers comunes
│   ├── _ViewStart.cshtml            ← layout por defecto (_Layout), anulado en Home
│   ├── Home/
│   │   ├── Index.cshtml             ← SHELL: navbar + <main id="contenido_main">
│   │   ├── Default.cshtml           ← sección D: Inicio
│   │   ├── Viajes.cshtml            ← sección V: buscador de vuelos
│   │   ├── Hoteles.cshtml           ← sección H: buscador de hospedaje
│   │   ├── Tours.cshtml             ← sección T: buscador de tours
│   │   ├── Paquetes.cshtml          ← sección P: buscador de paquetes
│   │   ├── sesion.cshtml            ← sección s: inicio de sesión (visual)
│   │   ├── card.cshtml              ← maqueta de tarjetas de resultados (prueba)
│   │   ├── Privacy.cshtml           ← plantilla, sin acción (404)
│   │   └── exIndex.cshtml           ← plantilla "Welcome", sin uso
│   └── Shared/
│       ├── _Layout.cshtml           ← plantilla maestra de ASP.NET (rutas rotas; no la usa Home)
│       ├── _Layout.cshtml.css       ← CSS aislado de _Layout
│       ├── _ValidationScriptsPartial.cshtml ← validación jQuery (sin uso)
│       └── Error.cshtml             ← página de error
│
├── wwwroot/                         ← todo lo que el navegador descarga directamente
│   ├── favicon.ico
│   ├── assets/
│   │   ├── css/   style.css (global) · style_D/V/H/T/P/s.css (por sección) · site.css · originalstyles.css
│   │   ├── js/    script.js (global) · script_D/V/H/T/P/s.js (por sección) · app.js · site.js
│   │   └── img/   imagen-prueba.jpg
│   └── lib/                         ← carpetas de la plantilla (bootstrap, jquery…) con SOLO archivos LICENSE
│
├── docs/                            ← esta documentación
├── README.md                        ← portada e instrucciones de ejecución
└── CONTEXTO_PROYECTO_FLYVIBEMEX.md  ← documento maestro (auditoría técnica)
```

> En el prototipo anterior a MVC, las páginas estaban en `pages/` (`index_D.html`, `index_H.html`…) y los recursos en `assets/` en la raíz. Hoy esas páginas son las vistas de `Views/Home/` y los recursos están en `wwwroot/assets/`.

## 3. Ciclo de vida en `Program.cs`

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();      // (1) registra MVC: controladores + vistas Razor

var app = builder.Build();
if (!app.Environment.IsDevelopment())            // (2) solo fuera de Development:
{
    app.UseExceptionHandler("/Home/Error");      //     excepciones → HomeController.Error()
    app.UseHsts();                               //     obliga HTTPS en el navegador
}
app.UseHttpsRedirection();                       // (3) redirige http → https cuando hay perfil https
app.UseRouting();                                // (4) decide qué endpoint atiende la URL
app.UseAuthorization();                          // (5) autorización (aún sin esquemas ni reglas)
app.MapStaticAssets();                           // (6) sirve wwwroot/ desde el manifiesto de compilación
app.MapControllerRoute(                          // (7) ruta convencional
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();
app.Run();                                       // (8) inicia el servidor Kestrel
```

**¿Qué hace la ruta por defecto?** `{controller=Home}/{action=Index}/{id?}` significa:

| URL | Controlador | Acción |
|---|---|---|
| `/` | Home | Index |
| `/Home/Hoteles` | Home | Hoteles |
| `/Home/Viajes/5` | Home | Viajes (con `id = 5`, hoy no se usa) |

**¿Qué es `MapStaticAssets()`?** Desde .NET 9, en lugar de leer `wwwroot/` en cada petición, ASP.NET genera **al compilar** un manifiesto con todos los archivos estáticos (con compresión y *fingerprint* para caché). Consecuencia práctica:

> ⚠️ Si agregas, renombras o mueves un archivo en `wwwroot/`, **detén y vuelve a ejecutar** la aplicación (`dotnet run`) y refresca con **Ctrl + F5**. Si no, el archivo nuevo dará 404 o verás la versión anterior.

**Lo que aún no está configurado:** autenticación (cookies), sesiones, servicios propios en inyección de dependencias, rutas de API JSON y áreas (`/Admin`).

## 4. Cómo se aplica MVC en el proyecto

| Capa | Carpeta | Responsabilidad | Estado actual |
|---|---|---|---|
| **Model** | `Models/` | Describir los datos del dominio | 24 clases con propiedades; ningún controlador las usa todavía |
| **View** | `Views/` | Generar el HTML | Una vista por sección, con contenido fijo |
| **Controller** | `Controllers/` | Recibir la petición y elegir la respuesta | `HomeController`: cada acción hace `return View();` |

Cuando una acción hace `return View();`, ASP.NET busca un archivo con el **mismo nombre que la acción** en `Views/<Controlador>/` y, si no lo encuentra, en `Views/Shared/`. Por ejemplo, `HomeController.Hoteles()` → `Views/Home/Hoteles.cshtml`.

## 5. Navegación tipo SPA paso a paso

```mermaid
sequenceDiagram
    actor U as Usuario
    participant B as Navegador (Index.cshtml + script.js)
    participant S as ASP.NET (HomeController)

    U->>B: Abre https://localhost:7045/
    B->>S: GET /
    S-->>B: Index.cshtml (documento completo)
    B->>B: DOMContentLoaded → cargarPagina('/Home/Default')
    B->>S: fetch GET /Home/Default
    S-->>B: Default.cshtml (fragmento, Layout = null)
    B->>B: main#contenido_main.innerHTML = fragmento
    U->>B: Clic en "Hoteles"
    B->>B: onclick → cargarPagina('/Home/Hoteles'); return false
    B->>S: fetch GET /Home/Hoteles
    S-->>B: Hoteles.cshtml (fragmento)
    B->>B: Reemplaza el contenido de main
```

1. El usuario abre la raíz `/` y recibe `Index.cshtml`, que trae la barra de navegación, un `<main id="contenido_main">` vacío, Bootstrap (CDN), `style.css` y `script.js`.
2. Cuando el DOM está listo (`DOMContentLoaded`), `script.js` llama a `cargarPagina('/Home/Default')`.
3. `cargarPagina(url)` hace `fetch(url)`; ASP.NET ejecuta la acción y devuelve la vista **sin layout** (solo el fragmento HTML).
4. Si la respuesta es correcta, el fragmento se coloca en `<main>` con `innerHTML`. Si falla, se muestra *"Error al cargar el contenido."*
5. Cada enlace del menú hace `cargarPagina('/Home/<Accion>'); return false;`. El `return false` evita que el `href="#"` añada `#` a la URL.

**Tres efectos que hay que conocer:**

| Efecto | Explicación | Consecuencia |
|---|---|---|
| **Scripts que no corren** | Por especificación HTML, un `<script>` insertado con `innerHTML` **no se ejecuta** | Los `script_<Letra>.js` enlazados al final de cada vista nunca corren. Hoy no se nota porque están vacíos. Si se les agrega código, hay que cargarlos desde `Index.cshtml` o hacer que `cargarPagina()` cree el `<script>` con `document.createElement` |
| **CSS por sección** | El `<link>` de cada vista se aplica mientras está en pantalla y se elimina junto con su HTML al cambiar de sección | No se acumulan, pero la hoja se vuelve a pedir en cada visita (posible parpadeo) y sus reglas globales (`*`, `body`) afectan también al shell mientras la sección está cargada |
| **Historial** | No se usa `history.pushState` | La URL nunca cambia: el botón **Atrás** no regresa a la sección anterior y no se pueden compartir enlaces directos a una sección |

## 6. Catálogo de controladores

### `HomeController` (`Controllers/HomeController.cs`)
Ruta base `/Home`. Todas las acciones son **GET**, sin parámetros, y devuelven `View()`.

| Acción | URL | Vista | Tipo de respuesta | Sección |
|---|---|---|---|---|
| `Index()` | `/`, `/Home`, `/Home/Index` | `Index.cshtml` | Documento completo | Shell |
| `Default()` | `/Home/Default` | `Default.cshtml` | Parcial | D · Inicio |
| `Viajes()` | `/Home/Viajes` | `Viajes.cshtml` | Parcial | V · Vuelos |
| `Hoteles()` | `/Home/Hoteles` | `Hoteles.cshtml` | Parcial | H · Hospedaje |
| `Tours()` | `/Home/Tours` | `Tours.cshtml` | Parcial | T · Tours |
| `Paquetes()` | `/Home/Paquetes` | `Paquetes.cshtml` | Parcial | P · Paquetes |
| `sesion()` | `/Home/sesion` | `sesion.cshtml` | Parcial | s · Inicio de sesión |
| `card()` | `/Home/card` | `card.cshtml` | Parcial | Maqueta de tarjetas |
| `Error()` | `/Home/Error` | `Shared/Error.cshtml` | Documento con `_Layout` | Manejador de errores (fuera de Development) |

> Falta la acción `Privacy()`: existe `Views/Home/Privacy.cshtml` y `_Layout` la enlaza, pero `/Home/Privacy` responde **404**.

### `DefaultController` (`Controllers/DefaultController.cs`) — obsoleto
- La clase se llama `Default` (sin el sufijo `Controller`), pero ASP.NET **sí la registra** como controlador porque hereda de `Controller`.
- Expone `/Default/Index`, `/Default/Hoteles`, `/Default/Paquetes`, `/Default/Tours`, `/Default/Viajes`, `/Default/sesion` y `/Default/card`.
- Como no existe `Views/Default/`, **cualquiera de esas URL produce un error 500** ("vista no encontrada").
- Ninguna vista ni script la usa. **Recomendación:** eliminar el archivo.

## 7. Catálogo de vistas

### Vistas activas
| Vista | Letra | CSS | JS | Pantalla del prototipo | Contenido actual |
|---|---|---|---|---|---|
| `Index.cshtml` | — | Bootstrap CDN + `style.css` | Bootstrap CDN + `script.js` | Home (shell) | Navbar: Home, Viajes, Hoteles, Tours, Paquetes, "VER TARJETA test", "Iniciar sesión" y foto |
| `Default.cshtml` | D | `style_D.css` | `script_D.js` *(vacío)* | Home / Explorar | Bienvenida, tarjetas de categorías, banner de promoción, bloque de contacto |
| `Viajes.cshtml` | V | `style_V.css` | `script_V.js` *(vacío)* | Vuelos + Resultados | Buscador (origen, destino, fechas, pasajeros) y 3 vuelos de ejemplo |
| `Hoteles.cshtml` | H | `style_H.css` | `script_H.js` *(vacío)* | Hospedaje + Resultados | Buscador (destino, entrada, salida, huéspedes) y 3 hoteles de ejemplo |
| `Tours.cshtml` | T | `style_T.css` | `script_T.js` *(vacío)* | *(no está en el mapa de sitio)* | Buscador y 3 tours de ejemplo |
| `Paquetes.cshtml` | P | `style_P.css` | `script_P.js` *(vacío)* | Paquetes + Resultados | Buscador y 3 paquetes de ejemplo |
| `sesion.cshtml` | s | `style_s.css` | `script_s.js` | Cuenta › Iniciar sesión | Formulario visual sin envío |
| `card.cshtml` | — | `style_V.css` | `script_H.js` | Resultados (maqueta) | 12 tarjetas de ejemplo (hoteles, paquetes, tours, vuelos) |

### Archivos de la plantilla original de ASP.NET
| Archivo | Situación |
|---|---|
| `Shared/_Layout.cshtml` | No lo usa Home (todas sus vistas tienen `Layout = null`). Apunta a `~/lib/.../dist`, `~/css/site.css` y `~/js/site.js`, que **no existen** |
| `Shared/Error.cshtml` | Se usa como página de error; hereda `_Layout`, así que sale sin estilos |
| `Shared/_ValidationScriptsPartial.cshtml` | Sin uso; sus librerías no están en `wwwroot/lib` |
| `Home/Privacy.cshtml` | Sin acción en el controlador (404) |
| `Home/exIndex.cshtml` | "Welcome" de la plantilla, sin uso |

Cada archivo `.cshtml` tiene al inicio un comentario Razor `@* … *@` con esta misma información y señala con `FIXME`/`TODO` lo que falta corregir.

## 8. Convenciones del frontend

### 8.1 Nomenclatura por letras
Cada sección se identifica con una letra que aparece en su CSS y su JS:

| Letra | Sección | Vista | CSS | JS |
|---|---|---|---|---|
| **D** | Default (Inicio) | `Default.cshtml` | `style_D.css` | `script_D.js` |
| **V** | Viajes (vuelos) | `Viajes.cshtml` | `style_V.css` | `script_V.js` |
| **H** | Hospedajes | `Hoteles.cshtml` | `style_H.css` | `script_H.js` |
| **T** | Tours | `Tours.cshtml` | `style_T.css` | `script_T.js` |
| **P** | Paquetes | `Paquetes.cshtml` | `style_P.css` | `script_P.js` |
| **s** | Sesión | `sesion.cshtml` | `style_s.css` | `script_s.js` |

Los archivos **sin letra** (`style.css`, `script.js`) son globales y afectan a toda la página.

### 8.2 Regla de oro de estilos
> **`class` = Bootstrap. `#id` = estilo propio.**

- Las clases se reservan para las utilidades y componentes de Bootstrap (`container`, `row`, `col-12`, `card`, `btn`, `d-flex`…).
- El diseño propio se aplica con selectores de **id**. Ids de uso común:

| Id | Uso |
|---|---|
| `#contenido_main` | Contenedor del shell donde se inyectan las secciones |
| `#mapas_preferidos` | Contenedor principal de cada sección |
| `#buscador` | Columna del formulario de búsqueda |
| `#tarjetas` | Fila o columna de tarjetas de resultados |
| `#navbar`, `#navbar_left`, `#navbar_right` | Barra de navegación |

> Nota: algunas hojas ya usan clases propias (`.mi-tarjeta`, `.search-card`, `.btn-card-action`, `.btn-hero-primary`…). Ver la decisión pendiente en el documento 04.

### 8.3 Rutas a recursos
- En `Index.cshtml` se usan rutas absolutas: `/assets/css/style.css`, `/assets/js/script.js`.
- En las vistas parciales se usa `~/assets/...`, que Razor convierte en la ruta raíz de la aplicación.

### 8.4 Tarjetas de resultados
`card.cshtml` es la referencia de diseño de la tarjeta que el backend usará para llenar la columna derecha de los buscadores: imagen, insignia de ruta/categoría, título, descripción, precio "desde" y botón de acción.

## 9. Receta: agregar una sección nueva

Ejemplo: sección **Vehículos** con la letra **A** (de *autos*; elige una letra libre y anótala en la tabla 8.1).

1. **Vista:** crear `Views/Home/Vehiculos.cshtml` con:
   ```cshtml
   @{
       ViewData["Title"] = "Vehiculos";
       Layout = null;               // obligatorio: se inyecta dentro de Index
   }
   <link rel="stylesheet" href="~/assets/css/style_A.css">
   <div class="container mt-4" id="mapas_preferidos">
       ...
   </div>
   ```
2. **Acción:** en `HomeController`:
   ```csharp
   public IActionResult Vehiculos()
   {
       return View();
   }
   ```
3. **Menú:** en `Index.cshtml`, dentro de `#navbar_right`:
   ```html
   <a class="m-1" onclick="cargarPagina('/Home/Vehiculos'); return false;" href="#">Vehículos</a>
   ```
4. **Estilos y scripts:** crear `wwwroot/assets/css/style_A.css` y, si hace falta, `script_A.js` **cargado desde `Index.cshtml`** (recuerda que dentro de la parcial no se ejecuta).
5. **Recompilar:** detener y ejecutar de nuevo `dotnet run`, y refrescar con Ctrl + F5 (por `MapStaticAssets`).

## 10. Limitaciones conocidas

| Limitación | Impacto | Dónde se documenta en el código |
|---|---|---|
| Contenido estático en todas las vistas | No hay búsquedas ni resultados reales | Encabezado de cada vista |
| Formularios sin `name` y con `action="#"` | Al pulsar "Buscar" se recarga el shell y se vuelve a Inicio | Comentarios sobre `#buscador` |
| `<script>` de las parciales no se ejecutan | JS por sección inutilizable tal como está | JSDoc de `cargarPagina` en `script.js` |
| Sin historial de navegación | Atrás y enlaces directos no funcionan | Encabezado de `Index.cshtml` |
| Login visual con contraseña visible (`type="text"`) | No apto para uso real | `sesion.cshtml` |
| `_Layout.cshtml` con rutas rotas | Página de error sin estilos | `_Layout.cshtml`, `Error.cshtml` |
| `DefaultController` | Error 500 en `/Default/*` | `DefaultController.cs` |
| Ids duplicados (`secion`, `tarjetas`) | HTML inválido; selectores frágiles | `Index.cshtml`, `card.cshtml` |

## 11. Correcciones respecto al documento anterior

El `.docx` del 10/10/2026 era correcto en lo general. Estas precisiones ya están incorporadas aquí:

| El `.docx` decía | Situación real |
|---|---|
| `DefaultController` "no es reconocido por ASP.NET" | **Sí** se registra (hereda de `Controller`); sus URL `/Default/*` dan error 500 por falta de vistas |
| `wwwroot/lib/` contiene "Bootstrap, jQuery (de la plantilla)" | Solo contiene archivos **LICENSE**; las librerías reales se cargan desde CDN en `Index.cshtml` |
| `card.cshtml` no tiene CSS ni JS asociado | Enlaza `style_V.css` y `script_H.js` |
| Los `<link>` de CSS de cada vista se aplican | Correcto, y además se eliminan al cambiar de sección (no se acumulan) |
