# PLAN_CALIDAD_Y_OPTIMIZACION.md

> Hoja de ruta de **calidad de código y optimización** para la versión candidata a lanzamiento (Release Candidate)
> de FlyVibeMex. Este documento **no agrega funcionalidades**: solo limpia, simplifica, acelera, ordena y documenta
> lo que ya existe, con la regla de oro de **cero regresiones**.

| Campo | Valor |
|---|---|
| Fecha | 02/11/2026 |
| Repositorio auditado | `Proyecto/FlyVibeMex/`, rama `main`, commit `7a714d5` |
| Estado de Git al auditar | Limpio salvo 2 docs modificados (`docs/07_…`, `docs/CONTEXTO_…`). 273 archivos versionados. |
| Alcance | `Program.cs`, `Controllers/` (18), `Services/` (47), `Models/` (47), `Data/` (2), `Bot/` (20), `Views/` (105), `wwwroot/` (12), `sql/` (3), `docs/` (11) |
| Volumen | **19 862 líneas de C#** · **7 648 líneas de Razor** · **2 891 líneas de T‑SQL** · 479 líneas de JS propio · 26.6 KB de CSS propio |
| Documentos de contexto leídos | `docs/01` a `docs/07`, `docs/CONTEXTO_PROYECTO_FLYVIBEMEX.md` |
| **Limitación de esta auditoría** | **No pude compilar.** El entorno donde corro no tiene SDK de .NET y la descarga está bloqueada por política de red (`dotnet.microsoft.com` → 403). Todo hallazgo es de **análisis estático**. Por eso el plan está dividido en lotes pequeños y **cada lote termina con `dotnet build` ejecutado por ustedes**. |

---

## 0. Diagnóstico: resumen ejecutivo

### 0.1 Veredicto en una línea

El proyecto **ya no es el prototipo** que describe `docs/CONTEXTO_PROYECTO_FLYVIBEMEX.md` en sus primeras
secciones: es una aplicación MVC completa, con capa de servicios real, transacciones, bitácora, roles, bot de
Telegram y 29 tablas. La calidad de fondo es **alta para un proyecto escolar y correcta para pre-producción**;
lo que queda no son errores de arquitectura sino **grasa acumulada**: carga de frontend desproporcionada,
tres consultas de catálogo que leen más filas de las necesarias, cinco métodos demasiado largos y un archivo
de 84 MB en la carpeta de trabajo.

### 0.2 Análisis Macro — la arquitectura está bien separada

```
Petición → Controllers/ (delgados: validan + llaman servicio + devuelven vista)
              ↓
           Services/  (47 archivos: TODA la lógica de negocio y acceso a datos)
              ↓
           Data/FlyVibeMexDbContext.cs (637 líneas, 29 entidades, 38 FK, sin navegación)
              ↓
           SQL Server  ← sql/01_crear_base_datos.sql es la fuente de verdad (database-first)

Bot/ (20 archivos, long polling) ──reutiliza──> los MISMOS Services/
```

Lo que está **bien resuelto** y este plan **no va a tocar**:

| Aspecto | Evidencia |
|---|---|
| Controladores delgados, lógica en servicios | Los 18 controladores delegan; no quedan `HttpContext.RequestServices` (solo dentro de `ServicioDiferido<T>`) |
| Inyección por constructor + `Lazy<T>` para "modo sin BD" | `Program.cs`, `Services/ServicioDiferido.cs` |
| Cero SQL concatenado | Todo es LINQ; el único SQL crudo son los `pa_*` de `sql/02`, que la app no usa |
| `async/await` completo | Auditoría de `Services/`, `Controllers/`, `Data/`: **no hay una sola consulta EF síncrona**; todas llevan `CancellationToken` |
| `AsNoTracking()` en lecturas | 323 usos frente a 186 materializaciones: la proporción indica que las lecturas ya están cubiertas |
| Transacciones con `CreateExecutionStrategy` | Compra, cancelación, altas del gestor, panel SA |
| Concurrencia por `UPDATE` condicional | Cupo, "Tomar ticket", cambio de rol: nunca lectura-modificación-escritura |
| Seguridad | `[Authorize]` por rol, antiforgery en todos los POST, PBKDF2, tokenización de tarjetas (sin PAN ni CVV), validación de imágenes por *magic bytes*, secretos fuera del repo |
| Comentarios | **895 bloques `/// <summary>`**; solo **1** archivo de los 134 `.cs` propios no tiene ninguno |
| Caché ya aplicada donde más duele | `ContenidoSitioService` (10 min, se invalida al guardar) |

**Conclusión macro:** no hace falta ninguna refactorización estructural. El riesgo de romper algo es mucho mayor
que el beneficio. El plan se queda en el nivel micro.

### 0.3 Análisis Micro — los 10 hallazgos que sí valen la pena

Ordenados por **impacto ÷ riesgo** (el criterio de un Release Candidate):

