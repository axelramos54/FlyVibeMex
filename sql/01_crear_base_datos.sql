/* =====================================================================================
   FlyVibe Mex · 01_crear_base_datos.sql                         (script 1 de 3)
   -------------------------------------------------------------------------------------
   Crea la base de datos de PRUEBA FlyVibeMex_Test COMPLETA:
       - las 31 tablas del modelo (una por clase de Models/), con sus PK, FK, UNIQUE, CHECK e índices;
       - los catálogos que la aplicación NECESITA para funcionar: roles, estados de reservación,
         estados de pago y métodos de pago;
       - una cuenta inicial SA (sa.prueba@flyvibemex.test) para poder entrar al panel.
   Los datos de demostración (ubicaciones, proveedores, servicios, reservaciones…) están en 03_seed_demo.sql.

   Orden de instalación (SSMS, F5 en cada archivo):
       1) 01_crear_base_datos.sql           (este archivo)
       2) 02_procedimientos_almacenados.sql
       3) 03_seed_demo.sql                  (opcional, pero recomendado para probar)
       4) Ejecutar la app y abrir /Home/TestDb: "esquemaCompleto" debe ser true.

   ATENCIÓN: BORRA Y VUELVE A CREAR TODAS LAS TABLAS de FlyVibeMex_Test (y sus datos).
   Nunca se ejecuta sobre la base real (FlyVibeMex): el candado de abajo lo impide.
   Requiere SQL Server 2016 SP1 o posterior.

   Regla de nombres: CADA COLUMNA SE LLAMA IGUAL QUE LA PROPIEDAD DEL MODELO C#
   (reservaciones.id_usuarioglobal, tarjetas_usuarios.id_usuariog, proveedores.tipo_proveedor…),
   así FlyVibeMexDbContext no necesita HasColumnName.
   Tipos: ulong -> BIGINT · ushort -> SMALLINT · int -> INT · byte -> TINYINT · bool -> BIT ·
          decimal -> DECIMAL(12,2) (cantidad: 8,2) · DateTime -> DATETIME · DateOnly -> DATE ·
          TimeOnly -> TIME · string -> NVARCHAR/VARCHAR (imagen_ruta: ruta del archivo en wwwroot, no los bytes).
   Una columna es NULL solo si la propiedad es nullable (ulong?, string?, DateTime?).

   Diseño (docs/01 y CONTEXTO_PROYECTO_FLYVIBEMEX.md):
       R1 subtipos vuelos / hospedajes / vehiculos / paquete con id_servicio como PK y FK.
       R2 paquete_servicio con PK (id_servicio, id_servicio_incluido).
       R3 disponibilidad_servicio con UNIQUE(id_servicio, fecha) y cupo 0 <= disponible <= total.
       R4 detalle_reservacion guarda el snapshot de precio, fechas, descripción y política de cancelación.
       R5 tarjetas_usuarios: solo token + marca + últimos 4 (nunca número completo ni CVV).
       R6 bitácoras de estado de reservación, pago y reembolso; bitacora del panel.
       R10 usuarios.password_hash: solo el hash de ASP.NET Core Identity (nunca la contraseña).
       UNIQUE de columnas opcionales como índice filtrado (WHERE col IS NOT NULL).

   Contraseña de la cuenta inicial (y de todas las del seed): FlyVibe#Prueba2026
   (SOLO para esta base de prueba). password_hash = PasswordHasher<UsuariosGlobales> formato V3
   (PBKDF2-HMAC-SHA512, 100 000 iteraciones, sal propia).
   ===================================================================================== */

USE master;
GO

IF DB_ID(N'FlyVibeMex_Test') IS NULL
    CREATE DATABASE FlyVibeMex_Test;
GO

USE FlyVibeMex_Test;
GO

/* Candado de seguridad: si por error el USE anterior no se aplicó, no se ejecuta nada más. */
IF DB_NAME() <> N'FlyVibeMex_Test'
BEGIN
    RAISERROR(N'Este script solo debe ejecutarse sobre FlyVibeMex_Test. Ejecución detenida.', 16, 1);
    SET NOEXEC ON;
END
GO

/* =====================================================================================
   Limpieza: quita TODAS las llaves foráneas y TODAS las tablas del esquema dbo
   (también las de scripts viejos con otros nombres, p. ej. UsuariosGlobales).
   Los procedimientos almacenados no se borran: 02_procedimientos_almacenados.sql usa CREATE OR ALTER.
   ===================================================================================== */
DECLARE @sql NVARCHAR(MAX) = N'';

SELECT @sql = @sql + N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name)
            + N' DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';' + NCHAR(10)
FROM sys.foreign_keys AS fk
JOIN sys.tables AS t ON t.object_id = fk.parent_object_id
WHERE t.schema_id = SCHEMA_ID(N'dbo');
EXEC sys.sp_executesql @sql;

SET @sql = N'';
SELECT @sql = @sql + N'DROP TABLE ' + QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name) + N';' + NCHAR(10)
FROM sys.tables
WHERE schema_id = SCHEMA_ID(N'dbo');
EXEC sys.sp_executesql @sql;
GO

/* =====================================================================================
   Usuarios y roles
   ===================================================================================== */
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
    imagen_ruta      NVARCHAR(260) NULL,  -- ruta pública: /uploads/<carpeta>/<guid>.jpg|png|webp

    -- El correo se guarda normalizado (sin espacios y en minúsculas) para que el login sea consistente.
    CONSTRAINT ck_usuarios_correo_formato CHECK (correo LIKE '%_@_%._%' AND correo NOT LIKE '% %'),
    -- Un hash de PasswordHasher V3 mide 84 caracteres: impide guardar contraseñas en texto plano por error.
    CONSTRAINT ck_usuarios_password_hash CHECK (LEN(password_hash) >= 60)
);
GO

/* =====================================================================================
   Ubicaciones y catálogo
   ===================================================================================== */
