/* =====================================================================================
   FlyVibe Mex · 03_alinear_nombres_legado.sql
   -------------------------------------------------------------------------------------
   Para una base de datos que se creó con un script ANTERIOR (por ejemplo la base real
   `FlyVibeMex` hecha con "script base de datos.sql"). Renombra tablas y columnas a los
   nombres que usan hoy los modelos C# y FlyVibeMexDbContext.

   - Solo RENOMBRA (sp_rename). No borra tablas, columnas ni datos.
   - Cada paso es condicional: si el nombre viejo no existe o el nuevo ya existe, no hace nada.
   - Se puede ejecutar varias veces.

   Qué NO arregla (requiere recrear con sql/01_esquema.sql o una migración a mano):
   diferencias de ESTRUCTURA, por ejemplo hospedajes/vehiculos con `id` identity propio y sin
   `id_servicio`, paquete con PK (id, id_servicio), tablas faltantes (vuelos,
   historial_estado_reservacion) o columnas faltantes (promociones.nombre, minimo_compra…).

   Después de ejecutarlo: correr la app y abrir /Home/TestDb; la lista "tablas" dice
   exactamente qué tabla sigue sin coincidir y por qué.

   Antes de usarlo en la base real: HAZ UN RESPALDO (clic derecho en la base > Tareas > Copia de seguridad).
   ===================================================================================== */

-- Cambia el nombre si tu base se llama distinto.
USE FlyVibeMex;
GO

IF DB_NAME() NOT IN (N'FlyVibeMex', N'FlyVibeMex_Test')
BEGIN
    RAISERROR(N'Ejecuta este script sobre FlyVibeMex o FlyVibeMex_Test. Ejecución detenida.', 16, 1);
    SET NOEXEC ON;
END
GO

/* ---------- Tabla de usuarios: el DbContext la mapea como `usuarios` ---------- */
IF OBJECT_ID(N'dbo.UsuariosGlobales', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.usuarios', N'U') IS NULL
BEGIN
    EXEC sp_rename N'dbo.UsuariosGlobales', N'usuarios';
    PRINT N'Tabla UsuariosGlobales -> usuarios';
END
IF OBJECT_ID(N'dbo.usuarios_globales', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.usuarios', N'U') IS NULL
BEGIN
    EXEC sp_rename N'dbo.usuarios_globales', N'usuarios';
    PRINT N'Tabla usuarios_globales -> usuarios';
END
IF OBJECT_ID(N'dbo.roles_usuario', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.roles_usuarios', N'U') IS NULL
BEGIN
    EXEC sp_rename N'dbo.roles_usuario', N'roles_usuarios';
    PRINT N'Tabla roles_usuario -> roles_usuarios';
END
GO

/* ---------- Columnas ---------- */
DECLARE @cambios TABLE (orden INT IDENTITY, tabla SYSNAME, viejo SYSNAME, nuevo SYSNAME);
INSERT INTO @cambios (tabla, viejo, nuevo) VALUES
    (N'proveedores',         N'tipo_proveerdor', N'tipo_proveedor'),
    (N'proveedores',         N'tipo_prooverdor', N'tipo_proveedor'),
    (N'ubicaciones',         N'creado',          N'creado_en'),
    (N'ubicaciones',         N'actualizado',     N'actualizado_en'),
    (N'reservaciones',       N'id_usuario',      N'id_usuarioglobal'),
    (N'solicitudes_soporte', N'id_usuario',      N'id_usuarioglobal'),
    (N'tarjetas_usuarios',   N'id_usuario',      N'id_usuariog'),
    (N'tarjetas_usuarios',   N'id_usuarioglobal',N'id_usuariog'),
    (N'detalle_reservacion', N'fehca_inicio',    N'fecha_inicio'),
    (N'hospedajes',          N'tipo_ubicacion',  N'tipo_habitacion'),
    (N'servicios',           N'Moneda',          N'moneda');

DECLARE @i INT = 1, @n INT = (SELECT COUNT(*) FROM @cambios);
DECLARE @tabla SYSNAME, @viejo SYSNAME, @nuevo SYSNAME, @objeto NVARCHAR(400);

WHILE @i <= @n
BEGIN
    SELECT @tabla = tabla, @viejo = viejo, @nuevo = nuevo FROM @cambios WHERE orden = @i;

    -- sys.columns compara con la intercalación de la base: 'Moneda' y 'moneda' son el mismo nombre
    -- en una base que no distingue mayúsculas; en ese caso no hace falta renombrar.
    IF OBJECT_ID(N'dbo.' + @tabla, N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.' + @tabla, @viejo) IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM sys.columns
                       WHERE object_id = OBJECT_ID(N'dbo.' + @tabla) AND name = @nuevo COLLATE Latin1_General_BIN)
       AND (COL_LENGTH(N'dbo.' + @tabla, @nuevo) IS NULL OR @viejo = @nuevo COLLATE Latin1_General_CI_AI)
    BEGIN
        SET @objeto = N'dbo.' + @tabla + N'.' + @viejo;
        EXEC sp_rename @objeto, @nuevo, N'COLUMN';
        PRINT N'Columna ' + @tabla + N'.' + @viejo + N' -> ' + @nuevo;
    END

    SET @i += 1;
END
GO

PRINT N'Listo. Abre /Home/TestDb para ver si quedan diferencias de estructura.';
GO

SET NOEXEC OFF;
GO
