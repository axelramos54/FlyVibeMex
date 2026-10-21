# CONTEXTO_PROYECTO_FLYVIBEMEX.md
> **Documento Maestro de Contexto (Single Source of Truth)** del proyecto integrador **FlyVibe Mex**.
> Pensado para que cualquier desarrollador o asistente de IA (Claude, Gemini) conozca la estructura exacta del proyecto sin volver a escanear el repositorio.

| Campo | Valor |
|---|---|
| Fecha de la auditoría | 17/10/2026 |
| Fuentes auditadas | `AVANCE2_EQUIPO1.pdf` (39 págs., portada "Avance #1", fecha 24/08/2026) + carpeta local `Proyecto/FlyVibeMex/` |
| Repositorio | `https://github.com/axelramos54/FlyVibeMex.git` |
| Rama local auditada | `main` — último commit `414c77e` ("Merge pull request #7 from axelramos54/Aportación-de-Ruth", 17/10/2026) |
| Ramas remotas relevantes | `origin/sqlyamil` (1 commit adelante: `3abf58b`, **no fusionado**), `origin/testingyamilmvc` (estructura previa a MVC, obsoleta) |
| Equipo | Yamil Alexei Courtois Paredes, Axel Ramos Monroy, Ruth Carolina Moscoso Villatoro, Alonso Gómez Burguete — UNACH, Lic. Sistemas Computacionales, 5° "K", materia *Administración de bases de datos* (Mtro. Rigoberto Pérez Ovando) |
| Documentación previa en el repo | `docs/01..04_*.md` + `README.md` (17/10/2026, migración y mejora de la documentación del equipo); `docs/FlyVibeMex – Documento explicativo del proyecto.docx` (10/10/2026, @Yamil 07) — respaldo histórico; `docs/documento explicativos.txt` — eliminado, era obsoleto (describe `pages/` y `assets/` en raíz) |

**Veredicto en una línea:** el proyecto es hoy un **prototipo de frontend servido por ASP.NET Core MVC (.NET 10)**, con 24 clases de modelo y un script T‑SQL de 24 tablas, pero **sin ninguna conexión a base de datos, sin autenticación, sin lógica de negocio y sin capa de datos**. Todo el contenido visible está escrito a mano en las vistas Razor.

---

## 0. Cambios recientes

### 20/10/2026 · Conexión EF Core y seguridad (rama `feature/efcore-conexion-segura`, base `96e6632` = PR #10)
- Antes de este cambio, `main` ya traía el PR #10 de Yamil: namespaces unificados en `FlyVibeMex.Models`, paneles `Admin`/`Gestor`/`SA`/`Soporte` (sin BD, `ModoSinBD = true`), `DefaultController.cs` y `Models/script base de datos.sql` **eliminados** (el script sigue disponible en el historial, commit `3ccf80d`).
- **EF Core 10** (`Microsoft.EntityFrameworkCore.SqlServer` + `Tools` 10.0.13) y `UserSecretsId` en `FlyVibeMex.csproj`.
- `appsettings.json`: `"DatabaseMode": "Test"` + `ConnectionStrings:FlyVibeMex_Test` / `FlyVibeMex_Real` solo con marcador `__SERVIDOR__`; las cadenas reales van en `dotnet user-secrets`.
- `Data/ConfiguracionBaseDatos.cs` (selector Test/Real) y `Data/FlyVibeMexDbContext.cs` (24 tablas, conversores `ulong↔bigint`/`ushort↔smallint`, erratas `tipo_proveerdor`/`fehca_inicio` mapeadas, NULL en textos opcionales).
- `Services/IAuthService.cs` + `AuthService.cs` (LINQ parametrizado, `PasswordHasher<Usuarios>`, DTO `Models/Auth/UsuarioSesion.cs` sin `password_hash`, mensajes genéricos) registrados en `Program.cs`.
- `GET /Home/TestDb` (solo Development) y `sql/00_db_prueba_conexion.sql` (BD `FlyVibeMex_Test` con `roles_usuarios` y `usuarios`).
- `.gitignore` ya ignora `bin/`, `obj/`, `.vs/`, `*.user`, `*.suo`, `appsettings.*.local.json`, `.env`; falta ejecutar `git rm -r --cached bin obj`.
- Guía para el equipo: `docs/05_CONEXION_EFCORE_Y_SEGURIDAD.md`.

---

## 1. Resumen Ejecutivo y Reglas de Negocio

### 1.1 Qué es FlyVibe Mex
Aplicación web centralizada para **cotizar, reservar y gestionar** vuelos, hospedaje, renta de vehículos y paquetes turísticos del mercado nacional mexicano. Resuelve la fragmentación de los sistemas de reserva (el viajero compara precios y disponibilidad en varias plataformas). Referentes de benchmarking: Despegar (promociones visibles), Expedia ("Mis viajes", orden), Booking (buscador prominente, filtros, políticas de cancelación visibles).

Decisiones de UX derivadas del benchmarking (PDF, pág. 7):
- Navegación por categorías claras: Paquetes, Vuelos, Hospedaje, Vehículos; sin saturar la cabecera.
- Buscador simple por tipo de servicio, destino y fechas.
- Promociones en una sección controlada, sin dominar la pantalla.
- "Mis viajes" con actuales, pasados, detalle, cancelación y soporte.
- Filtros relevantes y **políticas de cancelación visibles antes de pagar**.

### 1.2 Actores / roles
| Actor | Tipo | Cómo se modela | Qué hace |
|---|---|---|---|
| Cliente / Viajero | Directo | `usuarios` con rol en `roles_usuario` (p. ej. `CLIENTE`) | Busca, reserva, paga, consulta "Mis viajes", cancela, pide reembolso, abre solicitudes de soporte |
| Agente de la agencia | Directo | `usuarios` con rol `AGENTE` | Gestiona reservaciones y atiende `solicitudes_soporte` (`id_agente_asignado`); registra cambios de estado (`id_usuario_cambio`) |
| Administrador | Directo | `usuarios` con rol `ADMINISTRADOR` | Panel interno: catálogo de servicios/proveedores, paquetes, promociones |
| Proveedor (aerolínea, hotel, rentadora, otro) | Indirecto | Tabla `proveedores` (**no es usuario del sistema**) | Suministra los servicios del catálogo. FlyVibe Mex puede registrarse como proveedor interno para los paquetes |

### 1.3 Mapa de sitio (diseño, PDF pág. 8)
```
Home
├── Explorar y reservar
│   ├── Paquetes ─┐
│   ├── Vuelos ───┤
│   ├── Hospedaje ┼──► Resultados ► Detalle ► Resumen de reserva ► Pago ► Confirmación
│   ├── Vehículos ┤
│   └── Promociones┘
├── Mis viajes
│   ├── Actuales ──► Detalle de reservación ─┬─► Cancelar
│   └── Pasados                              ├─► Solicitar reembolso
│                                            └─► Reportar un problema
├── Soporte
│   └── Contactar a un asesor
├── Cuenta
│   ├── Iniciar sesión / Registro
│   ├── Perfil
│   └── Métodos de pago
└── Panel interno
    ├── Reservaciones
    ├── Servicios y proveedores
    ├── Paquetes y promociones
    └── Solicitudes de soporte
```
Pantallas prototipadas en el PDF (págs. 23‑37): Home, Explorar, Paquetes, Vuelos, Hospedaje, Vehículos, Promociones, Resultados, Detalle, Resumen reserva, Pago, Confirmación, Mis viajes, Actuales, Pasados, Detalles Reservación, Cancelar, Solicitar reembolso, Reportar problema, Soporte, Contactar asesor, Cuenta, Auth, Perfil, Métodos pago, Panel, Panel Reservaciones, Panel Servicios proveedores, Panel paquete promociones, Panel solicitudes soporte.

### 1.4 Flujos principales
1. **Reserva:** búsqueda (tipo + destino + fechas + personas) → resultados filtrados por `disponibilidad_servicio` → detalle (con política de cancelación) → resumen (aplica `promociones`) → crea `reservaciones` + `detalle_reservacion` (snapshot) → `pagos` → confirmación con `codigo_reserva`.
2. **Postventa:** "Mis viajes" (actuales vs pasados por `fecha_inicio`/`fecha_fin` del detalle) → cancelar (transición de estado + bitácora) → reembolso (`reembolsos` + `historial_estado_reembolso`) → reportar problema (`solicitudes_soporte` tipo `PROBLEMA`).
3. **Soporte:** contactar asesor = `solicitudes_soporte` tipo `ASESORIA`, asignable a un agente.
4. **Operación interna:** agentes/admins gestionan catálogo, inventario por fecha, promociones y tickets.