CREATE TABLE dbo.ubicaciones(
    id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_ubicaciones PRIMARY KEY,
    pais             NVARCHAR(60)  NOT NULL CONSTRAINT df_ubicaciones_pais DEFAULT(N'México'),
    estado           NVARCHAR(60)  NOT NULL,
    municipio_ciudad NVARCHAR(100) NOT NULL,
    codigo_postal    NVARCHAR(10)  NOT NULL,
    activo           BIT           NOT NULL CONSTRAINT df_ubicaciones_activo DEFAULT(1),
    creado_en        DATETIME      NOT NULL CONSTRAINT df_ubicaciones_creado_en DEFAULT(CURRENT_TIMESTAMP),
    actualizado_en   DATETIME      NOT NULL CONSTRAINT df_ubicaciones_actualizado_en DEFAULT(CURRENT_TIMESTAMP)
    -- La propiedad Ubicaciones.correo NO tiene columna (el DbContext la ignora: no es parte del diseño).
);
GO

CREATE TABLE dbo.proveedores(
    id             BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_proveedores PRIMARY KEY,
    id_ubicacion   BIGINT        NULL     CONSTRAINT fk_proveedores_ubicaciones REFERENCES dbo.ubicaciones(id),
    tipo_proveedor NVARCHAR(20)  NOT NULL CONSTRAINT ck_proveedores_tipo
                   CHECK (tipo_proveedor IN (N'AEROLINEA', N'HOTEL', N'RENTADORA', N'OTRO')),
    nombre         NVARCHAR(120) NOT NULL,
    telefono       NVARCHAR(20)  NOT NULL,
    correo         NVARCHAR(150) NOT NULL,
    activo         BIT           NOT NULL CONSTRAINT df_proveedores_activo DEFAULT(1),
    creado_en      DATETIME      NOT NULL CONSTRAINT df_proveedores_creado_en DEFAULT(CURRENT_TIMESTAMP),
    actualizado_en DATETIME      NOT NULL CONSTRAINT df_proveedores_actualizado_en DEFAULT(CURRENT_TIMESTAMP)
);
GO

/* Supertipo (R1): todo lo reservable es una fila aquí. EVENTO lo usa el panel del gestor de ventas. */
CREATE TABLE dbo.servicios(
    id             BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_servicios PRIMARY KEY,
    id_proveedor   BIGINT         NOT NULL CONSTRAINT fk_servicios_proveedores REFERENCES dbo.proveedores(id),
    tipo_servicio  NVARCHAR(20)   NOT NULL CONSTRAINT ck_servicios_tipo
                   CHECK (tipo_servicio IN (N'VUELO', N'HOSPEDAJE', N'VEHICULO', N'PAQUETE', N'EVENTO')),
    nombre         NVARCHAR(150)  NOT NULL,
    descripcion    NVARCHAR(2000) NULL,
    precio_base    DECIMAL(12,2)  NOT NULL CONSTRAINT ck_servicios_precio CHECK (precio_base >= 0),
    moneda         NVARCHAR(3)    NOT NULL CONSTRAINT df_servicios_moneda DEFAULT(N'MXN')
                   CONSTRAINT ck_servicios_moneda CHECK (moneda IN (N'MXN', N'USD', N'EUR')),
    -- Política de cancelación que define el gestor. Al comprar se COPIA a detalle_reservacion.
    -- Reembolso: 100 % si se cancela con al menos horas_reembolso_total de anticipación al inicio;
    -- después, porcentaje_reembolso_tardio. Ya iniciado el servicio, o si permite_cancelacion = 0, no se cancela.
    permite_cancelacion         BIT           NOT NULL CONSTRAINT df_servicios_permite_cancelacion DEFAULT(1),
    horas_reembolso_total       INT           NOT NULL CONSTRAINT df_servicios_horas_reembolso DEFAULT(48)
                                CONSTRAINT ck_servicios_horas_reembolso CHECK (horas_reembolso_total BETWEEN 0 AND 8760),
    porcentaje_reembolso_tardio DECIMAL(5,2)  NOT NULL CONSTRAINT df_servicios_pct_tardio DEFAULT(50)
                                CONSTRAINT ck_servicios_pct_tardio CHECK (porcentaje_reembolso_tardio BETWEEN 0 AND 100),
    politica_cancelacion        NVARCHAR(500) NULL,   -- texto que ve el cliente antes de pagar
    -- Imagen del servicio (L6). Es un dato COMÚN a todos los tipos, igual que el nombre y el precio, así que
    -- vive aquí y no en cada subtipo (R1). Antes estaba repetida en hospedajes, vehiculos y paquete, y por eso
    -- los VUELOS y los TOURS (EVENTO, que no tiene tabla de subtipo) no podían tener foto.
    imagen_ruta    NVARCHAR(260)  NULL,  -- ruta pública: /uploads/<carpeta>/<guid>.jpg|png|webp
    activo         BIT            NOT NULL CONSTRAINT df_servicios_activo DEFAULT(1),
    creado_en      DATETIME       NOT NULL CONSTRAINT df_servicios_creado_en DEFAULT(CURRENT_TIMESTAMP),
    actualizado_en DATETIME       NOT NULL CONSTRAINT df_servicios_actualizado_en DEFAULT(CURRENT_TIMESTAMP)
);
GO
CREATE INDEX ix_servicios_tipo_activo ON dbo.servicios(tipo_servicio, activo);
GO

/* ---------- Subtipos: id_servicio es PK y FK (relación 1:1 con servicios) ---------- */
CREATE TABLE dbo.vuelos(
    id_servicio   BIGINT       NOT NULL CONSTRAINT pk_vuelos PRIMARY KEY
                  CONSTRAINT fk_vuelos_servicios REFERENCES dbo.servicios(id),
    id_origen     BIGINT       NOT NULL CONSTRAINT fk_vuelos_origen  REFERENCES dbo.ubicaciones(id),
    id_destino    BIGINT       NOT NULL CONSTRAINT fk_vuelos_destino REFERENCES dbo.ubicaciones(id),
    codigo_vuelo  NVARCHAR(10) NOT NULL,
    fecha_salida  DATETIME     NOT NULL,
    fecha_llegada DATETIME     NOT NULL,
    clase         NVARCHAR(20) NOT NULL CONSTRAINT df_vuelos_clase DEFAULT(N'TURISTA')
                  CONSTRAINT ck_vuelos_clase CHECK (clase IN (N'TURISTA', N'PREMIUM', N'PRIMERA')),
    CONSTRAINT ck_vuelos_fechas CHECK (fecha_llegada > fecha_salida),
    CONSTRAINT ck_vuelos_ruta   CHECK (id_origen <> id_destino)
);
GO

