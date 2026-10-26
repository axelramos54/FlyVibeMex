# FlyVibeMex · Revisión técnica y plan de trabajo

| Campo | Valor |
|---|---|
| Fecha de la revisión | 26/10/2026 |
| Qué se revisó | Copia de trabajo local de `Proyecto/FlyVibeMex/` (rama `main`, último commit `949f1ae` + **53 archivos modificados/sin versionar**) |
| Alcance | `Controllers/`, `Models/` (+ `Models/Admin`, `Models/Auth`), `Data/`, `Services/`, `Views/`, `wwwroot/`, `sql/`, `Program.cs`, `appsettings*.json`, `FlyVibeMex.csproj` |
| Limitación | No se pudo **compilar** el proyecto (en el entorno de revisión no hay SDK de .NET). Todo lo que dice "compila / falla" se dedujo leyendo el código. **Antes de repartir tareas, ejecuten `dotnet build` y anoten los errores reales.** |

> Convención de este documento: `archivo:línea` indica dónde está cada cosa. Las líneas corresponden a la copia local del 26/10/2026.

---

# PARTE 1 — Análisis del avance actual

## 1. Resumen general

FlyVibeMex hoy es **una aplicación ASP.NET Core MVC (.NET 10) con una base técnica sólida, pero casi sin funcionalidades conectadas a la base de datos**:

- **Lo que sí funciona con la BD:** registro de clientes (`/Crearusuario`), inicio y cierre de sesión con cookie (`/Home/IniciarSesion`, `/Home/CerrarSesion`) y el diagnóstico `/Home/TestDb`.
- **El panel de administración** (`/Admin`, `/Admin/GestorVentas`, `/Admin/Soporte`, `/Admin/SA`) tiene pantallas, formularios, validaciones y rutas bien hechas, pero **no guarda nada**: el Gestor de ventas trabaja con datos de prueba en memoria (`ModoSinBD = true`) y Soporte devuelve listas vacías.
- **El sitio público** (Viajes, Hoteles, Tours, Paquetes, Vehículos, Mis compras, Mis reservaciones, Mis tarjetas, Soporte) son **maquetas HTML con datos escritos a mano**. Los buscadores tienen `action="#"` y no envían nada.
- **No existe el flujo de reserva** (resultados → detalle → resumen → pago → confirmación), que es el corazón de la aplicación.
- **No hay protección por roles**: cualquier persona, sin iniciar sesión, puede abrir `/Admin/GestorVentas`.

### Avance estimado: **≈ 30 %**

| Bloque | Peso | Avance | Aporta | Justificación |
|---|---|---|---|---|
| Infraestructura (EF Core, conexión Test/Real, esquema SQL de 26 tablas, seed demo) | 15 % | 85 % | 12,8 | `Data/FlyVibeMexDbContext.cs`, `sql/01_esquema.sql` y `sql/02_seed_demo.sql` están muy completos. Falta versionarlos y alinear la BD "Real". |
| Autenticación (registro, login, logout, roles) | 10 % | 55 % | 5,5 | Registro y login funcionan; no hay claim de rol, ni `[Authorize]`, ni cambio del menú al iniciar sesión. |
| Panel admin · Proveedores y servicios | 15 % | 40 % | 6,0 | UI + validaciones listas; 0 % de persistencia. |
| Panel admin · Paquetes y promociones | 8 % | 5 % | 0,4 | Solo pantallas "en construcción". |
| Panel admin · Soporte / SA / reservaciones | 7 % | 20 % | 1,4 | Vistas de tickets sin datos; SA vacío; no hay gestión de reservaciones. |
| Sitio público · catálogo y búsqueda | 15 % | 15 % | 2,3 | Solo maquetas estáticas. |
| Flujo de reserva + pago + confirmación | 15 % | 0 % | 0 | No existe ninguna pantalla ni acción. |
| Mis viajes / compras / tarjetas / cancelar / reembolso | 10 % | 20 % | 2,0 | Vistas estáticas + POST de cancelación sin BD. |
| Soporte del cliente (crear ticket) | 5 % | 5 % | 0,3 | `Views/Solicitar/index.cshtml` solo tiene el título. |
| **Total** | **100 %** | | **≈ 30,7 %** | |

**Contexto de calendario:** según el cronograma del equipo, la entrega es del **2 al 6 de noviembre** (≈ 1 semana y media). El plan de la Parte 2 está ordenado para que, si no alcanza el tiempo, al menos quede funcionando un flujo completo de punta a punta.

---

## 2. Inventario de lo que ya funciona

Leyenda CRUD: ✅ hecho y guardando en BD · 🟡 pantalla + validación, sin BD · ❌ no existe

| Módulo | Ruta | C | R | U | D | Estado real |
|---|---|---|---|---|---|---|
| Conexión a BD y diagnóstico | `/Home/TestDb` (solo Development) | — | ✅ | — | — | Funciona. Verifica las 26 tablas contra los modelos. |
| Registro de clientes | `/Crearusuario` | ✅ | — | — | — | Hash con `PasswordHasher`, correo/teléfono únicos, rol de cliente buscado en BD, antiforgery. **Bien hecho.** |
| Inicio / cierre de sesión | `/Home/IniciarSesion`, `/Home/CerrarSesion` | — | ✅ | — | — | Consulta LINQ parametrizada, mensajes genéricos, protección contra *open redirect*. Falta el rol (ver Problemas). |
| Proveedores (Gestor de ventas) | `/Admin/GestorVentas/Proveedores/...` | 🟡 | 🟡 | 🟡 | 🟡 | Alta, búsqueda, lista con filtros, actualizar, baja lógica con doble confirmación y reactivar. Todo con datos de prueba. |
| Servicios (Gestor de ventas) | `/Admin/GestorVentas/Servicios/...` | 🟡 | 🟡 | 🟡 | 🟡 | Igual que proveedores. Impide cambiar el tipo de un servicio existente (buena regla). |
| Asociar hospedaje / vehículo / evento a proveedor | `/Admin/GestorVentas/Proveedores/{id}/Asociar` | 🟡 | 🟡 | ❌ | ❌ | Valida la imagen por sus bytes reales (JPG/PNG/WEBP, 2 MB). No guarda. |
| Paquetes y promociones (admin) | `/Admin/GestorVentas/Paquetes`, `/Promociones` | ❌ | ❌ | ❌ | ❌ | Vista `EnConstruccion.cshtml`. |
| Soporte (agentes) | `/Admin/Soporte/...` | — | 🟡 | 🟡 | — | Vistas de "Nuevo ticket", "Seguir atendiendo", "Historial"; datos vacíos. Falta "Cerrar ticket". |
| SA | `/Admin/SA` | ❌ | ❌ | ❌ | ❌ | Solo un mensaje. |
| Catálogo público (Viajes, Hoteles, Tours, Paquetes, Vehículos) | `/Home/...` vía `cargarPagina()` | — | 🟡 | — | — | HTML estático, tarjetas escritas a mano. |
| Mis compras / Mis reservaciones / Mis tarjetas | `/Miusuario/...` | ❌ | 🟡 | ❌ | 🟡 | Maquetas; los modales de cancelar/reembolsar sí hacen POST con token, pero no guardan. |
| Soporte del cliente | `/Solicitar` | ❌ | ❌ | — | — | Solo el título "Soporte". |
| Otros / Políticas | `/Otros`, `/Otros/Politicas` | — | 🟡 | — | — | Contenido estático. |

**Puntos fuertes que conviene conservar** (están mejor que en muchos proyectos escolares):
- Formularios con **ViewModels + `[Bind]` + DataAnnotations** y `ModelState.IsValid` (`Models/Admin/*.cs`, `Models/Auth/CuentaViewModels.cs`).
- **Antiforgery en todos los POST**: `[AutoValidateAntiforgeryToken]` en los controladores del panel y `[ValidateAntiForgeryToken]` en los demás.
- **Cero SQL concatenado**: todo es LINQ (`Services/AuthService.cs`, `Controllers/CrearusuarioController.cs`).
- **Cadenas de conexión fuera del repositorio** (`appsettings.json` solo tiene el marcador `__SERVIDOR__`; las reales van en `dotnet user-secrets`).
- `bin/` y `obj/` ya no están versionados (`git ls-files` = 0).
- Esquema SQL con CHECKs, índices únicos filtrados y subtipos correctos (`sql/01_esquema.sql`).

---

## 3. Lo que está a medias

| # | Funcionalidad | Archivo(s) exactos | Qué le falta |
|---|---|---|---|
| M1 | Persistencia del Gestor de ventas | `Controllers/GestorController.cs:28` (`ModoSinBD = true`), métodos `TODO BD` en `:345-371` y `Controllers/GestorController.ProveedoresServicios.cs:267-327` | Inyectar el `DbContext`, reemplazar los métodos de acceso a datos y borrar los datos de prueba. |
| M2 | Lista de ubicaciones en formularios | `Controllers/GestorController.cs:360-364` | `ObtenerUbicaciones()` **siempre** devuelve una lista vacía (ni siquiera en modo prueba), así que nunca se puede elegir ubicación. |
| M3 | Asociar hospedaje/vehículo/evento | `Controllers/GestorController.cs:187-241` | Insertar `servicios` + subtipo en una transacción; leer la imagen a `byte[]`. |
| M4 | Paquetes y promociones | `Controllers/GestorController.cs:246-256` | CRUD completo (`paquete`, `paquete_servicio`, `promociones`, `promocion_servicio`). |
| M5 | Tickets de soporte (agente) | `Controllers/SoporteController.cs:31-83` | Consultas reales, "Tomar" con control de concurrencia, acción "Cerrar". |
| M6 | Cancelar / reembolsar | `Controllers/Miusuariocontroller.cs:38-78` | Validar que la reservación sea del usuario, transición de estado + bitácora + devolver inventario + reembolso. |
| M7 | Mis compras / reservaciones / tarjetas | `Views/Miusuario/Miscompras/index.cshtml`, `Misreservaciones/index.cshtml`, `Mistarjetas/index.cshtml` | Mostrar datos del usuario autenticado (hoy, tarjetas escritas a mano, p. ej. "Grand Oasis Cancún" con `data-id="15"`). |
| M8 | Buscadores públicos | `Views/Home/Viajes.cshtml:37`, `Hoteles.cshtml:37`, `Paquetes.cshtml:36`, `Tours.cshtml:37`, `vehiculos.cshtml:37` | `<form action="#">` sin `name` en los `<input>`: no envían nada. |
| M9 | Soporte del cliente | `Views/Solicitar/index.cshtml` (solo `<h2>Soporte</h2>`), `Controllers/SolicitarController.cs` | Formulario para crear `solicitudes_soporte`. |
| M10 | Menú según sesión | `Views/Home/Index.cshtml:66-69`, `Views/Admin/_AdminLayout.cshtml:37` (`TODO (login)`) | Mostrar nombre del usuario y botón "Cerrar sesión" (POST) cuando hay sesión. |
| M11 | Página de privacidad | `Controllers/HomeController.cs:379-380` (nota), `Views/Home/Privacy.cshtml` | No existe la acción `Privacy()` → 404. |
| M12 | Panel SA | `Controllers/SAController.cs`, `Views/Admin/SA/Index.cshtml` | No están definidas sus funciones. |

