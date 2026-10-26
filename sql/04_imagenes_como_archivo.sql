/* =====================================================================================
   FlyVibe Mex · 04_imagenes_como_archivo.sql                              (T2.5 · 26-10-2026)
   Las imágenes dejan de guardarse DENTRO de la base (VARBINARY) y pasan a ser archivos en
   wwwroot/uploads/. En la tabla solo queda la ruta pública del archivo:
       usuarios.imagen    VARBINARY(MAX)  ->  usuarios.imagen_ruta    NVARCHAR(260) NULL
       hospedajes.imagen  VARBINARY(MAX)  ->  hospedajes.imagen_ruta  NVARCHAR(260) NULL
       vehiculos.imagen   VARBINARY(MAX)  ->  vehiculos.imagen_ruta   NVARCHAR(260) NULL
       paquete.imagen     VARBINARY(MAX)  ->  paquete.imagen_ruta     NVARCHAR(260) NULL
   Ejemplo de valor: /uploads/hospedajes/3f2b9c0d4e6a4b1f9a7c2d5e8f0a1b2c.jpg

   ¿Cuándo ejecutarlo?
       Solo en una FlyVibeMex_Test creada ANTES del 26-10-2026 (con la columna `imagen`).
       Si creas la base desde cero (00 -> 01 -> 02) NO hace falta: esos scripts ya traen imagen_ruta.
   Es idempotente: se puede ejecutar varias veces sin error.
   ATENCIÓN: borra la columna `imagen` (las imágenes que hubiera guardadas en la BD se pierden;
   al 26-10-2026 ninguna pantalla guardaba imágenes, así que las columnas estaban vacías).
   Después: ejecutar la app y abrir /Home/TestDb -> "esquemaCompleto": true.
   ===================================================================================== */
USE FlyVibeMex_Test;
GO

/* Candado de seguridad: solo la base de prueba. */
IF DB_NAME() <> N'FlyVibeMex_Test'
BEGIN
    RAISERROR(N'Este script solo debe ejecutarse sobre FlyVibeMex_Test. Ejecución detenida.', 16, 1);
    SET NOEXEC ON;
END
GO

/* ---------- usuarios (foto de perfil) ---------- */
IF COL_LENGTH(N'dbo.usuarios', N'imagen') IS NOT NULL
    ALTER TABLE dbo.usuarios DROP COLUMN imagen;
IF COL_LENGTH(N'dbo.usuarios', N'imagen_ruta') IS NULL
    ALTER TABLE dbo.usuarios ADD imagen_ruta NVARCHAR(260) NULL;
GO

/* ---------- hospedajes ---------- */
IF COL_LENGTH(N'dbo.hospedajes', N'imagen') IS NOT NULL
    ALTER TABLE dbo.hospedajes DROP COLUMN imagen;
IF COL_LENGTH(N'dbo.hospedajes', N'imagen_ruta') IS NULL
    ALTER TABLE dbo.hospedajes ADD imagen_ruta NVARCHAR(260) NULL;
GO

/* ---------- vehiculos ---------- */
IF COL_LENGTH(N'dbo.vehiculos', N'imagen') IS NOT NULL
    ALTER TABLE dbo.vehiculos DROP COLUMN imagen;
IF COL_LENGTH(N'dbo.vehiculos', N'imagen_ruta') IS NULL
    ALTER TABLE dbo.vehiculos ADD imagen_ruta NVARCHAR(260) NULL;
GO

/* ---------- paquete ---------- */
IF COL_LENGTH(N'dbo.paquete', N'imagen') IS NOT NULL
    ALTER TABLE dbo.paquete DROP COLUMN imagen;
IF COL_LENGTH(N'dbo.paquete', N'imagen_ruta') IS NULL
    ALTER TABLE dbo.paquete ADD imagen_ruta NVARCHAR(260) NULL;
GO

/* ---------- Verificación: deben salir 4 filas con imagen_ruta y ninguna con imagen ---------- */
SELECT TABLE_NAME AS tabla, COLUMN_NAME AS columna, DATA_TYPE AS tipo, CHARACTER_MAXIMUM_LENGTH AS longitud
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME IN (N'usuarios', N'hospedajes', N'vehiculos', N'paquete')
  AND COLUMN_NAME IN (N'imagen', N'imagen_ruta')
ORDER BY TABLE_NAME;
GO

SET NOEXEC OFF;
GO
