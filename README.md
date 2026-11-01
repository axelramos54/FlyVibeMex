# ✈️ FlyVibe Mex

**Plataforma web para cotizar, reservar y gestionar vuelos, hospedaje, renta de vehículos y paquetes turísticos del mercado nacional mexicano.**

| | |
|---|---|
| **Institución** | Universidad Autónoma de Chiapas (UNACH) — Escuela de Tecnologías Digitales Aplicadas, Campus 1 |
| **Programa** | Licenciatura en Sistemas Computacionales — 5° "K" |
| **Tipo de proyecto** | Proyecto Integrador |
| **Lugar** | Tuxtla Gutiérrez, Chiapas — 2026 |
| **Licencia** | [MIT](LICENSE) |

### Equipo

| Integrante | GitHub |
|---|---|
| Yamil Alexei Courtois Paredes | [@Yamil07Gamer](https://github.com/Yamil07Gamer) |
| Axel Ramos Monroy | [@axelramos54](https://github.com/axelramos54) |
| Ruth Carolina Moscoso Villatoro | [@RuCaMoVil](https://github.com/RuCaMoVil) |
| Alonso Gómez Burguete | — |

---

## 📌 Descripción

Organizar un viaje completo suele obligar al usuario a comparar precios y disponibilidad en varias plataformas distintas. **FlyVibe Mex** busca centralizar ese proceso en un solo sitio: el viajero encuentra vuelos, hoteles, vehículos y paquetes, los reserva, los paga y después los gestiona desde **"Mis viajes"** (cancelaciones, reembolsos y soporte). Los agentes de la agencia administran reservaciones, catálogo, promociones y solicitudes de soporte desde un **panel interno**.

### Estado actual del proyecto (octubre 2026)

> El proyecto es un **prototipo navegable de frontend** servido por ASP.NET Core MVC. Las secciones de Inicio, Vuelos, Hoteles, Tours, Paquetes e Inicio de sesión ya se muestran, pero su contenido es **estático** (escrito en las vistas). Aún no hay inicio de sesión real, reservas ni pagos.

---

## 🧰 Tecnologías

| Capa | Tecnología |
|---|---|
| Backend | **ASP.NET Core MVC** sobre **.NET 10** (`net10.0`, *nullable* activado) |
| Vistas | Razor (`.cshtml`) |
| Estilos | **Bootstrap 5.3.8** (CDN de jsDelivr) + CSS propio por sección |
| Interacción | JavaScript *vanilla* (`fetch` para cargar secciones sin recargar la página) |
| Control de versiones | Git + GitHub (ramas por aportación y *pull requests* a `main`) |

---

## ✅ Requisitos previos

- [.NET 10 SDK](https://dotnet.microsoft.com/download) — verificar con `dotnet --version`.
- Git.
- Un editor: Visual Studio 2026, o Visual Studio Code con la extensión *C# Dev Kit*.
- Conexión a internet al ejecutar (Bootstrap y las imágenes de ejemplo se cargan desde servidores externos).

---

## ▶️ Cómo ejecutar el proyecto

```bash
# 1. Clonar el repositorio
git clone https://github.com/axelramos54/FlyVibeMex.git
cd FlyVibeMex            # carpeta donde está FlyVibeMex.csproj

# 2. Ejecutar (perfil "http")
dotnet run
#    → http://localhost:5106

# 2b. O con HTTPS (perfil "https")
dotnet run --launch-profile https
#    → https://localhost:7045  (también responde en http://localhost:5106)
```

Los puertos están definidos en `Properties/launchSettings.json`. Desde Visual Studio basta con elegir el perfil `http` o `https` y presionar **F5**.

> ⚠️ **Importante — archivos nuevos en `wwwroot/`:** el proyecto sirve los archivos estáticos con `MapStaticAssets()`, que los publica a partir de un **manifiesto que se genera al compilar**. Si agregas, renombras o mueves un archivo dentro de `wwwroot/` (CSS, JS, imágenes), **detén la aplicación, vuelve a ejecutar `dotnet run`** y refresca el navegador con **Ctrl + F5** (recarga sin caché). Si no, el navegador recibirá un 404 o la versión anterior del archivo.

---

## 🗂️ Estructura del repositorio (resumen)

```
FlyVibeMex/
├── Controllers/        HomeController (shell + secciones) y DefaultController (obsoleto)
├── Models/             24 clases de entidad del dominio + ErrorViewModel
├── Views/              Home/ (Index = shell, demás = secciones) y Shared/ (plantilla)
├── wwwroot/assets/     css/, js/ e img/ propios del proyecto
├── docs/               documentación del equipo (ver índice abajo)
├── Program.cs          configuración del servidor y rutas
```

El detalle completo está en [docs/02_GUIA_ARQUITECTURA_MVC_Y_FRONTEND.md](docs/02_GUIA_ARQUITECTURA_MVC_Y_FRONTEND.md).

---

## 🗄️ Base de datos: scripts SQL (fuente de verdad)

El proyecto **no usa migraciones de EF Core**: el esquema vive en `sql/` y los modelos de `Models/` se escriben a mano para que coincidan columna por columna.

**Crear la base de prueba** (SSMS, F5 en cada archivo, en este orden). Son solo 3 scripts:

| Script | Qué hace |
|---|---|
| `sql/01_crear_base_datos.sql` | Crea `FlyVibeMex_Test` y **borra y vuelve a crear** sus 27 tablas (PK, FK, CHECK, índices) con los catálogos que la app necesita (roles, estados de reservación y pago, métodos de pago) y la cuenta inicial `sa.prueba@flyvibemex.test` |
| `sql/02_procedimientos_almacenados.sql` | 24 procedimientos `pa_*` con las mismas reglas que la app: altas del catálogo (proveedor, hospedaje, vehículo, tour, vuelo con "repetir"), disponibilidad, compra con pago simulado, cancelación y reembolsos, tickets, y reportes (ventas, ocupación, reembolsos pendientes, tickets, bitácora). La app **no** los usa (sigue con EF Core); al final del archivo hay ejemplos para SSMS |
| `sql/03_seed_demo.sql` | Datos de demostración: 7 cuentas de prueba (una por rol), ubicaciones, proveedores, servicios con subtipo, 30 días de disponibilidad, promociones, reservaciones, pagos, reembolso y tickets |

Contraseña de todas las cuentas de prueba: `FlyVibe#Prueba2026` (solo para esta base).

> Las fechas del seed son relativas al día en que se ejecuta. **Vuelvan a ejecutar `03_seed_demo.sql` el día anterior a la presentación** (no hace falta repetir 01 ni 02).

**Regla para cambiar el esquema (todo el equipo):**
1. Nunca editar a mano la base de otro integrante ni modificar tablas desde SSMS sin script.
2. Cambiar el `CREATE TABLE` en `sql/01_crear_base_datos.sql` (y, si aplica, los procedimientos de `02` y los datos de `03`). No se crean scripts de migración: la base de prueba se vuelve a crear con `01 → 02 → 03` (se pierden sus datos).
3. Ajustar la clase en `Models/` (mismo nombre de columna; nullable si la columna admite `NULL`) y, si aplica, `Data/FlyVibeMexDbContext.cs` (`HasMaxLength`, FK con `HasOne<...>().WithMany()`).
4. Ejecutar los scripts, correr la app y abrir `/Home/TestDb`: debe responder `"esquemaCompleto": true`.
5. Subir scripts + modelo + DbContext en el **mismo** commit/PR, y avisar al equipo que debe recrear su base.

**Imágenes:** se guardan como archivo en `wwwroot/uploads/<carpeta>/` (ignorada por Git) y en la BD solo va la ruta (`imagen_ruta`). Ver `Services/IAlmacenImagenes.cs`. Las imágenes de demostración que sí deban subirse al repositorio van en `wwwroot/assets/img/demo/`.

---

## 📚 Documentación

| Documento | Contenido |
|---|---|
| [01 · Visión y reglas de negocio](docs/01_VISION_Y_REGLAS_DE_NEGOCIO.md) | Propósito, problemática, benchmarking, actores, mapa de sitio, flujos y reglas del dominio (R1–R13) |
| [02 · Guía de arquitectura MVC y frontend](docs/02_GUIA_ARQUITECTURA_MVC_Y_FRONTEND.md) | Cómo funciona el proyecto hoy: carpetas, `Program.cs`, navegación con `cargarPagina()`, controladores, vistas y convenciones |
| [03 · Catálogo de modelos C#](docs/03_CATALOGO_MODELOS_CSHARP.md) | Las 24 clases de `Models/` agrupadas por dominio, con su namespace y pendientes de estandarización |
| [04 · Estándares y hoja de ruta](docs/04_ESTANDARES_Y_HOJA_DE_RUTA_APP.md) | Convenciones de código, flujo de Git, limpieza pendiente y pantallas por construir |
| [05 · Conexión EF Core y seguridad](docs/05_CONEXION_EFCORE_Y_SEGURIDAD.md) | Crear `FlyVibeMex_Test`, registrar tu cadena con `dotnet user-secrets`, probar `/Home/TestDb`, cambiar a la BD real, reglas anti SQL Injection y login seguro |
| [06 · Análisis y diseño del bot de Telegram](docs/06_ANALISIS_Y_DISENO_BOT_TELEGRAM.md) | Evaluación de 7 librerías .NET, librería elegida, funciones del bot por módulo, flujos, reglas de seguridad y plan MVP de 3 días (Etapa 7) |


---

## 📄 Licencia

Distribuido bajo la licencia MIT. Consulta [LICENSE](LICENSE).