---

## 4. Problemas detectados

### 4.1 Críticos (seguridad o la app no cumple su función)

| # | Problema | Dónde | Por qué importa |
|---|---|---|---|
| P1 | **Panel de administración sin `[Authorize]`** | `Controllers/AdminController.cs:16`, `GestorController.cs:19`, `SoporteController.cs:11`, `SAController.cs:11` (solo hay comentarios `TODO`) | Cualquiera que escriba `/Admin/GestorVentas` en el navegador entra al panel. En cuanto se conecte la BD, cualquiera podrá dar de baja proveedores. |
| P2 | **La cookie de sesión no tiene el rol** | `Controllers/HomeController.cs:141-147` (solo se agrega `"id_rol"` como claim personalizado) | `[Authorize(Roles = "...")]` busca `ClaimTypes.Role`. Aunque pongan el atributo, **nunca dejará pasar a nadie**. |
| P3 | **Los nombres de rol no coinciden** | BD: `CLIENTE`, `AGENTE`, `ADMINISTRADOR` (`sql/00_db_prueba_conexion.sql:117-119`). Código: `"Admin,SA,Soporte,GestorVentas"` (`AdminController.cs:16`, etc.) | Si copian los `TODO` tal cual, ningún usuario tendrá esos roles. Hay que decidir una sola lista (ver Preguntas). |
| P4 | **"Mis compras / reservaciones / tarjetas" y cancelar no exigen sesión** | `Controllers/Miusuariocontroller.cs:7` | El POST `CancelarReservacion` recibe un `id` del formulario; sin `[Authorize]` y sin validar dueño, sería un IDOR (cancelar reservaciones ajenas). |
| P5 | **53 archivos sin commit en `main`, incluidos los scripts SQL** | `git status`: `sql/01_esquema.sql`, `02_seed_demo.sql`, `03_alinear_nombres_legado.sql` y `docs/06_...` están *untracked* | Si se daña la carpeta o alguien hace `git checkout`/`pull` con conflicto, se pierde el trabajo más valioso del proyecto. |
| P6 | **La BD "Real" no coincide con el código** | `docs/CAMBIOS_YAMIL 24-10-2026.md` (sección Pendiente): la tabla real se llama `UsuariosGlobales`, el DbContext usa `usuarios` | Con `"DatabaseMode": "Real"` el login y el registro fallan. Para la entrega hay que usar una sola BD alineada. |
| P7 | **Crear un servicio sin su subtipo rompe la regla R1** | `Controllers/GestorController.ProveedoresServicios.cs:84-105` (`AltaServicio`) | Permite dar de alta un `VUELO`, `HOSPEDAJE`, `VEHICULO` o `PAQUETE` solo en `servicios`, sin fila en `vuelos`/`hospedajes`/… El catálogo público no podrá mostrarlo (no tiene ubicación, fechas, etc.). |

### 4.2 Errores que fallarán al conectar la BD

| # | Problema | Dónde | Efecto |
|---|---|---|---|
| E1 | El formulario de hospedaje usa `tipo_ubicacion`, el modelo y la tabla usan `tipo_habitacion` | `Models/Admin/GestorViewModels.cs:162` vs `Models/hospedajes.cs:20` | Confusión al mapear; el dato se perdería. |
| E2 | Campos opcionales en el formulario pero `NOT NULL` en la BD | `GestorViewModels.cs:157` (`id_ubicacion?`), `:173` (`hora_entrada?`), `:186` (`id_ubicacion_retiro?`) vs `sql/01_esquema.sql` (`hospedajes.id_ubicacion`, `hora_entrada`, `vehiculos.id_ubicacion_retiro` son `NOT NULL`) | `SqlException` al insertar si el usuario los deja vacíos. |
| E3 | Estado de ticket mal escrito | `Controllers/SoporteController.cs:35` (`'EN PROCESO'` con espacio) | El CHECK de la tabla solo acepta `EN_PROCESO` → error al tomar un ticket. |
| E4 | Nota obsoleta sobre `APROVADO` | `Controllers/Miusuariocontroller.cs:74` | El esquema actual usa `APROBADO`; si alguien sigue la nota, el CHECK fallará. |
| E5 | `Hospedajes.categoria` es `string` no anulable, pero la columna admite NULL | `Models/hospedajes.cs:24` + `FlyVibeMexDbContext.cs:206` | Al leer un hospedaje sin categoría, la propiedad queda en `null` aunque C# diga que no puede (posible `NullReferenceException` en vistas). Cambiar a `string?`. |
| E6 | `Pagos.id_tarjeta` obligatorio | `Models/pagos.cs:20`, `sql/01_esquema.sql` (`pagos.id_tarjeta NOT NULL`) | Ningún pago sin tarjeta (transferencia) es posible. Aceptable si solo habrá tarjeta; documentarlo. |

### 4.3 Problemas de diseño y malas prácticas

| # | Problema | Dónde |
|---|---|---|
| D1 | **Barra de navegación copiada y pegada** en cada vista con `Layout = null` (con la errata "FlyVideMex" y el `<img src="">` vacío repetidos) | `Views/Home/Index.cshtml:43-69`, `Views/Solicitar/index.cshtml`, `Views/Miusuario/*`, `Views/Otros/*`, `Views/Crearusuario/*` |
| D2 | **`_Layout.cshtml` y `Error.cshtml` rotos**: apuntan a `~/lib/.../dist`, `~/css/site.css`, `~/js/site.js`, que no existen | `Views/Shared/_Layout.cshtml:18-62`; `wwwroot/lib/*` solo tiene archivos `LICENSE` |
| D3 | **Validación del lado del cliente inexistente**: `_ValidationScriptsPartial` apunta a archivos que no existen y ninguna vista lo usa | `Views/Shared/_ValidationScriptsPartial.cshtml:6-7` |
| D4 | **SPA casera con `innerHTML`**: los `<script>` de las parciales no se ejecutan, no hay URL por sección ni botón "Atrás", y mezcla páginas completas con fragmentos | `wwwroot/assets/js/script.js`, `Views/Home/Index.cshtml` |
| D5 | **Acceso a datos dentro de los controladores** (sin servicios) y el `DbContext` pedido con `HttpContext.RequestServices` | `HomeController.cs:130,233`, `CrearusuarioController.cs:75` |
| D6 | **Datos de prueba dentro del controlador** | `GestorController.ProveedoresServicios.cs:315-327` |
| D7 | **Sin páginas de error para 404/403** (`UseStatusCodePages` no está configurado) y `AccessDeniedPath` manda a `/Home/Index` sin explicar nada | `Program.cs` |
| D8 | Nombres de acciones en minúsculas (`sesion`, `card`, `vehiculos`) y comentarios equivocados (`// GET /Admin` en controladores que no son Admin) | `HomeController.cs:85,180,185`; `Miusuariocontroller.cs:9`; `SolicitarController.cs:7` |
| D9 | Enlace de prueba visible en el menú: "VER TARJETA test" | `Views/Home/Index.cshtml:59` |
| D10 | Erratas visibles: "FlyVideMex", "Iniciar secion", clase `imf-fluid` (debe ser `img-fluid`) | `Views/Home/Index.cshtml:46,66,69` y vistas copiadas |
| D11 | Archivos vacíos o sin uso: `script_D/H/P/T/V.js`, `app.js`, `originalstyles.css`, `Views/Home/exIndex.cshtml`, `Views/Otros/explicacion.txt`, `docs/documento-prueba.txt`; `CONTEXTO_PROYECTO_FLYVIBEMEX.md` duplicado en la raíz y en `docs/` | `wwwroot/assets/`, `Views/`, `docs/` |
| D12 | Comentario de `_ViewImports.cshtml` desactualizado (los namespaces ya están unificados en `FlyVibeMex.Models`) | `Views/_ViewImports.cshtml:1-5` |
| D13 | Sin propiedades de navegación ni FKs configuradas en EF (solo `usuarios → roles`). No es un error, pero obliga a escribir `join` a mano en cada consulta | `Data/FlyVibeMexDbContext.cs` |

### 4.4 Revisión de seguridad (lo que pediste verificar)

| Punto | Resultado |
|---|---|
| Rutas de admin sin `[Authorize]` | ❌ **Todas** (P1). |
| `[ValidateAntiForgeryToken]` en POST | ✅ Todos los POST lo tienen (directo o con `[AutoValidateAntiforgeryToken]`). |
| Cadenas de conexión expuestas | ✅ No. Solo marcadores en `appsettings.json`; reales en *user-secrets*. |
| SQL sin parametrizar | ✅ No se encontró. Todo es LINQ. |
| Contraseñas | ✅ Hash PBKDF2 (`PasswordHasher`), nunca texto plano; mensaje genérico de login. |
| Datos de tarjeta | ✅ El modelo solo guarda token + últimos 4 (regla R5). Mantenerlo así en "Mis tarjetas". |
| *Overposting* | ✅ ViewModels con `[Bind]`; el `id` sale de la URL. |
| IDOR (recursos de otro usuario) | ⚠️ Pendiente: cancelar/reembolsar no verifican dueño (P4). |
| Subida de archivos | ✅ Tamaño y tipo real validados. Falta definir cómo se sirven las imágenes. |
| `AllowedHosts: "*"` | ⚠️ Aceptable en desarrollo; restringir si se publica. |

