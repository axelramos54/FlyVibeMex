    /* =====================================================================================
   FlyVibe Mex · 00_db_prueba_conexion.sql
   -------------------------------------------------------------------------------------
   Crea la base de datos de PRUEBA `FlyVibeMex_Test` con las tablas mínimas para probar
   la conexión con Entity Framework Core y el flujo de inicio de sesión:
       - roles_usuarios
       - usuarios
   Las columnas son las mismas del script original del equipo (tipos T-SQL de SQL Server),
   con dos ajustes para que EF Core lea los datos sin errores:
       - usuarios.id_rol y usuarios.activo son NOT NULL (en C# son ulong y bool, no nullables).
       - restricciones UNIQUE/CHECK con nombre.

   Cómo ejecutarlo (SSMS): abrir este archivo, conectarse a tu servidor local y presionar F5.
   Se puede ejecutar varias veces: borra y vuelve a crear SOLO las tablas de FlyVibeMex_Test.
   Si ya habías ejecutado sql/01_esquema.sql, también borra esas 24 tablas (dependen de
   usuarios): después vuelve a ejecutar 01_esquema.sql y 02_seed_demo.sql.

   Orden completo:  00_db_prueba_conexion.sql -> 01_esquema.sql -> 02_seed_demo.sql

   Datos semilla de prueba:
       Contraseña de prueba de los 3 usuarios: FlyVibe#Prueba2026
       (es SOLO para esta base de prueba; nunca usarla en la base real ni en otro sistema).
       password_hash = hash de ASP.NET Core Identity PasswordHasher<UsuariosGlobales> formato V3
       (PBKDF2-HMAC-SHA512, 100 000 iteraciones, sal aleatoria de 128 bits). La contraseña
       en texto plano NO se guarda en ninguna tabla (regla R10).

   NOTA: este script NO modifica la base real `FlyVibeMex`.
   ===================================================================================== */

USE master;
GO

IF DB_ID(N'FlyVibeMex_Test') IS NULL
BEGIN
    CREATE DATABASE FlyVibeMex_Test;
END
GO

USE FlyVibeMex_Test;
GO

/* Candado de seguridad: si por error el USE anterior no se aplicó, no se ejecuta nada más
   (evita borrar tablas en otra base de datos). */
IF DB_NAME() <> N'FlyVibeMex_Test'
BEGIN
    RAISERROR(N'Este script solo debe ejecutarse sobre FlyVibeMex_Test. Ejecución detenida.', 16, 1);
    SET NOEXEC ON;
END
GO

/* ---------- Limpieza (solo en la base de prueba) ---------- */
/* Primero las tablas de sql/01_esquema.sql (si existen), porque tienen FK hacia usuarios. */
DROP TABLE IF EXISTS dbo.solicitudes_soporte;
DROP TABLE IF EXISTS dbo.historial_estado_reembolso;
DROP TABLE IF EXISTS dbo.reembolsos;
DROP TABLE IF EXISTS dbo.historial_estado_pago;
DROP TABLE IF EXISTS dbo.pagos;
DROP TABLE IF EXISTS dbo.estados_pago;
DROP TABLE IF EXISTS dbo.tarjetas_usuarios;
DROP TABLE IF EXISTS dbo.instituciones_financieras;
DROP TABLE IF EXISTS dbo.metodos_pago;
DROP TABLE IF EXISTS dbo.historial_estado_reservacion;
DROP TABLE IF EXISTS dbo.detalle_reservacion;
DROP TABLE IF EXISTS dbo.reservaciones;
DROP TABLE IF EXISTS dbo.estados_reservacion;
DROP TABLE IF EXISTS dbo.promocion_servicio;
DROP TABLE IF EXISTS dbo.promociones;
DROP TABLE IF EXISTS dbo.disponibilidad_servicio;
DROP TABLE IF EXISTS dbo.paquete_servicio;
DROP TABLE IF EXISTS dbo.paquete;
DROP TABLE IF EXISTS dbo.vehiculos;
DROP TABLE IF EXISTS dbo.hospedajes;
DROP TABLE IF EXISTS dbo.vuelos;
DROP TABLE IF EXISTS dbo.servicios;
DROP TABLE IF EXISTS dbo.proveedores;
DROP TABLE IF EXISTS dbo.ubicaciones;
GO
IF OBJECT_ID(N'dbo.usuarios', N'U') IS NOT NULL
    DROP TABLE dbo.usuarios;
GO
IF OBJECT_ID(N'dbo.roles_usuarios', N'U') IS NOT NULL
    DROP TABLE dbo.roles_usuarios;
