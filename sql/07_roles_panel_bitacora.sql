/* =====================================================================================
   FlyVibe Mex · 07_roles_panel_bitacora.sql
   -------------------------------------------------------------------------------------
   1) Roles del panel (además de CLIENTE):
        SOPORTE        solo atiende tickets de soporte
        AGENTE         gestiona reservaciones y reembolsos
        ADMINISTRADOR  todo lo anterior + Gestor de ventas (proveedores, servicios, paquetes, promociones)
        SA             todo lo del ADMINISTRADOR + usuarios y roles, catálogos base, bitácora y contenido del sitio
   2) Cuentas de prueba soporte.prueba y sa.prueba (contraseña FlyVibe#Prueba2026, solo para esta base).
      Los tickets que atendía agente.prueba pasan a soporte.prueba (el AGENTE ya no atiende tickets).
   3) Tabla bitacora: quién cambió qué y cuándo en el panel (la consulta el SA).

   ¿Cuándo ejecutarlo?
       En una FlyVibeMex_Test creada antes de este script. Si creas la base desde cero (00 -> 01 -> 02)
       NO hace falta: 00, 01 y 02 ya incluyen todo esto.
   Es idempotente (se puede correr varias veces). Después: /Home/TestDb -> "esquemaCompleto": true.
   ===================================================================================== */
USE FlyVibeMex_Test;
GO

IF DB_NAME() <> N'FlyVibeMex_Test'
BEGIN
    RAISERROR(N'Este script solo debe ejecutarse sobre FlyVibeMex_Test. Ejecución detenida.', 16, 1);
    SET NOEXEC ON;
END
GO

SET XACT_ABORT ON;
BEGIN TRANSACTION;

/* ---------- 1) Roles ---------- */
IF NOT EXISTS (SELECT 1 FROM dbo.roles_usuarios WHERE nombre = 'SOPORTE')
    INSERT INTO dbo.roles_usuarios (nombre, descripcion, activo)
    VALUES ('SOPORTE', 'Atiende los tickets de soporte de los clientes', 1);

IF NOT EXISTS (SELECT 1 FROM dbo.roles_usuarios WHERE nombre = 'SA')
    INSERT INTO dbo.roles_usuarios (nombre, descripcion, activo)
    VALUES ('SA', 'Super administrador: todo el panel, usuarios, catalogos base y bitacora', 1);

UPDATE dbo.roles_usuarios SET descripcion = 'Gestiona reservaciones y reembolsos de los clientes' WHERE nombre = 'AGENTE';
UPDATE dbo.roles_usuarios SET descripcion = 'Panel interno y Gestor de ventas (catalogo)'      WHERE nombre = 'ADMINISTRADOR';

/* ---------- 2) Cuentas de prueba (mismo hash PasswordHasher V3 de "FlyVibe#Prueba2026" que agente.prueba) ---------- */
IF NOT EXISTS (SELECT 1 FROM dbo.usuarios WHERE correo = 'soporte.prueba@flyvibemex.test')
    INSERT INTO dbo.usuarios (id_rol, nombre, apellido_paterno, apellido_materno, correo, telefono, password_hash, activo)
    VALUES ((SELECT id FROM dbo.roles_usuarios WHERE nombre = 'SOPORTE'),
            'Soporte', 'Prueba', 'FlyVibe', 'soporte.prueba@flyvibemex.test', '9610000006',
            'AQAAAAIAAYagAAAAEKrTa4pzbnSHVkqn1+0EcRQQhceq2uQeDEnxY6z6Axl4Obp+Xg5gjSKkHMhQeEL0Zw==', 1);

IF NOT EXISTS (SELECT 1 FROM dbo.usuarios WHERE correo = 'sa.prueba@flyvibemex.test')
    INSERT INTO dbo.usuarios (id_rol, nombre, apellido_paterno, apellido_materno, correo, telefono, password_hash, activo)
    VALUES ((SELECT id FROM dbo.roles_usuarios WHERE nombre = 'SA'),
            'Super', 'Admin', 'FlyVibe', 'sa.prueba@flyvibemex.test', '9610000007',
            'AQAAAAIAAYagAAAAEKrTa4pzbnSHVkqn1+0EcRQQhceq2uQeDEnxY6z6Axl4Obp+Xg5gjSKkHMhQeEL0Zw==', 1);

DECLARE @uAgente  BIGINT = (SELECT id FROM dbo.usuarios WHERE correo = 'agente.prueba@flyvibemex.test');
DECLARE @uSoporte BIGINT = (SELECT id FROM dbo.usuarios WHERE correo = 'soporte.prueba@flyvibemex.test');

IF OBJECT_ID(N'dbo.solicitudes_soporte', N'U') IS NOT NULL AND @uAgente IS NOT NULL
    UPDATE dbo.solicitudes_soporte SET id_agente_asignado = @uSoporte WHERE id_agente_asignado = @uAgente;

COMMIT TRANSACTION;
GO

/* ---------- 3) Bitácora ---------- */
IF OBJECT_ID(N'dbo.bitacora', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.bitacora(
        id          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_bitacora PRIMARY KEY,
        fecha       DATETIME      NOT NULL CONSTRAINT df_bitacora_fecha DEFAULT(CURRENT_TIMESTAMP),
        id_usuario  BIGINT        NULL     CONSTRAINT fk_bitacora_usuarios REFERENCES dbo.usuarios(id),
        accion      NVARCHAR(30)  NOT NULL,   -- CREAR, ACTUALIZAR, BAJA, REACTIVAR, DISPONIBILIDAD…
        entidad     NVARCHAR(40)  NOT NULL,   -- proveedores, servicios, disponibilidad_servicio…
        id_entidad  BIGINT        NULL,
        detalle     NVARCHAR(500) NULL
    );
    CREATE INDEX ix_bitacora_fecha   ON dbo.bitacora(fecha);
    CREATE INDEX ix_bitacora_entidad ON dbo.bitacora(entidad, id_entidad);
END
GO

/* ---------- Verificación ---------- */
SELECT r.nombre AS rol, COUNT(u.id) AS usuarios
FROM dbo.roles_usuarios AS r
LEFT JOIN dbo.usuarios AS u ON u.id_rol = r.id
GROUP BY r.nombre
ORDER BY r.nombre;

SELECT CASE WHEN OBJECT_ID(N'dbo.bitacora', N'U') IS NULL THEN 'FALTA' ELSE 'OK' END AS tabla_bitacora;
GO

SET NOEXEC OFF;
GO