---

# PARTE 2 — Plan de trabajo detallado

Cada tarea tiene un identificador (**T1.1**, **T2.3**…) que se repite en la checklist final.

> **Decisión sobre migraciones (`dotnet ef`)**: el proyecto **no tiene carpeta `Migrations/`**; trabaja en modo *database-first* con scripts (`sql/00` → `01` → `02`). Los scripts tienen CHECKs, índices filtrados y candados de seguridad que EF no generaría igual. **Recomendación para esta entrega: mantener los scripts SQL como fuente de verdad** y no mezclar con migraciones. Cada cambio de esquema = un script nuevo `sql/04_...sql` + ajuste del modelo C# + verificar con `/Home/TestDb`. Si el profesor exige migraciones, ver la tarea opcional **T2.6**.

---

## Sección 1 · Correcciones críticas (errores y seguridad)

### T1.1 · Guardar en Git todo el trabajo pendiente
- **Qué / por qué:** hay 53 cambios sin commit y los scripts SQL sin versionar (P5). Es el riesgo más grande del proyecto.
- **Dónde:** raíz del repo `Proyecto/FlyVibeMex/`.
- **Cómo:**
  1. `git checkout -b revision-26oct`
  2. `git add -A` (revisen antes con `git status` que no haya secretos ni `bin/`/`obj/`).
  3. `git commit -m "Scripts SQL 01-03, login/registro, CRUD gestor (sin BD), vistas de usuario"`
  4. `git push -u origin revision-26oct` y abrir PR a `main`.
- **Prioridad:** Crítica · **Dificultad:** Fácil
- **Verificar:** en GitHub aparecen `sql/01_esquema.sql`, `sql/02_seed_demo.sql` y `sql/03_alinear_nombres_legado.sql`; `git status` queda limpio.

### T1.2 · Compilar y registrar errores reales
- **Qué / por qué:** esta revisión no pudo compilar. Hay que confirmar que `main` compila antes de repartir tareas.
- **Cómo:** `dotnet build` desde la carpeta del `.csproj`. Corregir cualquier error y tratar de dejar en 0 las advertencias de nulabilidad (CS86xx) en los archivos que toquen.
- **Prioridad:** Crítica · **Dificultad:** Fácil
- **Verificar:** `Compilación correcta. 0 Errores`.

### T1.3 · Elegir UNA base de datos para la entrega y alinearla
- **Qué / por qué:** la BD "Real" no coincide con el código (P6).
- **Cómo:**
  1. Decidir (ver Preguntas): **opción A (recomendada)** usar `FlyVibeMex_Test` como BD de demostración (scripts `00` → `01` → `02`); **opción B** ejecutar `sql/03_alinear_nombres_legado.sql` sobre la BD real.
  2. Cada integrante: `dotnet user-secrets set "ConnectionStrings:FlyVibeMex_Test" "Server=TU_SERVIDOR;Database=FlyVibeMex_Test;Trusted_Connection=True;TrustServerCertificate=True"`
  3. Dejar `"DatabaseMode"` con el modo elegido en `appsettings.json`.
- **Prioridad:** Crítica · **Dificultad:** Fácil
- **Verificar:** `/Home/TestDb` responde `"conectado": true` y `"esquemaCompleto": true`.

### T1.4 · Agregar el rol a la cookie de sesión
- **Qué / por qué:** sin `ClaimTypes.Role`, ningún `[Authorize(Roles=...)]` funciona (P2).
- **Dónde:** `Models/Auth/UsuarioSesion.cs`, `Services/AuthService.cs`, `Controllers/HomeController.cs`, nuevo `Models/Auth/Roles.cs`.
- **Cómo:**
  1. Crear las constantes de roles (un solo lugar, sin "strings mágicos"):
     ```csharp
     // Models/Auth/Roles.cs
     namespace FlyVibeMex.Models;

     public static class Roles
     {
         public const string Cliente = "CLIENTE";
         public const string Agente = "AGENTE";
         public const string Administrador = "ADMINISTRADOR";

         // Para [Authorize(Roles = ...)] se separan con coma.
         public const string PanelInterno = Agente + "," + Administrador;
     }
     ```
  2. Agregar el nombre del rol al DTO de sesión:
     ```csharp
     // Models/Auth/UsuarioSesion.cs (línea 14)
     public sealed record UsuarioSesion(ulong id, string nombre, string correo, ulong id_rol, string rol);
     ```
  3. En `AuthService.ValidarCredencialesAsync` (líneas 89-100) traer el rol con un `join`:
     ```csharp
     var candidato = await (
             from u in _db.Usuarios.AsNoTracking()
             join r in _db.RolesUsuarios.AsNoTracking() on u.id_rol equals r.id
             where u.correo == correoNormalizado && u.activo && r.activo
             select new { u.id, u.nombre, u.correo, u.id_rol, rol = r.nombre, u.password_hash })
         .FirstOrDefaultAsync(cancellationToken);
     // ...
     return ResultadoAutenticacion.Correcto(
         new UsuarioSesion(candidato.id, candidato.nombre, candidato.correo, candidato.id_rol, candidato.rol));
     ```
  4. En `HomeController.IniciarSesion` (líneas 141-147) agregar el claim y redirigir según el rol:
     ```csharp
     new(ClaimTypes.Role, usuario.rol),
     // ...
     if (usuario.rol is Roles.Agente or Roles.Administrador)
         return RedirectToAction("Index", "Admin");
     return RedirectToAction("Index", "Miusuario");
     ```
- **Prioridad:** Crítica · **Dificultad:** Fácil
- **Verificar:** iniciar sesión con `admin.prueba@flyvibemex.test` (contraseña de prueba del seed) → llega a `/Admin`; con `cliente.prueba@...` → llega a `/Miusuario`.

### T1.5 · Proteger todas las rutas privadas con `[Authorize]`
- **Qué / por qué:** P1 y P4.
- **Dónde:** `AdminController.cs`, `GestorController.cs`, `SoporteController.cs`, `SAController.cs`, `Miusuariocontroller.cs`, `SolicitarController.cs`, `Program.cs`.
- **Cómo:**
  1. Reemplazar cada `// TODO (cuando exista el login)` por el atributo real (agregar `using Microsoft.AspNetCore.Authorization;`):
     ```csharp
     [Authorize(Roles = Roles.PanelInterno)]  public class AdminController ...
     [Authorize(Roles = Roles.Administrador)] public partial class GestorController ...   // solo en GestorController.cs
     [Authorize(Roles = Roles.PanelInterno)]  public class SoporteController ...
     [Authorize(Roles = Roles.Administrador)] public class SAController ...
     [Authorize]                              public class MiusuarioController ...
     [Authorize]                              public class SolicitarController ...
     ```
  2. En `Program.cs`, cambiar `AccessDeniedPath` a una página que explique el error:
     ```csharp
     opciones.AccessDeniedPath = "/Home/AccesoDenegado";
     ```
     y agregar en `HomeController` `public IActionResult AccesoDenegado() => View();` con su vista.
  3. (Recomendado) Hacer que **todo** requiera sesión salvo lo marcado con `[AllowAnonymous]`, para no olvidar ningún controlador nuevo:
     ```csharp
     builder.Services.AddAuthorization(o =>
         o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
     ```
     Si lo hacen, marquen con `[AllowAnonymous]` a `HomeController`, `CrearusuarioController` y `OtrosController`.
- **Prioridad:** Crítica · **Dificultad:** Fácil
- **Verificar:** en ventana de incógnito abrir `/Admin/GestorVentas` → redirige a `/Home/IniciarSesion?ReturnUrl=...`; con un cliente → "Acceso denegado"; con el admin → entra.

### T1.6 · Validar el dueño en cancelar / reembolsar
- **Qué / por qué:** evitar que un usuario cancele reservaciones ajenas cambiando el `id` (P4).
- **Dónde:** `Controllers/Miusuariocontroller.cs:38-78` (la lógica completa va en T3.5).
- **Cómo:** obtener el usuario de la cookie y filtrar siempre por él:
  ```csharp
  var idUsuario = ulong.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
  var reservacion = await _db.Reservaciones
      .FirstOrDefaultAsync(r => r.id == id && r.id_usuarioglobal == idUsuario);
  if (reservacion is null) return NotFound();   // no se dice si existe o no
  ```
- **Prioridad:** Crítica · **Dificultad:** Fácil
- **Verificar:** con el usuario A, enviar (con las herramientas del navegador) el `id` de una reservación de B → 404.

### T1.7 · Corregir errores que fallarán con la BD (E1–E5)
- **Dónde y cómo:**
  1. `Models/Admin/GestorViewModels.cs:162`: renombrar `tipo_ubicacion` → `tipo_habitacion` (y en `Views/Admin/GestorVentas/_FormHospedaje.cshtml`).
  2. `GestorViewModels.cs:157,173,176,186`: agregar `[Required(ErrorMessage = "...")]` a `id_ubicacion`, `hora_entrada`, `hora_salida` y `id_ubicacion_retiro`.
  3. `Controllers/SoporteController.cs:35`: `'EN PROCESO'` → `'EN_PROCESO'`.
  4. `Controllers/Miusuariocontroller.cs:74`: borrar la nota de `APROVADO`.
  5. `Models/hospedajes.cs:24`: `public string? categoria { get; set; }`.
- **Prioridad:** Crítica · **Dificultad:** Fácil
- **Verificar:** `dotnet build` sin errores; dar de alta un hospedaje sin ubicación → mensaje de validación (no error 500).

---

## Sección 2 · Base de datos y modelos

### T2.1 · Mantener scripts y modelos sincronizados (proceso)
- **Qué / por qué:** el proyecto es *database-first*; si alguien cambia una columna solo en SQL o solo en C#, EF falla en tiempo de ejecución.
- **Cómo (regla del equipo):**
  1. Cambio de esquema → nuevo archivo `sql/04_<descripcion>.sql` con `ALTER TABLE` (nunca editar a mano la BD de otro).
  2. Ajustar la clase en `Models/` y, si hace falta, `Data/FlyVibeMexDbContext.cs`.
  3. Ejecutar el script y abrir `/Home/TestDb`.