CREATE TABLE dbo.hospedajes(
    id_servicio         BIGINT       NOT NULL CONSTRAINT pk_hospedajes PRIMARY KEY
                        CONSTRAINT fk_hospedajes_servicios REFERENCES dbo.servicios(id),
    id_ubicacion        BIGINT       NOT NULL CONSTRAINT fk_hospedajes_ubicaciones REFERENCES dbo.ubicaciones(id),
    tipo_habitacion     NVARCHAR(60) NOT NULL,
    categoria           NVARCHAR(40) NULL,
    capacidad_huespedes INT          NOT NULL CONSTRAINT df_hospedajes_capacidad DEFAULT(1)
                        CONSTRAINT ck_hospedajes_capacidad CHECK (capacidad_huespedes > 0),
    hora_entrada        TIME(0)      NOT NULL,
    hora_salida         TIME(0)      NOT NULL
);
GO

CREATE TABLE dbo.vehiculos(
    id_servicio         BIGINT       NOT NULL CONSTRAINT pk_vehiculos PRIMARY KEY
                        CONSTRAINT fk_vehiculos_servicios REFERENCES dbo.servicios(id),
    id_ubicacion_retiro BIGINT       NOT NULL CONSTRAINT fk_vehiculos_ubicaciones REFERENCES dbo.ubicaciones(id),
    categoria           NVARCHAR(50) NOT NULL,
    marca               NVARCHAR(50) NOT NULL,
    modelo              NVARCHAR(60) NOT NULL,
    capacidad_pasajeros TINYINT      NOT NULL CONSTRAINT ck_vehiculos_capacidad CHECK (capacidad_pasajeros > 0)
);
GO

/* max_personas es NOT NULL porque la propiedad es ushort (no ushort?). Ver doc 06, sección 9. */
CREATE TABLE dbo.paquete(
    id_servicio          BIGINT        NOT NULL CONSTRAINT pk_paquete PRIMARY KEY
                         CONSTRAINT fk_paquete_servicios REFERENCES dbo.servicios(id),
    duracion_dias        SMALLINT      NOT NULL CONSTRAINT ck_paquete_duracion CHECK (duracion_dias > 0),
    min_personas         SMALLINT      NOT NULL CONSTRAINT df_paquete_min DEFAULT(1)
                         CONSTRAINT ck_paquete_min CHECK (min_personas > 0),
    max_personas         SMALLINT      NOT NULL,
    -- Destino del paquete. Obligatorio cuando sus servicios están en ciudades distintas; si es NULL,
    -- el destino es la ciudad de su hospedaje incluido o, si no tiene, el destino de su vuelo.
    id_ubicacion_destino BIGINT        NULL     CONSTRAINT fk_paquete_destino REFERENCES dbo.ubicaciones(id),
    CONSTRAINT ck_paquete_personas CHECK (max_personas >= min_personas)
);
GO

/* Puente R2: id_servicio = el paquete; id_servicio_incluido = lo que contiene. */
CREATE TABLE dbo.paquete_servicio(
    id_servicio          BIGINT       NOT NULL CONSTRAINT fk_paquete_servicio_paquete REFERENCES dbo.paquete(id_servicio),
    id_servicio_incluido BIGINT       NOT NULL CONSTRAINT fk_paquete_servicio_incluido REFERENCES dbo.servicios(id),
    cantidad             DECIMAL(8,2) NOT NULL CONSTRAINT ck_paquete_servicio_cantidad CHECK (cantidad > 0),
    orden                SMALLINT     NULL     CONSTRAINT ck_paquete_servicio_orden CHECK (orden >= 0),
    CONSTRAINT pk_paquete_servicio PRIMARY KEY (id_servicio, id_servicio_incluido),
    CONSTRAINT ck_paquete_servicio_distinto CHECK (id_servicio <> id_servicio_incluido)
);
GO

/* Inventario y precio por día (R3). */
CREATE TABLE dbo.disponibilidad_servicio(
    id                  BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_disponibilidad_servicio PRIMARY KEY,
    id_servicio         BIGINT        NOT NULL CONSTRAINT fk_disponibilidad_servicios REFERENCES dbo.servicios(id),
    fecha               DATE          NOT NULL,
    cantidad_total      INT           NOT NULL CONSTRAINT ck_disponibilidad_total CHECK (cantidad_total >= 0),
    cantidad_disponible INT           NOT NULL,
    precio_fecha        DECIMAL(12,2) NOT NULL CONSTRAINT ck_disponibilidad_precio CHECK (precio_fecha >= 0),
    actualizado_en      DATETIME      NOT NULL CONSTRAINT df_disponibilidad_actualizado_en DEFAULT(CURRENT_TIMESTAMP),
    CONSTRAINT uq_disponibilidad_servicio_fecha UNIQUE (id_servicio, fecha),
    CONSTRAINT ck_disponibilidad_rango CHECK (cantidad_disponible BETWEEN 0 AND cantidad_total)
);
GO

/* =====================================================================================
   Promociones
   ===================================================================================== */