| # | Hallazgo | Dónde | Impacto | Riesgo de arreglarlo |
|---|---|---|---|---|
| **H1** | **La pila de jQuery se carga en TODAS las páginas**, incluidas las ~40 % que no tienen ningún formulario (Inicio, Políticas, Confirmación, Panel › Índice, Historial de tickets, Reembolsos, SA › Índice, páginas de error) | `Views/Shared/_Layout.cshtml:115`, `Views/Admin/_AdminLayout.cshtml`, vía `_ValidationScriptsPartial.cshtml` | **Alto** (≈131 KB de JS que se descarga, se parsea y se compila sin usarse) | Bajo con opt-out |
| **H2** | **Imágenes subidas sin redimensionar y sin caché.** `AlmacenImagenes` guarda el archivo tal cual (hasta 2 MB) y las tarjetas lo pintan a ~300 px. La imagen de prueba actual pesa **1.12 MB**. `/uploads` se sirve **sin `Cache-Control`** | `Services/AlmacenImagenes.cs:56-78`, `Program.cs` (bloque `UseStaticFiles`) | **Alto** (decodificar un JPG grande es lo más caro que hace un equipo antiguo; y se vuelve a pedir en cada visita) | Bajo (caché) / Medio (redimensionar) |
| **H3** | **`DisponibilidadAsync(null, …)` lee la disponibilidad de TODOS los servicios** de la BD en la ventana de fechas, aunque la sección ya filtró los candidatos | `Services/CatalogoService.cs:53` (llamada) y `:515` (método) | **Alto** (es la tabla más grande: 29 días × todos los servicios, en cada carga de sección) | **Muy bajo** |
| **H4** | **Las tablas de subtipo se leen completas y con todas sus columnas.** `_db.Hospedajes.ToDictionaryAsync()` sin `Where` ni `Select`. En la sección Paquetes son **4 lecturas completas** (paquetes + `paquete_servicio` + todos los hospedajes + todos los vuelos) | `Services/CatalogoService.cs:409, 424, 437, 452-462` | **Medio-alto** | **Muy bajo** |
| **H5** | **Búsqueda O(n×m) en memoria:** `componentes.Where(x => x.id_servicio == c.id)` dentro del `foreach` de candidatos recorre la lista completa del puente por cada paquete | `Services/CatalogoService.cs:468` | Medio (crece al cuadrado con el catálogo) | **Muy bajo** |
| **H6** | **El HTML no se comprime.** `MapStaticAssets()` sirve CSS/JS precomprimidos, pero las respuestas dinámicas (20–40 KB de HTML por página) viajan en claro: no hay `AddResponseCompression` | `Program.cs` | **Medio-alto** en red lenta (≈70–75 % menos bytes) | **Muy bajo** |
| **H7** | **Cinco métodos demasiado largos** para leerse de un tirón: `ReservacionService.PagarAsync` **368 líneas**, `GestorCatalogoService.CrearServicioAsync` **282**, `CancelacionesService.EjecutarEnTransaccionAsync` **241**, `CatalogoService.UbicacionesAsync`→`CandidatosAsync` **167**, `GestorCatalogoService.DisponibilidadAsync` **159** | ver tabla del Eje 4 | Medio (mantenibilidad) | **Medio-alto** → se hace por extracción pura, sin tocar lógica |
| **H8** | **Dos orígenes de CDN** (`cdn.jsdelivr.net` + `code.jquery.com`) = 2 DNS + 2 TLS extra; y **2 de los 3 scripts de validación no tienen `integrity`** (SRI), con el `TODO` ya anotado | `_Layout.cshtml:116`, `_ValidationScriptsPartial.cshtml:16-17` | Medio (latencia + integridad) | Bajo |
| **H9** | **Dos consultas al disco por página** para saber si existe `logo.png` (`_Marca` + `_Favicon`), decisión consciente y documentada… pero **el archivo no existe todavía**, así que hoy son 2 fallos de `stat` por página y el sitio se ve sin logo | `Services/LogoSitio.cs:18`, `Views/Shared/_Marca.cshtml`, `_Favicon.cshtml` | Bajo-medio | **Muy bajo** |
| **H10** | **Grasa en la carpeta de trabajo:** `publish/` ocupa **84 MB** y contiene copias de archivos que ya se dieron de baja (`script_D.js`, `app.js`, `site.js`, `originalstyles.css`). Está en `.gitignore:80`, así que es basura local, no del repositorio | `publish/` | Bajo (confunde búsquedas y `grep`) | **Nulo** |

### 0.4 Lo que NO es un problema (para que nadie "lo arregle")

- **`ToList()` en `Services/`**: los ~25 casos que aparecen son LINQ‑to‑Objects **después** de materializar, no filtrado en servidor mal hecho. Correcto.
- **CSS/JS propios**: 479 líneas de JS y 26.6 KB de CSS en total. Es **muy poco**. No hay nada que minificar.
- **Transiciones CSS**: 13 de 16 ya usan `transform` / `box-shadow` / `background-color` (compuestas o baratas).
- **El botón "mantén presionado"** anima `transform: scaleX(var(--progreso))` en un `::after` con `requestAnimationFrame`: ya es la forma correcta.
- **`wwwroot/lib/`** ya no existe (se movió al respaldo en T8).
- **`bin/` y `obj/`** ya no se versionan.

---

## EJE 1 · Depuración de archivos redundantes o innecesarios

**Resultado de la búsqueda de huérfanos:** se verificaron los 105 `.cshtml`, los 6 CSS y los 4 JS de `wwwroot`,
y las 37 referencias a parciales. **No hay vistas, parciales, CSS ni JS huérfanos**: los 4 `_*Layout.cshtml`
que no aparecían en la primera pasada se referencian desde el `_ViewStart.cshtml` de su carpeta, y los
`_Form*.cshtml` por ruta completa. La limpieza de T7/T8 hizo bien su trabajo.

Lo que sí queda por borrar:

| # | Qué | Peso | Por qué se borra | ¿Afecta a Git? |
|---|---|---|---|---|
| **L1.1** | `publish/` (carpeta completa) | **84 MB** | Salida de un `dotnet publish` anterior. Contiene copias de archivos ya dados de baja (`script_D/H/P/T/V.js`, `app.js`, `site.js`, `originalstyles.css`, `site.css`), así que **contamina cualquier búsqueda** en el repositorio y hace creer que esos archivos siguen vivos. Ya está en `.gitignore:80`. Se regenera con un comando. | No (ignorada) |
| **L1.2** | `docs/documento explicativos.txt` | 1.2 KB | Obsoleto: describe una estructura (`pages/`, `assets/` en la raíz) que **ya no existe**. `docs/CONTEXTO…` incluso lo da por eliminado desde el 17/10, pero el archivo sigue ahí. | Sí (`git rm`) |
| **L1.3** | `docs/FlyVibeMex – Documento explicativo del proyecto.docx` | 10 KB | Respaldo histórico del 10/10/2026, superado por `docs/01`–`docs/07`. Un `.docx` en Git no se puede revisar en un PR. | Sí (`git rm`) — **ver pregunta P1** |
| **L1.4** | `docs/CAMBIOS_YAMIL 24-10-2026.md` | 3.1 KB | Bitácora de un día concreto; su contenido vigente ya está incorporado en `docs/07` y en `docs/CONTEXTO…`. | Sí (`git rm`) — **ver pregunta P1** |
| **L1.5** | `../_respaldo_claude/` | 3.4 MB | Respaldos de cada etapa (T4a…T8, `fix_*`, `sql_antes_unificar`). Está **fuera** del repositorio, como sibling de `FlyVibeMex/`. Es su red de seguridad. | No | 
| **L1.6** | `wwwroot/uploads/hospedajes/e5ec…jpg` (1.12 MB) | 1.1 MB | Solo si se decide regenerar la BD demo (el seed no la referencia; la subió alguien probando el panel). | No (ignorada) |

> **L1.5 no se borra sin su permiso** (pregunta P2). Es el único lugar donde viven las versiones anteriores
> de los archivos dados de baja en T6/T7/T8.