- **Prioridad:** Alta · **Dificultad:** Fácil
- **Verificar:** `/Home/TestDb` → todas las tablas `"ok": true`.

### T2.2 · Agregar las relaciones (FK) al `DbContext`
- **Qué / por qué:** hoy solo `usuarios → roles` está configurada. Declarar las demás ayuda a EF a ordenar los INSERT y deja el modelo documentado. No requiere cambiar los modelos.
- **Dónde:** `Data/FlyVibeMexDbContext.cs`, dentro de cada `modelBuilder.Entity<...>`.
- **Cómo (ejemplos):**
  ```csharp
  modelBuilder.Entity<Servicios>(e =>
  {
      // ... lo que ya existe ...
      e.HasOne<Proveedores>().WithMany().HasForeignKey(x => x.id_proveedor).OnDelete(DeleteBehavior.Restrict);
  });

  modelBuilder.Entity<Hospedajes>(e =>
  {
      // ... lo que ya existe ...
      e.HasOne<Servicios>().WithOne().HasForeignKey<Hospedajes>(x => x.id_servicio);
      e.HasOne<Ubicaciones>().WithMany().HasForeignKey(x => x.id_ubicacion).OnDelete(DeleteBehavior.Restrict);
  });

  modelBuilder.Entity<DetallesReservacion>(e =>
  {
      // ... lo que ya existe ...
      e.HasOne<Reservaciones>().WithMany().HasForeignKey(x => x.id_reservacion);
      e.HasOne<Servicios>().WithMany().HasForeignKey(x => x.id_servicio).OnDelete(DeleteBehavior.Restrict);
  });
  ```
  Repetir el patrón para `vuelos`, `vehiculos`, `paquete`, `disponibilidad_servicio`, `reservaciones`, `pagos`, `reembolsos`, `solicitudes_soporte` y los historiales.
- **Prioridad:** Media · **Dificultad:** Media
- **Verificar:** `/Home/TestDb` sigue en verde (las FK de EF no cambian la BD porque no hay migraciones).

### T2.3 · DataAnnotations en los modelos de entidad (opcional pero útil)
- **Qué / por qué:** las validaciones ya están en los ViewModels (correcto). En las entidades basta con documentar longitudes para que EF no mande parámetros `nvarchar(max)`.
- **Cómo:** preferir Fluent API (`HasMaxLength`) en el `DbContext` para las columnas de texto más consultadas (`servicios.nombre`, `proveedores.nombre`, `reservaciones.codigo_reserva`).
- **Prioridad:** Baja · **Dificultad:** Fácil

### T2.4 · Seed mínimo para la demostración
- **Qué / por qué:** `sql/02_seed_demo.sql` ya trae ubicaciones, proveedores, servicios con subtipos, promociones, reservaciones, pagos, reembolso y tickets. El inventario se genera **desde hoy hasta +13 días** (`sql/02_seed_demo.sql:207-229`, variable `@hoy`) y los vuelos solo en +3 y +7 días. Si el seed se ejecuta más de 2 semanas antes de exponer, las búsquedas saldrán vacías.
- **Dónde:** `sql/02_seed_demo.sql:215-216` (tabla `@dias`) y `:230-232` (vuelos).
- **Cómo:** ampliar `@dias` a 30 valores (0 a 29) para tener margen, y volver a ejecutar `00 → 01 → 02` el día anterior a la entrega.
- **Prioridad:** Alta · **Dificultad:** Fácil
- **Verificar:** `SELECT MIN(fecha), MAX(fecha) FROM disponibilidad_servicio;` incluye la fecha de la presentación.

### T2.5 · Imágenes de servicios
- **Qué / por qué:** las imágenes se guardan como `VARBINARY(MAX)` pero no hay forma de mostrarlas.
- **Dónde:** nuevo `Controllers/CatalogoController.cs` (ver T3.1).
- **Cómo:**
  ```csharp
  [AllowAnonymous]
  [HttpGet("Catalogo/Imagen/{id}")]
  [ResponseCache(Duration = 3600)]
  public async Task<IActionResult> Imagen(ulong id)
  {
      var bytes = await _db.Hospedajes.Where(h => h.id_servicio == id).Select(h => h.imagen).FirstOrDefaultAsync()
               ?? await _db.Vehiculos.Where(v => v.id_servicio == id).Select(v => v.imagen).FirstOrDefaultAsync()
               ?? await _db.Paquetes.Where(p => p.id_servicio == id).Select(p => p.imagen).FirstOrDefaultAsync();
      if (bytes is null) return Redirect("~/assets/img/imagen-prueba.jpg");
      var tipo = bytes[0] == 0x89 ? "image/png" : bytes[0] == (byte)'R' ? "image/webp" : "image/jpeg";
      return File(bytes, tipo);
  }
  ```
  En las vistas: `<img src="/Catalogo/Imagen/@s.id" alt="@s.nombre">`.
- **Prioridad:** Media · **Dificultad:** Fácil
- **Verificar:** subir una imagen desde "Asociar hospedaje" y verla en el catálogo.

### T2.6 · (Opcional) Usar migraciones de EF a partir de la BD actual
Solo si el profesor lo exige. **No mezclar** con los scripts después de hacerlo.
```bash
dotnet tool install --global dotnet-ef          # una vez por equipo
dotnet ef dbcontext info                         # comprueba que EF encuentra el DbContext
dotnet ef migrations add Inicial                 # genera Migrations/xxxx_Inicial.cs
```
Como la BD ya existe, vacíen los métodos `Up()` y `Down()` de esa primera migración (queda como "línea base") y ejecuten:
```bash
dotnet ef database update                        # registra la línea base en __EFMigrationsHistory
```
A partir de ahí, cada cambio: modificar el modelo → `dotnet ef migrations add <Nombre>` → `dotnet ef database update`.
- **Prioridad:** Baja · **Dificultad:** Media

---

## Sección 3 · Funcionalidades faltantes del sitio público

> Orden sugerido: primero **un solo tipo de servicio (Hospedaje)** de punta a punta (T3.1 → T3.4), después replicar a vuelos, vehículos y paquetes.

### T3.1 · Catálogo con datos reales (búsqueda y resultados)
- **Qué / por qué:** los buscadores no envían nada (M8) y las tarjetas están escritas a mano.
- **Dónde:** nuevos `Services/ICatalogoService.cs`, `Services/CatalogoService.cs`, `Controllers/CatalogoController.cs`, `Models/Catalogo/BusquedaViewModel.cs`, `Views/Catalogo/Resultados.cshtml`; registrar en `Program.cs`.
- **Cómo:**
  1. ViewModel de búsqueda:
     ```csharp
     public class BusquedaViewModel
     {
         [Required] [RegularExpression("^(VUELO|HOSPEDAJE|VEHICULO|PAQUETE)$")]
         public string tipo { get; set; } = "HOSPEDAJE";
         public ulong? id_destino { get; set; }
         [Required, DataType(DataType.Date)] public DateOnly? desde { get; set; }
         [Required, DataType(DataType.Date)] public DateOnly? hasta { get; set; }
         [Range(1, 20)] public int personas { get; set; } = 1;
     }

     public record ResultadoServicio(ulong id, string nombre, string? descripcion, string ciudad, decimal precio_desde, string moneda);
     ```
  2. Servicio (ejemplo para hospedaje: solo servicios activos con cupo **todas** las noches):
     ```csharp
     public async Task<List<ResultadoServicio>> BuscarHospedajesAsync(BusquedaViewModel b, CancellationToken ct)
     {
         var desde = b.desde!.Value; var hasta = b.hasta!.Value;
         int noches = hasta.DayNumber - desde.DayNumber;

         var q = from s in _db.Servicios.AsNoTracking()
                 join h in _db.Hospedajes on s.id equals h.id_servicio
                 join u in _db.Ubicaciones on h.id_ubicacion equals u.id
                 where s.activo && s.tipo_servicio == "HOSPEDAJE" && h.capacidad_huespedes >= b.personas
                       && (b.id_destino == null || h.id_ubicacion == b.id_destino)
                 let dias = _db.DisponibilidadServicios.Where(d => d.id_servicio == s.id
                                 && d.fecha >= desde && d.fecha < hasta && d.cantidad_disponible > 0)
                 where dias.Count() == noches
                 select new ResultadoServicio(s.id, s.nombre, s.descripcion, u.municipio_ciudad,
                                              dias.Min(d => d.precio_fecha), s.moneda);
         return await q.OrderBy(r => r.precio_desde).Take(50).ToListAsync(ct);
     }
     ```
  3. Controlador:
     ```csharp
     [AllowAnonymous]
     public class CatalogoController : Controller
     {
         private readonly ICatalogoService _catalogo;
         public CatalogoController(ICatalogoService catalogo) => _catalogo = catalogo;

         [HttpGet]
         public async Task<IActionResult> Buscar(BusquedaViewModel b, CancellationToken ct)
         {
             if (b.desde >= b.hasta) ModelState.AddModelError(nameof(b.hasta), "La fecha final debe ser posterior.");
             if (b.desde < DateOnly.FromDateTime(DateTime.Today)) ModelState.AddModelError(nameof(b.desde), "No se puede buscar en el pasado.");
             if (!ModelState.IsValid) return View("Resultados", (b, new List<ResultadoServicio>()));
             return View("Resultados", (b, await _catalogo.BuscarHospedajesAsync(b, ct)));
         }
     }
     ```
  4. En `Views/Home/Hoteles.cshtml:37` cambiar el formulario:
     ```html
     <form asp-controller="Catalogo" asp-action="Buscar" method="get">
         <input type="hidden" name="tipo" value="HOSPEDAJE" />
         <select name="id_destino" asp-items="ViewBag.Destinos"></select>
         <input type="date" name="desde" required />
         <input type="date" name="hasta" required />
         <input type="number" name="personas" min="1" max="20" value="1" />
         <button type="submit">Buscar</button>
     </form>
     ```
  5. Registrar: `builder.Services.AddScoped<ICatalogoService, CatalogoService>();`