CREATE TABLE dbo.promociones(
    id             BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_promociones PRIMARY KEY,
    codigo         NVARCHAR(30)  NOT NULL CONSTRAINT uq_promociones_codigo UNIQUE,
    nombre         NVARCHAR(120) NULL,
    descripcion    NVARCHAR(500) NULL,
    tipo_descuento NVARCHAR(15)  NOT NULL CONSTRAINT ck_promociones_tipo CHECK (tipo_descuento IN (N'PORCENTAJE', N'MONTO')),
    valor          DECIMAL(12,2) NOT NULL CONSTRAINT ck_promociones_valor CHECK (valor > 0),
    fecha_inicio   DATETIME      NOT NULL,
    fecha_fin      DATETIME      NOT NULL,
    activo         BIT           NOT NULL CONSTRAINT df_promociones_activo DEFAULT(1),
    minimo_compra  DECIMAL(12,2) NOT NULL CONSTRAINT df_promociones_minimo DEFAULT(0)
                   CONSTRAINT ck_promociones_minimo CHECK (minimo_compra >= 0),
    CONSTRAINT ck_promociones_vigencia CHECK (fecha_fin > fecha_inicio),
    CONSTRAINT ck_promociones_porcentaje CHECK (tipo_descuento <> N'PORCENTAJE' OR valor <= 100)
);
GO

CREATE TABLE dbo.promocion_servicio(
    id_promocion BIGINT NOT NULL CONSTRAINT fk_promocion_servicio_promociones REFERENCES dbo.promociones(id),
    id_servicio  BIGINT NOT NULL CONSTRAINT fk_promocion_servicio_servicios REFERENCES dbo.servicios(id),
    CONSTRAINT pk_promocion_servicio PRIMARY KEY (id_promocion, id_servicio)
);
GO

/* =====================================================================================
   Reservaciones
   ===================================================================================== */
CREATE TABLE dbo.estados_reservacion(
    id          SMALLINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_estados_reservacion PRIMARY KEY,
    nombre      NVARCHAR(30)  NOT NULL CONSTRAINT uq_estados_reservacion_nombre UNIQUE,
    descripcion NVARCHAR(150) NULL,
    es_final    BIT           NOT NULL CONSTRAINT df_estados_reservacion_final DEFAULT(0)
);
GO

/* Totales históricos (R4): no se recalculan aunque cambie el catálogo. */
CREATE TABLE dbo.reservaciones(
    id                    BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_reservaciones PRIMARY KEY,
    id_usuarioglobal      BIGINT        NOT NULL CONSTRAINT fk_reservaciones_usuarios REFERENCES dbo.usuarios(id),
    id_estado_reservacion SMALLINT      NOT NULL CONSTRAINT fk_reservaciones_estados REFERENCES dbo.estados_reservacion(id),
    codigo_reserva        NVARCHAR(30)  NOT NULL CONSTRAINT uq_reservaciones_codigo UNIQUE,
    moneda                NVARCHAR(3)   NOT NULL CONSTRAINT df_reservaciones_moneda DEFAULT(N'MXN'),
    subtotal              DECIMAL(12,2) NOT NULL CONSTRAINT df_reservaciones_subtotal DEFAULT(0),
    descuento_total       DECIMAL(12,2) NOT NULL CONSTRAINT df_reservaciones_descuento DEFAULT(0),
    total                 DECIMAL(12,2) NOT NULL CONSTRAINT df_reservaciones_total DEFAULT(0),
    creada_en             DATETIME      NOT NULL CONSTRAINT df_reservaciones_creada_en DEFAULT(CURRENT_TIMESTAMP),
    CONSTRAINT ck_reservaciones_importes CHECK (subtotal >= 0 AND descuento_total >= 0
                                                AND descuento_total <= subtotal AND total >= 0)
);
GO
CREATE INDEX ix_reservaciones_usuario ON dbo.reservaciones(id_usuarioglobal);
GO

/* Snapshot (R4): copia del precio, descuento, fechas y descripción al momento de reservar. */
CREATE TABLE dbo.detalle_reservacion(
    id                   BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_detalle_reservacion PRIMARY KEY,
    id_reservacion       BIGINT        NOT NULL CONSTRAINT fk_detalle_reservaciones REFERENCES dbo.reservaciones(id),
    id_servicio          BIGINT        NOT NULL CONSTRAINT fk_detalle_servicios REFERENCES dbo.servicios(id),
    id_promocion         BIGINT        NULL     CONSTRAINT fk_detalle_promociones REFERENCES dbo.promociones(id),
    cantidad             DECIMAL(8,2)  NOT NULL CONSTRAINT ck_detalle_cantidad CHECK (cantidad > 0),
    fecha_inicio         DATETIME      NOT NULL,
    fecha_fin            DATETIME      NOT NULL,
    precio_unitario      DECIMAL(12,2) NOT NULL CONSTRAINT ck_detalle_precio CHECK (precio_unitario >= 0),
    descuento_aplicado   DECIMAL(12,2) NOT NULL CONSTRAINT df_detalle_descuento DEFAULT(0)
                         CONSTRAINT ck_detalle_descuento CHECK (descuento_aplicado >= 0),
    subtotal             DECIMAL(12,2) NOT NULL CONSTRAINT ck_detalle_subtotal CHECK (subtotal >= 0),
    descripcion_snapshot NVARCHAR(250) NOT NULL,
    -- Snapshot de la política de cancelación del servicio al momento de comprar.
    permite_cancelacion         BIT           NOT NULL CONSTRAINT df_detalle_permite_cancelacion DEFAULT(1),
    horas_reembolso_total       INT           NOT NULL CONSTRAINT df_detalle_horas_reembolso DEFAULT(48)
                                CONSTRAINT ck_detalle_horas_reembolso CHECK (horas_reembolso_total BETWEEN 0 AND 8760),
    porcentaje_reembolso_tardio DECIMAL(5,2)  NOT NULL CONSTRAINT df_detalle_pct_tardio DEFAULT(50)
                                CONSTRAINT ck_detalle_pct_tardio CHECK (porcentaje_reembolso_tardio BETWEEN 0 AND 100),
    politica_cancelacion        NVARCHAR(500) NULL,
    CONSTRAINT ck_detalle_fechas CHECK (fecha_fin >= fecha_inicio)
);
GO
CREATE INDEX ix_detalle_reservacion ON dbo.detalle_reservacion(id_reservacion);
GO