**Verificación del lote:** `git status` sigue limpio salvo los 3 borrados intencionales; `dotnet build` sin errores;
`grep -rn "script_D" .` ya no devuelve nada (prueba de que la contaminación de `publish/` desapareció).

---

## EJE 2 · Simplificación arquitectónica (Zero-Regression)

No hay sobreingeniería estructural que quitar: la cadena *Controlador → Servicio → DbContext* es la correcta y
el bot reutiliza los servicios sin duplicar lógica. Las simplificaciones son **locales** y todas verificables a ojo:

| # | Simplificación | Dónde | Qué cambia exactamente | Garantía de no regresión |
|---|---|---|---|---|
| **L2.1** | **Quitar la doble llamada al contenido del sitio** | `Views/Home/Index.cshtml:14`, `Views/Otros/Politicas/Index.cshtml:9` | Hoy el `_Layout` ya resolvió `ObtenerAsync()` y la vista lo vuelve a pedir. Se expone el objeto desde el layout vía `ViewData` y las vistas lo leen. | Mismo objeto, misma caché; es el mismo valor por construcción |
| **L2.2** | **Un solo `*` reset** | `wwwroot/assets/css/{style,style_D,style_s,busqueda}.css` | El bloque `* { … }` (144 caracteres) está **repetido idéntico en 4 hojas**. En una página de sección se cargan 2 de ellas, así que el navegador aplica el mismo reset dos veces. Se deja solo en `style.css` (la global, que siempre se carga). | Reglas idénticas: el resultado computado no cambia |
| **L2.3** | **Quitar los 5 bloques duplicados entre `busqueda.css` y `style_D.css`** | `#mapas_preferidos`, `.mi-tarjeta`, `.mi-tarjeta .card-title`, `.mi-tarjeta .card-text`, `.btn-card-action:hover` | Son **byte a byte iguales**. `style_D.css` solo lo usa `Home/Index`; `busqueda.css`, las 5 secciones. Se mueven a `style.css` o se deja que `Home/Index` cargue también `busqueda.css`. | Mismas declaraciones; se comprueba con el inspector (estilos computados antes/después) |
| **L2.4** | **`transition: all` → lista explícita** | `style.css:100`, `style_D.css:76`, `style_D.css:92` | `all` obliga al navegador a vigilar **todas** las propiedades animables del elemento. Se sustituye por las 2–3 que de verdad cambian en el `:hover` correspondiente. | Visualmente idéntico; se revisa el hover de esos 3 elementos |
| **L2.5** | **Caché del `stat` del logo** | `Services/LogoSitio.cs:18` | `Existe()` consulta el disco en cada render de `_Marca` y `_Favicon` (2 veces por página). Se envuelve en `IMemoryCache` con **30 segundos** de vigencia: se conserva la propiedad que el equipo quería a propósito ("copias el logo y aparece sin reiniciar"), pero se dejan de hacer ~2 `stat` por petición. | La propiedad buscada se mantiene (30 s de espera como máximo); documentado en el propio comentario |

> **Lo que explícitamente NO se va a hacer:** no se agregan propiedades de navegación a EF (el equipo decidió
> FK sin navegación en T2.2), no se pasa a migraciones, no se reorganizan carpetas, no se renombra nada público,
> no se toca el `DbContext` ni el esquema. A 4 días de la entrega, eso no es simplificar: es arriesgar.

---

## EJE 3 · Optimización extrema para hardware antiguo / bajos recursos

### 3.A Frontend

**Línea base medida** (una página de sección del sitio público, p. ej. `/Home/Hoteles`):

| Recurso | Origen | Peso sin comprimir | ¿Se usa en esa página? |
|---|---|---|---|
| `bootstrap.min.css` 5.3.8 | cdn.jsdelivr.net | ≈232 KB | Sí |
| `style.css` + `busqueda.css` | propio | 12.3 KB | Sí |
| `bootstrap.bundle.min.js` | cdn.jsdelivr.net | ≈80 KB | Sí (colapsable, modales) |
| `jquery-3.7.1.min.js` | **code.jquery.com** | ≈87 KB | Solo por la validación |
| `jquery.validate.min.js` | cdn.jsdelivr.net | ≈25 KB | Solo si hay formulario |
| `jquery.validate.unobtrusive.min.js` | cdn.jsdelivr.net | ≈19 KB | Solo si hay formulario |
| `script_validacion.js` | propio | 8 KB | Solo si hay formulario |
| **Total** | **2 orígenes externos** | **≈463 KB** (≈**211 KB solo de JS**) | |

En un equipo antiguo el cuello de botella no son los bytes: es el **tiempo de parseo y compilación de JS en el
hilo principal** y la **decodificación de imágenes**. Esos son los dos objetivos.