- **Prioridad:** Alta · **Dificultad:** Media
- **Verificar:** buscar "Cancún" para mañana-pasado → aparecen los hospedajes del seed; poner un rango sin inventario → "No hay resultados"; fecha final antes de la inicial → mensaje de error.

### T3.2 · Detalle del servicio
- **Dónde:** `CatalogoController.Detalle(ulong id, DateOnly desde, DateOnly hasta, int personas)` + `Views/Catalogo/Detalle.cshtml`.
- **Cómo:** mostrar datos del servicio + subtipo + precio por noche (de `disponibilidad_servicio`) + **política de cancelación visible antes de pagar** (regla de UX del proyecto). Botón "Reservar" que lleva al resumen (requiere sesión: `[Authorize]` en esa acción).
- **Prioridad:** Alta · **Dificultad:** Media
- **Verificar:** sin sesión, "Reservar" manda al login y regresa al detalle después (`ReturnUrl`).

### T3.3 · Resumen, promoción y creación de la reservación (transacción)
- **Qué / por qué:** es la regla de negocio central (R3 inventario, R4 snapshot, R6 bitácora).
- **Dónde:** nuevos `Services/IReservacionService.cs`, `Services/ReservacionService.cs`, `Controllers/ReservasController.cs`, `Views/Reservas/Resumen.cshtml`, `Views/Reservas/Confirmacion.cshtml`.
- **Cómo:**
  1. El **precio siempre se calcula en el servidor**; el formulario solo manda `id_servicio`, fechas, cantidad y código de promoción.
  2. Núcleo del servicio (fíjense en la *execution strategy*, obligatoria porque `Program.cs:39` activa reintentos):
     ```csharp
     public async Task<string> CrearAsync(ulong idUsuario, ulong idServicio, DateOnly desde, DateOnly hasta,
                                          int cantidad, string? codigoPromo, CancellationToken ct)
     {
         var estrategia = _db.Database.CreateExecutionStrategy();
         return await estrategia.ExecuteAsync(async () =>
         {
             _db.ChangeTracker.Clear();                       // por si es un reintento
             await using var tx = await _db.Database.BeginTransactionAsync(ct);

             int noches = hasta.DayNumber - desde.DayNumber;
             var servicio = await _db.Servicios.FirstAsync(s => s.id == idServicio && s.activo, ct);
             var precios = await _db.DisponibilidadServicios
                 .Where(d => d.id_servicio == idServicio && d.fecha >= desde && d.fecha < hasta)
                 .Select(d => d.precio_fecha).ToListAsync(ct);
             if (precios.Count != noches) throw new ReservaException("No hay inventario para esas fechas.");

             // R3: descontar cupo SOLO si alcanza (evita sobreventa aunque dos personas reserven a la vez).
             int filas = await _db.DisponibilidadServicios
                 .Where(d => d.id_servicio == idServicio && d.fecha >= desde && d.fecha < hasta
                             && d.cantidad_disponible >= cantidad)
                 .ExecuteUpdateAsync(s => s
                     .SetProperty(d => d.cantidad_disponible, d => d.cantidad_disponible - cantidad)
                     .SetProperty(d => d.actualizado_en, DateTime.Now), ct);
             if (filas != noches) throw new ReservaException("Se agotó la disponibilidad.");   // rollback automático

             decimal precioUnitario = precios.Sum();          // precio total de la estancia por unidad
             decimal subtotal = precioUnitario * cantidad;
             var (promo, descuento) = await CalcularDescuentoAsync(codigoPromo, idServicio, subtotal, ct);

             var pendiente = await _db.EstadosReservacion.Where(e => e.nombre == "PENDIENTE").Select(e => e.id).FirstAsync(ct);
             var reservacion = new Reservaciones
             {
                 id_usuarioglobal = idUsuario,
                 id_estado_reservacion = pendiente,
                 codigo_reserva = $"FV-{DateTime.Now:yyMMdd}-{Random.Shared.Next(1000, 9999)}",
                 moneda = servicio.moneda,
                 subtotal = subtotal,
                 descuento_total = descuento,
                 total = subtotal - descuento
             };
             _db.Reservaciones.Add(reservacion);
             await _db.SaveChangesAsync(ct);                  // genera reservacion.id

             _db.DetallesReservacion.Add(new DetallesReservacion       // R4: snapshot
             {
                 id_reservacion = reservacion.id, id_servicio = idServicio, id_promocion = promo?.id,
                 cantidad = cantidad, fecha_inicio = desde.ToDateTime(TimeOnly.MinValue),
                 fecha_fin = hasta.ToDateTime(TimeOnly.MinValue), precio_unitario = precioUnitario,
                 descuento_aplicado = descuento, subtotal = subtotal,
                 descripcion_snapshot = $"{servicio.nombre} · {desde:dd/MM/yyyy}-{hasta:dd/MM/yyyy}"
             });
             _db.HistorialEstadosReservacion.Add(new HistorialEstadoReservacion   // R6: bitácora
             {
                 id_reservacion = reservacion.id, id_estado_reservacion = pendiente,
                 id_usuario_cambio = idUsuario, motivo = "Reservación creada"
             });
             await _db.SaveChangesAsync(ct);
             await tx.CommitAsync(ct);
             return reservacion.codigo_reserva;
         });
     }
     ```
     `ReservaException` es una clase propia (`public class ReservaException(string m) : Exception(m);`) que el controlador captura para mostrar el mensaje en `ModelState`.
  3. `CalcularDescuentoAsync`: promoción `activo`, vigente (`fecha_inicio <= ahora <= fecha_fin`), ligada al servicio en `promocion_servicio`, `subtotal >= minimo_compra`; `PORCENTAJE` → `subtotal * valor / 100`, `MONTO` → `valor`; nunca mayor que el subtotal.
  4. Si `codigo_reserva` choca con el UNIQUE (muy raro), reintentar con otro.
- **Prioridad:** Alta · **Dificultad:** Difícil
- **Verificar:**
  - Reservar 2 noches → en SSMS `cantidad_disponible` bajó en esas 2 fechas, existe la fila en `reservaciones`, `detalle_reservacion` y `historial_estado_reservacion`.
  - Cambiar el precio del servicio en el panel → el detalle de la reservación **no cambia** (snapshot).
  - Dejar `cantidad_disponible = 1` y reservar 2 → mensaje "Se agotó la disponibilidad" y nada se guardó.

### T3.4 · Pago simulado y confirmación
- **Qué / por qué:** no hay pasarela real; se simula **sin guardar números de tarjeta** (R5).
- **Dónde:** `ReservasController.Pagar` (POST) + `Views/Reservas/Pago.cshtml`.
- **Cómo:**
  1. El usuario elige una de **sus** tarjetas guardadas (`tarjetas_usuarios` con `id_usuariog` = usuario) — nunca escribe el número aquí.
  2. En una transacción: `INSERT pagos` (estado `APROBADO`, `referencia_externa = "SIM-" + Guid`), `INSERT historial_estado_pago`, `UPDATE reservaciones` a `CONFIRMADA` + `INSERT historial_estado_reservacion`.
  3. Redirigir a `Confirmacion?codigo=FV-...` mostrando el código de reserva.
- **Prioridad:** Alta · **Dificultad:** Media
- **Verificar:** después de pagar, la reservación aparece como CONFIRMADA en "Mis reservaciones"; recargar la página de pago no crea un segundo pago (validar que el estado siga en PENDIENTE antes de cobrar).

### T3.5 · "Mis reservaciones", "Mis compras", cancelar y reembolsar con datos reales
- **Dónde:** `Controllers/Miusuariocontroller.cs`, `Views/Miusuario/Misreservaciones/index.cshtml`, `Miscompras/index.cshtml`, `_ModalesCancelacion.cshtml`; nuevo `Models/Usuario/MisReservacionesViewModel.cs`.
- **Cómo:**
  1. Inyectar `FlyVibeMexDbContext` (o un `IMisViajesService`) en el constructor.
  2. Listar con `where r.id_usuarioglobal == idUsuario` uniendo `detalle_reservacion` y `estados_reservacion`; separar **actuales** (`fecha_fin >= hoy`) y **pasados**.
  3. En las vistas, reemplazar las tarjetas fijas por `@foreach (var r in Model.Actuales)` y usar `data-id="@r.id"`.
  4. `CancelarReservacion`: T1.6 (dueño) → si el estado es final, error → `UPDATE` a `CANCELADA` + historial → **devolver cupo** con `ExecuteUpdateAsync(... cantidad_disponible + cantidad)` por cada fecha del detalle. Todo en una transacción con la *execution strategy*.
  5. `SolicitarReembolso`: además, buscar el pago `APROBADO` de esa reservación (R8) → `INSERT reembolsos` (`SOLICITADO`) + `historial_estado_reembolso`.
- **Prioridad:** Alta · **Dificultad:** Media
- **Verificar:** con `ana.lopez@...` se ven FV-260901 (confirmada) y FV-260903 (cancelada); cancelar una reservación devuelve el cupo en `disponibilidad_servicio`.

### T3.6 · "Mis tarjetas" (tokenizadas)
- **Dónde:** `Miusuariocontroller.Mistarjetas` + `Views/Miusuario/Mistarjetas/index.cshtml`.
- **Cómo:** listar `marca`, `**** ultimos4`, vencimiento y alias. Para "agregar", **simular el gateway**: el formulario pide número, mes y año; en el servidor solo se guardan `ultimos4 = numero[^4..]`, `marca` (por el primer dígito: 4 = VISA, 5 = MASTERCARD, 3 = AMEX) y `token_referencia = "tok_sim_" + Guid.NewGuid()`. **No crear campo CVV ni guardar el número completo en ningún lado** (tampoco en logs ni en `TempData`). Eliminar = baja lógica (`activo = 0`).
- **Prioridad:** Media · **Dificultad:** Media
- **Verificar:** en SSMS, `SELECT * FROM tarjetas_usuarios` no contiene ningún número de 16 dígitos.