/* Bitácora (R6). id_usuario_cambio NULL = cambio automático del sistema. */
CREATE TABLE dbo.historial_estado_reservacion(
    id                    BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_historial_estado_reservacion PRIMARY KEY,
    id_reservacion        BIGINT        NOT NULL CONSTRAINT fk_hist_reservacion_reservaciones REFERENCES dbo.reservaciones(id),
    id_estado_reservacion SMALLINT      NOT NULL CONSTRAINT fk_hist_reservacion_estados REFERENCES dbo.estados_reservacion(id),
    id_usuario_cambio     BIGINT        NULL     CONSTRAINT fk_hist_reservacion_usuarios REFERENCES dbo.usuarios(id),
    fecha_cambio          DATETIME      NOT NULL CONSTRAINT df_hist_reservacion_fecha DEFAULT(CURRENT_TIMESTAMP),
    motivo                NVARCHAR(250) NULL
);
GO

/* =====================================================================================
   Pagos y reembolsos
   ===================================================================================== */
CREATE TABLE dbo.metodos_pago(
    id          SMALLINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_metodos_pago PRIMARY KEY,
    nombre      NVARCHAR(40)  NOT NULL CONSTRAINT uq_metodos_pago_nombre UNIQUE,
    descripcion NVARCHAR(150) NULL,
    activo      BIT           NOT NULL CONSTRAINT df_metodos_pago_activo DEFAULT(1)
);
GO

CREATE TABLE dbo.instituciones_financieras(
    id                   BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_instituciones_financieras PRIMARY KEY,
    nombre               NVARCHAR(120) NOT NULL,
    clave_institucion    NVARCHAR(10)  NULL,
    codigo_internacional NVARCHAR(15)  NULL,
    telefono             NVARCHAR(20)  NULL,
    activo               BIT           NOT NULL CONSTRAINT df_instituciones_activo DEFAULT(1)
);
GO
CREATE UNIQUE INDEX uq_instituciones_clave  ON dbo.instituciones_financieras(clave_institucion)    WHERE clave_institucion IS NOT NULL;
CREATE UNIQUE INDEX uq_instituciones_codigo ON dbo.instituciones_financieras(codigo_internacional) WHERE codigo_internacional IS NOT NULL;
GO

/* R5: solo token del gateway + marca + últimos 4 dígitos. PROHIBIDO guardar el número completo (PAN) o el CVV. */
CREATE TABLE dbo.tarjetas_usuarios(
    id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_tarjetas_usuarios PRIMARY KEY,
    id_usuariog      BIGINT        NOT NULL CONSTRAINT fk_tarjetas_usuarios REFERENCES dbo.usuarios(id),
    id_institucion   BIGINT        NULL     CONSTRAINT fk_tarjetas_instituciones REFERENCES dbo.instituciones_financieras(id),
    token_referencia NVARCHAR(100) NOT NULL CONSTRAINT uq_tarjetas_token UNIQUE,
    marca            NVARCHAR(20)  NOT NULL,
    ultimos4         NVARCHAR(4)   NOT NULL CONSTRAINT ck_tarjetas_ultimos4 CHECK (ultimos4 LIKE N'[0-9][0-9][0-9][0-9]'),
    mes_expiracion   TINYINT       NOT NULL CONSTRAINT ck_tarjetas_mes CHECK (mes_expiracion BETWEEN 1 AND 12),
    anio_expiracion  SMALLINT      NOT NULL CONSTRAINT ck_tarjetas_anio CHECK (anio_expiracion BETWEEN 2000 AND 2100),
    alias            NVARCHAR(40)  NULL,
    activo           BIT           NOT NULL CONSTRAINT df_tarjetas_activo DEFAULT(1),
    creado_en        DATETIME      NOT NULL CONSTRAINT df_tarjetas_creado_en DEFAULT(CURRENT_TIMESTAMP)
);
GO

CREATE TABLE dbo.estados_pago(
    id          SMALLINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_estados_pago PRIMARY KEY,
    nombre      NVARCHAR(30)  NOT NULL CONSTRAINT uq_estados_pago_nombre UNIQUE,
    descripcion NVARCHAR(150) NULL,
    es_final    BIT           NOT NULL CONSTRAINT df_estados_pago_final DEFAULT(0)
);
GO

/* id_tarjeta es NOT NULL porque Pagos.id_tarjeta es ulong (no ulong?): hoy todo pago es con tarjeta.
   Si se aceptan transferencias, cambiar la propiedad a ulong? y esta columna a NULL (doc 06, sección 9). */
CREATE TABLE dbo.pagos(
    id                 BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_pagos PRIMARY KEY,
    id_reservacion     BIGINT        NOT NULL CONSTRAINT fk_pagos_reservaciones REFERENCES dbo.reservaciones(id),
    id_metodo_pago     SMALLINT      NOT NULL CONSTRAINT fk_pagos_metodos REFERENCES dbo.metodos_pago(id),
    id_tarjeta         BIGINT        NOT NULL CONSTRAINT fk_pagos_tarjetas REFERENCES dbo.tarjetas_usuarios(id),
    id_estado_pago     SMALLINT      NOT NULL CONSTRAINT fk_pagos_estados REFERENCES dbo.estados_pago(id),
    monto              DECIMAL(12,2) NOT NULL CONSTRAINT ck_pagos_monto CHECK (monto > 0),
    moneda             NVARCHAR(3)   NOT NULL CONSTRAINT df_pagos_moneda DEFAULT(N'MXN'),
    referencia_externa NVARCHAR(100) NULL,
    fecha_pago         DATETIME      NOT NULL CONSTRAINT df_pagos_fecha DEFAULT(CURRENT_TIMESTAMP),
    creado_en          DATETIME      NOT NULL CONSTRAINT df_pagos_creado_en DEFAULT(CURRENT_TIMESTAMP)
);
GO
CREATE UNIQUE INDEX uq_pagos_referencia ON dbo.pagos(referencia_externa) WHERE referencia_externa IS NOT NULL;
CREATE INDEX ix_pagos_reservacion ON dbo.pagos(id_reservacion);
GO