| # | Acción | Efecto esperado | Riesgo |
|---|---|---|---|
| **L3.1** | **Cargar la validación solo donde hay formularios.** Se invierte con un **opt-out**: el layout sigue incluyéndola por defecto (así ninguna pantalla pierde validación por olvido) y las páginas sin formulario declaran `ViewData["SinValidacion"] = true`. Páginas detectadas sin ningún `<form>`: `Home/Index`, `Otros/Politicas`, `Reservas/Confirmacion`, `Crearusuario/Creado`, `Estado/*` (4), `Admin/Admin`, `Admin/GestorVentas/Index`, `Admin/Soporte/{Index,Historial,SeguirAtendiendo,NuevoTicket,Ticket}`, `Admin/Reservaciones/Reembolsos`, `Admin/SA/{Index,ContrasenaTemporal}` | **−131 KB y ≈−150 ms de hilo principal** en esas páginas; el Inicio (la primera impresión) es una de ellas | **Bajo**: el opt-out nunca quita validación por accidente, y el servidor valida siempre |
| **L3.2** | **Un solo origen de CDN + SRI completo.** jQuery pasa de `code.jquery.com` a `cdn.jsdelivr.net` y se calculan los `integrity` que faltan (el `TODO` de `_ValidationScriptsPartial.cshtml`). Yo **no puedo** calcularlos (red bloqueada): se incluye el comando de PowerShell exacto para que ustedes los peguen | −1 DNS y −1 TLS (≈100–300 ms en red lenta); cierra el hueco de integridad | Bajo |
| **L3.3** | **`defer` en los `<script>`** del final del `<body>` | El parseo del HTML no se detiene mientras se descarga el JS | Bajo — hay que comprobar que `script_validacion.js` siga ejecutándose después de jQuery (`defer` conserva el orden) |
| **L3.4** | **Caché inmutable para `/uploads`.** Los nombres ya son GUID (`e5ec2def…jpg`), es decir **inmutables por construcción**: nunca se reescribe un archivo con el mismo nombre. Se agrega `Cache-Control: public, max-age=31536000, immutable` en el `OnPrepareResponse` que ya existe en `Program.cs` | La segunda visita y toda la navegación interna **no vuelven a pedir ninguna imagen** (hoy: una petición por imagen por página) | **Muy bajo** |
| **L3.5** | **`loading="lazy"` + `decoding="async"` + `width`/`height` en los 9 `<img>`** del sitio. Hoy solo 2 tienen `lazy` (`_ResultadosCatalogo`, `_TarjetaReservacion`) y ninguno de los 9 declara dimensiones intrínsecas | Menos decodificación simultánea y **se elimina el salto de maquetación** (CLS) al cargar las tarjetas | Muy bajo |
| **L3.6** | **Redimensionar al subir** (decidido: se hace en el servidor, ver *Decisiones tomadas*). Hoy un JPG de 4000 px se guarda tal cual y se pinta a 300 px: el navegador decodifica la imagen completa en RAM. Se agrega `SixLabors.ImageSharp` **fijado en la rama 3.x** y `AlmacenImagenes.GuardarAsync` redimensiona a un ancho máximo antes de escribir el archivo | Cada imagen baja de ~1.1 MB a ~120 KB y, sobre todo, **baja drásticamente la RAM y el CPU de decodificación** en el navegador | **Medio** (paquete nuevo que no puedo compilar) → mitigado con la versión fijada y un lote propio (**L3b**) |

### 3.B Backend (.NET MVC)

| # | Acción | Dónde | Efecto esperado | Riesgo |
|---|---|---|---|---|
| **L3.7** | **Filtrar la disponibilidad por los candidatos ya filtrados.** `LlenarSeccionAsync` llama `DisponibilidadAsync(null, …)`, que trae la ventana de fechas de **todos** los servicios de la BD. Se le pasa la lista de ids de `filtrados` para que el SQL lleve `WHERE id_servicio IN (…)` | `CatalogoService.cs:53` y `:515` | La consulta más pesada del sitio público se reduce a lo que de verdad se pinta. Con una ciudad seleccionada, la reducción es de casi todo el catálogo a unas pocas filas | **Muy bajo**: el diccionario resultante solo se consulta con `GetValueOrDefault(c.id)` para ids que están en `filtrados` |
| **L3.8** | **Proyectar y filtrar las tablas de subtipo.** `_db.Hospedajes.ToDictionaryAsync()` → `.Where(h => ids.Contains(h.id_servicio)).Select(h => new { … solo las 7 columnas que se usan … })`. Igual en vehículos, vuelos y paquetes | `CatalogoService.cs:409, 424, 437, 452` | Menos filas y menos columnas por la red (hoy viaja `imagen_ruta` y la política de todos los registros, aunque el servicio esté inactivo) | **Muy bajo**: las columnas que se copian al `Candidato` están a la vista en el mismo bloque |
| **L3.9** | **Una sola lectura de `paquete_servicio` + `ToLookup`.** La sección Paquetes hace 4 lecturas completas y luego un `Where` lineal por cada paquete dentro del `foreach` | `CatalogoService.cs:452-478` | Pasa de O(n×m) a O(n+m) y de 4 lecturas completas a 4 filtradas | **Muy bajo** |
| **L3.10** | **Caché de ubicaciones.** `UbicacionesAsync` lee la tabla completa en **cada** carga de sección y de detalle. Es un catálogo que solo cambia cuando el SA lo edita. Se cachea con `IMemoryCache` **siguiendo el patrón que ya usa `ContenidoSitioService`** (10 min + invalidación explícita al guardar en `SACatalogosService`) | `CatalogoService.cs:363`, `SACatalogosService.cs` | −1 consulta por página del catálogo | **Bajo**: hay que acordarse de invalidar al editar una ubicación; por eso se copia el patrón ya probado |
| **L3.11** | **Compresión de las respuestas HTML.** `AddResponseCompression` con Brotli + Gzip, **solo para `text/html`, `application/json` y `text/css`** y **sin** tocar los estáticos (de eso ya se encarga `MapStaticAssets`) | `Program.cs` | 20–40 KB de HTML por página → 5–10 KB. En red lenta es el cambio que más se nota | **Muy bajo** (se habilita solo fuera de `Development` para no estorbar al depurar) |
| **L3.12** | **`CancellationToken` ya está completo** y las consultas ya son `async`: **nada que hacer**. Se deja constancia para que no se "optimice" dos veces | — | — | — |

> **Lo que deliberadamente NO se propone:** `AddOutputCache` sobre las páginas del catálogo. Cachear HTML que
> contiene **cupo y precios** es exactamente la clase de optimización que provoca una regresión funcional
> (mostrar como disponible algo que ya se agotó) el día de la demostración. Si lo quieren, va **después** de
> la entrega.

---

## EJE 4 · Reestructuración para legibilidad y mantenibilidad

Solo **extracción de métodos auxiliares**: mover bloques tal cual a métodos privados con nombre semántico,
sin cambiar una condición, un orden ni un valor. Es la refactorización más segura que existe y la única
aceptable sin compilador propio.