### T3.7 · Soporte del cliente
- **Dónde:** `Controllers/SolicitarController.cs`, `Views/Solicitar/index.cshtml`, nuevo `Models/Soporte/NuevaSolicitudViewModel.cs`.
- **Cómo:** formulario POST (`tipo` ASESORIA/PROBLEMA, `asunto` ≤ 120, `mensaje` ≤ 2000, `id_reservacion` opcional elegido de **sus** reservaciones) → `INSERT solicitudes_soporte` con `estado = 'ABIERTA'` e `id_usuarioglobal` tomado de la cookie. Debajo, la lista de sus tickets y su estado.
- **Prioridad:** Media · **Dificultad:** Fácil
- **Verificar:** el ticket creado aparece en `/Admin/Soporte/NuevoTicket` para el agente.

### T3.8 · Decidir qué es "Tours"
- `Views/Home/Tours.cshtml` no tiene tipo en la BD. El esquema sí acepta `EVENTO` (`sql/01_esquema.sql`, CHECK de `servicios.tipo_servicio`) y el panel ya puede crear eventos. **Propuesta:** Tours = servicios `EVENTO`. Ver Preguntas.
- **Prioridad:** Media · **Dificultad:** Fácil

---

## Sección 4 · Sección de administración (CRUDs, roles, protección)

### T4.1 · Conectar el Gestor de ventas a la BD (proveedores y servicios)
- **Qué / por qué:** M1; es lo que están desarrollando ahora y ya tiene toda la UI.
- **Dónde:** `Controllers/GestorController.cs`, `Controllers/GestorController.ProveedoresServicios.cs`.
- **Cómo:**
  1. Constructor (**solo en uno** de los dos archivos `partial`):
     ```csharp
     private readonly FlyVibeMexDbContext _db;
     public GestorController(FlyVibeMexDbContext db) => _db = db;
     ```
  2. `ModoSinBD = false` (`GestorController.cs:28`) y después borrar `ProveedoresDePrueba()`, `ServiciosDePrueba()` y los `ObtenerXOPrueba` (`ProveedoresServicios.cs:242-261, 313-327`; `GestorController.cs:315-332`).
  3. Reemplazar cada método `TODO BD` por su consulta (el código ya está escrito en los comentarios). Ejemplo:
     ```csharp
     private List<Ubicaciones> ObtenerUbicaciones() =>
         _db.Ubicaciones.AsNoTracking().Where(u => u.activo).OrderBy(u => u.municipio_ciudad).ToList();

     private Proveedores? ObtenerProveedor(ulong id) =>
         _db.Proveedores.AsNoTracking().FirstOrDefault(p => p.id == id);
     ```
  4. Alta de proveedor (`GestorController.cs:65`):
     ```csharp
     _db.Proveedores.Add(new Proveedores
     {
         id_ubicacion = datos.id_ubicacion, tipo_proveedor = datos.tipo_proveedor,
         nombre = datos.nombre.Trim(), telefono = datos.telefono.Trim(),
         correo = datos.correo.Trim().ToLowerInvariant(), activo = true
     });
     _db.SaveChanges();
     TempData["Exito"] = $"Se registró \"{datos.nombre}\".";
     ```
  5. Baja / reactivación lógica (nunca `DELETE`):
     ```csharp
     _db.Proveedores.Where(p => p.id == id)
        .ExecuteUpdate(s => s.SetProperty(p => p.activo, false).SetProperty(p => p.actualizado_en, DateTime.Now));
     ```
  6. Actualizar: cargar con `Find(id)`, copiar los campos permitidos del ViewModel, `actualizado_en = DateTime.Now`, `SaveChanges()`.
  7. Cambiar los mensajes "se guardará cuando se conecte la base de datos" por mensajes reales.
- **Prioridad:** Alta · **Dificultad:** Media
- **Verificar:** dar de alta, editar, dar de baja y reactivar un proveedor; revisar cada cambio en SSMS (`SELECT * FROM proveedores`).

### T4.2 · Alta de servicios con su subtipo (arreglar P7)
- **Dónde:** `ProveedoresServicios.cs:76-105` y `GestorController.cs:187-241`.
- **Cómo:**
  1. En "Alta de servicio" genérica, si el tipo es `HOSPEDAJE` o `VEHICULO` redirigir a `Proveedores/{id}/Asociar?tipo=...` (que sí pide los datos del subtipo). Para `VUELO` y `PAQUETE` crear formularios propios (T4.3, T4.4).
  2. Implementar `AsociarHospedaje` / `AsociarVehiculo` en transacción:
     ```csharp
     var estrategia = _db.Database.CreateExecutionStrategy();
     await estrategia.ExecuteAsync(async () =>
     {
         _db.ChangeTracker.Clear();
         await using var tx = await _db.Database.BeginTransactionAsync();
         var servicio = new Servicios
         {
             id_proveedor = id, tipo_servicio = "HOSPEDAJE", nombre = datos.nombre.Trim(),
             descripcion = datos.descripcion, precio_base = datos.precio_base, moneda = datos.moneda, activo = true
         };
         _db.Servicios.Add(servicio);
         await _db.SaveChangesAsync();                       // SQL Server genera servicio.id
         _db.Hospedajes.Add(new Hospedajes
         {
             id_servicio = servicio.id, id_ubicacion = datos.id_ubicacion!.Value,
             tipo_habitacion = datos.tipo_habitacion, categoria = datos.categoria,
             capacidad_huespedes = datos.capacidad_huespedes,
             hora_entrada = datos.hora_entrada!.Value, hora_salida = datos.hora_salida!.Value,
             imagen = await LeerImagenAsync(datos.imagen)
         });
         await _db.SaveChangesAsync();
         await tx.CommitAsync();
     });
     ```
     (convertir la acción a `async Task<IActionResult>`.) Ayudante:
     ```csharp
     private static async Task<byte[]?> LeerImagenAsync(IFormFile? archivo)
     {
         if (archivo is null || archivo.Length == 0) return null;
         using var ms = new MemoryStream();
         await archivo.CopyToAsync(ms);
         return ms.ToArray();
     }
     ```
  3. `AsociarEvento`: `INSERT servicios` (tipo `EVENTO`) + `INSERT disponibilidad_servicio` para la fecha del evento.
- **Prioridad:** Alta · **Dificultad:** Media
- **Verificar:** después de asociar un hospedaje existe una fila en `servicios` **y** otra en `hospedajes` con el mismo id; si falla el segundo INSERT, no queda la primera (probar con una ubicación inexistente).

### T4.3 · Alta de vuelos
- **Dónde:** nueva vista `Views/Admin/GestorVentas/_FormVuelo.cshtml`, `VueloFormViewModel` en `Models/Admin/GestorViewModels.cs`, acción `AsociarVuelo`; agregar `"vuelo"` a `AsociarViewModel.TiposValidos`.
- **Cómo:** campos `id_origen`, `id_destino` (distintos), `codigo_vuelo`, `fecha_salida`, `fecha_llegada` (posterior), `clase` (TURISTA/PREMIUM/PRIMERA), `cantidad_total` asientos. Insertar `servicios` + `vuelos` + **una fila de `disponibilidad_servicio`** para la fecha de salida.
- **Prioridad:** Alta · **Dificultad:** Media
- **Verificar:** el vuelo aparece al buscar en "Viajes" para esa fecha.

### T4.4 · CRUD de paquetes
- **Dónde:** `GestorController.cs:246-250` (`CrearPaquete`, `GestionarPaquetes`), nuevas vistas `CrearPaquete.cshtml`, `Paquetes.cshtml`, `PaqueteFormViewModel`.
- **Cómo:** formulario con datos de `servicios` (tipo `PAQUETE`) + `paquete` (`duracion_dias`, `min_personas`, `max_personas >= min`, `politica_cancelacion`) + lista de servicios incluidos con checkboxes y cantidad → `paquete_servicio`. Validar que un paquete no se incluya a sí mismo. Baja lógica en `servicios.activo`.
- **Prioridad:** Media · **Dificultad:** Difícil
- **Verificar:** crear un paquete con 1 vuelo + 1 hospedaje y verlo con sus componentes.

### T4.5 · CRUD de promociones
- **Dónde:** `GestorController.cs:252-256`, vistas `Promociones.cshtml`, `PromocionForm.cshtml`, `PromocionFormViewModel`.
- **Cómo:** `codigo` único (validar antes de insertar), `tipo_descuento` PORCENTAJE (≤ 100) o MONTO, `valor > 0`, `fecha_fin > fecha_inicio`, `minimo_compra >= 0`, y selección de servicios → `promocion_servicio`. Activar/desactivar con `activo`.
- **Prioridad:** Media · **Dificultad:** Media
- **Verificar:** aplicar el código en el resumen de reserva (T3.3) y ver el descuento.

### T4.6 · Inventario por fecha (disponibilidad)
- **Qué / por qué:** sin filas en `disponibilidad_servicio` un servicio nunca aparece en búsquedas (R3).
- **Dónde:** nueva acción `Servicios/{id}/Disponibilidad` + vista.
- **Cómo:** formulario "desde, hasta, cupo total, precio por día" que inserta una fila por día (si ya existe esa fecha, actualizarla, nunca duplicar: hay UNIQUE `(id_servicio, fecha)`). No permitir bajar `cantidad_total` por debajo de lo ya vendido.
- **Prioridad:** Alta · **Dificultad:** Media
- **Verificar:** cargar 7 días a un hospedaje nuevo y encontrarlo en la búsqueda pública.

### T4.7 · Soporte (agentes) con datos reales
- **Dónde:** `Controllers/SoporteController.cs`.
- **Cómo:**
  1. Inyectar `_db`; el agente sale de `User.FindFirstValue(ClaimTypes.NameIdentifier)`.
  2. `Tomar` sin que dos agentes tomen el mismo ticket:
     ```csharp
     int filas = await _db.SolicitudesSoporte
         .Where(t => t.id == id && t.id_agente_asignado == null && t.estado == "ABIERTA")
         .ExecuteUpdateAsync(s => s
             .SetProperty(t => t.id_agente_asignado, idAgente)
             .SetProperty(t => t.estado, "EN_PROCESO")
             .SetProperty(t => t.actualizada_en, DateTime.Now));
     TempData[filas == 1 ? "Exito" : "Aviso"] = filas == 1 ? "Ticket asignado." : "Otro agente ya tomó este ticket.";
     ```
  3. Nueva acción `POST Cerrar/{id}` (solo si el ticket es del agente) → `estado = 'CERRADA'`.
  4. Consultas de `NuevoTicket`, `SeguirAtendiendo` e `Historial` (los `TODO BD` de las líneas 63-83).
