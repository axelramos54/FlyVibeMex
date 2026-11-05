/* =====================================================================================
   FlyVibe Mex · 04_log_errores.sql                                     (script aditivo)
   -------------------------------------------------------------------------------------
   Crea la tabla dbo.LogError: el registro de errores de los procedimientos almacenados.

   ¿Cuándo usar este script y no 01_crear_base_datos.sql?
       Cuando la base YA EXISTE y no se puede volver a crear (el caso de somee: la base está en
       producción y tiene datos). Este script es ADITIVO:
         - no borra ni modifica ninguna tabla, fila ni procedimiento;
         - no lleva USE ni CREATE DATABASE, así que funciona en somee tal cual;
         - se puede ejecutar las veces que haga falta: si la tabla ya existe, no hace nada.

   Orden de ejecución en una base que ya existe:
       1) este script  (crea dbo.LogError)
       2) sql/02_procedimientos_almacenados.sql  (los 28 procedimientos con BEGIN TRY / BEGIN CATCH)
   En una base nueva no hace falta: 01_crear_base_datos.sql ya crea la tabla.

   Requiere SQL Server 2016 SP1 o posterior.
   ===================================================================================== */

SET NOCOUNT ON;
GO

/* Candado: la base destino es la que está conectada. Nunca una base del sistema. */
IF DB_NAME() IN (N'master', N'tempdb', N'model', N'msdb')
BEGIN
    /* El mensaje se arma en una variable porque RAISERROR no acepta funciones como argumento. */
    DECLARE @aviso NVARCHAR(300) = CONCAT(N'Estás conectado a la base del sistema "', DB_NAME(),
                                          N'". Conéctate a la base de FlyVibeMex y repite.');
    RAISERROR(@aviso, 16, 1);
    SET NOEXEC ON;
END
GO

PRINT N'Base de datos destino: ' + DB_NAME();
GO

/* ---------- La tabla ----------
   Las columnas son exactamente lo que devuelven las funciones ERROR_* dentro de un bloque CATCH,
   más la fecha y hora en que se registró.
   El nombre va en PascalCase (LogError) por requisito de la entrega; el resto del modelo usa snake_case. */
IF OBJECT_ID(N'dbo.LogError', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.LogError(
        id                  BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_logerror PRIMARY KEY,
        error_numero        INT            NOT NULL,   -- ERROR_NUMBER()
        error_severidad     INT            NOT NULL,   -- ERROR_SEVERITY()
        error_estado        INT            NOT NULL,   -- ERROR_STATE()
        error_procedimiento NVARCHAR(128)  NULL,       -- ERROR_PROCEDURE(): NULL si el error no nació dentro de un procedimiento
        error_linea         INT            NOT NULL,   -- ERROR_LINE()
        error_mensaje       NVARCHAR(4000) NOT NULL,   -- ERROR_MESSAGE()
        fecha               DATETIME       NOT NULL CONSTRAINT df_logerror_fecha DEFAULT(GETDATE())
    );

    CREATE INDEX ix_logerror_fecha         ON dbo.LogError(fecha DESC);
    CREATE INDEX ix_logerror_procedimiento ON dbo.LogError(error_procedimiento);

    PRINT N'Tabla dbo.LogError creada.';
END
ELSE
    PRINT N'La tabla dbo.LogError ya existía: no se tocó nada.';
GO

/* ---------- Verificación ---------- */
SELECT DB_NAME() AS base_de_datos,
       CASE WHEN OBJECT_ID(N'dbo.LogError', N'U') IS NULL THEN N'FALTA' ELSE N'LISTA' END AS tabla_logerror,
       (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.LogError')) AS columnas,   -- 8
       (SELECT COUNT(*) FROM dbo.LogError) AS errores_registrados;
GO

SET NOEXEC OFF;
GO

/* =====================================================================================
   Consultas útiles (quitar el comentario para usarlas)
   =====================================================================================

-- Los últimos 50 errores
SELECT TOP (50) id, fecha, error_procedimiento, error_numero, error_linea, error_mensaje
FROM dbo.LogError
ORDER BY id DESC;

-- Qué procedimiento falla más
SELECT error_procedimiento, COUNT(*) AS veces, MAX(fecha) AS ultimo
FROM dbo.LogError
GROUP BY error_procedimiento
ORDER BY veces DESC;

-- Separar los errores de negocio (THROW 51xxx, que son respuestas esperadas: "no hay cupo", "sin permiso")
-- de los errores reales de SQL Server, que son los que hay que revisar.
SELECT CASE WHEN error_numero BETWEEN 51000 AND 51999 THEN N'Regla de negocio' ELSE N'Error de SQL Server' END AS clase,
       COUNT(*) AS veces
FROM dbo.LogError
GROUP BY CASE WHEN error_numero BETWEEN 51000 AND 51999 THEN N'Regla de negocio' ELSE N'Error de SQL Server' END;

-- Prueba de que el registro funciona: este EXEC falla a propósito (THROW 51090) y deja una fila.
EXEC dbo.pa_tomar_ticket @id_usuario = 1, @id_ticket = 999999;
SELECT TOP (5) * FROM dbo.LogError ORDER BY id DESC;

-- Limpiar el registro (solo si hace falta; no borra nada más)
-- DELETE FROM dbo.LogError WHERE fecha < DATEADD(DAY, -30, GETDATE());

   ===================================================================================== */
