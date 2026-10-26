/* =====================================================================================
   FlyVibe Mex · 01_esquema.sql
   -------------------------------------------------------------------------------------
   Crea las 24 tablas restantes del modelo (las otras 2, roles_usuarios y usuarios, las
   crea sql/00_db_prueba_conexion.sql). Total: 26 tablas = 26 clases de Models/.

   Regla de nombres: CADA COLUMNA SE LLAMA IGUAL QUE LA PROPIEDAD DEL MODELO C#
   (nombres nuevos del 21-10-2026): reservaciones.id_usuarioglobal,
   solicitudes_soporte.id_usuarioglobal, tarjetas_usuarios.id_usuariog,
   detalle_reservacion.fecha_inicio, proveedores.tipo_proveedor, ubicaciones.creado_en…
   Así FlyVibeMexDbContext no necesita HasColumnName.

   Tipos: también coinciden con C# (si no, EF Core falla al leer):
       ulong -> BIGINT · ushort -> SMALLINT · int -> INT · byte -> TINYINT · bool -> BIT
       decimal -> DECIMAL(12,2) (cantidad: 8,2) · DateTime -> DATETIME · DateOnly -> DATE
       TimeOnly -> TIME · byte[] -> VARBINARY(MAX) · string -> NVARCHAR
   Una columna es NULL solo si la propiedad es nullable (ulong?, string?, DateTime?) o si el
   DbContext la marca IsRequired(false).

   Correcciones de diseño incluidas (CONTEXTO_PROYECTO_FLYVIBEMEX.md, secciones 4.2 y 4.3):
       - Subtipos vuelos / hospedajes / vehiculos / paquete con id_servicio como PK y FK (R1).
       - Tabla vuelos (no existía) e historial_estado_reservacion (no existía).
       - paquete_servicio con PK (id_servicio, id_servicio_incluido).
       - disponibilidad_servicio con UNIQUE(id_servicio, fecha) y rangos válidos (R3).
       - CHECK de catálogos de texto (tipos y estados), de importes y de fechas.
       - UNIQUE de columnas opcionales como índice filtrado (WHERE col IS NOT NULL), porque
         en SQL Server un UNIQUE normal solo admite UNA fila con NULL.
       - FK obligatorias como NOT NULL.

   Cómo ejecutarlo (SSMS):
       1) sql/00_db_prueba_conexion.sql   (crea FlyVibeMex_Test, roles_usuarios y usuarios)
       2) este archivo                    (F5)
       3) sql/02_seed_demo.sql            (datos de demostración)
       4) Ejecutar la app y abrir /Home/TestDb: "esquemaCompleto" debe ser true.

   ATENCIÓN: borra y vuelve a crear estas 24 tablas (y sus datos) en FlyVibeMex_Test.
   No toca roles_usuarios ni usuarios. No se puede ejecutar sobre la base real.
   Requiere SQL Server 2016 o posterior (DROP TABLE IF EXISTS).
   ===================================================================================== */

USE FlyVibeMex_Test;
GO

/* Candados de seguridad: solo la base de prueba y solo si ya se ejecutó el script 00. */
IF DB_NAME() <> N'FlyVibeMex_Test'
BEGIN
    RAISERROR(N'Este script solo debe ejecutarse sobre FlyVibeMex_Test. Ejecución detenida.', 16, 1);
    SET NOEXEC ON;
END
GO
IF OBJECT_ID(N'dbo.usuarios', N'U') IS NULL OR OBJECT_ID(N'dbo.roles_usuarios', N'U') IS NULL
BEGIN
    RAISERROR(N'Faltan roles_usuarios/usuarios. Ejecuta primero sql/00_db_prueba_conexion.sql.', 16, 1);
    SET NOEXEC ON;
END
GO

/* ---------- Limpieza: de las tablas hijas a las padres ---------- */
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
    hora_salida         TIME(0)      NOT NULL,
    imagen              VARBINARY(MAX) NULL
);
GO

CREATE TABLE dbo.vehiculos(
    id_servicio         BIGINT       NOT NULL CONSTRAINT pk_vehiculos PRIMARY KEY
                        CONSTRAINT fk_vehiculos_servicios REFERENCES dbo.servicios(id),
    id_ubicacion_retiro BIGINT       NOT NULL CONSTRAINT fk_vehiculos_ubicaciones REFERENCES dbo.ubicaciones(id),
    categoria           NVARCHAR(50) NOT NULL,
    marca               NVARCHAR(50) NOT NULL,
    modelo              NVARCHAR(60) NOT NULL,
    capacidad_pasajeros TINYINT      NOT NULL CONSTRAINT ck_vehiculos_capacidad CHECK (capacidad_pasajeros > 0),
    imagen              VARBINARY(MAX) NULL
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
    politica_cancelacion NVARCHAR(MAX) NULL,
    imagen               VARBINARY(MAX) NULL,
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
    -- Escalado a humano: EN_PROCESO siempre tiene un agente asignado.
    CONSTRAINT ck_solicitudes_en_proceso CHECK (estado <> N'EN_PROCESO' OR id_agente_asignado IS NOT NULL)
);
GO
CREATE INDEX ix_solicitudes_estado ON dbo.solicitudes_soporte(estado, creada_en);
GO

/* ---------- Verificación: deben salir 26 tablas ---------- */
SELECT COUNT(*) AS tablas_creadas
FROM sys.tables
WHERE name IN (N'roles_usuarios', N'usuarios', N'ubicaciones', N'proveedores', N'servicios', N'vuelos',
               N'hospedajes', N'vehiculos', N'paquete', N'paquete_servicio', N'disponibilidad_servicio',
               N'promociones', N'promocion_servicio', N'estados_reservacion', N'reservaciones',
               N'detalle_reservacion', N'historial_estado_reservacion', N'metodos_pago',
               N'instituciones_financieras', N'tarjetas_usuarios', N'estados_pago', N'pagos',
               N'historial_estado_pago', N'reembolsos', N'historial_estado_reembolso', N'solicitudes_soporte');
GO

SET NOEXEC OFF;
GO