| # | Método | Líneas | Cómo se parte |
|---|---|---|---|
| **L4.1** | `ReservacionService.PagarAsync` (`:90`) | **368** | Extraer `ValidarTarjetaElegidaAsync`, `ResolverMetodoPagoAsync`, `GuardarTarjetaNuevaAsync`, `DescontarCupoAsync`, `CrearReservacionConDetalleAsync`, `RegistrarPagoAprobadoAsync`. El cuerpo de `PagarAsync` queda como el **guion legible de la transacción**, que es justo lo que hay que poder auditar de un pago |
| **L4.2** | `GestorCatalogoService.CrearServicioAsync` (`:298`) | **282** | Extraer un `InsertarSubtipoAsync` por tipo (`Vuelo`/`Hospedaje`/`Vehiculo`/`Evento`) + `GenerarSalidasRepetidas` (la lógica de "repetir vuelo" hasta 120 salidas) + `InsertarDisponibilidadInicialAsync` |
| **L4.3** | `CancelacionesService.EjecutarEnTransaccionAsync` (`:152`) | **241** | Extraer `CambiarEstadoAReservacionCanceladaAsync`, `DevolverCupoAsync`, `CrearReembolsoSolicitadoAsync`, `ProcesarReembolsoInmediatoAsync` |
| **L4.4** | `CatalogoService.CandidatosAsync` (`:377`) | **167** | El `switch` de 5 ramas pasa a 5 métodos `CompletarHospedajes/Vehiculos/Vuelos/Paquetes/Eventos`. Esto se hace **junto con L3.8/L3.9** (toca las mismas líneas: un solo cambio, una sola revisión) |
| **L4.5** | `GestorCatalogoService.DisponibilidadAsync` (`:622`) | **159** | Separar "leer el rango actual" de "aplicar el ajuste" |
| **L4.6** | `Bot/ManejadorActualizaciones.cs` (803 líneas, 117 ramas) y `GestorCatalogoService.cs` (969 líneas, 112 ramas) | — | **No se parten.** Ya están divididos en `partial` por tema (`.Busqueda`, `.Catalogo`, `.Cuenta`, `.Soporte`) y son enrutadores: un `switch` largo y plano es más legible que diez métodos saltando entre archivos. Se queda como está, a propósito |

**Verificación de cada extracción:** `dotnet build` + ejecutar el guion de la Tarea 2 para ese flujo concreto.
Un método extraído que compila y cuyo flujo sigue pasando la prueba manual **es** la garantía de no regresión.

---

## EJE 5 · Estandarización de documentación en código

La base es buena (**895 `/// <summary>`**). Lo que falta es **cobertura uniforme** y, sobre todo, explicar el
**"por qué"** donde hoy solo está el "qué".

| # | Acción | Detalle |
|---|---|---|
| **L5.1** | Documentar el único archivo sin XML | `Models/historial_estado_reservacion.cs` es el único de los 134 `.cs` propios sin un solo `///`. Se le pone el `<summary>` de la entidad y la referencia a la regla **R6** (toda transición escribe su historial) |
| **L5.2** | **`<summary>` en los métodos públicos que aún no lo tienen** | ~1 418 miembros públicos frente a 895 bloques: se recorren interfaz por interfaz (`I*Service`) y se documenta la que falte, **en la interfaz** (que es donde lo lee quien consume el servicio), no en la implementación |
| **L5.3** | **Anclar cada regla de negocio a su regla numerada** | Donde el código implementa R1…R13 se agrega un comentario corto que la cite: `// R4: snapshot — cambiar el catálogo nunca altera una compra pasada`. Los puntos: snapshot en `ReservacionService`, descuento de cupo (R3), historiales (R6), reembolso desde el pago (R8), tokenización (R5). Así quien toque ese código **sabe que no es un detalle, es una regla** |
| **L5.4** | **Documentar las decisiones de concurrencia** | Cada `ExecuteUpdateAsync` condicional (cupo, "Tomar ticket", cambio de rol) lleva una línea explicando *por qué* es un `UPDATE` condicional y no leer-modificar-escribir: **dos usuarios a la vez**. Es el comentario que evita que alguien lo "simplifique" el año que viene |
| **L5.5** | **Documentar las optimizaciones de este plan** | Cada cambio del Eje 3 se comenta en su sitio con la razón (`// Se filtra por los ids ya filtrados: antes se leía la disponibilidad de TODO el catálogo`). Sin esto, la optimización se revierte en el próximo refactor |
| **L5.6** | **Comentarios en JS y Razor donde hay truco** | `script_validacion.js` (los 2 parches de unobtrusive: `\p{L}` sin la bandera `u` y `[Range]` en casillas) ya está comentado y es ejemplar. Falta el mismo trato en `script_R.js` (clic sostenido: por qué `touch-action: none` y por qué se previene el `contextmenu`) |
| **L5.7** | **Corregir los comentarios que mienten** | Al terminar, revisar que ningún comentario describa el estado anterior (p. ej. los que hablaban de `cargarPagina`/`innerHTML` ya se actualizaron en T7; hay que hacer lo mismo con los que toque este plan) |

> **Regla de oro de este eje:** un comentario que repite el nombre del método **es ruido**. Solo se escribe
> comentario cuando explica una **decisión**, una **regla de negocio** o una **trampa**.

---

## Orden de ejecución por lotes

Cada lote es independiente, se revisa solo y **termina con una verificación concreta**. Si un lote falla, se
revierte ese lote y nada más.

| Lote | Contenido | Archivos tocados | Verificación |
|---|---|---|---|
| **L1 · Limpieza** | Eje 1 completo | 0 de código | `git status` limpio; `grep -rn "script_D"` sin resultados |
| **L2 · Backend sin riesgo** | L3.7, L3.8, L3.9, L3.10, L3.11 + L4.4 (mismas líneas) | `CatalogoService.cs`, `SACatalogosService.cs`, `Program.cs` | `dotnet build`; buscar en las 5 secciones y comparar **resultado por resultado** con el estado actual (precio, cupo, "Agotado", orden); `/Home/TestDb` en verde |
| **L3 · Frontend** | L3.1 a L3.5 + L2.2, L2.3, L2.4 | Los 2 layouts, `_ValidationScriptsPartial`, 4 CSS, los 9 `<img>` | `dotnet build`; **F12 → Red**: confirmar que el Inicio ya no pide jQuery; probar **un formulario de cada zona** (Crear cuenta, Buscar, Resumen de pago, Alta de proveedor, Nuevo ticket) y ver que la validación del navegador sigue viva; revisar a **375 px** |
| **L3b · Imágenes** | L3.6 (paquete `SixLabors.ImageSharp` 3.1.12 + redimensionado) | `FlyVibeMex.csproj`, `Services/AlmacenImagenes.cs` | `dotnet build` **en las 4 máquinas**; subir un JPG grande desde Alta de hospedaje y comprobar que el archivo guardado en `wwwroot/uploads` pesa ~120 KB y se ve bien; subir un PNG pequeño y comprobar que **no** se agranda ni se recorta |
| **L4 · Extracciones** | L4.1, L4.2, L4.3, L4.5 | 4 servicios | `dotnet build` **después de cada método** (no de los cuatro juntos) + el flujo correspondiente del guion de pruebas |
| **L5 · Comentarios** | Eje 5 completo | Transversal | `dotnet build` (los `///` no rompen nada, pero se compila igual) |
| **L6 · Cierre** | Actualizar `docs/CONTEXTO…` y `docs/07` con lo hecho; commit y PR | `docs/` | Revisión del PR con la plantilla de `.github/pull_request_template.md` |