CREATE TABLE dbo.historial_estado_pago(
    id             BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_historial_estado_pago PRIMARY KEY,
    id_pago        BIGINT        NOT NULL CONSTRAINT fk_hist_pago_pagos REFERENCES dbo.pagos(id),
    id_estado_pago SMALLINT      NOT NULL CONSTRAINT fk_hist_pago_estados REFERENCES dbo.estados_pago(id),
    fecha_cambio   DATETIME      NOT NULL CONSTRAINT df_hist_pago_fecha DEFAULT(CURRENT_TIMESTAMP),
    detalle        NVARCHAR(250) NULL
);
GO

CREATE TABLE dbo.reembolsos(
    id                 BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_reembolsos PRIMARY KEY,
    id_pago            BIGINT        NOT NULL CONSTRAINT fk_reembolsos_pagos REFERENCES dbo.pagos(id),
    monto              DECIMAL(12,2) NOT NULL CONSTRAINT ck_reembolsos_monto CHECK (monto > 0),
    motivo             NVARCHAR(250) NOT NULL,
    estado             NVARCHAR(15)  NOT NULL CONSTRAINT df_reembolsos_estado DEFAULT(N'SOLICITADO')
                       CONSTRAINT ck_reembolsos_estado CHECK (estado IN (N'SOLICITADO', N'APROBADO', N'RECHAZADO', N'PROCESADO')),
    fecha_solicitud    DATETIME      NOT NULL CONSTRAINT df_reembolsos_fecha DEFAULT(CURRENT_TIMESTAMP),
    fecha_resolucion   DATETIME      NULL,
    referencia_externa NVARCHAR(100) NULL,
    CONSTRAINT ck_reembolsos_resolucion CHECK (fecha_resolucion IS NULL OR fecha_resolucion >= fecha_solicitud)
);
GO
CREATE UNIQUE INDEX uq_reembolsos_referencia ON dbo.reembolsos(referencia_externa) WHERE referencia_externa IS NOT NULL;
GO

CREATE TABLE dbo.historial_estado_reembolso(
    id                BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_historial_estado_reembolso PRIMARY KEY,
    id_reembolso      BIGINT        NOT NULL CONSTRAINT fk_hist_reembolso_reembolsos REFERENCES dbo.reembolsos(id),
    id_usuario_cambio BIGINT        NULL     CONSTRAINT fk_hist_reembolso_usuarios REFERENCES dbo.usuarios(id),
    estado            NVARCHAR(15)  NOT NULL CONSTRAINT ck_hist_reembolso_estado
                      CHECK (estado IN (N'SOLICITADO', N'APROBADO', N'RECHAZADO', N'PROCESADO')),
    fecha_cambio      DATETIME      NOT NULL CONSTRAINT df_hist_reembolso_fecha DEFAULT(CURRENT_TIMESTAMP),
    detalle           NVARCHAR(250) NULL
);
GO

/* =====================================================================================
   Soporte (R9) — también lo usa el bot de Telegram (docs/06)
   ===================================================================================== */
CREATE TABLE dbo.solicitudes_soporte(
    id                 BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_solicitudes_soporte PRIMARY KEY,
    id_usuarioglobal   BIGINT         NOT NULL CONSTRAINT fk_solicitudes_usuarios REFERENCES dbo.usuarios(id),
    id_reservacion     BIGINT         NULL     CONSTRAINT fk_solicitudes_reservaciones REFERENCES dbo.reservaciones(id),
    id_agente_asignado BIGINT         NULL     CONSTRAINT fk_solicitudes_agentes REFERENCES dbo.usuarios(id),
    tipo               NVARCHAR(20)   NOT NULL CONSTRAINT ck_solicitudes_tipo CHECK (tipo IN (N'ASESORIA', N'PROBLEMA')),
    asunto             NVARCHAR(120)  NOT NULL,
    mensaje            NVARCHAR(2000) NOT NULL,
    estado             NVARCHAR(20)   NOT NULL CONSTRAINT df_solicitudes_estado DEFAULT(N'ABIERTA')
                       CONSTRAINT ck_solicitudes_estado CHECK (estado IN (N'ABIERTA', N'EN_PROCESO', N'CERRADA')),
    creada_en          DATETIME       NOT NULL CONSTRAINT df_solicitudes_creada_en DEFAULT(CURRENT_TIMESTAMP),
    actualizada_en     DATETIME       NOT NULL CONSTRAINT df_solicitudes_actualizada_en DEFAULT(CURRENT_TIMESTAMP),
    -- Calificación de la atención (1 a 5 estrellas) que da el cliente desde el bot cuando el ticket ya está cerrado.
    calificacion       TINYINT        NULL     CONSTRAINT ck_solicitudes_calificacion CHECK (calificacion BETWEEN 1 AND 5),
    calificada_en      DATETIME       NULL,
    -- Escalado a humano: EN_PROCESO siempre tiene un agente asignado.
    CONSTRAINT ck_solicitudes_en_proceso CHECK (estado <> N'EN_PROCESO' OR id_agente_asignado IS NOT NULL),
    CONSTRAINT ck_solicitudes_calificada CHECK ((calificacion IS NULL AND calificada_en IS NULL)
                                                OR (calificacion IS NOT NULL AND calificada_en IS NOT NULL AND estado = N'CERRADA'))
);
GO
CREATE INDEX ix_solicitudes_estado ON dbo.solicitudes_soporte(estado, creada_en);
GO

/* Conversación del ticket (T4 fase C). El primer mensaje es solicitudes_soporte.mensaje; aquí van las respuestas
   del cliente y del personal, en orden. es_personal = 1 si lo escribió alguien de FlyVibeMex (así "quién respondió
   al último" no depende de que el rol de esa cuenta cambie después). */