### 1.5 Reglas críticas del dominio (no negociables)
| # | Regla | Dónde vive |
|---|---|---|
| R1 | **Supertipo/subtipo de servicios.** Todo lo reservable es una fila en `servicios` (`tipo_servicio` ∈ `VUELO`, `HOSPEDAJE`, `VEHICULO`, `PAQUETE`). Cada subtipo (`vuelos`, `hospedajes`, `vehiculos`, `paquete`) usa **`id_servicio` como PK y FK** (relación 1:1). Los datos comunes (nombre, precio base, moneda, proveedor) solo existen en `servicios`. | `servicios` + subtablas |
| R2 | **Paquetes compuestos.** Un paquete es un servicio cuyo contenido son otros servicios vía `paquete_servicio (id_paquete, id_servicio_incluido, cantidad, orden)`. | `paquete`, `paquete_servicio` |
| R3 | **Inventario y precio por fecha.** La disponibilidad no se guarda en el servicio sino por día en `disponibilidad_servicio` (`cantidad_total`, `cantidad_disponible`, `precio_fecha`). Reservar descuenta `cantidad_disponible`; cancelar la devuelve. Nunca `cantidad_disponible < 0` ni `> cantidad_total`. Debe existir una sola fila por (`id_servicio`, `fecha`). | `disponibilidad_servicio` |
| R4 | **Snapshot histórico.** `detalle_reservacion` copia `precio_unitario`, `descuento_aplicado`, `subtotal`, `fecha_inicio`, `fecha_fin` y `descripcion_snapshot` al momento de reservar. Cambiar el catálogo **jamás** altera una compra pasada. Los totales de `reservaciones` (`subtotal`, `descuento_total`, `total`) también son históricos. | `detalle_reservacion`, `reservaciones` |
| R5 | **Tarjetas tokenizadas.** `tarjetas_usuario` guarda solo `token_referencia` (del gateway), `marca`, `ultimos4`, mes/año de expiración y `alias`. **Prohibido almacenar PAN completo o CVV** en BD, logs, modelos, ViewModels o JavaScript. | `tarjetas_usuario` |
| R6 | **Máquinas de estado con bitácora.** Cada cambio de estado de reservación, pago o reembolso se escribe en su historial (`historial_estado_reservacion`, `historial_estado_pago`, `historial_estado_reembolso`) además de actualizar el estado actual. `id_usuario_cambio = NULL` ⇒ cambio automático del sistema. Estados con `es_final = 1` no admiten más transiciones. | tablas `estados_*` + `historial_*` |
| R7 | **Pagos separados de bancos.** `metodos_pago` (forma de pago) ≠ `instituciones_financieras` (banco emisor). Un pago puede tener varios intentos; `referencia_externa` es la referencia del gateway. | `pagos`, `metodos_pago`, `instituciones_financieras` |
| R8 | **Reembolsos derivan de un pago**, nunca de la reservación directamente. Estados: `SOLICITADO → APROBADO / RECHAZADO → PROCESADO`. | `reembolsos` |
| R9 | **Soporte unificado.** "Contactar asesor" y "Reportar problema" son la misma tabla `solicitudes_soporte` (`tipo` ∈ `ASESORIA`, `PROBLEMA`; `estado` ∈ `ABIERTA`, `EN_PROCESO`, `CERRADA`), opcionalmente ligada a una reservación y a un agente. | `solicitudes_soporte` |
| R10 | **Contraseñas solo como hash** (`password_hash`), nunca en texto plano. | `usuarios` |
| R11 | **Borrado lógico** mediante columnas `activo` en catálogos, usuarios, proveedores, servicios, promociones, tarjetas. | varias |
| R12 | **Moneda ISO‑4217 de 3 letras**, por defecto `MXN`. | `servicios`, `reservaciones`, `pagos` |
| R13 | **Promociones con vigencia** (`fecha_inicio`, `fecha_fin`, `activo`), tipo `PORCENTAJE` o `MONTO`, `minimo_compra`, aplicables a servicios concretos vía `promocion_servicio`. | `promociones`, `promocion_servicio` |

### 1.6 Cronograma (PDF pág. 3) y situación al 17/10/2026
| # | Etapa | Fechas | Estado real observado en el código |
|---|---|---|---|
| 1 | Análisis | 31 ago – 4 sep | Hecho (PDF) |
| 2 | Planeación | 7 – 11 sep | Hecho (PDF) |
| 3 | Desarrollo de la BD | 14 – 25 sep | **Parcial**: script T‑SQL de 24/26 tablas con divergencias del diseño (sección 4) |
| 4 | Desarrollo del backend | 28 sep – 4 oct | **No iniciado**: controladores solo devuelven vistas; sin acceso a datos |
| 5 | Desarrollo del frontend | 5 – 16 oct | **Parcial**: 5 secciones estáticas de las ~30 pantallas del prototipo |
| 6 | Primeras pruebas | 19 – 22 oct | Pendiente |
| 7 | Implementación de bots | 23 – 26 oct | Pendiente |
| 8 | Implementación de IA | 27 – 30 oct | Pendiente |
| 9 | Pruebas finales | 31 oct – 1 nov | Pendiente |
| 10 | Entrega | 2 – 6 nov | Pendiente |

---

## 2. Mapa de Arquitectura Física (.NET MVC)

### 2.1 Stack tecnológico exacto
| Capa | Tecnología | Evidencia |
|---|---|---|
| Runtime / framework | **.NET 10** (`net10.0`), ASP.NET Core MVC, `Nullable=enable`, `ImplicitUsings=enable` | `FlyVibeMex.csproj` (`Microsoft.NET.Sdk.Web`) |
| Paquetes NuGet | `Microsoft.EntityFrameworkCore.SqlServer` y `Microsoft.EntityFrameworkCore.Tools` 10.0.13 (desde 20/10/2026). `PasswordHasher<T>` viene del framework compartido de ASP.NET Core | `FlyVibeMex.csproj` |
| Solución | **No existe archivo `.sln`/`.slnx`**; el proyecto se abre por `FlyVibeMex.csproj` | búsqueda recursiva |
| Motor de BD real | **Microsoft SQL Server (T‑SQL)**: `identity(1,1)`, `bit`, `varbinary(max)`, `getdate()` | `Models/script base de datos.sql` |
| Motor de BD del diseño | Sintaxis estilo **MySQL** (`BIGINT UNSIGNED`, `AUTO_INCREMENT`, `BOOL`, `TINYINT(1)`) | Diccionario de datos del PDF |
| Nombre de la BD | `FlyVideMex` (con "d" — errata frente a FlyVibeMex) | script SQL |
| Cadena de conexión | Marcadores en `appsettings.json` (`FlyVibeMex_Test`, `FlyVibeMex_Real`, `DatabaseMode`); la real de cada integrante en `dotnet user-secrets` | `appsettings.json`, `docs/05_CONEXION_EFCORE_Y_SEGURIDAD.md` |
| Frontend | Razor (`.cshtml`) + HTML/CSS/JS vanilla; **Bootstrap 5.3.8 por CDN jsDelivr** con SRI | `Views/Home/Index.cshtml` |
| Librerías locales | `wwwroot/lib/{bootstrap,jquery,jquery-validation,jquery-validation-unobtrusive}` contienen **solo archivos LICENSE** (sin `dist/`) | `wwwroot/lib` |
| Imágenes | URLs externas (Unsplash, barcelo.com, sanity.io, etc.) + `wwwroot/assets/img/imagen-prueba.jpg` | vistas |
| Puertos de desarrollo | `http://localhost:5106`, `https://localhost:7045` | `Properties/launchSettings.json` |
| Control de versiones | Git + GitHub; `bin/` y `obj/` ya están en `.gitignore`, pero siguen versionados (143 archivos) hasta ejecutar `git rm -r --cached bin obj` | `git ls-files` |
| Licencia | MIT (axelramos54, Yamil07Gamer, RuCaMoVil) | `LICENSE` |