- **Prioridad:** Media · **Dificultad:** Fácil
- **Verificar:** con `agente.prueba@...` tomar un ticket, verlo en "Seguir atendiendo", cerrarlo y verlo en "Historial".

### T4.8 · Gestión de reservaciones y reembolsos (panel)
- **Qué / por qué:** el mapa de sitio incluye "Panel › Reservaciones" y los reembolsos `SOLICITADO` necesitan que alguien los apruebe (R8).
- **Dónde:** nuevo `Controllers/ReservacionesAdminController.cs` (`[Route("Admin/Reservaciones")]`, `[Authorize(Roles = Roles.PanelInterno)]`), vistas en `Views/Admin/Reservaciones/`.
- **Cómo:** lista filtrable por código/estado/fecha; detalle con historial; acciones POST "Aprobar reembolso" / "Rechazar" que actualizan `reembolsos` + `historial_estado_reembolso` (con `id_usuario_cambio` = agente) y, al aprobar, el pago pasa a `REEMBOLSADO` + `historial_estado_pago`.
- **Prioridad:** Media · **Dificultad:** Media
- **Verificar:** aprobar el reembolso de FV-260903 y ver los 3 historiales actualizados.

### T4.9 · Panel SA
- **Cómo:** definir su alcance (ver Preguntas). Propuesta mínima: CRUD de usuarios internos (crear AGENTE/ADMINISTRADOR, activar/desactivar) y de ubicaciones. Si no da tiempo, **quitar la tarjeta "SA"** de `Views/Admin/Admin.cshtml:40-46` para no presentar una pantalla vacía.
- **Prioridad:** Baja · **Dificultad:** Media

---

## Sección 5 · Autenticación y autorización

> Decisión: **mantener autenticación por cookies** (ya está bien implementada). Cambiar a ASP.NET Core Identity ahora obligaría a rehacer tablas y pantallas; no conviene a una semana de la entrega.

### T5.1 · Rol en la cookie y `[Authorize]` → ver **T1.4** y **T1.5**.

### T5.2 · Menú que cambia según la sesión
- **Dónde:** nuevo `Views/Shared/_MenuUsuario.cshtml`; usarlo en `Views/Home/Index.cshtml:62-70` y `Views/Admin/_AdminLayout.cshtml:37`.
- **Cómo:**
  ```cshtml
  @using System.Security.Claims
  @if (User.Identity?.IsAuthenticated == true)
  {
      <span class="m-1">Hola, @User.Identity.Name</span>
      @if (User.IsInRole(Roles.Agente) || User.IsInRole(Roles.Administrador))
      {
          <a class="m-1" asp-controller="Admin" asp-action="Index">Panel</a>
      }
      <a class="m-1" asp-controller="Miusuario" asp-action="Index">Mi cuenta</a>
      <form asp-controller="Home" asp-action="CerrarSesion" method="post" class="d-inline">
          <button type="submit" class="btn btn-link m-1">Cerrar sesión</button>
      </form>
  }
  else
  {
      <a class="m-1" asp-controller="Home" asp-action="IniciarSesion">Iniciar sesión</a>
  }
  ```
  Uso: `<partial name="_MenuUsuario" />`.
- **Prioridad:** Alta · **Dificultad:** Fácil
- **Verificar:** antes de entrar se ve "Iniciar sesión"; después, el nombre y "Cerrar sesión"; un cliente no ve "Panel".

### T5.3 · Perfil del usuario
- **Dónde:** `Miusuariocontroller.Index` + `Views/Miusuario/index.cshtml`.
- **Cómo:** mostrar y editar nombre, apellidos y teléfono (no correo ni rol) con un `PerfilViewModel`; cambio de contraseña pidiendo la actual (verificar con `IPasswordHasher`).
- **Prioridad:** Baja · **Dificultad:** Media

### T5.4 · Bloqueo básico contra fuerza bruta (opcional)
- **Cómo:** `builder.Services.AddRateLimiter(...)` con una política de 5 intentos por minuto aplicada con `[EnableRateLimiting("login")]` al POST de `IniciarSesion`, y `app.UseRateLimiter();`.
- **Prioridad:** Baja · **Dificultad:** Media

---

## Sección 6 · Validaciones y manejo de errores

### T6.1 · Validación del lado del cliente
- **Qué / por qué:** hoy los errores solo aparecen después de enviar (D3).
- **Dónde:** `Views/Shared/_ValidationScriptsPartial.cshtml`.
- **Cómo:** usar CDN (igual que con Bootstrap) y agregarlo en cada formulario con `@section Scripts { <partial name="_ValidationScriptsPartial" /> }` (o directo al final de las vistas con `Layout = null`):
  ```html
  <script src="https://cdn.jsdelivr.net/npm/jquery@3.7.1/dist/jquery.min.js"></script>
  <script src="https://cdn.jsdelivr.net/npm/jquery-validation@1.21.0/dist/jquery.validate.min.js"></script>
  <script src="https://cdn.jsdelivr.net/npm/jquery-validation-unobtrusive@4.0.0/dist/jquery.validate.unobtrusive.min.js"></script>
  ```
  **Importante:** la validación del cliente es comodidad; la del servidor (`ModelState.IsValid`) se queda siempre.
- **Prioridad:** Media · **Dificultad:** Fácil
- **Verificar:** en "Crear cuenta", dejar el correo vacío y salir del campo → el mensaje aparece sin enviar.

### T6.2 · Páginas de error 404 / 403 / 500
- **Dónde:** `Program.cs`, `Controllers/HomeController.cs`, `Views/Shared/Error.cshtml`, nueva `Views/Shared/NoEncontrado.cshtml`.
- **Cómo:**
  1. En `Program.cs`, después de `UseExceptionHandler`:
     ```csharp
     app.UseStatusCodePagesWithReExecute("/Home/Estado/{0}");
     ```
  2. Acción:
     ```csharp
     [AllowAnonymous]
     public IActionResult Estado(int id) => id switch
     {
         404 => View("NoEncontrado"),
         403 => View("AccesoDenegado"),
         _   => View("Error", new ErrorViewModel { RequestId = HttpContext.TraceIdentifier })
     };
     ```
  3. Arreglar `_Layout.cshtml` (T7.1) para que `Error.cshtml` tenga estilos, y que **no** muestre detalles técnicos.
- **Prioridad:** Media · **Dificultad:** Fácil
- **Verificar:** abrir `/noexiste` → página 404 propia; en `Production` lanzar un error → página de error con estilos.

### T6.3 · Manejo de errores de BD en los nuevos servicios
- **Cómo:** seguir el patrón ya usado en `CrearusuarioController.cs:126-143`: capturar `DbUpdateException` (duplicados/UNIQUE) y mostrar un mensaje claro en `ModelState`; registrar el detalle con `_logger`, nunca mostrarlo al usuario.
- **Prioridad:** Media · **Dificultad:** Fácil

---

## Sección 7 · Interfaz y experiencia de usuario

### T7.1 · Un solo layout para el sitio público
- **Qué / por qué:** D1 y D2. Hoy cada vista copia la barra de navegación (con sus errores) y `_Layout.cshtml` está roto.
- **Dónde:** `Views/Shared/_Layout.cshtml`, `Views/_ViewStart.cshtml`, todas las vistas con `Layout = null` que son páginas completas (`Miusuario/*`, `Solicitar`, `Otros/*`, `Crearusuario/*`, `IniciarSesion`).
- **Cómo:**
  1. Reescribir `_Layout.cshtml` copiando el `<head>` y el `<header>` de `Views/Home/Index.cshtml` (Bootstrap por CDN + `~/assets/css/style.css`), con `<partial name="_MenuUsuario" />`, `@RenderBody()` y `@await RenderSectionAsync("Scripts", required: false)`.
  2. En cada página completa: borrar su `<html>/<head>/<header>` y el `Layout = null`; cada una agrega su CSS con `@section Styles { <link rel="stylesheet" href="~/assets/css/style_R.css" /> }` (agregar `@await RenderSectionAsync("Styles", required: false)` en el `<head>`).
- **Prioridad:** Alta · **Dificultad:** Media
- **Verificar:** todas las páginas muestran la misma barra; corregir la errata una vez la corrige en todas.

### T7.2 · Decidir el futuro de la "SPA casera"
- **Qué / por qué:** D4. Con `innerHTML`, los formularios de búsqueda funcionan (envían a `/Catalogo/Buscar` como página normal), pero no hay URL por sección ni botón "Atrás".
- **Recomendación:** convertir Viajes/Hoteles/Tours/Paquetes/Vehículos en **páginas normales** con el layout de T7.1 (los enlaces del menú pasan a `asp-controller="Home" asp-action="Hoteles"`). Si el equipo prefiere conservarla, al menos agregar `history.pushState` en `script.js`.
- **Prioridad:** Media · **Dificultad:** Media

### T7.3 · Limpieza visual y erratas
- **Cómo:** quitar "VER TARJETA test" (`Index.cshtml:59`); "FlyVideMex" → "FlyVibeMex"; "Iniciar secion" → "Iniciar sesión"; `imf-fluid` → `img-fluid`; poner un logo real o quitar el `<img src="">`; footer con contacto y enlace a Políticas/Privacidad; agregar la acción `Privacy()` o quitar su enlace.
- **Prioridad:** Media · **Dificultad:** Fácil

### T7.4 · Diseño responsivo
- **Cómo:** revisar cada pantalla en el modo dispositivo del navegador (F12 → 375 px). Usar el grid de Bootstrap (`row`/`col-md-*`) en tarjetas de resultados, y `navbar-expand-lg` con botón hamburguesa en la barra de navegación.
- **Prioridad:** Baja · **Dificultad:** Fácil
- **Verificar:** sin scroll horizontal a 375 px en Inicio, Resultados, Detalle y Mis reservaciones.