GO

/* ---------- Tablas ---------- */
CREATE TABLE dbo.roles_usuarios(
    id          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_roles_usuarios PRIMARY KEY,
    nombre      VARCHAR(30)  NOT NULL CONSTRAINT uq_roles_usuarios_nombre UNIQUE,
    descripcion VARCHAR(150) NULL,
    activo      BIT          NOT NULL CONSTRAINT df_roles_usuarios_activo DEFAULT(1)
);
GO

CREATE TABLE dbo.usuarios(
    id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_usuarios PRIMARY KEY,
    id_rol           BIGINT        NOT NULL CONSTRAINT fk_usuarios_roles REFERENCES dbo.roles_usuarios(id),
    nombre           VARCHAR(80)   NOT NULL,
    apellido_paterno VARCHAR(60)   NOT NULL,
    apellido_materno VARCHAR(60)   NOT NULL,
    correo           VARCHAR(150)  NOT NULL CONSTRAINT uq_usuarios_correo UNIQUE,
    telefono         VARCHAR(20)   NOT NULL CONSTRAINT uq_usuarios_telefono UNIQUE,
    password_hash    VARCHAR(255)  NOT NULL,
    activo           BIT           NOT NULL CONSTRAINT df_usuarios_activo DEFAULT(1),
    creado_en        DATETIME      NOT NULL CONSTRAINT df_usuarios_creado_en DEFAULT(CURRENT_TIMESTAMP),
    imagen_ruta      NVARCHAR(260) NULL,  -- ruta pública: /uploads/<carpeta>/<guid>.jpg|png|webp (T2.5)

    -- El correo se guarda normalizado (sin espacios y en minúsculas) para que el login sea consistente.
    CONSTRAINT ck_usuarios_correo_formato CHECK (correo LIKE '%_@_%._%' AND correo NOT LIKE '% %'),
    -- Un hash de PasswordHasher V3 mide 84 caracteres: impide guardar contraseñas en texto plano por error.
    CONSTRAINT ck_usuarios_password_hash CHECK (LEN(password_hash) >= 60)
);
GO

/* ---------- Datos semilla de prueba ---------- */
INSERT INTO dbo.roles_usuarios (nombre, descripcion, activo)
VALUES
    ('CLIENTE',       'Viajero que busca, reserva y paga servicios',            1),
    ('AGENTE',        'Personal de la agencia que gestiona reservas y soporte', 1),
    ('ADMINISTRADOR', 'Acceso al panel interno y catalogos',                     1);
GO

-- Los IDs de rol se buscan por nombre (no se asume que sean 1, 2, 3).
INSERT INTO dbo.usuarios
    (id_rol, nombre, apellido_paterno, apellido_materno, correo, telefono, password_hash, activo)
VALUES
    ((SELECT id FROM dbo.roles_usuarios WHERE nombre = 'ADMINISTRADOR'),
     'Admin', 'Prueba', 'FlyVibe', 'admin.prueba@flyvibemex.test', '9610000001',
     'AQAAAAIAAYagAAAAEEcduaxJK5ctZce6lsf3nV9E1mDNB37SmYZxuo0EAl0QcNcFMpA1FkIhgbiQsGzatw==', 1),

    ((SELECT id FROM dbo.roles_usuarios WHERE nombre = 'CLIENTE'),
     'Cliente', 'Prueba', 'FlyVibe', 'cliente.prueba@flyvibemex.test', '9610000002',
     'AQAAAAIAAYagAAAAEPBRFDFALgzNysnpFQjwX1a2N8JeVpY6hz8nmsLgpxI5ru+j9wTq7wljn9cmB7kkvw==', 1),

    -- Usuario INACTIVO: sirve para comprobar que el login lo rechaza aunque la contraseña sea correcta.
    ((SELECT id FROM dbo.roles_usuarios WHERE nombre = 'CLIENTE'),
     'Inactivo', 'Prueba', 'FlyVibe', 'inactivo.prueba@flyvibemex.test', '9610000003',
     'AQAAAAIAAYagAAAAEPTwt2YbGnbZ2WWeyCJVkV56vYBAnVrD5M8bmx8WvzwHQNtraVrm3FCdk25W3CLoYQ==', 0);
GO

/* ---------- Verificación rápida ---------- */
SELECT r.nombre AS rol, COUNT(u.id) AS usuarios
FROM dbo.roles_usuarios AS r
LEFT JOIN dbo.usuarios AS u ON u.id_rol = r.id
GROUP BY r.nombre
ORDER BY r.nombre;
GO

SET NOEXEC OFF;
GO