### 2.2 Árbol de directorios comentado (excluye contenido de `bin/`, `obj/`, `.git/`)
```
Proyecto/                                   ← carpeta contenedora (sin .sln)
└── FlyVibeMex/                             ← RAÍZ DEL REPO Y DEL PROYECTO (.git aquí)
    ├── .gitignore                          ← ignora SO/IDE/.env/node_modules/dist/build; NO ignora bin/, obj/, .vs/
    ├── LICENSE                             ← MIT (modificado localmente sin commit)
    ├── README.md                           ← portada, requisitos, cómo ejecutar e índice de docs/
    ├── FlyVibeMex.csproj                   ← net10.0, Sdk.Web, sin paquetes NuGet
    ├── Program.cs                          ← host mínimo MVC (ver 2.3)
    ├── appsettings.json                    ← solo Logging + AllowedHosts="*"; sin ConnectionStrings
    ├── appsettings.Development.json        ← solo Logging
    ├── CONTEXTO_PROYECTO_FLYVIBEMEX.md     ← ESTE DOCUMENTO
    ├── Properties/
    │   └── launchSettings.json             ← perfiles http (5106) y https (7045), ASPNETCORE_ENVIRONMENT=Development
    ├── Controllers/
    │   ├── HomeController.cs               ← ÚNICO controlador funcional: 8 acciones GET que devuelven vistas + Error
    │   └── DefaultController.cs            ← clase `Default : Controller` duplicada; sus vistas no existen (ver 3.1)
    ├── Models/                             ← 24 clases de entidad (POCO, sin lógica) + ErrorViewModel + script SQL
    │   ├── ErrorViewModel.cs               ← plantilla ASP.NET (RequestId, ShowRequestId)
    │   ├── usuario.cs                      ← class Usuarios               (ns FlyVibeMex.Models)
    │   ├── roles_usuario.cs                ← class RolesUsuario           (ns fvm)
    │   ├── ubicaciones.cs                  ← class Ubicaciones            (ns fvm)
    │   ├── proveedores.cs                  ← class Proveedores            (ns fvm)
    │   ├── servicios.cs                    ← class Servicios              (ns fvm)
    │   ├── hospedajes.cs                   ← class Hospedajes             (ns fvm)
    │   ├── vehiculos.cs                    ← class Vehiculos              (ns fvm)
    │   ├── paquete.cs                      ← class Paquetes               (ns fvm)
    │   ├── paquete_servicio.cs             ← class PaqueteServicio        (ns fvm)
    │   ├── disponibilidad_servicio.cs      ← class DisponibilidadServicio (ns FlyVibeMex.Models)
    │   ├── promociones.cs                  ← class Promociones            (ns fvm)
    │   ├── promocion_servicio.cs           ← class PromocionServicio      (ns fvm)
    │   ├── estados_reservacion.cs          ← class EstadosReservacion     (ns fvm)
    │   ├── reservaciones.cs                ← class Reservaciones          (ns fvm)
    │   ├── detalle_reservacion.cs          ← class DetallesReservacion    (ns FlyVibeMex.Models)
    │   ├── metodos_pago.cs                 ← class MetodosPago            (ns fvm)
    │   ├── instituciones_financieras.cs    ← class InstitucionesFinancieras (ns fvm.models)
    │   ├── tarjetas_usuarios.cs            ← class TarjetasUsuarios       (ns fvm.models)
    │   ├── estados_pago.cs                 ← class EstadosPago            (ns fvm.models)
    │   ├── pagos.cs                        ← class Pagos                  (ns fvm.models)
    │   ├── historial_estado_pagos.cs       ← class HistorialEstadoPago    (ns fvm.models)
    │   ├── reembolsos.cs                   ← class Reembolsos             (ns fvm.models)
    │   ├── historial_estado_reembolso.cs   ← class HistorialEstadoReembolso (ns fvm.models)
    │   ├── solicitudes_soporte.cs          ← class SolicitudesSoporte     (ns fvm.models)
    │   └── script base de datos.sql        ← DDL T‑SQL de 24 tablas + seed roto (ver 4.4). En origin/sqlyamil se ELIMINA de aquí
    ├── Views/
    │   ├── _ViewImports.cshtml             ← @using FlyVibeMex, FlyVibeMex.Models; TagHelpers
    │   ├── _ViewStart.cshtml               ← Layout = "_Layout" (anulado con Layout=null en todas las vistas Home)
    │   ├── Shared/
    │   │   ├── _Layout.cshtml              ← plantilla por defecto de ASP.NET; ROTA (apunta a ~/lib/.../dist, ~/css/site.css, ~/js/site.js inexistentes)
    │   │   ├── _Layout.cshtml.css          ← CSS aislado de la plantilla
    │   │   ├── _ValidationScriptsPartial.cshtml ← jquery-validation (archivos inexistentes)
    │   │   └── Error.cshtml                ← vista de error (@model ErrorViewModel), usa _Layout
    │   └── Home/
    │       ├── Index.cshtml                ← SHELL de la SPA casera: navbar + <main id="contenido_main"> + script.js
    │       ├── Default.cshtml              ← parcial "Inicio": bienvenida, 4 tarjetas de categoría, promo, contacto
    │       ├── Viajes.cshtml               ← parcial buscador de vuelos (origen, destino, fechas, pasajeros) + 3 tarjetas fijas
    │       ├── Hoteles.cshtml              ← parcial buscador de hospedaje + 3 tarjetas fijas
    │       ├── Tours.cshtml                ← parcial buscador de tours + 3 tarjetas fijas (tipo NO existe en la BD)
    │       ├── Paquetes.cshtml             ← parcial buscador de paquetes + 3 tarjetas fijas
    │       ├── sesion.cshtml               ← parcial de login no funcional (form sin action/name; password type="text")
    │       ├── card.cshtml                 ← parcial de prueba: 12 tarjetas fijas (hoteles, paquetes, tours, vuelos)
    │       ├── Privacy.cshtml              ← plantilla; SIN acción en el controlador (404)
    │       └── exIndex.cshtml              ← "Welcome" de la plantilla; sin uso
    ├── wwwroot/
    │   ├── favicon.ico
    │   ├── assets/
    │   │   ├── css/
    │   │   │   ├── style.css               ← global (reset, navbar con degradado #3E8FD6→#7FC4F2→#F07B3F, header fijo)
    │   │   │   ├── style_D.css             ← sección Default (201 líneas)
    │   │   │   ├── style_H.css             ← sección Hoteles (197)
    │   │   │   ├── style_P.css             ← sección Paquetes (198; difiere 1 línea de style_H)
    │   │   │   ├── style_T.css             ← sección Tours (idéntico a style_H)
    │   │   │   ├── style_V.css             ← sección Viajes (idéntico a style_H)
    │   │   │   ├── style_s.css             ← sección sesión (87)
    │   │   │   ├── site.css                ← CSS de la plantilla (no referenciado por las vistas activas)
    │   │   │   └── originalstyles.css      ← vacío
    │   │   ├── js/
    │   │   │   ├── script.js               ← ÚNICO JS con lógica: cargarPagina(url) vía fetch + innerHTML
    │   │   │   ├── script_s.js             ← función noiniciar() (console.log)
    │   │   │   ├── script_D.js, script_H.js, script_P.js, script_T.js, script_V.js ← VACÍOS
    │   │   │   ├── app.js                  ← vacío
    │   │   │   └── site.js                 ← comentario de plantilla
    │   │   └── img/imagen-prueba.jpg       ← foto de perfil de prueba
    │   └── lib/{bootstrap,jquery,jquery-validation,jquery-validation-unobtrusive}/ ← SOLO LICENSE
    ├── docs/
    │   ├── 01_VISION_Y_REGLAS_DE_NEGOCIO.md         ← propósito, actores, mapa de sitio, flujos, reglas R1–R13
    │   ├── 02_GUIA_ARQUITECTURA_MVC_Y_FRONTEND.md   ← cómo funciona hoy (sustituye al .docx)
    │   ├── 03_CATALOGO_MODELOS_CSHARP.md            ← las 24 clases de Models/ por dominio
    │   ├── 04_ESTANDARES_Y_HOJA_DE_RUTA_APP.md      ← convenciones, Git, limpieza, pantallas faltantes
    │   └── FlyVibeMex – Documento explicativo del proyecto.docx ← respaldo histórico (10/10/2026)
    ├── bin/, obj/                          ← artefactos de compilación (versionados por error)
    └── (solo en origin/sqlyamil) sql/db.rar ← 24 archivos db/procedures/<tabla>.sql + db/script base de datos.sql (RAR, no auditado: ver 5.2)
```

### 2.3 Flujo de inicialización (`Program.cs`)
```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();          // único servicio registrado
var app = builder.Build();
if (!app.Environment.IsDevelopment()) {
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();                              // sin UseAuthentication ni esquemas: no hace nada útil
app.MapStaticAssets();                               // .NET 9+: sirve wwwroot desde el manifiesto de build
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}").WithStaticAssets();
app.Run();
```
Lo que **no** existe: `DbContext`/`AddDbContext`, repositorios/servicios en DI, `AddAuthentication` (cookies/Identity), `AddSession`, `AddAntiforgery` explícito, CORS, políticas de autorización por rol, `UseStatusCodePages`, áreas (`/Admin`), rutas de API, logging estructurado, health checks.

Consecuencia práctica de `MapStaticAssets`: un archivo nuevo en `wwwroot/` no se sirve hasta recompilar (`dotnet run` de nuevo + Ctrl+F5), tal como advierte el `.docx` del equipo.

### 2.4 Arquitectura de navegación en el navegador (SPA casera)
1. `GET /` → `HomeController.Index()` → `Views/Home/Index.cshtml` (documento HTML completo, `Layout = null`).
2. `DOMContentLoaded` → `cargarPagina('/Home/Default')`.
3. Cada enlace del navbar ejecuta `cargarPagina('/Home/<Accion>'); return false;`.
4. `cargarPagina` hace `fetch(url)` → recibe la vista parcial (todas con `Layout = null`) → `document.getElementById('contenido_main').innerHTML = html`.
5. Los `<link rel="stylesheet">` de cada parcial **sí** se aplican; los `<script>` de cada parcial **no se ejecutan** (limitación de `innerHTML`). Cada `<link>` se elimina junto con su HTML al cambiar de sección (no se acumulan), pero la hoja se vuelve a pedir en cada visita y sus reglas globales (`*`, `body`) afectan al shell mientras la sección está cargada.
6. No hay historial (`history.pushState`), por lo que el botón Atrás y los enlaces profundos no funcionan.

---

## 3. Catálogo de Componentes (Controladores, Modelos y Vistas)

### 3.1 Controladores y endpoints
Todas las acciones son **GET implícito** (sin atributos `[HttpGet]`/`[HttpPost]`), **sin parámetros**, sin modelo y sin acceso a datos.

**`HomeController`** — `Controllers/HomeController.cs`, namespace `FlyVibeMex.Controllers`, ruta base `/Home`.

| Acción | URL | Parámetros | Retorna | Uso real |
|---|---|---|---|---|
| `Index()` | `/`, `/Home`, `/Home/Index` | — | `View()` → `Views/Home/Index.cshtml` (página completa) | Shell de la SPA |
| `Default()` | `/Home/Default` | — | `View()` → `Default.cshtml` (parcial) | Inicio |
| `Hoteles()` | `/Home/Hoteles` | — | `View()` → `Hoteles.cshtml` (parcial) | Buscador de hospedaje (estático) |
| `Paquetes()` | `/Home/Paquetes` | — | `View()` → `Paquetes.cshtml` (parcial) | Buscador de paquetes (estático) |
| `Tours()` | `/Home/Tours` | — | `View()` → `Tours.cshtml` (parcial) | Buscador de tours (estático) |
| `Viajes()` | `/Home/Viajes` | — | `View()` → `Viajes.cshtml` (parcial) | Buscador de vuelos (estático) |
| `sesion()` | `/Home/sesion` | — | `View()` → `sesion.cshtml` (parcial) | Login (no funcional) |
| `card()` | `/Home/card` | — | `View()` → `card.cshtml` (parcial) | Prueba de tarjetas ("VER TARJETA test") |
| `Error()` | `/Home/Error` | — | `View(new ErrorViewModel{RequestId})` → `Shared/Error.cshtml`; `[ResponseCache(NoStore)]` | Manejador de excepciones en producción |
| *(falta)* `Privacy()` | `/Home/Privacy` | — | **404** — existe `Privacy.cshtml` y `_Layout` lo enlaza, pero no hay acción | — |