### T7.5 · Mensajes de éxito/error consistentes
- **Cómo:** mover el bloque de `TempData["Exito"]` / `["Aviso"]` de `_AdminLayout.cshtml:63-71` a un parcial `_Mensajes.cshtml` y usarlo también en el layout público.
- **Prioridad:** Baja · **Dificultad:** Fácil

---

## Sección 8 · Calidad del código y buenas prácticas

### T8.1 · Capa de servicios
- **Qué / por qué:** D5. Controladores delgados (validar → llamar servicio → devolver vista) y lógica reutilizable (el bot de Telegram del doc 06 necesitará la misma lógica).
- **Dónde:** `Services/` — `ICatalogoService`, `IReservacionService`, `IMisViajesService`, `ISoporteService`, `IProveedorService` (opcional para el Gestor).
- **Cómo:** interfaz + clase + `builder.Services.AddScoped<IX, X>();` en `Program.cs`; inyectar por **constructor** (dejar de usar `HttpContext.RequestServices.GetRequiredService` cuando la BD ya sea obligatoria).
- **Prioridad:** Media · **Dificultad:** Media

### T8.2 · Usar `async`/`await` en las acciones con BD
- **Cómo:** `ToListAsync`, `FirstOrDefaultAsync`, `SaveChangesAsync`, `ExecuteUpdateAsync`, recibiendo `CancellationToken` como ya hace `CrearusuarioController`.
- **Prioridad:** Baja · **Dificultad:** Fácil

### T8.3 · Orden y limpieza de archivos
- **Cómo:**
  1. Renombrar `Controllers/Miusuariocontroller.cs` → `MiusuarioController.cs` (igual que la clase).
  2. Acciones en PascalCase (`Sesion`, `Vehiculos`); si cambian `card`/`sesion`, actualizar `script.js` e `Index.cshtml`.
  3. Corregir comentarios `// GET /Admin` en `Miusuariocontroller.cs:9` y `SolicitarController.cs:7`.
  4. Borrar archivos vacíos o sin uso de D11 y el `CONTEXTO_PROYECTO_FLYVIBEMEX.md` duplicado en la raíz (dejar el de `docs/`).
  5. Actualizar el comentario de `Views/_ViewImports.cshtml` y agregar `@using FlyVibeMex.Models` (ya está) + `@using System.Security.Claims`.
  6. Consolidar `style_H/T/V/P.css` (casi idénticos) en un `busqueda.css`.
- **Prioridad:** Baja · **Dificultad:** Fácil

### T8.4 · Flujo de Git del equipo
- **Cómo:** una rama por tarea (`feature/T4.1-gestor-bd`), PR a `main`, nadie trabaja directo en `main`. Mensajes de commit en español con el id de la tarea.
- **Prioridad:** Alta · **Dificultad:** Fácil

---

## Sección 9 · Preparación para la entrega final

### T9.1 · Datos de prueba listos
- Ejecutar `00 → 01 → 02` en una BD limpia el día anterior; confirmar los 4 usuarios de prueba y que hay inventario en las fechas de la presentación (T2.4).

### T9.2 · README completo
- **Dónde:** `README.md`.
- **Contenido mínimo:** requisitos (.NET 10 SDK, SQL Server), pasos para crear la BD (scripts en orden), configurar *user-secrets* (comando exacto), `dotnet run` y URLs (`https://localhost:7045`), usuarios de prueba por rol, mapa de módulos/rutas, integrantes y tareas de cada quien.

### T9.3 · Guion de pruebas manuales (marcar en la exposición)
1. Registro de cliente nuevo → login → cerrar sesión.
2. Cliente intenta abrir `/Admin` → acceso denegado.
3. Admin: alta de proveedor → asociar hospedaje con imagen → cargar disponibilidad 7 días.
4. Cliente: buscar ese hospedaje → detalle → resumen con código de promoción → pagar con tarjeta guardada → confirmación con código.
5. Verificar en SSMS: `reservaciones`, `detalle_reservacion`, `pagos`, historiales y cupo descontado.
6. Cliente cancela y pide reembolso → cupo devuelto.
7. Agente: aprueba el reembolso; toma y cierra un ticket creado por el cliente.
8. Admin: da de baja el proveedor → sus servicios ya no aparecen en la búsqueda.
9. Página 404 propia; formulario con errores muestra mensajes sin perder lo escrito (excepto contraseñas).

### T9.4 · Actualizar la documentación
- Actualizar `docs/CONTEXTO_PROYECTO_FLYVIBEMEX.md` (todavía describe el estado del 17-20/10) y marcar en este documento las tareas terminadas.

---

## Preguntas abiertas para el equipo

Hasta que se respondan, el plan usa la opción marcada como **(propuesta)**:

1. **Roles:** ¿los paneles se controlan con los 3 roles de la BD (`CLIENTE`, `AGENTE`, `ADMINISTRADOR`) **(propuesta)** o quieren roles separados `SOPORTE`, `GESTOR_VENTAS`, `SA`? Si es lo segundo, hay que insertarlos en `roles_usuarios` y crear usuarios de prueba para cada uno.
2. **Base de datos de la entrega:** ¿`FlyVibeMex_Test` con los scripts 00-02 **(propuesta)** o la BD "Real" alineada con `sql/03_alinear_nombres_legado.sql`?
3. **Tours:** ¿son servicios de tipo `EVENTO` **(propuesta)**, paquetes, o se quita la sección?
4. **Panel SA:** ¿qué debe hacer? (propuesta: usuarios internos y ubicaciones, o quitarlo si no da tiempo).
5. **SPA casera:** ¿se conserva o se pasa a páginas MVC normales **(propuesta)**?
6. **Migraciones:** ¿el profesor exige `dotnet ef migrations` o aceptan scripts SQL **(propuesta)**?

---

## Checklist final

### 1. Correcciones críticas
- [ ] T1.1 Commit y push de los 53 cambios y de los scripts `sql/01`, `02`, `03` (rama + PR)
- [ ] T1.2 `dotnet build` sin errores; anotar errores reales
- [ ] T1.3 Elegir la BD de la entrega, configurar *user-secrets* y `/Home/TestDb` en verde
- [ ] T1.4 Claim `ClaimTypes.Role` en el login + `Models/Auth/Roles.cs` + redirección por rol
- [ ] T1.5 `[Authorize]` en Admin, Gestor, Soporte, SA, Miusuario y Solicitar; página "Acceso denegado"
- [ ] T1.6 Validar dueño de la reservación en cancelar/reembolsar
- [ ] T1.7 Corregir `tipo_habitacion`, campos `[Required]`, `EN_PROCESO`, nota `APROVADO`, `categoria` anulable

### 2. Base de datos y modelos
- [ ] T2.1 Acordar el proceso script SQL + modelo + `/Home/TestDb`
- [ ] T2.2 Configurar FKs en `FlyVibeMexDbContext`
- [ ] T2.3 (Opcional) `HasMaxLength` en columnas de texto clave
- [ ] T2.4 Seed con inventario en las fechas de la presentación
- [ ] T2.5 Acción para mostrar imágenes de servicios
- [ ] T2.6 (Opcional) Migración línea base con `dotnet ef`

### 3. Sitio público
- [ ] T3.1 Búsqueda y resultados con datos reales (empezar por Hospedaje)
- [ ] T3.1b Replicar búsqueda para Vuelos, Vehículos y Paquetes
- [ ] T3.2 Detalle del servicio con política de cancelación
- [ ] T3.3 Resumen + promoción + creación de reservación en transacción
- [ ] T3.4 Pago simulado + confirmación con código de reserva
- [ ] T3.5 Mis reservaciones / Mis compras reales + cancelar + reembolsar
- [ ] T3.6 Mis tarjetas tokenizadas (sin número completo ni CVV)
- [ ] T3.7 Formulario de soporte del cliente
- [ ] T3.8 Definir y conectar la sección Tours

### 4. Administración
- [ ] T4.1 Gestor: proveedores y servicios guardando en la BD; borrar datos de prueba
- [ ] T4.2 Alta de servicios siempre con su subtipo (transacción) + imagen
- [ ] T4.3 Alta de vuelos
- [ ] T4.4 CRUD de paquetes
- [ ] T4.5 CRUD de promociones
- [ ] T4.6 Carga de disponibilidad por fecha
- [ ] T4.7 Soporte: tomar (sin doble asignación), seguir, cerrar, historial
- [ ] T4.8 Panel de reservaciones y aprobación de reembolsos
- [ ] T4.9 Panel SA (definir o quitar)

### 5. Autenticación y autorización
- [ ] T5.2 Menú según sesión con "Cerrar sesión" (POST)
- [ ] T5.3 Perfil del usuario y cambio de contraseña
- [ ] T5.4 (Opcional) Límite de intentos de login

### 6. Validaciones y errores
- [ ] T6.1 Validación del lado del cliente (jQuery Validation por CDN)
- [ ] T6.2 Páginas 404 / 403 / 500 propias
- [ ] T6.3 Manejo de `DbUpdateException` en los servicios nuevos

### 7. Interfaz y experiencia de usuario
- [ ] T7.1 Un solo `_Layout.cshtml` para todo el sitio público
- [ ] T7.2 Decidir y aplicar: SPA casera o páginas normales
- [ ] T7.3 Quitar "VER TARJETA test", corregir erratas, logo, footer, Privacy
- [ ] T7.4 Revisión responsiva a 375 px
- [ ] T7.5 Parcial `_Mensajes.cshtml` para TempData

### 8. Calidad del código
- [ ] T8.1 Servicios con interfaz e inyección por constructor
- [ ] T8.2 Acciones con BD en `async`
- [ ] T8.3 Renombrar archivos/acciones, borrar archivos vacíos, consolidar CSS
- [ ] T8.4 Ramas por tarea y PR a `main`

### 9. Entrega final
- [ ] T9.1 BD limpia con seed el día anterior
- [ ] T9.2 README completo
- [ ] T9.3 Guion de pruebas manuales ejecutado sin errores
- [ ] T9.4 Documentación de contexto actualizada
- [ ] Responder las 6 preguntas abiertas