**Antes de empezar L2**, ustedes corren `dotnet build` en el estado actual y me pasan el resultado: así sabemos
que cualquier error posterior es mío y no venía de antes.

---

## Riesgos y cómo se evita la regresión

| Riesgo | Mitigación |
|---|---|
| **No puedo compilar** (sin SDK de .NET, red bloqueada) | Lotes pequeños; `dotnet build` suyo al final de cada uno; en L4, build **por método extraído**. Reviso además el balance de llaves y paréntesis de cada archivo que edito antes de entregarlo |
| Una optimización de consulta cambia un resultado | L3.7/L3.8/L3.9 no alteran ningún filtro ni ningún orden: solo **dejan de traer filas que el código ya descartaba**. La verificación es comparar las 5 secciones resultado por resultado |
| Una página pierde la validación del navegador | Se usa **opt-out**, no opt-in: por omisión la validación sigue cargando. Solo se apaga en páginas donde `grep` confirmó que no hay ni un `<form>` |
| La caché de ubicaciones muestra datos viejos | Se copia el patrón ya probado de `ContenidoSitioService`: invalidación explícita al guardar, no solo expiración |
| Un método extraído cambia de comportamiento | Extracción pura: se mueven líneas, no se reescriben. Nada de "y ya que estoy, mejoro esto" |
| La caché inmutable de `/uploads` fija una imagen vieja | Los nombres son GUID: **jamás** se reescribe un archivo con el mismo nombre. Cambiar la imagen de un servicio genera un nombre nuevo |

---

## Decisiones tomadas (02/11/2026)

| # | Pregunta | **Decisión** | Consecuencia en el plan |
|---|---|---|---|
| **P1** | Documentos obsoletos de `docs/` | **Borrar los tres** | L1.2, L1.3 y L1.4 se ejecutan (`git rm`). Quedan en el historial de Git si alguien los necesita |
| **P2** | `_respaldo_claude/` | **Conservar** (no se preguntó: se asume la recomendación) | L1.5 no se ejecuta |
| **P3** | Imágenes | **Redimensionar en el servidor** | L3.6 se ejecuta con `SixLabors.ImageSharp`. **Se separa en su propio lote L3b** por ser el único cambio con dependencia nueva |
| **P4** | Ubicación de los otros dos entregables | `docs/` (recomendación) | `docs/GUIA_DE_TESTEO_QA.md` y `docs/PROPUESTA_REFINAMIENTO_UX.md` |
| **P5** | Logo | **Lo pone el equipo** en `wwwroot/assets/img/logo.png` | El código no cambia (ya soporta el logo opcional). **Pendiente de la entrega**: hoy el sitio se ve sin marca gráfica y sin favicon |
| **P6** | Ritmo | **Un lote a la vez**, con `dotnet build` del equipo entre cada uno | El orden de lotes es obligatorio, no sugerido |

### Nota de licenciamiento sobre ImageSharp (P3) — leer antes de instalar

**No instalen la versión más reciente.** Desde **ImageSharp 4.0.0**, un proyecto que dependa *directamente* del
paquete **exige una licencia de Six Labors en tiempo de compilación**: sin ella `dotnet build` **falla**. La
versión más reciente de la rama 4 (4.1.3, octubre 2026) rompería la compilación en las cuatro máquinas del
equipo.

**Lo que se instala:**

```bash
dotnet add package SixLabors.ImageSharp --version 3.1.12
```

- La rama **3.x** usa la *Six Labors Split License v1.0* **sin validación en tiempo de compilación**.
- Esa licencia concede los términos de **Apache 2.0** cuando el software que la consume tiene una licencia
  Open Source o Source Available. **FlyVibeMex es MIT** (archivo `LICENSE`), así que califica. También
  califica por el criterio de organización sin fines de lucro / menos de 1 M USD de ingresos.
- La referencia va **con versión exacta** y un comentario en `FlyVibeMex.csproj` explicando *por qué* está
  fijada, para que nadie la "actualice" a la 4.x y rompa la compilación del equipo.
- Después de instalar: `dotnet list package --vulnerable` (igual que se hizo con `System.Security.Cryptography.Pkcs`).

**Diseño del cambio en `AlmacenImagenes.GuardarAsync`** (L3b):

1. Se conserva **intacta** la validación actual por *magic bytes* y el límite de 2 MB.
2. Si la imagen es más ancha que **1600 px**, se reduce a 1600 px manteniendo proporción (`ResizeMode.Max`:
   nunca agranda una imagen pequeña ni recorta).
3. Se guarda como JPG de calidad 82 (o PNG si la original era PNG con transparencia).
4. **Si el redimensionado falla por cualquier razón, se guarda el archivo original tal cual.** La subida de una
   imagen nunca debe romperse por una optimización: es la garantía de no regresión de este lote.
5. El nombre sigue siendo un GUID, así que la caché inmutable de **L3.4** sigue siendo válida.

---

---

## Nota operativa · Finales de línea (CRLF) y por qué no ejecuto Git

**Descubierto el 02/11/2026 al ejecutar el lote L1.**

Los archivos del repositorio están en disco con finales de línea **CRLF** (Windows) y los blobs de Git guardan
**LF**. En las máquinas del equipo eso es invisible porque el `core.autocrlf = true` de la configuración global
de Windows convierte al leer. Pero cualquier herramienta que ejecute Git **fuera de Windows** (o un integrante
con `core.autocrlf` en otro valor) no hace esa conversión y Git marca **todos** los archivos de texto como
modificados.

Comprobación que lo demuestra:

```bash
git diff --stat                      # ~250 archivos "modificados"
git diff --ignore-cr-at-eol --stat   # solo los archivos realmente cambiados
```

**Consecuencias para este plan:**

1. **Yo no ejecuto ningún comando de Git que escriba** (`add`, `rm`, `commit`, `checkout`). Un `git add -A`
   lanzado desde fuera de Windows commitearía una renormalización de finales de línea de todo el repositorio y
   dejaría el PR imposible de revisar. Los archivos que haya que dar de baja los borro del disco y **ustedes
   registran la baja en Windows**.
2. **Para revisar el estado real del repositorio usen siempre** `git diff --ignore-cr-at-eol --stat`
   mientras no exista `.gitattributes`.

**Causa de fondo:** el repositorio **no tiene `.gitattributes`**. Lo estándar es:

```gitattributes
* text=auto
*.sql  text eol=crlf
*.cs   text diff=csharp
*.png  binary
*.jpg  binary
*.docx binary
```

**No se agrega ahora.** Introducir `.gitattributes` **renormaliza todo el repositorio** y produce justo el diff
masivo que queremos evitar a cuatro días de la entrega. Queda en el backlog post-entrega (anexo de la Tarea 3),
donde el procedimiento correcto es: crear el archivo, `git add --renormalize .` y **un commit propio** que no
mezcle ningún cambio funcional.

---

## Bitácora de ejecución

### Lote L1 · Limpieza — **COMPLETADO** (02/11/2026)

| Acción | Resultado |
|---|---|
| L1.1 · `publish/` | **Borrada. 84 MB liberados.** Se regenera con `dotnet publish -c Release -o publish` |
| L1.2 · `docs/documento explicativos.txt` | Borrado del disco (44 líneas). Pendiente: registrar la baja en Git |
| L1.3 · `docs/FlyVibeMex – Documento explicativo del proyecto.docx` | Borrado del disco (10 066 bytes). Pendiente: registrar la baja en Git |
| L1.4 · `docs/CAMBIOS_YAMIL 24-10-2026.md` | Borrado del disco (38 líneas). Pendiente: registrar la baja en Git |
| L1.5 · `_respaldo_claude/` | **Conservada** por decisión del equipo |
| Verificación | `grep -rln "script_D.js\|app.js\|site.js\|originalstyles"` fuera de `docs/` y `.git/` → **0 archivos**. La contaminación de `publish/` desapareció |
| Efecto en el código | **Ninguno.** L1 no toca un solo archivo de código |

**Lo que falta de L1, en Windows:**

```powershell
git status                                 # debe mostrar 3 borrados + docs/PLAN_... sin seguimiento
git add -A docs/
git commit -m "L1: limpieza de documentos obsoletos y plan de calidad y optimizacion"
```

### Siguiente paso · línea base de compilación

Antes del lote L2 hace falta el resultado de `dotnet build` **en el estado actual**, para que cualquier error
posterior sea atribuible al lote que lo introdujo y no a algo que ya venía:

```bash
dotnet build 2>&1 | Select-Object -Last 20     # PowerShell
```

---

### ⚠ Hueco en esta bitácora (04/11/2026)

**Faltan las entradas de todo el 03/11/2026.** Los lotes L2a, L2b, L3a, L3b, L4, L5-libs, L6, L6a, L6b y
L5 primera mitad **sí se ejecutaron y están en `main`** —el código se subió y se verificó—, pero sus
entradas de bitácora y la sección «Correcciones al propio plan» se escribían en este archivo sin llegar a
un commit, y se perdieron con la laptop de trabajo. El commit de las 19:35 del 03/11 incluyó este
documento en su estado del 02/11.

No se reconstruyen de memoria a propósito: una bitácora inventada es peor que una bitácora con un hueco
declarado. Lo que se puede afirmar con certeza de esos lotes está en el código y en sus comentarios, que
sí sobrevivieron. Si hace falta recuperar el detalle, está en el historial de la conversación de ese día.

### Lote L5 (2.ª mitad) · Columnas de control — **REAPLICADO** (04/11/2026)

**Historia de este lote, porque la lección vale más que el código.** Se aplicó el 03/11/2026, el build
falló por una causa ajena (ver el punto 4), se corrigió, y antes de que llegara el build siguiente **se
perdió la laptop de trabajo**. Como la costumbre del equipo es subir a GitHub solo después de verificar,
estos 32 cambios nunca se commitearon y desaparecieron. El último commit era de las 19:35 del 03/11 con
L5 primera mitad, así que la pérdida de código se limitó a lo no verificado. Se reaplicó el 04/11/2026
desde el registro de la conversación, en la rama `l5b-columnas-de-control`.

**Recomendación que sale de esto:** commitear al final de cada lote **aunque no esté verificado**, en una
rama de trabajo, y hacer merge a `main` solo después de verificar. Así «no verificado» deja de significar
«no existe en ningún lado». Y commitear el documento junto con el código, no por separado.

**32 cambios en 13 archivos.** Rompe la compatibilidad con cualquier base creada con el script anterior:
EF lista las columnas explícitamente en cada `SELECT`, así que en cuanto las propiedades existen, una BD
vieja falla en *toda* consulta a `usuarios` y `reservaciones`, no solo en los UPDATE. Hay que correr
`01 → 02 → 03`.

#### 1. `Ubicaciones.correo` eliminada

Propiedad fantasma: la tabla `dbo.ubicaciones` nunca tuvo esa columna, y el `e.Ignore(x => x.correo)` del
`DbContext` existía solo para que EF no la pidiera. Se verificó que **ningún** otro archivo la lee ni la
escribe. Se borró la propiedad, el `Ignore`, las dos NOTAS que la describían y el comentario que la
mencionaba dentro del `CREATE TABLE`.

#### 2. `usuarios.actualizado_en` y `reservaciones.actualizada_en`

**El proyecto ya tenía la convención y se siguió tal cual.** Seis tablas ya declaraban la columna como
`DATETIME NOT NULL DEFAULT(CURRENT_TIMESTAMP)`, y `AplicarFechasDeControlPorDefecto`
(`FlyVibeMexDbContext.cs`) ya aplica `HasDefaultValueSql("CURRENT_TIMESTAMP")` a toda propiedad `DateTime`
cuyo nombre esté en `ColumnasFechaControl` — lista que **ya incluía** ambos nombres. Por eso **no se tocó
el `DbContext`**: agregar la propiedad al modelo bastó para el INSERT.

La trampa: **`ExecuteUpdateAsync` no pasa por el rastreador de cambios de EF**, así que los UPDATE no se
estampan solos. Cada sitio necesita su `SetProperty` escrito a mano.