**`Default`** — `Controllers/DefaultController.cs`, clase `public class Default : Controller` (sin sufijo `Controller`).
- ASP.NET Core **sí la descubre** como controlador (hereda `[Controller]` de `ControllerBase`), con nombre `Default`; expone `/Default/Index`, `/Default/Hoteles`, `/Default/Paquetes`, `/Default/Tours`, `/Default/Viajes`, `/Default/sesion`, `/Default/card`.
- Como no existe `Views/Default/` ni esas vistas en `Views/Shared/`, **cada una de esas URLs lanza `InvalidOperationException` (500)**. *(Nota: el `.docx` del equipo dice que "ASP.NET no la reconoce"; eso es inexacto.)*
- Acción recomendada: eliminar el archivo.

**Endpoints JSON / API:** ninguno. **POST:** ninguno.

### 3.2 Estado de los Modelos/Entidades (C# vs. tablas)
Convención actual: **archivo** en snake_case, **clase** en PascalCase plural, **propiedades** en snake_case idénticas a la columna, PK llamada `id`. Sin data annotations, sin propiedades de navegación, sin validaciones.

Tipos usados: `bigint→ulong`, `smallint→ushort`, `tinyint→byte`, `bit→bool`, `decimal→decimal`, `datetime→DateTime`, `date→DateOnly`, `time→TimeOnly`, texto→`string = string.Empty`, `varbinary(max)→byte[]?`.

| # | Tabla diseño (PDF) | Tabla en script SQL | Clase C# (archivo, namespace) | Divergencias C# relevantes |
|---|---|---|---|---|
| 1 | `roles_usuario` | `roles_usuarios` | `RolesUsuario` (`roles_usuario.cs`, `fvm`) | — |
| 2 | `usuarios` | `usuarios` | `Usuarios` (`usuario.cs`, `FlyVibeMex.Models`) | falta `actualizado_en`; añade `imagen` |
| 3 | `ubicaciones` | `ubicaciones` | `Ubicaciones` (`ubicaciones.cs`, `fvm`) | faltan `colonia`, `calle`, `numero_exterior`, `numero_interior`, `referencias`; añade `correo` (eliminado en `sqlyamil`), `activo`; `creado`/`actualizado` son **`string`** (deberían ser `DateTime`) |
| 4 | `proveedores` | `proveedores` | `Proveedores` (`proveedores.cs`, `fvm`) | propiedad `tipo_proveedor` vs columna SQL **`tipo_proveerdor`** (errata) → no mapearía |
| 5/6 | `servicios` | `servicios` | `Servicios` (`servicios.cs`, `fvm`) | `descripcion` debería ser `string?` |
| — | **`vuelos`** | **NO EXISTE** | **NO EXISTE** | Ver 4.2 |
| 7 | `hospedajes` | `hospedajes` | `Hospedajes` (`hospedajes.cs`, `fvm`) | PK `id` propia (no `id_servicio`); `tipo_ubicacion` en vez de `tipo_habitacion`; añade `imagen` |
| 8 | `vehiculos` | `vehiculos` | `Vehiculos` (`vehiculos.cs`, `fvm`) | PK `id` propia (no `id_servicio`); falta `transmision` (aparece en el diagrama ER); añade `imagen` |
| 9 | `paquete` | `paquete` | `Paquetes` (`paquete.cs`, `fvm`) | `id` + `id_servicio` (PK compuesta en SQL); `max_personas` no nullable; añade `imagen` |
| 10 | `paquete_servicio` | `paquete_servicio` | `PaqueteServicio` (`paquete_servicio.cs`, `fvm`) | `id`, `id_servicio`, `id_servicio_incluido` (diseño: `id_paquete`, `id_servicio_incluido`) |
| 11 | `disponibilidad_servicio` | `disponibilidad_servicio` | `DisponibilidadServicio` (`disponibilidad_servicio.cs`, `FlyVibeMex.Models`) | — |
| 12 | `promociones` | `promociones` | `Promociones` (`promociones.cs`, `fvm`) | faltan `nombre`, `minimo_compra` (también en SQL) |
| 13 | `promocion_servicio` | `promocion_servicio` | `PromocionServicio` (`promocion_servicio.cs`, `fvm`) | — |
| 14 | `estados_reservacion` | `estados_reservacion` | `EstadosReservacion` (`estados_reservacion.cs`, `fvm`) | — |
| 15 | `reservaciones` | `reservaciones` | `Reservaciones` (`reservaciones.cs`, `fvm`) | falta `actualizada_en` (también en SQL) |
| 16 | `detalle_reservacion` | `detalle_reservacion` | `DetallesReservacion` (`detalle_reservacion.cs`, `FlyVibeMex.Models`) | errata **`fehca_inicio`**; `id_promocion`, `fecha_inicio`, `fecha_fin` deberían ser nullable |
| 17 | `historial_estado_reservacion` | **NO EXISTE** | **NO EXISTE** | bitácora de reservaciones ausente |
| 18 | `metodos_pago` | `metodos_pago` | `MetodosPago` (`metodos_pago.cs`, `fvm`) | — |
| 19 | `instituciones_financieras` | `instituciones_financieras` | `InstitucionesFinancieras` (`instituciones_financieras.cs`, `fvm.models`) | claves/teléfono deberían ser `string?` |
| 20 | `tarjetas_usuario` | `tarjetas_usuarios` | `TarjetasUsuarios` (`tarjetas_usuarios.cs`, `fvm.models`) | `id_institucion` y `alias` deberían ser nullable |
| 21 | `estados_pago` | `estados_pago` | `EstadosPago` (`estados_pago.cs`, `fvm.models`) | `es_final` es `byte` (en el resto de catálogos es `bool`) |
| 22 | `pagos` | `pagos` | `Pagos` (`pagos.cs`, `fvm.models`) | `id_tarjeta`, `referencia_externa`, `fecha_pago` deberían ser nullable |
| 23 | `historial_estado_pago` | `historial_estado_pago` | `HistorialEstadoPago` (`historial_estado_pagos.cs`, `fvm.models`) | — |
| 24 | `reembolsos` | `reembolsos` | `Reembolsos` (`reembolsos.cs`, `fvm.models`) | `fecha_resolucion`, `referencia_externa` deberían ser nullable |
| 25 | `historial_estado_reembolso` | `historial_estado_reembolso` | `HistorialEstadoReembolso` (`historial_estado_reembolso.cs`, `fvm.models`) | `id_usuario_cambio` debería ser `ulong?` |
| 26 | `solicitudes_soporte` | `solicitudes_soporte` | `SolicitudesSoporte` (`solicitudes_soporte.cs`, `fvm.models`) | `id_reservacion`, `id_agente_asignado` deberían ser nullable |

Resumen: **24 entidades** en C# y en SQL; **faltan 2** del diseño (`vuelos`, `historial_estado_reservacion`). **Tres namespaces distintos** (`FlyVibeMex.Models` ×4 incl. ErrorViewModel, `fvm` ×13, `fvm.models` ×8): `_ViewImports.cshtml` solo importa `FlyVibeMex.Models`, así que 21 clases no son visibles en vistas/controladores sin `using` adicional.

**ViewModels:** no existen (salvo `ErrorViewModel`). No hay DTOs.

### 3.3 Mapa de vistas (`.cshtml`) y frontend (`wwwroot`)
| Vista | Layout | CSS que enlaza | JS que enlaza | ¿El JS corre? | Contenido / formulario | Pantalla del prototipo |
|---|---|---|---|---|---|---|
| `Home/Index.cshtml` | `null` (HTML completo) | Bootstrap 5.3.8 CDN + `/assets/css/style.css` | Bootstrap bundle CDN + `/assets/js/script.js` | Sí | Navbar: Home, Viajes, Hoteles, Tours, Paquetes, "VER TARJETA test", "Iniciar secion" + foto; `<main id="contenido_main">`; footer vacío; marca "FlyVideMex" (errata) | Home (shell) |
| `Home/Default.cshtml` | `null` | `style_D.css` | `script_D.js` (vacío) | No | Bienvenida, botones "Explorar opciones"/"Mis Viajes" (`href="#"`), tarjetas Vuelos/Paquetes/Hospedaje/Tours, banner promo "12 MSI", bloque "Contáctanos" | Home / Explorar |
| `Home/Viajes.cshtml` | `null` | `style_V.css` | `script_V.js` (vacío) | No | Form GET `action="#"`: `origen`, `ubicacion` (destino), `fecha_inicio`, `fecha_final`, `num_viajeros`; 3 tarjetas fijas (CDMX→Cancún $8,499; GDL→Los Cabos $12,850; MTY→Oaxaca $4,920) | Vuelos + Resultados |
| `Home/Hoteles.cshtml` | `null` | `style_H.css` | `script_H.js` (vacío) | No | Form: `ubicacion`, `fecha_inicio`, `fecha_final`, `num_viajeros` (huéspedes); 3 hoteles fijos | Hospedaje + Resultados |
| `Home/Paquetes.cshtml` | `null` | `style_P.css` | `script_P.js` (vacío) | No | Form igual; 3 paquetes fijos | Paquetes + Resultados |
| `Home/Tours.cshtml` | `null` | `style_T.css` | `script_T.js` (vacío) | No | Form igual; 3 tours fijos | *(no está en el mapa de sitio)* |
| `Home/sesion.cshtml` | `null` | `style_s.css` | Bootstrap CDN (duplicado) + `script_s.js` | No | `<form id="formu_inicio_sesion" action="">` con 2 `input type="text"` sin `name`; sin botón | Cuenta › Auth |
| `Home/card.cshtml` | `null` | `style_V.css` | `script_H.js` | No | 12 tarjetas fijas; `id="tarjetas"` repetido 4 veces (HTML inválido) | Resultados (maqueta) |
| `Home/Privacy.cshtml` | `null` | — | — | — | Plantilla; inaccesible | — |
| `Home/exIndex.cshtml` | `_Layout` | — | — | — | "Welcome" plantilla | — |
| `Shared/_Layout.cshtml` | — | `~/lib/bootstrap/dist/css/bootstrap.min.css`, `~/css/site.css`, `~/FlyVibeMex.styles.css` | `~/lib/jquery/dist/jquery.min.js`, `~/lib/bootstrap/dist/js/bootstrap.bundle.min.js`, `~/js/site.js` | — | **Rutas rotas** (no existen) | — |
| `Shared/Error.cshtml` | `_Layout` | (hereda rotas) | — | — | Error estándar | — |

