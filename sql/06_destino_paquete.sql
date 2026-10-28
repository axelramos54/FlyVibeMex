/* =====================================================================================
   FlyVibe Mex · 06_destino_paquete.sql                                      (T3 fase B · 28-10-2026)
   paquete + id_ubicacion_destino BIGINT NULL (FK -> ubicaciones.id)
       Destino explícito del paquete. Es obligatorio cuando sus servicios están en ciudades distintas
       (lo validará el formulario de paquetes del gestor, T4.4). Si es NULL, el catálogo usa la ciudad de su
       hospedaje incluido o, si no tiene, el destino de su vuelo incluido.

   ¿Cuándo ejecutarlo?
       Solo en una FlyVibeMex_Test creada ANTES del 28-10-2026. Si creas la base desde cero (00 -> 01 -> 02)
       NO hace falta. Después, volver a correr 02_seed_demo.sql: el seed también corrige la convención de
       detalle_reservacion (cantidad = unidades por día; precio_unitario = precio por todo el periodo).
   Es idempotente. Después: /Home/TestDb -> "esquemaCompleto": true.
   ===================================================================================== */
USE FlyVibeMex_Test;
GO

IF DB_NAME() <> N'FlyVibeMex_Test'
BEGIN
    RAISERROR(N'Este script solo debe ejecutarse sobre FlyVibeMex_Test. Ejecución detenida.', 16, 1);
    SET NOEXEC ON;
END
GO

IF COL_LENGTH(N'dbo.paquete', N'id_ubicacion_destino') IS NULL
    ALTER TABLE dbo.paquete ADD id_ubicacion_destino BIGINT NULL
        CONSTRAINT fk_paquete_destino REFERENCES dbo.ubicaciones(id);
GO

SELECT TABLE_NAME AS tabla, COLUMN_NAME AS columna, DATA_TYPE AS tipo, IS_NULLABLE AS acepta_null
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = N'paquete' AND COLUMN_NAME = N'id_ubicacion_destino';   -- debe salir 1 fila
GO

SET NOEXEC OFF;
GO