| Tabla | Sitio | Qué cambia | Estampa |
|---|---|---|---|
| usuarios | `AuthService` · registro de cliente | INSERT | sí (= `creado_en`) |
| usuarios | `SAUsuariosService` · alta interna | INSERT | sí (= `creado_en`) |
| usuarios | `MiCuentaService` · perfil propio | nombre, apellidos, teléfono | sí |
| usuarios | `MiCuentaService` · contraseña propia | `password_hash` | sí |
| usuarios | `SAUsuariosService` · el SA edita datos | nombre, apellidos, teléfono | sí |
| usuarios | `SAUsuariosService` · cambio de rol | `id_rol` | sí |
| usuarios | `SAUsuariosService` · baja lógica | `activo` | sí |
| usuarios | `SAUsuariosService` · restablecer contraseña | `password_hash` | sí |
| usuarios | `AuthService` · re-hash automático al iniciar sesión | `password_hash` | **sí** |
| reservaciones | `ReservacionService` · alta | INSERT (PENDIENTE) | sí (= `creada_en`) |
| reservaciones | `ReservacionService` · PENDIENTE → CONFIRMADA | estado | **no** |
| reservaciones | `CancelacionesService` · → CANCELADA | estado | sí |

**Las dos decisiones que no son obvias:**

- **El re-hash automático sí estampa.** Es un UPDATE que ocurre al iniciar sesión, cuando el hasher pide
  actualizar sus parámetros, así que en teoría un login puede mover la fecha sin que nadie haya cambiado
  nada. Se estampa igual porque la columna significa «cuándo cambió esta fila por última vez», y cualquier
  excepción necesitaría un comentario que la justificara — más difícil de defender que la regla simple.
- **El paso a CONFIRMADA no estampa.** Ocurre un segundo después del INSERT, en la misma transacción y la
  misma petición: es parte de crear la reserva, no una modificación posterior. Si estampara,
  `actualizada_en` jamás igualaría a `creada_en` y siempre marcaría +1 s, un dato sin significado. Mismo
  criterio y mismo comentario en `pa_crear_reservacion_pago`.

Detalle menor pero visible: en los dos INSERT de usuario se usa **una sola lectura del reloj** (`var
ahora`) para las dos fechas. Con dos `DateTime.Now` distintos, la cuenta nacería con `actualizado_en` unos
microsegundos después de `creado_en`, y la interfaz mostraría una cuenta recién creada como «modificada».

#### 3. Procedimientos almacenados y datos demo

Los 4 procedimientos que escriben en esas tablas (`pa_cambiar_rol_usuario`, `pa_cambiar_estado_usuario`,
`pa_cancelar_reservacion`, `pa_crear_reservacion_pago`) **no los llama la aplicación** —duplican en SQL la
lógica que vive en C#—, así que el riesgo de regresión es cero; se actualizaron porque son parte de la
entrega y un revisor notaría que el SQL ignora una columna que el C# sí mantiene.

En el seed, las 3 reservaciones demo tienen `creada_en` hacia atrás. Con solo el DEFAULT habrían quedado
con `actualizada_en` = el momento de instalar la BD, incluidas las dos que nunca se modificaron. Ahora:
las dos confirmadas con `actualizada_en = creada_en`, y la cancelada con `DATEADD(DAY, 1, @creada3)`, que
es exactamente cuando la cancela su propia bitácora de estados.

#### 4. La exclusión que costó 77 errores

`FlyVibeMex.csproj` ahora excluye `_respaldo_claude/**` de la compilación, con el mismo mecanismo que ya
usaba para `wwwroot/uploads/**`. Sin ella, el SDK compila las copias de seguridad que se hacen antes de
refactorizar y **cada clase respaldada queda definida dos veces**: 77 errores `CS0111` («ya define un
miembro denominado…») que no se parecen en nada a su causa. También se agregó `_respaldo_claude/` al
`.gitignore`: son copias del código fuente y no deben versionarse.

> Nota sobre los respaldos: `_respaldo_claude/` **no** es una red de seguridad confiable. Se ha borrado
> solo entre sesiones más de una vez, igual que le pasó a `herramientas/`. Lo que de verdad protege son
> los commits.

#### Cómo se comprobó, sin compilador

1. **Arnés con verificación previa**: las 32 sustituciones se validaron todas (`count == 1` en cada texto
   buscado) **antes de escribir un solo archivo**; si una hubiera fallado, no se habría tocado nada.
2. **Riesgo de CS0128/CS0136** (declarar `ahora` donde ya existe uno en un ámbito envolvente): se listó
   cada declaración con su método contenedor. Las 14 están en métodos distintos; en `CrearInternoAsync` la
   variable se llama `ahoraAlta` justamente para no chocar, y en `ReservacionService` y
   `CancelacionesService` se reutilizó el `ahora` que ya existía en el método en lugar de declarar otro.
3. **Conteo final**: 7 `SetProperty(u => u.actualizado_en)` sobre `_db.Usuarios` (un octavo resultado del
   grep es un UPDATE preexistente de `_db.Ubicaciones` que también usa `u` como parámetro), 2 INSERT de
   usuario con la fecha, 1 `SetProperty(r => r.actualizada_en)` sobre `_db.Reservaciones`.
4. **Paréntesis balanceados** en los tres `CREATE TABLE` modificados y **csproj válido como XML**.
5. **Revisión de cada línea eliminada que no fuera comentario**: las 24 son exactamente las sentencias que
   se reemplazaron por su versión extendida.

**Pendiente de verificación:** recrear la BD (`01 → 02 → 03`) y `dotnet build`. No hay SDK de .NET
disponible del lado de Claude, así que la compilación la hace el equipo.

## Anexo · Comandos de referencia

```bash
# Borrar la salida de publicación local (84 MB) — se regenera cuando haga falta
rmdir /s /q publish            # cmd
Remove-Item -Recurse -Force publish   # PowerShell

# Quitar del repositorio el documento obsoleto (L1.2)
git rm "docs/documento explicativos.txt"

# Compilar y publicar
dotnet build
dotnet publish -c Release -o publish

# Hashes SRI que faltan en _ValidationScriptsPartial.cshtml (L3.2) — PowerShell
$urls = @(
  "https://cdn.jsdelivr.net/npm/jquery-validation@1.21.0/dist/jquery.validate.min.js",
  "https://cdn.jsdelivr.net/npm/jquery-validation-unobtrusive@4.0.0/dist/jquery.validate.unobtrusive.min.js"
)
foreach ($u in $urls) {
  $b = (Invoke-WebRequest $u -UseBasicParsing).RawContentStream.ToArray()
  $h = [System.Security.Cryptography.SHA384]::Create().ComputeHash($b)
  "$u`n  integrity=`"sha384-$([Convert]::ToBase64String($h))`"`n"
}

# Comprobar el tamaño de las imágenes subidas (L3.6)
Get-ChildItem wwwroot\uploads -Recurse -File | Sort-Object Length -Descending |
  Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,2)}} -First 10
```