Convención de letras (del equipo): `D`=Default, `V`=Viajes (vuelos), `H`=Hospedajes, `T`=Tours, `P`=Paquetes, `s`=sesión. Cada sección tiene `style_<L>.css` y `script_<L>.js`. Regla del equipo: **`class` solo para Bootstrap; estilos propios por `id`** (`#mapas_preferidos` contenedor, `#buscador` columna de búsqueda, `#tarjetas` fila de resultados, `.mi-tarjeta` tarjeta).

### 3.4 ¿MockAPI / MemStore o BD real?
- **No existe** capa `MockAPI` ni `MemStore` en JavaScript, ni listas en memoria en C#.
- Los datos de catálogo están **escritos en el HTML** de cada vista Razor (nombres, precios, imágenes).
- El único `fetch` (`script.js`) pide **HTML de vistas parciales**, no datos.
- **Ninguna petición llega a una base de datos.**

---

## 4. Esquema de Base de Datos y Mapeo de Datos

### 4.1 Modelo relacional de diseño (fuente de verdad lógica: PDF + diagrama ER)
Notación: **PK**, FK→tabla.columna, (N) = nullable. En el script SQL real todas las PK se llaman `id` (no `id_<tabla>`); se indica la equivalencia.

**Usuarios**
| Tabla | PK | FK | Notas |
|---|---|---|---|
| `roles_usuario` | `id_rol` | — | `nombre` UNIQUE, `activo` |
| `usuarios` | `id_usuario` | `id_rol`→`roles_usuario` | `correo` UNIQUE, `telefono` UNIQUE, `password_hash`, `creado_en`, `actualizado_en` |
| `ubicaciones` | `id_ubicacion` | — | país (def. 'México'), estado, municipio_ciudad, CP, colonia, calle, nº ext (N), nº int (N), referencias |

**Catálogo / Servicios**
| Tabla | PK | FK | Notas |
|---|---|---|---|
| `proveedores` | `id_proveedor` | `id_ubicacion`→`ubicaciones` (N) | `tipo_proveedor` CHECK ∈ AEROLINEA, HOTEL, RENTADORA, OTRO |
| `servicios` (supertipo) | `id_servicio` | `id_proveedor`→`proveedores` | `tipo_servicio` ∈ VUELO, HOSPEDAJE, VEHICULO, PAQUETE; `precio_base` DECIMAL(12,2); `moneda` CHAR(3) |
| `vuelos` (subtipo) | `id_servicio` (PK+FK) | `id_servicio`→`servicios`; `id_origen`→`ubicaciones`; `id_destino`→`ubicaciones` | `codigo_vuelo`, `fecha_salida`, `fecha_llegada`, `clase` (solo en diagrama ER) |
| `hospedajes` (subtipo) | `id_servicio` (PK+FK) | `id_ubicacion`→`ubicaciones` | `tipo_habitacion`, `categoria` (N), `capacidad_huespedes`, `hora_entrada` (N), `hora_salida` |
| `vehiculos` (subtipo) | `id_servicio` (PK+FK) | `id_ubicacion_retiro`→`ubicaciones` | `categoria`, `marca`, `modelo`, `capacidad_pasajeros`, `transmision` (solo diagrama) |
| `paquete` (subtipo) | `id_servicio` (PK+FK) | — | `duracion_dias`, `min_personas`, `max_personas` (N), `politica_cancelacion` (N) |
| `paquete_servicio` (puente) | (`id_paquete`, `id_servicio_incluido`) | `id_paquete`→`paquete.id_servicio`; `id_servicio_incluido`→`servicios` | `cantidad`, `orden` (N) |
| `disponibilidad_servicio` | `id_disponibilidad` | `id_servicio`→`servicios` | `fecha`, `cantidad_total`, `cantidad_disponible`, `precio_fecha`, `actualizado_en` |

**Promociones**
| Tabla | PK | FK | Notas |
|---|---|---|---|
| `promociones` | `id_promocion` | — | `codigo` UNIQUE, `nombre`, `tipo_descuento` ∈ PORCENTAJE, MONTO; `valor`, `minimo_compra`, vigencia, `activo` |
| `promocion_servicio` (puente) | (`id_promocion`, `id_servicio`) | →`promociones`; →`servicios` | — |

**Reservaciones**
| Tabla | PK | FK | Notas |
|---|---|---|---|
| `estados_reservacion` | `id_estado_reservacion` | — | `nombre` UNIQUE, `es_final` |
| `reservaciones` | `id_reservacion` | `id_usuario`→`usuarios`; `id_estado_reservacion`→`estados_reservacion` | `codigo_reserva` UNIQUE; `subtotal`, `descuento_total`, `total` históricos |
| `detalle_reservacion` | `id_detalle` | `id_reservacion`→`reservaciones`; `id_servicio`→`servicios`; `id_promocion`→`promociones` (N) | **snapshot**: `precio_unitario`, `descuento_aplicado`, `subtotal`, `fecha_inicio`/`fecha_fin` (N), `descripcion_snapshot` |
| `historial_estado_reservacion` | `id_historial` | `id_reservacion`→`reservaciones`; `id_estado_reservacion`→`estados_reservacion`; `id_usuario_cambio`→`usuarios` (N) | `fecha_cambio`, `motivo` (N) |

**Pagos / Reembolsos**
| Tabla | PK | FK | Notas |
|---|---|---|---|
| `metodos_pago` | `id_metodo_pago` | — | `nombre` UNIQUE |
| `instituciones_financieras` | `id_institucion` | — | `clave_institucion` UNIQUE (N), `codigo_internacional` UNIQUE (N) |
| `tarjetas_usuario` | `id_tarjeta` | `id_usuario`→`usuarios`; `id_institucion`→`instituciones_financieras` (N) | `token_referencia` UNIQUE; `marca`, `ultimos4`, expiración, `alias` — **sin PAN/CVV** |
| `estados_pago` | `id_estado_pago` | — | `nombre` UNIQUE, `es_final` |
| `pagos` | `id_pago` | `id_reservacion`→`reservaciones`; `id_metodo_pago`→`metodos_pago`; `id_tarjeta`→`tarjetas_usuario` (N); `id_estado_pago`→`estados_pago` | `monto`, `moneda`, `referencia_externa` UNIQUE (N), `fecha_pago` (N) |
| `historial_estado_pago` | `id_historial_pago` | `id_pago`→`pagos`; `id_estado_pago`→`estados_pago` | `fecha_cambio`, `detalle` (N) |
| `reembolsos` | `id_reembolso` | `id_pago`→`pagos` | `monto`, `motivo`, `estado` ∈ SOLICITADO, APROBADO, RECHAZADO, PROCESADO |
| `historial_estado_reembolso` | `id_historial_reembolso` | `id_reembolso`→`reembolsos`; `id_usuario_cambio`→`usuarios` (N) | `estado`, `fecha_cambio`, `detalle` (N) |

**Soporte**
| Tabla | PK | FK | Notas |
|---|---|---|---|
| `solicitudes_soporte` | `id_solicitud` | `id_usuario`→`usuarios`; `id_reservacion`→`reservaciones` (N); `id_agente_asignado`→`usuarios` (N) | `tipo` ∈ ASESORIA, PROBLEMA; `estado` ∈ ABIERTA, EN_PROCESO, CERRADA |

### 4.2 Estado de la tabla `vuelos` y resolución de los subtipos
**Nota de auditoría sobre el PDF:** las secciones 5 y 6 del diccionario son la misma tabla `servicios` copiada dos veces; la sección destinada a `vuelos` se perdió. El diagrama ER (pág. 9) **sí** contiene `vuelos`:

| Columna (diagrama ER) | Tipo | Clave |
|---|---|---|
| `id_servicio` | bigint | PK, FK, UK → `servicios.id_servicio` |
| `id_origen` | bigint | FK → `ubicaciones.id_ubicacion` |
| `id_destino` | bigint | FK → `ubicaciones.id_ubicacion` |
| `codigo_vuelo` | varchar | — |
| `fecha_salida` | datetime | — |
| `fecha_llegada` | datetime | — |
| `clase` | varchar | — |

**Resolución en el código: no se resolvió.** No hay `CREATE TABLE vuelos` en `Models/script base de datos.sql`, ni clase `Vuelos` en `Models/`, ni archivo `vuelos.sql` entre los procedimientos de `origin/sqlyamil` (`sql/db.rar`). La vista `Viajes.cshtml` (buscador de vuelos) no tiene respaldo en la BD.

