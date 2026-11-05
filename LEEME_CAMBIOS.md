# FlyVibeMex · Cambios del 05/11/2026

Dos cosas: **tabla `LogError` con TRY/CATCH en los 28 procedimientos** y **panel de estadísticas de ventas en el SA**.

Los archivos de este ZIP van en las mismas rutas del proyecto (se copian encima).

---

## 1. Base de datos · `LogError`

### Qué se hizo

- Tabla nueva `dbo.LogError` con exactamente lo que piden las funciones de error:
  `error_numero` (ERROR_NUMBER), `error_severidad` (ERROR_SEVERITY), `error_estado` (ERROR_STATE),
  `error_procedimiento` (ERROR_PROCEDURE), `error_linea` (ERROR_LINE), `error_mensaje` (ERROR_MESSAGE)
  y `fecha` (GETDATE), más un `id` autoincremental.
- Los **28 procedimientos** tienen ahora el cuerpo dentro de `BEGIN TRY … END TRY` + `BEGIN CATCH … END CATCH`.
  El cuerpo no se tocó: solo quedó indentado dentro del TRY.

### El CATCH, igual en los 28

```sql
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
    IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
        INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                  error_linea, error_mensaje, fecha)
        VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
    THROW;   -- el error sigue llegando a quien llamó: el comportamiento no cambia
END CATCH
```

Las tres líneas tienen motivo, por si lo preguntan:

1. **El ROLLBACK va primero.** Los procedimientos que escriben usan `SET XACT_ABORT ON`, así que un error
   dentro de la transacción la deja *condenada*: cualquier INSERT falla hasta deshacerla. Si se registrara
   antes, el registro se perdería junto con el resto.
2. **El `IF ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)`** evita filas repetidas cuando un procedimiento llama
   a otro: si el error nació en un `pa_interno_*`, ese ya lo registró, y quien lo llamó solo deshace y lo
   vuelve a lanzar. Cada error deja **una** fila, con el nombre y la línea donde nació.
3. **`THROW;` sin argumentos** vuelve a lanzar el MISMO error (número, mensaje, severidad y línea). Nada
   cambia para quien llama: los errores de negocio 51xxx se siguen recibiendo igual.

### Qué correr en SSMS

| Si la base… | Correr |
|---|---|
| se crea desde cero | `01_crear_base_datos.sql` → `02_procedimientos_almacenados.sql` → `03_seed_demo.sql` |
| ya existe (somee, con datos) | `04_log_errores.sql` → `EMERGENCIA_2_procedimientos.sql` |

- `04_log_errores.sql` es **aditivo**: no borra nada, no lleva `USE` (somee no lo acepta) y se puede correr
  varias veces. Si la tabla ya existe, no hace nada.
- `EMERGENCIA_2_procedimientos.sql` es la copia para somee del script 02: mismo cuerpo, sin `USE`, con el
  candado que solo comprueba que no sea una base del sistema.
- El script 02 se detiene con un aviso si `dbo.LogError` no existe todavía.

### Probar que funciona

```sql
EXEC dbo.pa_tomar_ticket @id_usuario = 1, @id_ticket = 999999;   -- falla con THROW 51090, como siempre
SELECT TOP (20) * FROM dbo.LogError ORDER BY id DESC;            -- y ahora quedó registrado
```

---

## 2. Panel de estadísticas de ventas (SA)

`/Admin/SA/Estadisticas`, con enlace en el menú del SA y en su pantalla de inicio.

Muestra, para el periodo que se elija: **vendido, reembolsado y neto**, una gráfica de barras por día
(por mes si el periodo pasa de 45 días), la tabla por tipo de servicio y los 10 servicios más vendidos.

Los números usan **las mismas reglas que `pa_reporte_ventas`**, para que el panel y el reporte de SSMS
coincidan: la venta se fecha en el primer pago cobrado (APROBADO o REEMBOLSADO), los importes son los
históricos de la reservación, y se restan los reembolsos PROCESADOS por su fecha de resolución.

**«Neto» no es ganancia** y la vista lo dice: la base no guarda el costo de cada servicio ni la comisión
del proveedor, así que lo que se muestra es ingreso menos devoluciones.

### Archivos

| Archivo | |
|---|---|
| `Models/Admin/EstadisticasViewModels.cs` | nuevo |
| `Services/SAEstadisticasService.cs` | nuevo |
| `Controllers/SAController.Estadisticas.cs` | nuevo (cuarta parte del controlador parcial) |
| `Views/Admin/SA/Estadisticas.cshtml` | nuevo |
| `Controllers/SAController.cs` | modificado: una dependencia más en el constructor |
| `Program.cs` | modificado: una línea de registro del servicio |
| `Views/Admin/SA/_SALayout.cshtml` | modificado: enlace en el menú |
| `Views/Admin/SA/Index.cshtml` | modificado: tarjeta de acceso |

Sin paquetes NuGet nuevos y sin JavaScript: la gráfica es un `<svg>` que arma la propia vista.

### Nada de esto toca la integración con GoldStay

El panel vive bajo `/Admin`, que tiene su propio `_ViewStart` y no está en `RutasEmbebibles`. No se tocaron
`Program.cs` (más allá de la línea de registro), middleware, cabeceras, SSO ni los widgets.