CREATE TABLE dbo.mensajes_soporte(
    id           BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_mensajes_soporte PRIMARY KEY,
    id_solicitud BIGINT         NOT NULL CONSTRAINT fk_mensajes_solicitudes REFERENCES dbo.solicitudes_soporte(id),
    id_usuario   BIGINT         NOT NULL CONSTRAINT fk_mensajes_usuarios REFERENCES dbo.usuarios(id),
    es_personal  BIT            NOT NULL,
    mensaje      NVARCHAR(2000) NOT NULL CONSTRAINT ck_mensajes_texto CHECK (LEN(mensaje) > 0),
    creado_en    DATETIME       NOT NULL CONSTRAINT df_mensajes_creado_en DEFAULT(CURRENT_TIMESTAMP)
);
GO
CREATE INDEX ix_mensajes_solicitud ON dbo.mensajes_soporte(id_solicitud, creado_en);
GO

/* ---------- Bitácora del panel: quién cambió qué y cuándo (la consulta el SA) ---------- */
CREATE TABLE dbo.bitacora(
    id          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_bitacora PRIMARY KEY,
    fecha       DATETIME      NOT NULL CONSTRAINT df_bitacora_fecha DEFAULT(CURRENT_TIMESTAMP),
    id_usuario  BIGINT        NULL     CONSTRAINT fk_bitacora_usuarios REFERENCES dbo.usuarios(id),
    accion      NVARCHAR(30)  NOT NULL,   -- CREAR, ACTUALIZAR, BAJA, REACTIVAR, DISPONIBILIDAD…
    entidad     NVARCHAR(40)  NOT NULL,   -- proveedores, servicios, disponibilidad_servicio…
    id_entidad  BIGINT        NULL,
    detalle     NVARCHAR(500) NULL
);
GO
CREATE INDEX ix_bitacora_fecha   ON dbo.bitacora(fecha);
CREATE INDEX ix_bitacora_entidad ON dbo.bitacora(entidad, id_entidad);
GO

/* ---------- Contenido del sitio que edita el SA sin tocar código (T4 fase D) ----------
   Una fila por CLAVE (p. ej. 'inicio.bienvenida.titulo', 'politicas.privacidad'); la lista de claves válidas y su
   texto por defecto están en Models/ContenidoSitioClaves.cs. Si una clave no tiene fila, el sitio muestra el texto
   por defecto. texto = texto plano (se respetan los saltos de línea; nunca se interpreta como HTML). */
CREATE TABLE dbo.contenido_sitio(
    clave          VARCHAR(60)    NOT NULL CONSTRAINT pk_contenido_sitio PRIMARY KEY,
    texto          NVARCHAR(4000) NULL,
    imagen_ruta    NVARCHAR(260)  NULL,  -- ruta pública: /uploads/contenido/<guid>.jpg|png|webp
    actualizado_en DATETIME       NOT NULL CONSTRAINT df_contenido_actualizado_en DEFAULT(CURRENT_TIMESTAMP),
    id_usuario     BIGINT         NULL     CONSTRAINT fk_contenido_usuarios REFERENCES dbo.usuarios(id),
    CONSTRAINT ck_contenido_algo CHECK (texto IS NOT NULL OR imagen_ruta IS NOT NULL)
);
GO

/* ---------- Bot de Telegram (docs/06): cuentas de CLIENTE vinculadas a un chat privado con el bot ----------
   El cliente genera un código de un solo uso en Mi cuenta › Telegram (vive 10 minutos en la memoria de la app,
   nunca en la BD) y lo manda al bot. Al vincular se desactivan los vínculos anteriores de esa cuenta y de ese chat:
   los dos índices únicos filtrados garantizan UNA cuenta por chat y UN chat por cuenta (solo entre los activos).
   chat_id = id del chat privado de Telegram (en un chat privado es igual al id del usuario de Telegram). */
CREATE TABLE dbo.telegram_vinculos(
    id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_telegram_vinculos PRIMARY KEY,
    id_usuarioglobal BIGINT   NOT NULL CONSTRAINT fk_telegram_vinculos_usuarios REFERENCES dbo.usuarios(id),
    chat_id          BIGINT   NOT NULL CONSTRAINT ck_telegram_vinculos_chat CHECK (chat_id > 0),
    activo           BIT      NOT NULL CONSTRAINT df_telegram_vinculos_activo DEFAULT(1),
    vinculado_en     DATETIME NOT NULL CONSTRAINT df_telegram_vinculos_vinculado_en DEFAULT(CURRENT_TIMESTAMP),
    desvinculado_en  DATETIME NULL,
    -- Avisos que el cliente quiere recibir (👤 Mi cuenta › 🔔 Avisos en el bot). Al volver a vincular se copian del vínculo anterior.
    avisos_reservaciones BIT NOT NULL CONSTRAINT df_telegram_vinculos_av_reservaciones DEFAULT(1),
    avisos_recordatorios BIT NOT NULL CONSTRAINT df_telegram_vinculos_av_recordatorios DEFAULT(1),
    avisos_soporte       BIT NOT NULL CONSTRAINT df_telegram_vinculos_av_soporte       DEFAULT(1),
    CONSTRAINT ck_telegram_vinculos_fechas CHECK (activo = 1 OR desvinculado_en IS NOT NULL)
);
GO
CREATE UNIQUE INDEX uq_telegram_vinculos_usuario ON dbo.telegram_vinculos(id_usuarioglobal) WHERE activo = 1;
CREATE UNIQUE INDEX uq_telegram_vinculos_chat    ON dbo.telegram_vinculos(chat_id)          WHERE activo = 1;
GO

/* Avisos automáticos que el bot YA mandó. El índice único (tipo, id_referencia) impide mandar el mismo aviso dos
   veces, aunque la app se reinicie. Solo metadatos: nunca el texto del mensaje.
     Al cliente:  RESERVACION (ref: historial_estado_reservacion.id) · REEMBOLSO (ref: historial_estado_reembolso.id)
                  RECORDATORIO_72 / RECORDATORIO_24 (ref: reservaciones.id)
     Al grupo de agentes (id_usuarioglobal = cliente del ticket o NULL):
                  TICKET_GRUPO (ref: solicitudes_soporte.id; guarda chat_id e id_mensaje para EDITAR ese mensaje)
                  URGENTE_SIN_AGENTE (ref: solicitudes_soporte.id) · RESUMEN_DIARIO (ref: fecha AAAAMMDD) */
