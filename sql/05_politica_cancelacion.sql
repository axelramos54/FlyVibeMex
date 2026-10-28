/* =====================================================================================
   FlyVibe Mex · 05_politica_cancelacion.sql  
   La política de cancelación la define el gestor POR SERVICIO y se copia a cada compra:
       servicios            + permite_cancelacion, horas_reembolso_total, porcentaje_reembolso_tardio,
                              politica_cancelacion (texto para el cliente)
       detalle_reservacion  + las mismas 4 columnas (snapshot al comprar, regla R4)
       paquete              - politica_cancelacion (su texto pasa a servicios.politica_cancelacion)
   Regla de reembolso: 100 % si se cancela con al menos horas_reembolso_total de anticipación al
   inicio; después, porcentaje_reembolso_tardio. Ya iniciado, o con permite_cancelacion = 0: no se cancela.

   ¿Cuándo ejecutarlo?
       Solo en una FlyVibeMex_Test creada ANTES del 26-10-2026 por la tarde.
       Si creas la base desde cero (00 -> 01 -> 02) NO hace falta.
       Recomendado después de ejecutarlo: volver a correr 02_seed_demo.sql (trae políticas distintas
       por servicio y los 3 Tours de ejemplo).
   Es idempotente: se puede ejecutar varias veces sin error.
   Después: abrir /Home/TestDb -> "esquemaCompleto": true.
   ===================================================================================== */
USE FlyVibeMex_Test;
GO

IF DB_NAME() <> N'FlyVibeMex_Test'
BEGIN
    RAISERROR(N'Este script solo debe ejecutarse sobre FlyVibeMex_Test. Ejecución detenida.', 16, 1);
    SET NOEXEC ON;
END
GO

/* ---------- servicios: columnas de la política ---------- */
IF COL_LENGTH(N'dbo.servicios', N'permite_cancelacion') IS NULL
    ALTER TABLE dbo.servicios ADD permite_cancelacion BIT NOT NULL
        CONSTRAINT df_servicios_permite_cancelacion DEFAULT(1);
IF COL_LENGTH(N'dbo.servicios', N'horas_reembolso_total') IS NULL
    ALTER TABLE dbo.servicios ADD horas_reembolso_total INT NOT NULL
        CONSTRAINT df_servicios_horas_reembolso DEFAULT(48);
IF COL_LENGTH(N'dbo.servicios', N'porcentaje_reembolso_tardio') IS NULL
    ALTER TABLE dbo.servicios ADD porcentaje_reembolso_tardio DECIMAL(5,2) NOT NULL
        CONSTRAINT df_servicios_pct_tardio DEFAULT(50);
IF COL_LENGTH(N'dbo.servicios', N'politica_cancelacion') IS NULL
    ALTER TABLE dbo.servicios ADD politica_cancelacion NVARCHAR(500) NULL;
GO

IF OBJECT_ID(N'dbo.ck_servicios_horas_reembolso', N'C') IS NULL
    ALTER TABLE dbo.servicios ADD CONSTRAINT ck_servicios_horas_reembolso CHECK (horas_reembolso_total BETWEEN 0 AND 8760);
IF OBJECT_ID(N'dbo.ck_servicios_pct_tardio', N'C') IS NULL
    ALTER TABLE dbo.servicios ADD CONSTRAINT ck_servicios_pct_tardio CHECK (porcentaje_reembolso_tardio BETWEEN 0 AND 100);
GO

/* ---------- paquete: su texto de política pasa a servicios y se borra la columna ---------- */
IF COL_LENGTH(N'dbo.paquete', N'politica_cancelacion') IS NOT NULL
BEGIN
    -- SQL dinámico: si la columna ya no existe, este UPDATE no se compila (por eso no va directo).
    EXEC sys.sp_executesql N'
        UPDATE s SET s.politica_cancelacion = LEFT(p.politica_cancelacion, 500)
        FROM dbo.servicios AS s
        JOIN dbo.paquete AS p ON p.id_servicio = s.id
        WHERE s.politica_cancelacion IS NULL AND p.politica_cancelacion IS NOT NULL;';
    ALTER TABLE dbo.paquete DROP COLUMN politica_cancelacion;
END
GO

/* ---------- detalle_reservacion: snapshot de la política ---------- */
IF COL_LENGTH(N'dbo.detalle_reservacion', N'permite_cancelacion') IS NULL
BEGIN
    ALTER TABLE dbo.detalle_reservacion ADD
        permite_cancelacion         BIT          NOT NULL CONSTRAINT df_detalle_permite_cancelacion DEFAULT(1),
        horas_reembolso_total       INT          NOT NULL CONSTRAINT df_detalle_horas_reembolso DEFAULT(48),
        porcentaje_reembolso_tardio DECIMAL(5,2) NOT NULL CONSTRAINT df_detalle_pct_tardio DEFAULT(50),
        politica_cancelacion        NVARCHAR(500) NULL;

    -- Las compras que ya existían toman la política ACTUAL de su servicio (solo esta vez).
    EXEC sys.sp_executesql N'
        UPDATE d SET d.permite_cancelacion = s.permite_cancelacion,
                     d.horas_reembolso_total = s.horas_reembolso_total,
                     d.porcentaje_reembolso_tardio = s.porcentaje_reembolso_tardio,
                     d.politica_cancelacion = s.politica_cancelacion
        FROM dbo.detalle_reservacion AS d
        JOIN dbo.servicios AS s ON s.id = d.id_servicio;';
END
GO

IF OBJECT_ID(N'dbo.ck_detalle_horas_reembolso', N'C') IS NULL
    ALTER TABLE dbo.detalle_reservacion ADD CONSTRAINT ck_detalle_horas_reembolso CHECK (horas_reembolso_total BETWEEN 0 AND 8760);
IF OBJECT_ID(N'dbo.ck_detalle_pct_tardio', N'C') IS NULL
    ALTER TABLE dbo.detalle_reservacion ADD CONSTRAINT ck_detalle_pct_tardio CHECK (porcentaje_reembolso_tardio BETWEEN 0 AND 100);
GO

/* ---------- Verificación ---------- */
SELECT TABLE_NAME AS tabla, COLUMN_NAME AS columna, DATA_TYPE AS tipo
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME IN (N'servicios', N'detalle_reservacion', N'paquete')
  AND COLUMN_NAME IN (N'permite_cancelacion', N'horas_reembolso_total', N'porcentaje_reembolso_tardio', N'politica_cancelacion')
ORDER BY TABLE_NAME, COLUMN_NAME;   -- deben salir 8 filas (4 de servicios y 4 de detalle_reservacion; 0 de paquete)
GO

SET NOEXEC OFF;
GO