**El patrón supertipo/subtipo está roto en el script real:**
- `hospedajes` y `vehiculos` tienen `id bigint identity` propia y **ninguna FK a `servicios`** → no hay forma de saber a qué servicio pertenece un hospedaje o vehículo.
- `paquete` tiene PK compuesta (`id`, `id_servicio`) con `id` **sin identity** → hay que inventar el `id` manualmente; un servicio podría tener varios paquetes.
- `paquete_servicio` tiene PK (`id`, `id_servicio`) donde `id` no es FK a `paquete`; no garantiza que `id_servicio` sea de tipo PAQUETE.
- `servicios.tipo_servicio` no tiene `CHECK`.
- Otras divergencias del diseño: `hospedajes.tipo_ubicacion` (debe ser `tipo_habitacion`); falta `vehiculos.transmision`.

**Propuesta de corrección (T‑SQL, SQL Server)** — aplicar en un script de migración versionado, no a mano en producción:
```sql
-- Supertipo: restringir tipos
ALTER TABLE servicios ADD CONSTRAINT ck_servicios_tipo
    CHECK (tipo_servicio IN ('VUELO','HOSPEDAJE','VEHICULO','PAQUETE'));

CREATE TABLE vuelos(
    id_servicio   BIGINT       NOT NULL PRIMARY KEY
                  REFERENCES servicios(id),
    id_origen     BIGINT       NOT NULL REFERENCES ubicaciones(id),
    id_destino    BIGINT       NOT NULL REFERENCES ubicaciones(id),
    codigo_vuelo  VARCHAR(10)  NOT NULL,
    fecha_salida  DATETIME     NOT NULL,
    fecha_llegada DATETIME     NOT NULL,
    clase         VARCHAR(20)  NOT NULL DEFAULT('TURISTA'),
    CONSTRAINT ck_vuelos_fechas  CHECK (fecha_llegada > fecha_salida),
    CONSTRAINT ck_vuelos_ruta    CHECK (id_origen <> id_destino)
);

CREATE TABLE hospedajes(
    id_servicio          BIGINT      NOT NULL PRIMARY KEY REFERENCES servicios(id),
    id_ubicacion         BIGINT      NOT NULL REFERENCES ubicaciones(id),
    tipo_habitacion      VARCHAR(60) NOT NULL,
    categoria            VARCHAR(40) NULL,
    capacidad_huespedes  TINYINT     NOT NULL DEFAULT(1),
    hora_entrada         TIME        NULL,
    hora_salida          TIME        NOT NULL,
    imagen               VARBINARY(MAX) NULL
);

CREATE TABLE vehiculos(
    id_servicio          BIGINT      NOT NULL PRIMARY KEY REFERENCES servicios(id),
    id_ubicacion_retiro  BIGINT      NOT NULL REFERENCES ubicaciones(id),
    categoria            VARCHAR(50) NOT NULL,
    marca                VARCHAR(50) NOT NULL,
    modelo               VARCHAR(60) NOT NULL,
    capacidad_pasajeros  TINYINT     NOT NULL,
    transmision          VARCHAR(20) NULL,
    imagen               VARBINARY(MAX) NULL
);

CREATE TABLE paquete(
    id_servicio          BIGINT   NOT NULL PRIMARY KEY REFERENCES servicios(id),
    duracion_dias        SMALLINT NOT NULL DEFAULT(1),
    min_personas         SMALLINT NOT NULL DEFAULT(1),
    max_personas         SMALLINT NULL,
    politica_cancelacion NVARCHAR(MAX) NULL,
    imagen               VARBINARY(MAX) NULL
);

CREATE TABLE paquete_servicio(
    id_paquete           BIGINT       NOT NULL REFERENCES paquete(id_servicio),
    id_servicio_incluido BIGINT       NOT NULL REFERENCES servicios(id),
    cantidad             DECIMAL(8,2) NOT NULL,
    orden                SMALLINT     NULL CHECK (orden >= 0),
    PRIMARY KEY (id_paquete, id_servicio_incluido),
    CHECK (id_paquete <> id_servicio_incluido)
);

CREATE TABLE historial_estado_reservacion(
    id                    BIGINT IDENTITY(1,1) PRIMARY KEY,
    id_reservacion        BIGINT   NOT NULL REFERENCES reservaciones(id),
    id_estado_reservacion SMALLINT NOT NULL REFERENCES estados_reservacion(id),
    id_usuario_cambio     BIGINT   NULL REFERENCES usuarios(id),
    fecha_cambio          DATETIME NOT NULL DEFAULT(CURRENT_TIMESTAMP),
    motivo                VARCHAR(250) NULL
);

-- Inventario: una fila por servicio y día, y nunca más disponible que total
ALTER TABLE disponibilidad_servicio ADD CONSTRAINT uq_disp_servicio_fecha UNIQUE (id_servicio, fecha);
ALTER TABLE disponibilidad_servicio ADD CONSTRAINT ck_disp_rango CHECK (cantidad_disponible <= cantidad_total);
```
La coherencia "el subtipo coincide con `tipo_servicio`" (p. ej. que una fila de `vuelos` apunte a un servicio `VUELO`) no se puede expresar con un `CHECK` simple; se valida en la capa de servicio o con un trigger, o con la técnica de FK compuesta (`id_servicio`, `tipo_servicio`) + columna calculada persistida en cada subtabla.

### 4.3 Divergencias del script SQL real frente al diseño
| Tabla | Divergencia | Severidad |
|---|---|---|
| (global) | Motor: diseño estilo MySQL vs. script SQL Server. PK `id` vs `id_<tabla>`. `ulong` en C# vs `bigint` con signo en SQL Server | Alta (decidir y documentar) |
| (global) | BD llamada `FlyVideMex`; `CREATE DATABASE` y `USE` en el mismo lote sin `GO` (SSMS suele fallar en el `USE`) | Media |
| (global) | Muchas FK declaradas nullable aunque el diseño dice NOT NULL (`usuarios.id_rol`, `servicios.id_proveedor`, `reservaciones.id_estado_reservacion`, `detalle_reservacion.id_reservacion`/`id_servicio`, `disponibilidad_servicio.id_servicio`, `pagos.id_estado_pago`, `historial_estado_pago.id_estado_pago`, `reembolsos.id_pago`, `historial_estado_reembolso.id_reembolso`) | Alta |
| (global) | `text` está obsoleto en SQL Server → usar `NVARCHAR(MAX)`; textos con acentos deberían ser `NVARCHAR` | Media |
| `roles_usuario` | renombrada `roles_usuarios`; `nombre` sin NOT NULL | Baja |
| `ubicaciones` | sin `colonia`, `calle`, `numero_exterior`, `numero_interior`, `referencias`; con `correo`, `activo`, `creado`, `actualizado` | Alta (no sirve para direcciones) |
| `usuarios` | sin `actualizado_en`; con `imagen` | Baja |
| `proveedores` | columna **`tipo_proveerdor`** (errata) | Media |
| `servicios` | `precio_base DECIMAL(10,2)` (diseño 12,2); `Moneda VARCHAR(3)` (diseño CHAR(3)); sin CHECK de tipo | Media |
| `vuelos` | **no existe** | Crítica |
| `hospedajes`, `vehiculos`, `paquete`, `paquete_servicio` | herencia rota (ver 4.2) | Crítica |
| `disponibilidad_servicio` | sin `UNIQUE(id_servicio, fecha)` ni `CHECK(cantidad_disponible <= cantidad_total)` | Alta |
| `promociones` | sin `nombre`, `minimo_compra`; sin CHECK de `tipo_descuento` ni de `fecha_fin > fecha_inicio` | Media |
| `reservaciones` | sin `actualizada_en` | Baja |
| `historial_estado_reservacion` | **no existe** | Alta |
| `instituciones_financieras` | `nombre` nullable; `clave_institucion` y `codigo_internacional` **UNIQUE + NULL**: en SQL Server un UNIQUE solo admite **una** fila NULL → usar índice único filtrado `WHERE col IS NOT NULL` | Alta |
| `tarjetas_usuarios` | renombrada (plural); `CHECK(mes_expiracion >= 0)` debería ser `BETWEEN 1 AND 12`; sin CHECK de 4 dígitos en `ultimos4` | Media |
| `estados_pago` | `es_final TINYINT` (resto usa `bit`) | Baja |
| `pagos` | `referencia_externa UNIQUE DEFAULT NULL` → mismo problema de NULL único: el **segundo pago sin referencia fallará** | Alta |
| `reembolsos` | sin CHECK `monto > 0` ni validación contra el monto del pago | Media |
| `solicitudes_soporte` | sin CHECK de `tipo` ∈ ASESORIA, PROBLEMA | Baja |

### 4.4 Datos semilla
- El script inserta 2 `ubicaciones` y luego hace `INSERT INTO hospedaje (...)` → **falla**: la tabla se llama `hospedajes`, y además esos hospedajes no tendrían servicio ni proveedor.
- No hay semillas para catálogos obligatorios: `roles_usuarios` (CLIENTE, AGENTE, ADMINISTRADOR), `estados_reservacion`, `estados_pago`, `metodos_pago`.
- Propuesta mínima de catálogos (ajustar con el equipo): estados de reservación `PENDIENTE`, `CONFIRMADA`, `CANCELADA`(final), `COMPLETADA`(final); estados de pago `PENDIENTE`, `APROBADO`(final), `RECHAZADO`(final), `REEMBOLSADO`(final); métodos `TARJETA_CREDITO`, `TARJETA_DEBITO`, `TRANSFERENCIA`, `EFECTIVO_OXXO`.