CREATE TABLE dbo.telegram_avisos(
    id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_telegram_avisos PRIMARY KEY,
    id_usuarioglobal BIGINT       NULL     CONSTRAINT fk_telegram_avisos_usuarios REFERENCES dbo.usuarios(id),
    tipo             NVARCHAR(20) NOT NULL CONSTRAINT ck_telegram_avisos_tipo
                     CHECK (tipo IN (N'RESERVACION', N'REEMBOLSO', N'RECORDATORIO_72', N'RECORDATORIO_24',
                                     N'TICKET_GRUPO', N'URGENTE_SIN_AGENTE', N'RESUMEN_DIARIO')),
    id_referencia    BIGINT       NOT NULL,
    chat_id          BIGINT       NULL,
    id_mensaje       INT          NULL,
    enviado_en       DATETIME     NOT NULL CONSTRAINT df_telegram_avisos_enviado_en DEFAULT(CURRENT_TIMESTAMP),
    -- Los avisos al cliente siempre tienen cliente; el mensaje de un ticket en el grupo siempre sabe dónde está.
    CONSTRAINT ck_telegram_avisos_cliente CHECK (id_usuarioglobal IS NOT NULL
                                                 OR tipo IN (N'TICKET_GRUPO', N'URGENTE_SIN_AGENTE', N'RESUMEN_DIARIO')),
    CONSTRAINT ck_telegram_avisos_mensaje CHECK (tipo <> N'TICKET_GRUPO' OR (chat_id IS NOT NULL AND id_mensaje IS NOT NULL))
);
GO
CREATE UNIQUE INDEX uq_telegram_avisos ON dbo.telegram_avisos(tipo, id_referencia);
GO

/* =====================================================================================
   Catálogos que la aplicación necesita (los busca por NOMBRE, nunca por id)
   ===================================================================================== */
INSERT INTO dbo.roles_usuarios (nombre, descripcion, activo)
VALUES
    ('CLIENTE',       'Viajero que busca, reserva y paga servicios',                              1),
    ('SOPORTE',       'Atiende los tickets de soporte de los clientes',                           1),
    ('AGENTE',        'Gestiona reservaciones y reembolsos de los clientes',                      1),
    ('ADMINISTRADOR', 'Panel interno y Gestor de ventas (catalogo)',                              1),
    ('SA',            'Super administrador: todo el panel, usuarios, catalogos base y bitacora', 1);

INSERT INTO dbo.estados_reservacion (nombre, descripcion, es_final)
VALUES (N'PENDIENTE',  N'Creada, en espera de pago', 0),
       (N'CONFIRMADA', N'Pagada y confirmada', 0),
       (N'EN_CURSO',   N'El viaje ya comenzó', 0),
       (N'COMPLETADA', N'El viaje terminó', 1),
       (N'CANCELADA',  N'Cancelada por el cliente o por la agencia', 1);

INSERT INTO dbo.estados_pago (nombre, descripcion, es_final)
VALUES (N'PENDIENTE',   N'En espera de respuesta del gateway', 0),
       (N'APROBADO',    N'Cobro autorizado', 0),
       (N'RECHAZADO',   N'Cobro rechazado', 1),
       (N'REEMBOLSADO', N'Cobro devuelto al cliente', 1);

INSERT INTO dbo.metodos_pago (nombre, descripcion, activo)
VALUES (N'Tarjeta de crédito', N'Visa, Mastercard o American Express. El cobro se hace solo en la web.', 1),
       (N'Tarjeta de débito',  N'Tarjetas de débito de bancos mexicanos. El cobro se hace solo en la web.', 1),
       (N'Transferencia SPEI', N'Próximamente.', 0);

/* Cuenta inicial SA: permite entrar al panel aunque no se cargue el seed. */
INSERT INTO dbo.usuarios (id_rol, nombre, apellido_paterno, apellido_materno, correo, telefono, password_hash, activo)
VALUES ((SELECT id FROM dbo.roles_usuarios WHERE nombre = 'SA'),
        'Super', 'Admin', 'FlyVibe', 'sa.prueba@flyvibemex.test', '9610000007',
        'AQAAAAIAAYagAAAAEKrTa4pzbnSHVkqn1+0EcRQQhceq2uQeDEnxY6z6Axl4Obp+Xg5gjSKkHMhQeEL0Zw==', 1);
GO

/* ---------- Verificación: deben salir 31 tablas y los catálogos ---------- */
SELECT COUNT(*) AS tablas_creadas   -- 31
FROM sys.tables
WHERE name IN (N'roles_usuarios', N'usuarios', N'ubicaciones', N'proveedores', N'servicios', N'vuelos',
               N'hospedajes', N'vehiculos', N'paquete', N'paquete_servicio', N'disponibilidad_servicio',
               N'promociones', N'promocion_servicio', N'estados_reservacion', N'reservaciones',
               N'detalle_reservacion', N'historial_estado_reservacion', N'metodos_pago',
               N'instituciones_financieras', N'tarjetas_usuarios', N'estados_pago', N'pagos',
               N'historial_estado_pago', N'reembolsos', N'historial_estado_reembolso', N'solicitudes_soporte',
               N'mensajes_soporte', N'bitacora', N'contenido_sitio', N'telegram_vinculos',
               N'telegram_avisos');

SELECT 'roles_usuarios' AS catalogo, COUNT(*) AS filas FROM dbo.roles_usuarios UNION ALL   -- 5
SELECT 'estados_reservacion', COUNT(*) FROM dbo.estados_reservacion UNION ALL              -- 5
SELECT 'estados_pago', COUNT(*) FROM dbo.estados_pago UNION ALL                            -- 4
SELECT 'metodos_pago', COUNT(*) FROM dbo.metodos_pago UNION ALL                            -- 3
SELECT 'usuarios', COUNT(*) FROM dbo.usuarios;                                             -- 1 (sa.prueba)
GO

SET NOEXEC OFF;
GO
