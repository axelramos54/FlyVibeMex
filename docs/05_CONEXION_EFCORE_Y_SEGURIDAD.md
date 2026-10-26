# 05 · Conexión a SQL Server con EF Core y seguridad

> Guía para **todo el equipo**: cómo conectar FlyVibe Mex a SQL Server con Entity Framework Core sin subir contraseñas a GitHub, cómo probar la conexión y qué reglas seguir para que el login no se pueda vulnerar.
> Última actualización: 20/10/2026.

## Índice
1. [Qué se agregó al proyecto](#1-qué-se-agregó-al-proyecto)
2. [Crear la base de prueba `FlyVibeMex_Test`](#2-crear-la-base-de-prueba-flyvibemex_test)
3. [Registrar tu cadena de conexión con `dotnet user-secrets`](#3-registrar-tu-cadena-de-conexión-con-dotnet-user-secrets)
4. [Probar la conexión en `/Home/TestDb`](#4-probar-la-conexión-en-hometestdb)
5. [Cambiar de `FlyVibeMex_Test` a la base real](#5-cambiar-de-flyvibemex_test-a-la-base-real)
6. [Credenciales de BD con permisos mínimos](#6-credenciales-de-bd-con-permisos-mínimos)
7. [Reglas contra SQL Injection en EF Core](#7-reglas-contra-sql-injection-en-ef-core)
8. [Manejo seguro del login](#8-manejo-seguro-del-login)
9. [Seguridad en GitHub (`.gitignore`, `bin/`, `obj/`)](#9-seguridad-en-github-gitignore-bin-obj)
10. [Compatibilidad de los modelos actuales con EF Core](#10-compatibilidad-de-los-modelos-actuales-con-ef-core)

---

## 1. Qué se agregó al proyecto

| Archivo | Para qué sirve |
|---|---|
| `FlyVibeMex.csproj` | Paquetes `Microsoft.EntityFrameworkCore.SqlServer` y `Microsoft.EntityFrameworkCore.Tools` (10.0.x) y el `UserSecretsId` (equivale a `dotnet user-secrets init`, ya está hecho). |
| `appsettings.json` | `"DatabaseMode": "Test"` y dos cadenas **de ejemplo** (`FlyVibeMex_Test`, `FlyVibeMex_Real`) con el marcador `__SERVIDOR__`. **No contienen servidores ni contraseñas reales.** |
| `appsettings.Development.json` | Solo niveles de log (EF Core en `Warning`). No lleva cadenas de conexión. |
| `Data/ConfiguracionBaseDatos.cs` | Lee `DatabaseMode` y elige la cadena correcta. Detecta si la cadena sigue siendo el marcador. |
| `Data/FlyVibeMexDbContext.cs` | `DbContext` con las 26 tablas, conversores `ulong↔bigint` / `ushort↔smallint` y fechas de control con `CURRENT_TIMESTAMP` por defecto. |
| `Services/IAuthService.cs`, `Services/AuthService.cs` | Servicio de autenticación seguro (LINQ parametrizado + `PasswordHasher<UsuariosGlobales>`). |
| `Models/Auth/UsuarioSesion.cs` | DTO `UsuarioSesion` (id, nombre, correo, id_rol) y `ResultadoAutenticacion`. **Sin** `password_hash`. |
| `Controllers/HomeController.cs` | Nueva acción `GET /Home/TestDb` (solo en Development). |
| `Program.cs` | Registro del `DbContext` (Test/Real, `EnableRetryOnFailure`, `EnableSensitiveDataLogging(false)`) y de los servicios. |
| `sql/00_db_prueba_conexion.sql` | Crea `FlyVibeMex_Test` con `roles_usuarios` y `usuarios` + datos semilla. |
| `sql/01_esquema.sql` (25/10/2026) | Crea las otras 24 tablas con los **nombres nuevos de los modelos** (`id_usuarioglobal`, `id_usuariog`, `fecha_inicio`, `tipo_proveedor`…), subtipos con `id_servicio` como PK/FK, `vuelos` e `historial_estado_reservacion`. |
| `sql/02_seed_demo.sql` (25/10/2026) | Datos de demostración coherentes (catálogo, disponibilidad, promociones, 3 reservaciones, pagos, reembolso y tickets). |
| `sql/03_alinear_nombres_legado.sql` (25/10/2026) | Solo para bases creadas con un script anterior: renombra `UsuariosGlobales`, `tipo_prooverdor`, `creado`, `id_usuario`… a los nombres actuales. No borra nada. |
| `.gitignore` | Ahora ignora `bin/`, `obj/`, `.vs/`, `*.user`, `*.suo`, `appsettings.*.local.json`, `.env`, `secrets.json`. |

> La app **sigue funcionando sin base de datos**: si no has configurado tu cadena, todas las vistas cargan igual; solo fallará lo que use el `DbContext` (con un mensaje que te dice qué comando ejecutar).

---

## 2. Crear la base de prueba `FlyVibeMex_Test`

Requisitos: SQL Server (Developer, Express o LocalDB) y SQL Server Management Studio (SSMS).

1. Abre **SSMS** y conéctate a tu servidor local:
   - SQL Server Developer/Standard: `localhost`
   - SQL Server Express: `.\SQLEXPRESS`
   - LocalDB (viene con Visual Studio): `(localdb)\MSSQLLocalDB`
   - Autenticación: **Windows Authentication**.
2. `Archivo › Abrir › Archivo…` y selecciona `sql/00_db_prueba_conexion.sql`.
3. Presiona **F5** (Ejecutar).
4. Al final debe aparecer una tabla con 3 roles: `ADMINISTRADOR` (1 usuario), `AGENTE` (0) y `CLIENTE` (2).
5. Abre y ejecuta (F5) `sql/01_esquema.sql`. Al final debe decir `tablas_creadas = 26`.
6. Abre y ejecuta (F5) `sql/02_seed_demo.sql`. Al final verás el conteo por tabla y las promociones vigentes (`BIENVENIDA10`, `CARIBE500`).

Orden obligatorio: **00 → 01 → 02**. Si vuelves a ejecutar `00`, también borra las tablas de `01` (dependen de `usuarios`): repite `01` y `02` después.

El script se puede ejecutar las veces que quieras: borra y vuelve a crear **solo** las tablas de `FlyVibeMex_Test` (tiene un candado que detiene la ejecución si no está en esa base). **No toca la base real.**

**Usuarios de prueba** (contraseña de prueba para los tres: `FlyVibe#Prueba2026`; existe solo en esta base de prueba):

| Correo | Rol | Activo | Resultado esperado al iniciar sesión |
|---|---|---|---|
| `admin.prueba@flyvibemex.test` | ADMINISTRADOR | Sí | Éxito |
| `cliente.prueba@flyvibemex.test` | CLIENTE | Sí | Éxito |
| `inactivo.prueba@flyvibemex.test` | CLIENTE | **No** | "Credenciales inválidas." |
| `agente.prueba@flyvibemex.test` | AGENTE | Sí | Éxito (lo crea `02_seed_demo.sql`; atiende el ticket `EN_PROCESO`) |
| `ana.lopez@flyvibemex.test` | CLIENTE | Sí | Éxito (lo crea `02_seed_demo.sql`; reservaciones `FV-260901` y `FV-260903`) |

`cliente.prueba` es titular de `FV-260902` (viaje dentro de 2 días).

En la tabla solo está el **hash** (`AQAAAAIAAYag…`, 84 caracteres, formato V3 de ASP.NET Core Identity: PBKDF2‑HMAC‑SHA512, 100 000 iteraciones, sal aleatoria). La contraseña en texto plano no está guardada en ningún lado de la BD.

---

## 3. Registrar tu cadena de conexión con `dotnet user-secrets`

`user-secrets` guarda la cadena **en tu usuario de Windows**, fuera de la carpeta del proyecto:
`%APPDATA%\Microsoft\UserSecrets\flyvibemex-9b6d9483-4ded-4987-a992-d9edf61e015e\secrets.json`.
Git nunca la ve, así que es imposible subirla por accidente. Cada integrante hace esto **una sola vez en su PC**.

Abre una terminal (PowerShell o la terminal de Visual Studio) **en la carpeta donde está `FlyVibeMex.csproj`** y ejecuta el comando que corresponda a tu instalación:

```powershell
# SQL Server Developer / Standard (instancia por defecto) con autenticación de Windows
dotnet user-secrets set "ConnectionStrings:FlyVibeMex_Test" "Server=localhost;Database=FlyVibeMex_Test;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=15;Application Name=FlyVibeMex"

# SQL Server Express
dotnet user-secrets set "ConnectionStrings:FlyVibeMex_Test" "Server=.\SQLEXPRESS;Database=FlyVibeMex_Test;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=15;Application Name=FlyVibeMex"

# LocalDB
dotnet user-secrets set "ConnectionStrings:FlyVibeMex_Test" "Server=(localdb)\MSSQLLocalDB;Database=FlyVibeMex_Test;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=15;Application Name=FlyVibeMex"
```

Comandos útiles:

```powershell
dotnet user-secrets list                                        # ver lo que tienes registrado (solo en tu PC)
dotnet user-secrets remove "ConnectionStrings:FlyVibeMex_Test"  # borrar una entrada
dotnet user-secrets clear                                       # borrar todos tus secretos de este proyecto
```

> En Visual Studio también puedes usar: clic derecho sobre el proyecto › **Administrar secretos de usuario**. Se abre el mismo `secrets.json`:
> ```json
> {
>   "ConnectionStrings:FlyVibeMex_Test": "Server=localhost;Database=FlyVibeMex_Test;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=15;Application Name=FlyVibeMex"
> }
> ```

**Qué significa cada parte de la cadena**

| Parte | Por qué |
|---|---|
| `Trusted_Connection=True` | Autenticación integrada de Windows: **no hay contraseña** en la cadena. Es la opción recomendada en local. |
| `Encrypt=True` | El tráfico entre la app y SQL Server va cifrado (TLS): nadie en la red puede leer consultas ni credenciales. |
| `TrustServerCertificate=True` | Acepta el certificado autofirmado que trae SQL Server local. **Solo para desarrollo**; en un servidor real se instala un certificado válido y se quita (o se pone en `False`). |
| `Connect Timeout=15` | Si el servidor no responde en 15 s, falla rápido en vez de colgar la petición. |
| `Application Name=FlyVibeMex` | Permite identificar las conexiones de la app en SSMS (Monitor de actividad). |

**¿Y en el servidor de despliegue?** Ahí no hay `user-secrets`: se usan variables de entorno (el doble guion bajo `__` reemplaza a los dos puntos):

```powershell
$env:ConnectionStrings__FlyVibeMex_Real = "Server=...;Database=FlyVibeMex;User Id=flyvibemex_app;Password=...;Encrypt=True;Connect Timeout=15;"
$env:DatabaseMode = "Real"
```

---

## 4. Probar la conexión en `/Home/TestDb`

1. Ejecuta la app: `dotnet run` (o F5 en Visual Studio). En la consola verás:
   `Base de datos: modo Test (cadena "FlyVibeMex_Test", configurada).`
2. Abre `https://localhost:7045/Home/TestDb` (o `http://localhost:5106/Home/TestDb`).

Respuesta esperada si todo está bien:

```json
{
  "modo": "Test",
  "cadena": "FlyVibeMex_Test",
  "baseDeDatos": "FlyVibeMex_Test",
  "configurada": true,
  "conectado": true,
  "tiempoMs": 412,
  "consultaPrueba": {
    "rolesActivos": ["CLIENTE", "AGENTE", "ADMINISTRADOR"],
    "usuariosActivos": 2,
    "usuariosInactivos": 1
  },
  "esquemaCompleto": true,
  "tablas": [
    { "tabla": "roles_usuarios", "ok": true, "filas": 3, "error": null },
    { "tabla": "usuarios", "ok": true, "filas": 5, "error": null },
    "… (las 26 tablas)"
  ],
  "mensaje": "Conexión correcta con Entity Framework Core y las 26 tablas coinciden con los modelos."
}
```

`tablas` lee **una fila con todas sus columnas** de cada tabla: si una columna no existe o su tipo no coincide con la propiedad del modelo, esa tabla sale con `"ok": false` y el motivo en `error` (por ejemplo `SqlException: Invalid column name 'tipo_proveedor'.`).

Si algo falla, la respuesta trae `"conectado": false` y un `mensaje` con la sugerencia:

| Situación | Qué hacer |
|---|---|
| `"configurada": false` | No registraste tu cadena: paso 3. |
| `codigoSql` 4060 | La base no existe: ejecuta `sql/00_db_prueba_conexion.sql` (paso 2). |
| `codigoSql` 208 | La base existe pero faltan tablas: ejecuta los scripts `00`, `01` y `02`. |
| `"esquemaCompleto": false` | Conecta, pero alguna tabla no coincide con los modelos: revisa `tablas[].error`. En la base de prueba vuelve a ejecutar `01` y `02`; en una base creada con un script anterior ejecuta `sql/03_alinear_nombres_legado.sql`. |
| `codigoSql` 18456 | SQL Server rechazó el usuario: revisa usuario/contraseña o usa `Trusted_Connection=True`. |
| `codigoSql` 2, 53, -1 o -2 | No encontró el servidor: revisa el nombre (`localhost`, `.\SQLEXPRESS`, `(localdb)\MSSQLLocalDB`) y que el servicio de SQL Server esté iniciado. |

Seguridad de este endpoint: **solo existe en Development** (en cualquier otro entorno responde 404), no devuelve servidor, usuario, contraseña, hashes ni datos personales, y no se guarda en caché.

---

## 5. Cambiar de `FlyVibeMex_Test` a la base real

Cuando el esquema final esté listo:

1. Crear la base real `FlyVibeMex` con el script definitivo del equipo (en SSMS). Si ya existe y se creó con un script anterior, respaldarla y ejecutar `sql/03_alinear_nombres_legado.sql`; después revisar `/Home/TestDb`.
2. Registrar la cadena real en tu PC:
   ```powershell
   dotnet user-secrets set "ConnectionStrings:FlyVibeMex_Real" "Server=localhost;Database=FlyVibeMex;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=15;Application Name=FlyVibeMex"
   ```
3. Cambiar el interruptor. Dos opciones:
   - **Solo en tu PC (recomendado mientras se prueba):** `dotnet user-secrets set "DatabaseMode" "Real"`. Para volver: `dotnet user-secrets remove "DatabaseMode"`.
   - **Para todo el equipo:** en `appsettings.json` cambiar `"DatabaseMode": "Test"` por `"DatabaseMode": "Real"` y hacer commit (cada integrante deberá tener registrada su `FlyVibeMex_Real`).
4. Reiniciar la app y entrar a `/Home/TestDb`: debe decir `"modo": "Real"` y `"baseDeDatos": "FlyVibeMex"`.

Valores válidos: `Test` o `Real` (no distingue mayúsculas). Cualquier otro valor detiene la app al arrancar con un mensaje claro.

---

## 6. Credenciales de BD con permisos mínimos

**En local (recomendado):** autenticación integrada de Windows (`Trusted_Connection=True`). No hay contraseña que robar.

**En un servidor compartido o de despliegue:** un usuario SQL **exclusivo para la app**, con permisos mínimos. Nunca `sa` ni `sysadmin`, nunca `db_owner`. Requiere que SQL Server tenga activado el modo de autenticación mixta.

```sql
-- Ejecutar en SSMS como administrador. Sustituye la contraseña por una larga y única
-- (NO la escribas en ningún archivo del repositorio).
USE master;
CREATE LOGIN flyvibemex_app WITH PASSWORD = N'<<CONTRASEÑA_LARGA_Y_ÚNICA>>', CHECK_POLICY = ON;
GO
USE FlyVibeMex_Test;   -- repetir en FlyVibeMex cuando exista
CREATE USER flyvibemex_app FOR LOGIN flyvibemex_app;
ALTER ROLE db_datareader ADD MEMBER flyvibemex_app;   -- solo SELECT
ALTER ROLE db_datawriter ADD MEMBER flyvibemex_app;   -- solo INSERT / UPDATE / DELETE
GO
```

Cadena correspondiente (en user-secrets o variable de entorno, **nunca** en `appsettings.json`):

```text
Server=<servidor>;Database=FlyVibeMex;User Id=flyvibemex_app;Password=<contraseña>;Encrypt=True;TrustServerCertificate=False;Connect Timeout=15;Application Name=FlyVibeMex
```

Con estos permisos, aunque alguien lograra ejecutar SQL a través de la app, **no podría** crear/borrar tablas, crear usuarios, leer otras bases ni ejecutar comandos del sistema. Los cambios de esquema los hace un integrante con su propia cuenta de administrador desde SSMS.

---

## 7. Reglas contra SQL Injection en EF Core

**Regla de oro: consultar siempre con LINQ.** EF Core convierte cada valor en un parámetro SQL; el valor nunca se pega dentro del texto de la consulta.

```csharp
// ✅ CORRECTO: LINQ parametrizado
var usuario = await _db.Usuarios
    .AsNoTracking()
    .Where(u => u.correo == correo && u.activo)
    .Select(u => new { u.id, u.nombre })
    .FirstOrDefaultAsync();
// SQL generado: ... WHERE [u].[correo] = @__correo_0 AND [u].[activo] = CAST(1 AS bit)
// Si correo = "' OR 1=1 --", SQL Server busca ese texto LITERAL como correo -> no encuentra a nadie.
```

```csharp
// ❌ PROHIBIDO: interpolación o concatenación con FromSqlRaw / ExecuteSqlRaw / SqlQueryRaw
_db.Usuarios.FromSqlRaw($"SELECT * FROM usuarios WHERE correo = '{correo}'");        // VULNERABLE
_db.Database.ExecuteSqlRaw("DELETE FROM usuarios WHERE id = " + id);               // VULNERABLE
```

```csharp
// ✅ Si algún día se necesita SQL manual o un procedimiento almacenado:
_db.Usuarios.FromSqlInterpolated($"SELECT * FROM usuarios WHERE correo = {correo}"); // {correo} -> @p0
_db.Database.ExecuteSqlInterpolated($"EXEC sp_reservar {idServicio}, {cantidad}");

// o FromSqlRaw con parámetros explícitos
_db.Usuarios.FromSqlRaw("SELECT * FROM usuarios WHERE correo = @correo",
    new SqlParameter("@correo", correo));
```

Más buenas prácticas:
- `AsNoTracking()` en todas las consultas de solo lectura (más rápido y no deja entidades rastreadas).
- Proyectar con `.Select(...)` solo las columnas que la vista necesita; nunca mandar entidades completas a las vistas (usar ViewModels/DTOs).
- Formularios: `[HttpPost]` + `[ValidateAntiForgeryToken]` + ViewModel con DataAnnotations + `ModelState.IsValid`.
- Nunca construir nombres de columnas o de orden (`ORDER BY`) con texto que venga del usuario; usar un `switch` con valores permitidos.
- Con `EnableRetryOnFailure` activo, las transacciones manuales deben ir dentro de `_db.Database.CreateExecutionStrategy().ExecuteAsync(...)`.
- `EnableSensitiveDataLogging` se queda en `false`: los logs nunca muestran valores de parámetros (correos, hashes).

---

## 8. Manejo seguro del login

`AuthService.ValidarCredencialesAsync(correo, password)` aplica estas reglas (ver comentarios en `Services/AuthService.cs`):

1. **Validación de entrada** antes de tocar la BD (vacíos, longitudes máximas).
2. **Correo normalizado** (`Trim().ToLowerInvariant()`) y búsqueda con LINQ **solo por correo y `activo`**: `u.correo == correo && u.activo`.
3. **La contraseña nunca va a SQL Server.** El hash se lee y se verifica **en memoria** con `PasswordHasher<Usuarios>.VerifyHashedPassword`.
4. **El `password_hash` nunca sale del servicio.** Al controlador solo llega `UsuarioSesion(id, nombre, correo, id_rol)`.
5. **Mensaje genérico** "Credenciales inválidas." para correo inexistente, usuario inactivo o contraseña incorrecta, y tiempo de respuesta similar en los tres casos → no se puede averiguar qué correos están registrados.
6. **Errores de BD** (`SqlException`, reintentos agotados, BD sin configurar) se registran con `ILogger` en el servidor; al navegador solo llega "No fue posible iniciar sesión en este momento…".
7. Si el hash fue creado con parámetros antiguos (`SuccessRehashNeeded`), se actualiza automáticamente.
8. Para **registro o cambio de contraseña** usar `IAuthService.GenerarHash(password)` y `NormalizarCorreo(correo)` antes de guardar.

Ejemplo de cómo debe usarse desde un controlador (siguiente paso: conectar `sesion.cshtml`):

```csharp
public class CuentaController : Controller
{
    private readonly IAuthService _auth;
    public CuentaController(IAuthService auth) => _auth = auth;

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IniciarSesion(LoginViewModel modelo, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(modelo);

        var resultado = await _auth.ValidarCredencialesAsync(modelo.correo, modelo.password, ct);
        if (!resultado.Exitoso)
        {
            ModelState.AddModelError(string.Empty, resultado.Mensaje); // siempre genérico
            return View(modelo);
        }

        // resultado.Usuario es UsuarioSesion (sin hash). Aquí se crea la cookie de autenticación
        // (AddAuthentication().AddCookie(...) + SignInAsync con claims id, nombre, rol).
        return RedirectToAction("Index", "Home");
    }
}
```

Pendiente para cuando se implemente la sesión completa: `AddAuthentication().AddCookie()` con cookie `HttpOnly`, `Secure` y `SameSite=Strict`, `[Authorize(Roles = "...")]` en Mis viajes / Cuenta / Panel interno, y bloqueo temporal tras varios intentos fallidos (rate limiting).

---

## 9. Seguridad en GitHub (`.gitignore`, `bin/`, `obj/`)

- `.gitignore` ahora ignora: `bin/`, `obj/`, `.vs/`, `.vscode/`, `*.user`, `*.suo`, `appsettings.*.local.json`, `.env`, `secrets.json`, `*.pfx`, `*.publishsettings`.
- Las carpetas `bin/` y `obj/` estaban versionadas (143 archivos). Para sacarlas del rastreo **sin borrarlas del disco** (una sola vez, la hace quien suba este cambio):

  ```powershell
  git rm -r --cached bin obj
  git commit -m "Dejar de versionar bin/ y obj/"
  ```
  Después de hacer `git pull` de ese commit, a los demás integrantes Git les borrará sus `bin/` y `obj/` locales; no pasa nada: se regeneran al compilar (`dotnet build`).
- Antes de cada commit, revisar con `git status` / `git diff --cached` que no vaya ninguna cadena con `Password=` ni archivos `secrets.json`.
- Si alguna vez se sube una contraseña por error: **cambiarla de inmediato** en SQL Server (borrar el commit no basta, queda en el historial de GitHub).

---

## 10. Compatibilidad de los modelos actuales con EF Core

Actualizado el 25/10/2026 (Paso 0 de `docs/06`). Desde los cambios de Yamil del 21–24/10, los modelos usan los nombres definitivos y **`sql/01_esquema.sql` nombra cada columna igual que su propiedad**, así que el `DbContext` ya no necesita parches de nombres.

| Situación en el código | Cómo se resuelve |
|---|---|
| IDs `ulong` / `ushort` vs `bigint` / `smallint` con signo | Conversores globales en `ConfigureConventions` (`ulong↔long`, `ushort↔short`); también para `ulong?` / `ushort?`. |
| Propiedades en snake_case | Coinciden con las columnas (incluidos `id_usuarioglobal`, `id_usuariog`, `fecha_inicio`, `tipo_proveedor`, `creado_en`). Ya no hay `HasColumnName`. |
| Nombres de tabla (`roles_usuarios`, `usuarios`, `paquete`, `detalle_reservacion`…) | `ToTable(...)` explícito para las 26 entidades. La clase `UsuariosGlobales` se guarda en la tabla `usuarios`. |
| Subtipos `vuelos`, `hospedajes`, `vehiculos`, `paquete` | PK = `id_servicio` (también FK a `servicios`) con `ValueGeneratedNever`: primero se inserta el servicio y después el subtipo con el mismo id. |
| `paquete_servicio`, `promocion_servicio` | PK compuestas sin autoincremento. |
| `decimal` | `decimal(12,2)` por defecto (también `precio_base`); `cantidad` `(8,2)`. |
| `DateTime` / `DateOnly` / `TimeOnly` | `datetime` / `date` / `time`. |
| Fechas de control (`creado_en`, `actualizada_en`, `fecha_cambio`, `fecha_solicitud`…) | `HasDefaultValueSql("CURRENT_TIMESTAMP")`: si el código no las asigna, las llena SQL Server (evita guardar `0001-01-01`). |
| Columnas de texto que aceptan NULL (`descripcion`, `alias`, `detalle`, `referencia_externa`…) | `IsRequired(false)` o `string?`. |
| `Ubicaciones.correo` | No es parte del diseño: `Ignore(...)`, no tiene columna. |

**Pendientes de los modelos** (decisiones del equipo; el esquema refleja los modelos tal como están):
- `pagos.id_tarjeta` es `ulong` (obligatorio): hoy no se puede registrar un pago sin tarjeta (transferencia, efectivo). Si se aceptan, cambiar a `ulong?` y la columna a `NULL`.
- `paquete.max_personas` es `ushort` (obligatorio) aunque el diseño lo marca opcional.
- `usuarios.actualizado_en` y `reservaciones.actualizada_en` (diseño) no existen en los modelos; `vehiculos.transmision` y las columnas de dirección de `ubicaciones` (colonia, calle…) tampoco.
- La base **real** debe crearse con estos mismos scripts (o migrarse con `03_alinear_nombres_legado.sql` + `/Home/TestDb`) antes de cambiar `DatabaseMode` a `Real`.