### 4.5 Erratas del propio PDF (para no propagarlas)
- Secciones 5 y 6 duplicadas (`servicios`); falta el texto de `vuelos`.
- Sección 13 (`promocion_servicio`) reutiliza la descripción de `promociones`.
- Sección 10: FK `id_paquete` → "`paquetes.id_servicio`", pero la tabla se llama `paquete`.
- Secciones 17 (`motivo`) y 21 (`es_final`) tienen la columna "Observaciones" truncada.
- `hospedajes.hora_salida` NOT NULL pero `hora_entrada` NULL (posible inversión).
- `ubicaciones.referencias` NOT NULL (debería ser opcional).
- `vuelos.clase` y `vehiculos.transmision` solo aparecen en el diagrama ER.
- La portada dice "Avance #1" aunque el archivo es `AVANCE2_EQUIPO1.pdf`.

### 4.6 Mapeo de datos (estado y objetivo)
| Aspecto | Hoy | Objetivo recomendado |
|---|---|---|
| ORM / acceso | Ninguno | EF Core SQL Server (`Microsoft.EntityFrameworkCore.SqlServer` 10.x) **o** Dapper + `Microsoft.Data.SqlClient` si el equipo usará los procedimientos almacenados de `sqlyamil`. No mezclar ambos sin razón |
| Enteros | `ulong`/`ushort` | `long`/`short`: SQL Server no tiene enteros sin signo y EF Core no mapea `ulong` a `bigint` sin conversores |
| Nulabilidad | casi todo no nullable | Reflejar exactamente las columnas NULL (`long?`, `DateTime?`, `string?`) |
| Nombres | propiedades snake_case = columnas | Ver 6.1 |
| Relaciones | ninguna navegación | Propiedades de navegación + Fluent API para PK/FK, índices únicos y precisión decimal |

---

## 5. Análisis de Brecha (Diseño vs. Implementación Actual)

### 5.1 Matriz de estado por módulo
| Módulo | ¿Vista? | ¿Controlador/Rutas? | ¿Datos simulados o BD real? | ¿Qué falta implementar? |
|---|---|---|---|---|
| **Explorar y reservar** | Parcial: `Viajes` (vuelos), `Hoteles`, `Paquetes`, `Tours` (extra), `card` (maqueta). **Faltan** Vehículos, Promociones, Resultados, Detalle, Resumen de reserva, Pago, Confirmación | `HomeController` GET sin parámetros; formularios `action="#"` sin `name` | **Ni mock ni BD**: tarjetas escritas a mano en HTML | Tabla `vuelos`; búsqueda real contra `servicios` + subtipos + `disponibilidad_servicio`; ViewModels de búsqueda/resultado; detalle con política de cancelación; carrito/resumen con promociones; creación transaccional de reservación + detalle (snapshot) + descuento de inventario; pago (simulado vía gateway sandbox con token); confirmación con `codigo_reserva`. Decidir qué es "Tours" (paquete o nuevo tipo `TOUR`) |
| **Mis viajes** | No (solo botón "Mis Viajes" `href="#"` en Default) | No | — | Requiere auth; listados actuales/pasados; detalle; cancelar (estado + bitácora + devolver inventario); solicitar reembolso; reportar problema |
| **Soporte** | No (solo texto "¿Tienes alguna duda…?" en Default) | No | — | Formulario de `solicitudes_soporte` (ASESORIA/PROBLEMA), listado del usuario, asignación a agente |
| **Cuenta** | Parcial: `sesion.cshtml` (no funcional) | `sesion()` GET únicamente | — | Registro, login/logout con cookies (hash de contraseña con `PasswordHasher<T>` o BCrypt), perfil, métodos de pago tokenizados; menú que cambie "Iniciar sesión"→"Mi perfil" |
| **Panel interno** | No | No | — | Área `/Admin` con `[Authorize(Roles="AGENTE,ADMINISTRADOR")]`: reservaciones, servicios/proveedores (CRUD + inventario por fecha), paquetes/promociones, solicitudes de soporte |

Pantallas del prototipo implementadas: **5 de ~30** (Home/Explorar, Vuelos, Hospedaje, Paquetes, Auth parcial) más Tours fuera de diseño.

### 5.2 Deuda técnica y riesgos detectados
**Arquitectura**
1. **Sin capa de datos**: ni `DbContext`, ni repositorios, ni servicios, ni cadena de conexión. El backend (etapa 4) no ha empezado aunque su ventana cerró el 4/oct.
2. **Tres namespaces** en `Models/` (`FlyVibeMex.Models`, `fvm`, `fvm.models`).
3. **`DefaultController.cs`** duplica rutas y produce errores 500 en `/Default/*`.
4. **SPA casera con `innerHTML`**: los `<script>` de las parciales no se ejecutan; no hay historial ni URLs profundas; las reglas globales (`*`, `body`) de cada hoja de sección afectan al shell mientras está cargada. Cualquier lógica por sección debe vivir en `script.js` o en un cargador que inyecte scripts, o abandonarse este enfoque en favor de páginas MVC normales con `_Layout`.
5. **`_Layout.cshtml` y `Error.cshtml` rotos**: referencian `~/lib/*/dist`, `~/css/site.css`, `~/js/site.js` que no existen. Si se lanza una excepción en producción, la página de error sale sin estilos.
6. **Acción `Privacy` inexistente** pero enlazada.
7. **Supertipo/subtipo roto en BD** y tabla `vuelos` ausente (sección 4.2).
8. **Mismatch de motor y tipos** (diseño MySQL vs SQL Server; `ulong` vs `bigint`).
9. **Rama `origin/sqlyamil` sin fusionar** (commit `3abf58b`, 17/10/2026): elimina `Models/script base de datos.sql`, quita `correo` de `Ubicaciones`, y agrega `sql/db.rar` con `db/procedures/<tabla>.sql` para 24 tablas (`detalle_reservacion`, `disponibilidad_servicio`, `estados_pago`, `estados_reservacion`, `historial_estado_pago`, `historial_estado_reembolso`, `hospedajes`, `instituciones_financieras`, `metodos_pago`, `pagos`, `paquete`, `paquete_servicio`, `promociones`, `promocion_servicio`, `proveedores`, `reembolsos`, `reservaciones`, `roles_usuarios`, `servicios`, `solicitudes_soporte`, `tarjetas_usuarios`, `ubicaciones`, `usuarios`, `vehiculos`) y `db/script base de datos.sql`. **Su contenido no pudo auditarse** (RAR v4 sólido, sin herramienta para descomprimir en el entorno de auditoría). Riesgo: un binario `.rar` en Git no se puede revisar en PR ni comparar; debe subirse como `.sql` en texto plano. No incluye `vuelos` ni `historial_estado_reservacion`.

**Seguridad**
10. **Sin autenticación ni autorización**; `UseAuthorization()` sin esquemas.
11. Login con contraseña en `<input type="text">`, sin `name`, sin POST, sin antiforgery.
12. Cuando exista login, **no** registrar ni devolver `password_hash`; nunca exponer entidades directamente a las vistas (usar ViewModels) para evitar *overposting*.
13. Credenciales futuras de BD, gateway de pagos y APIs de IA **no** deben ir en `appsettings.json` versionado: usar `dotnet user-secrets` en desarrollo y variables de entorno en despliegue. El `.gitignore` ya ignora `.env`.
14. `AllowedHosts: "*"` aceptable en desarrollo, restringir en despliegue.
15. Imágenes externas (Unsplash y otros dominios) pueden romperse o cambiar.

**Integridad de datos**
16. UNIQUE sobre columnas NULL (`pagos.referencia_externa`, `instituciones_financieras.clave_institucion`/`codigo_internacional`) bloqueará el segundo NULL en SQL Server.
17. FKs nullable donde deben ser obligatorias.
18. Sin restricciones de inventario (`UNIQUE(id_servicio, fecha)`, `disponible <= total`) ⇒ riesgo de sobreventa; además la reserva debe hacerse en **transacción** con bloqueo (`UPDATE ... SET cantidad_disponible = cantidad_disponible - @n WHERE ... AND cantidad_disponible >= @n` y verificar filas afectadas).
19. Seed roto (`hospedaje`) y catálogos sin datos iniciales.
20. Errata `fehca_inicio` (C#) y `tipo_proveerdor` (SQL) romperán cualquier mapeo automático.

**Calidad / repositorio**
21. `bin/` y `obj/` versionados (143 archivos): conflictos constantes de merge entre equipos (ya se ven rutas `C:\Users\César\...` y `C:\Users\User\...` en `obj/`). Añadir `bin/`, `obj/`, `.vs/` al `.gitignore` y `git rm -r --cached bin obj`.
22. Archivos vacíos o de plantilla: `script_D/H/P/T/V.js`, `app.js`, `originalstyles.css`, `site.js`, `site.css`, `exIndex.cshtml`, `Privacy.cshtml`.
23. CSS duplicado: `style_H.css`, `style_T.css`, `style_V.css` idénticos y `style_P.css` difiere en 1 línea → consolidar en un `search.css` común.
24. HTML inválido: `id="tarjetas"` repetido en `card.cshtml`; `id="secion"` repetido en `Index.cshtml`; `<img src="">` del logo; clase `imf-fluid` (errata de `img-fluid`); `label for` que no corresponden a ningún `id` en `sesion.cshtml`.
25. Erratas visibles al usuario: "FlyVideMex", "Iniciar secion", "aceder".
26. Sin pruebas automatizadas de ningún tipo (etapa 6 empieza el 19/oct).
27. `README.md` vacío.

---

## 6. Guía de Trabajo para IA (Instrucciones para Claude y Gemini)

### 6.1 Convenciones de nombres
| Elemento | Convención vigente | Regla para código nuevo |
|---|---|---|
| Tablas SQL | snake_case, mayoritariamente plural (`usuarios`, `servicios`); excepciones `paquete`, `detalle_reservacion`, `disponibilidad_servicio` | Respetar **exactamente** el nombre existente en el script; no renombrar tablas sin acuerdo del equipo |
| Columnas SQL | snake_case; PK = `id`; FK = `id_<entidad>` | Igual. Nuevas tablas: PK `id`, FKs `id_<tabla_singular>` |
| Valores de catálogo/estado | MAYÚSCULAS con guion bajo (`EN_PROCESO`, `HOSPEDAJE`) | Igual; validarlos con `CHECK` |
| Archivos de modelo | snake_case (`roles_usuario.cs`) | Nuevos archivos: **nombre igual a la clase** (`Vuelos.cs`) es lo idiomático en C#; si el equipo prefiere mantener snake_case, ser consistente |
| Clases C# | PascalCase plural (`Usuarios`, `Reservaciones`) | PascalCase; mantener plural mientras no se acuerde migrar a singular |
| Propiedades C# | snake_case idéntico a la columna (`id_usuario`, `creado_en`) | **Decisión pendiente del equipo.** Opción A (mínima): seguir con snake_case (funciona directo con Dapper). Opción B (idiomática): PascalCase (`IdUsuario`) + `[Column("id_usuario")]` o Fluent API. Aplicar la opción elegida en **un solo commit de refactor** junto con la unificación de namespaces; no mezclar estilos dentro de una misma clase |
| Namespace | mezcla `FlyVibeMex.Models` / `fvm` / `fvm.models` | **Siempre** `FlyVibeMex.Models` (y `FlyVibeMex.Controllers`, `FlyVibeMex.Data`, `FlyVibeMex.Services`, `FlyVibeMex.ViewModels`) con namespace de archivo (`namespace X;`) |
| Controladores | `HomeController` | `<Nombre>Controller` siempre con sufijo |
| Acciones | PascalCase salvo `sesion()` y `card()` | PascalCase |
| Vistas | `Views/Home/<Accion>.cshtml` | `Views/<Controlador>/<Accion>.cshtml`; parciales con prefijo `_` (`_TarjetaServicio.cshtml`) |
| CSS/JS por sección | `style_<L>.css`, `script_<L>.js` (D, V, H, T, P, s) | Mantener la letra si se agrega una sección al shell (p. ej. `R` Resultados, `M` Mis viajes, `C` Cuenta, `A` Admin) y documentarla aquí |
| HTML | `class` solo para Bootstrap; estilo propio por `id` | Respetar; `id` único por documento |
| Idioma | Español en dominio, UI y BD | Mantener español en nombres de dominio; comentarios en español |

### 6.2 Reglas obligatorias para cualquier IA que modifique el repositorio
1. **Leer este documento primero** y verificar el estado real (`git log`, `git status`) antes de suponer que algo de aquí sigue igual. Si se cambia algo estructural, **actualizar este archivo** en el mismo cambio.
2. **No inventar tablas ni columnas.** El diseño lógico es el del PDF (sección 4.1); el físico es el script SQL. Si difieren, señalarlo y proponer la migración; no "arreglar" silenciosamente en un solo lado.
3. **Respetar las reglas R1–R13** (sección 1.5). En particular: snapshot en `detalle_reservacion`, nunca PAN/CVV, toda transición de estado escribe su `historial_*`, inventario por fecha descontado en transacción.
4. **Un solo motor: SQL Server (T‑SQL).** No generar sintaxis MySQL (`AUTO_INCREMENT`, `UNSIGNED`, `BOOL`) salvo que el equipo cambie de motor.
5. **Cambios de esquema = script de migración versionado** (`sql/migraciones/NNN_descripcion.sql` o migraciones de EF Core), nunca editar a mano la BD de otro integrante. Separar lotes con `GO`.
6. **Controladores delgados:** validación de entrada + llamada a un servicio + retorno. La lógica de negocio va en `Services/` (p. ej. `IReservacionService`), el acceso a datos en `Data/` (DbContext o repositorios). Inyectar dependencias por constructor y registrarlas en `Program.cs`.
7. **ViewModels para todas las vistas y formularios** (`ViewModels/BusquedaServicioViewModel`, `ResultadoServicioViewModel`, `ResumenReservaViewModel`, `LoginViewModel`, etc.). Nunca enlazar entidades directamente a formularios.
8. **POST con `[HttpPost]` + `[ValidateAntiForgeryToken]`**; DataAnnotations (`[Required]`, `[EmailAddress]`, `[Range]`) y comprobación de `ModelState.IsValid`.
9. **Autorización por rol** (`[Authorize(Roles = "...")]`) en "Mis viajes", "Cuenta" y "Panel interno". Usar autenticación por cookies y hash con `PasswordHasher<Usuarios>` (o BCrypt).
10. **Nulabilidad exacta:** si la columna permite NULL, la propiedad es nullable; si no, no.
11. **Dinero:** `decimal` con precisión `(12,2)`; nunca `double`/`float`. Calcular totales en servidor, nunca confiar en precios enviados desde el navegador.
12. **Frontend:** si se mantiene el shell con `cargarPagina`, todo JS de sección debe cargarse desde `Index.cshtml` (o mediante un cargador que cree elementos `<script>`); no confiar en `<script>` dentro de una parcial. Cualquier dato dinámico debe venir del servidor (Razor con modelo, o endpoint JSON), no escrito a mano.
13. **No commitear** `bin/`, `obj/`, `.vs/`, secretos, ni binarios comprimidos (`.rar`) con código o SQL.
14. **Commits pequeños y en español**, una intención por commit; ramas por funcionalidad y PR a `main` (flujo que el equipo ya usa).

### 6.3 Estructura objetivo sugerida (sin romper lo existente)
```
FlyVibeMex/
├── Controllers/            HomeController, ExplorarController, ReservasController, MisViajesController,
│                           SoporteController, CuentaController
├── Areas/Admin/Controllers PanelController, ServiciosController, PromocionesController, SoporteAdminController
├── Controllers/Api/        endpoints JSON /api/... (para el shell JS, bots e IA)
├── Data/                   FlyVibeMexContext.cs (o ConexionSql.cs + repositorios Dapper)
├── Services/               ICatalogoService, IDisponibilidadService, IReservacionService, IPagoService,
│                           IReembolsoService, ISoporteService, IAuthService (+ implementaciones)
├── ViewModels/
├── Models/                 entidades (namespace FlyVibeMex.Models)
├── sql/                    00_esquema.sql, 01_catalogos_seed.sql, migraciones/NNN_*.sql, procedures/*.sql
└── tests/FlyVibeMex.Tests/ xUnit
```

### 6.4 Orden de trabajo recomendado (alineado al cronograma restante)
**Antes y durante "Primeras pruebas" (19–22 oct)**
1. Limpieza: `.gitignore` (`bin/`, `obj/`, `.vs/`), borrar `DefaultController.cs` y archivos de plantilla/vacíos, unificar namespaces, corregir erratas (`fehca_inicio`, `tipo_proveerdor`, `FlyVideMex`).
2. Fusionar o descartar `origin/sqlyamil`, extrayendo el `.rar` a `.sql` en texto plano.
3. Script de esquema corregido: crear `vuelos` e `historial_estado_reservacion`, reparar herencia (4.2), índices únicos filtrados, CHECKs, seeds de catálogos y datos de prueba coherentes (proveedor → servicio → subtipo → disponibilidad).
4. Conexión: paquete NuGet elegido, `ConnectionStrings:FlyVibeMex` vía user-secrets, `AddDbContext`/registro de conexión en `Program.cs`.
5. Primer flujo vertical completo: búsqueda de hospedaje/vuelo con datos reales → resultados → detalle.
6. Pruebas: xUnit para cálculo de totales/descuentos, transición de estados y descuento de inventario; prueba de integración de la búsqueda.

**Bots (23–26 oct)**
- Exponer API JSON estable y documentada: `GET /api/servicios?tipo=&destino=&desde=&hasta=&personas=`, `GET /api/reservaciones/{codigo}` (solo propietario autenticado), `POST /api/soporte`.
- El bot crea `solicitudes_soporte` (tipo `ASESORIA` o `PROBLEMA`) y consulta el estado de una reserva por `codigo_reserva` + verificación del usuario; **nunca** procesa pagos ni ve datos de tarjetas.
- Escalado a humano = `id_agente_asignado` + `estado = 'EN_PROCESO'`.

**IA (27–30 oct)**
- Encapsular el proveedor de IA detrás de una interfaz (`IAsistenteViajes`) registrada en DI; clave API por user-secrets/variables de entorno.
- Casos de uso acotados: recomendación de destinos/paquetes a partir del catálogo real (consultas parametrizadas, no SQL generado por el modelo), resumen de itinerario de una reservación, clasificación automática de tickets de soporte.
- Nunca enviar al modelo `password_hash`, tokens de tarjeta ni datos personales innecesarios; registrar qué datos se envían.

**Pruebas finales y entrega (31 oct – 6 nov)**
- Recorrido completo de los 5 módulos contra la matriz 5.1, actualización de este documento y del `README.md` (cómo crear la BD, cómo configurar secretos, cómo ejecutar).

### 6.5 Comandos de referencia
```bash
# Ejecutar (desde FlyVibeMex/, donde está FlyVibeMex.csproj)
dotnet run                         # https://localhost:7045  |  http://localhost:5106

# Secretos locales (cuando exista la BD)
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:FlyVibeMex" "Server=localhost;Database=FlyVibeMex;Trusted_Connection=True;TrustServerCertificate=True"

# Opción EF Core
dotnet add package Microsoft.EntityFrameworkCore.SqlServer
dotnet add package Microsoft.EntityFrameworkCore.Design

# Opción Dapper
dotnet add package Dapper
dotnet add package Microsoft.Data.SqlClient

# Sacar artefactos de Git
git rm -r --cached bin obj
```
