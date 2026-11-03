/* =====================================================================================
   FlyVibe Mex · 02_procedimientos_almacenados.sql                (script 2 de 3)
   -------------------------------------------------------------------------------------
   Procedimientos almacenados de FlyVibeMex_Test. Aplican LAS MISMAS reglas de negocio que la aplicación
   (Services/*.cs), para la entrega de base de datos y para probar desde SSMS. La aplicación NO los llama:
   sigue usando Entity Framework Core (consultas LINQ).

   Requisito: 01_crear_base_datos.sql. Se puede ejecutar varias veces (CREATE OR ALTER).
   Requiere SQL Server 2016 SP1 o posterior.

   Convenciones:
       - Prefijo pa_ ("procedimiento almacenado"). No se usa sp_, que SQL Server reserva para el sistema.
       - pa_interno_* son ayudantes: los llaman los demás procedimientos, no la gente.
       - Todo procedimiento que modifica datos recibe @id_usuario (quién lo hace), valida su ROL, trabaja
         en UNA transacción y deja registro en las bitácoras (historiales y tabla bitacora).
       - Los errores de negocio se lanzan con THROW 51xxx y un mensaje en español; cualquier error deshace todo.
       - Las fechas "ahora" usan GETDATE() (hora local del servidor, igual que DateTime.Now en la app).

   Índice:
       Ayudantes            pa_interno_validar_rol, pa_interno_registrar_bitacora, pa_interno_validar_inventario,
                            pa_interno_generar_disponibilidad, pa_interno_insertar_servicio
       Altas del catálogo   pa_alta_proveedor, pa_alta_hospedaje, pa_alta_vehiculo, pa_alta_evento, pa_alta_vuelo
       Operaciones          pa_ajustar_disponibilidad, pa_cancelar_reservacion, pa_resolver_reembolso,
                            pa_tomar_ticket, pa_responder_ticket, pa_reasignar_ticket, pa_cerrar_ticket
       Usuarios (SA)        pa_cambiar_rol_usuario, pa_cambiar_estado_usuario
       Compra               pa_crear_reservacion_pago
       Consultas/reportes   pa_buscar_disponibilidad, pa_detalle_reservacion, pa_reservaciones_cliente,
                            pa_reporte_ventas, pa_reporte_ocupacion, pa_reembolsos_pendientes,
                            pa_reporte_tickets, pa_consultar_bitacora
   Al final hay ejemplos de uso (comentados).
   ===================================================================================== */

USE FlyVibeMex_Test;
GO

IF DB_NAME() <> N'FlyVibeMex_Test'
BEGIN
    RAISERROR(N'Este script solo debe ejecutarse sobre FlyVibeMex_Test. Ejecución detenida.', 16, 1);
    SET NOEXEC ON;
END
GO
IF OBJECT_ID(N'dbo.bitacora', N'U') IS NULL OR OBJECT_ID(N'dbo.disponibilidad_servicio', N'U') IS NULL
BEGIN
    RAISERROR(N'Faltan tablas. Ejecuta primero sql/01_crear_base_datos.sql.', 16, 1);
    SET NOEXEC ON;
END
GO

/* =====================================================================================
   AYUDANTES INTERNOS
   ===================================================================================== */

/* El usuario debe existir, estar activo y tener UNO de los roles de la lista (separados por coma). */
CREATE OR ALTER PROCEDURE dbo.pa_interno_validar_rol
    @id_usuario BIGINT,
    @roles      VARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (
        SELECT 1
        FROM dbo.usuarios AS u
        JOIN dbo.roles_usuarios AS r ON r.id = u.id_rol
        WHERE u.id = @id_usuario AND u.activo = 1 AND r.activo = 1
          AND CHARINDEX(',' + r.nombre + ',', ',' + REPLACE(@roles, ' ', '') + ',') > 0)
    BEGIN
        DECLARE @mensaje NVARCHAR(300) = CONCAT(N'El usuario ', @id_usuario, N' no tiene permiso para esta operación (se requiere: ', @roles, N').');
        THROW 51001, @mensaje, 1;
    END
END
GO

/* Agrega un registro a la bitácora del panel (quién cambió qué y cuándo). */
CREATE OR ALTER PROCEDURE dbo.pa_interno_registrar_bitacora
    @id_usuario BIGINT,
    @accion     NVARCHAR(30),
    @entidad    NVARCHAR(40),
    @id_entidad BIGINT,
    @detalle    NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.bitacora (fecha, id_usuario, accion, entidad, id_entidad, detalle)
    VALUES (GETDATE(), @id_usuario, @accion, @entidad, @id_entidad,
            CASE WHEN LEN(@detalle) > 500 THEN LEFT(@detalle, 497) + N'...' ELSE @detalle END);
END
GO

/* Rango de inventario válido: desde hoy, hasta >= desde, máximo 366 días y cupo en rango. */
CREATE OR ALTER PROCEDURE dbo.pa_interno_validar_inventario
    @desde       DATE,
    @hasta       DATE,
    @cupo        INT,
    @cupo_minimo INT = 1
AS
BEGIN
    SET NOCOUNT ON;
    IF @desde IS NULL                          THROW 51002, N'Indica la fecha inicial de la disponibilidad.', 1;
    IF @desde < CAST(GETDATE() AS DATE)        THROW 51003, N'La disponibilidad no puede empezar en una fecha pasada.', 1;
    IF @hasta < @desde                         THROW 51004, N'La fecha final debe ser igual o posterior a la inicial.', 1;
    IF DATEDIFF(DAY, @desde, @hasta) + 1 > 366 THROW 51005, N'Máximo 366 días por cambio de disponibilidad.', 1;
    IF @cupo IS NULL OR @cupo < @cupo_minimo OR @cupo > 100000
    BEGIN
        DECLARE @mensaje NVARCHAR(200) = CONCAT(N'El cupo debe estar entre ', @cupo_minimo, N' y 100,000.');
        THROW 51006, @mensaje, 1;
    END
END
GO

/* Inserta una fila de disponibilidad por cada día del rango que todavía no exista (nunca duplica). */
CREATE OR ALTER PROCEDURE dbo.pa_interno_generar_disponibilidad
    @id_servicio BIGINT,
    @desde       DATE,
    @hasta       DATE,
    @cupo        INT,
    @precio      DECIMAL(12,2),
    @insertados  INT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    WITH numeros AS (
        SELECT TOP (DATEDIFF(DAY, @desde, @hasta) + 1)
               ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS n
        FROM sys.all_objects AS a CROSS JOIN sys.all_objects AS b
    )
    INSERT INTO dbo.disponibilidad_servicio (id_servicio, fecha, cantidad_total, cantidad_disponible, precio_fecha, actualizado_en)
    SELECT @id_servicio, DATEADD(DAY, n, @desde), @cupo, @cupo, @precio, GETDATE()
    FROM numeros
    WHERE NOT EXISTS (SELECT 1 FROM dbo.disponibilidad_servicio AS d
                      WHERE d.id_servicio = @id_servicio AND d.fecha = DATEADD(DAY, numeros.n, @desde));
    SET @insertados = @@ROWCOUNT;
END
GO

/* Inserta el supertipo servicios (R1) validando el proveedor y normalizando la política de cancelación. */
CREATE OR ALTER PROCEDURE dbo.pa_interno_insertar_servicio
    @id_proveedor                BIGINT,
    @tipo_servicio               NVARCHAR(20),
    @nombre                      NVARCHAR(150),
    @descripcion                 NVARCHAR(2000),
    @precio_base                 DECIMAL(12,2),
    @moneda                      NVARCHAR(3),
    @permite_cancelacion         BIT,
    @horas_reembolso_total       INT,
    @porcentaje_reembolso_tardio DECIMAL(5,2),
    @politica_cancelacion        NVARCHAR(500),
    @imagen_ruta                 NVARCHAR(260),   -- L6: la imagen es dato común del servicio, no del subtipo
    @id_servicio                 BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.proveedores WHERE id = @id_proveedor)
        THROW 51010, N'El proveedor no existe.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.proveedores WHERE id = @id_proveedor AND activo = 1)
        THROW 51011, N'El proveedor está dado de baja. Reactívalo antes de asociarle servicios.', 1;
    IF NULLIF(LTRIM(RTRIM(@nombre)), N'') IS NULL
        THROW 51012, N'El nombre del servicio es obligatorio.', 1;

    -- Igual que PoliticaCancelacion.Normalizar: sin cancelación, horas y % en 0; texto vacío = NULL.
    IF @permite_cancelacion = 0
        SELECT @horas_reembolso_total = 0, @porcentaje_reembolso_tardio = 0;

    INSERT INTO dbo.servicios (id_proveedor, tipo_servicio, nombre, descripcion, precio_base, moneda,
                               permite_cancelacion, horas_reembolso_total, porcentaje_reembolso_tardio,
                               politica_cancelacion, imagen_ruta, activo, creado_en, actualizado_en)
    VALUES (@id_proveedor, @tipo_servicio, LTRIM(RTRIM(@nombre)), NULLIF(LTRIM(RTRIM(@descripcion)), N''),
            @precio_base, COALESCE(@moneda, N'MXN'),
            @permite_cancelacion, @horas_reembolso_total, @porcentaje_reembolso_tardio,
            NULLIF(LTRIM(RTRIM(@politica_cancelacion)), N''),
            NULLIF(LTRIM(RTRIM(@imagen_ruta)), N''), 1, GETDATE(), GETDATE());
    SET @id_servicio = SCOPE_IDENTITY();
END
GO

/* =====================================================================================
   ALTAS DEL CATÁLOGO (roles ADMINISTRADOR o SA)
   Cada alta guarda el servicio, su subtipo y su disponibilidad inicial juntos (R1, R3): si algo falla,
   no queda nada a medias.
   ===================================================================================== */

/* Alta de proveedor. Correo y teléfono no se repiten entre proveedores. */
CREATE OR ALTER PROCEDURE dbo.pa_alta_proveedor
    @id_usuario     BIGINT,
    @tipo_proveedor NVARCHAR(20),          -- AEROLINEA | HOTEL | RENTADORA | OTRO
    @nombre         NVARCHAR(120),
    @telefono       NVARCHAR(20),
    @correo         NVARCHAR(150),
    @id_ubicacion   BIGINT = NULL,
    @id_proveedor   BIGINT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXEC dbo.pa_interno_validar_rol @id_usuario, 'ADMINISTRADOR,SA';

    SELECT @nombre = LTRIM(RTRIM(@nombre)), @telefono = LTRIM(RTRIM(@telefono)), @correo = LOWER(LTRIM(RTRIM(@correo)));
    IF NULLIF(@nombre, N'') IS NULL
        THROW 51020, N'El nombre del proveedor es obligatorio.', 1;
    IF @id_ubicacion IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.ubicaciones WHERE id = @id_ubicacion AND activo = 1)
        THROW 51021, N'La ubicación no existe o está inactiva.', 1;
    IF EXISTS (SELECT 1 FROM dbo.proveedores WHERE correo = @correo)
        THROW 51022, N'Ya hay otro proveedor con ese correo.', 1;
    IF EXISTS (SELECT 1 FROM dbo.proveedores WHERE telefono = @telefono)
        THROW 51023, N'Ya hay otro proveedor con ese teléfono.', 1;

    BEGIN TRANSACTION;
        INSERT INTO dbo.proveedores (id_ubicacion, tipo_proveedor, nombre, telefono, correo, activo, creado_en, actualizado_en)
        VALUES (@id_ubicacion, @tipo_proveedor, @nombre, @telefono, @correo, 1, GETDATE(), GETDATE());
        SET @id_proveedor = SCOPE_IDENTITY();

        DECLARE @detalle NVARCHAR(500) = CONCAT(N'Alta del proveedor "', @nombre, N'" (', @tipo_proveedor, N').');
        EXEC dbo.pa_interno_registrar_bitacora @id_usuario, N'CREAR', N'proveedores', @id_proveedor, @detalle;
    COMMIT TRANSACTION;

    SELECT @id_proveedor AS id_proveedor;
END
GO

/* Alta de hospedaje: servicios + hospedajes + una fila de disponibilidad por noche del rango. */
CREATE OR ALTER PROCEDURE dbo.pa_alta_hospedaje
    @id_usuario                  BIGINT,
    @id_proveedor                BIGINT,
    @nombre                      NVARCHAR(150),
    @precio_base                 DECIMAL(12,2),
    @id_ubicacion                BIGINT,
    @tipo_habitacion             NVARCHAR(60),
    @hora_entrada                TIME(0),
    @hora_salida                 TIME(0),
    @inventario_desde            DATE,
    @inventario_cupo             INT,                   -- habitaciones por noche
    @inventario_hasta            DATE          = NULL,  -- NULL = solo @inventario_desde
    @inventario_precio           DECIMAL(12,2) = NULL,  -- NULL = precio base
    @capacidad_huespedes         INT           = 2,
    @categoria                   NVARCHAR(40)  = NULL,
    @descripcion                 NVARCHAR(2000) = NULL,
    @moneda                      NVARCHAR(3)   = N'MXN',
    @permite_cancelacion         BIT           = 1,
    @horas_reembolso_total       INT           = 48,
    @porcentaje_reembolso_tardio DECIMAL(5,2)  = 50,
    @politica_cancelacion        NVARCHAR(500) = NULL,
    @imagen_ruta                 NVARCHAR(260) = NULL,  -- la imagen la sube la app a wwwroot/uploads/hospedajes/
    @id_servicio                 BIGINT        = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXEC dbo.pa_interno_validar_rol @id_usuario, 'ADMINISTRADOR,SA';

    SET @inventario_hasta = COALESCE(@inventario_hasta, @inventario_desde);
    EXEC dbo.pa_interno_validar_inventario @inventario_desde, @inventario_hasta, @inventario_cupo;
    IF NOT EXISTS (SELECT 1 FROM dbo.ubicaciones WHERE id = @id_ubicacion AND activo = 1)
        THROW 51030, N'La ubicación del hospedaje no existe o está inactiva.', 1;

    BEGIN TRANSACTION;
        EXEC dbo.pa_interno_insertar_servicio @id_proveedor, N'HOSPEDAJE', @nombre, @descripcion, @precio_base, @moneda,
             @permite_cancelacion, @horas_reembolso_total, @porcentaje_reembolso_tardio, @politica_cancelacion,
             @imagen_ruta, @id_servicio OUTPUT;

        INSERT INTO dbo.hospedajes (id_servicio, id_ubicacion, tipo_habitacion, categoria, capacidad_huespedes, hora_entrada, hora_salida)
        VALUES (@id_servicio, @id_ubicacion, LTRIM(RTRIM(@tipo_habitacion)), NULLIF(LTRIM(RTRIM(@categoria)), N''),
                @capacidad_huespedes, @hora_entrada, @hora_salida);

        DECLARE @precio DECIMAL(12,2) = COALESCE(@inventario_precio, @precio_base);
        EXEC dbo.pa_interno_generar_disponibilidad @id_servicio, @inventario_desde, @inventario_hasta, @inventario_cupo, @precio;

        DECLARE @detalle NVARCHAR(500) = CONCAT(N'Alta de hospedaje "', @nombre, N'" con cupo ', @inventario_cupo,
            N' del ', FORMAT(@inventario_desde, 'dd/MM/yyyy'), N' al ', FORMAT(@inventario_hasta, 'dd/MM/yyyy'), N'.');
        EXEC dbo.pa_interno_registrar_bitacora @id_usuario, N'CREAR', N'servicios', @id_servicio, @detalle;
    COMMIT TRANSACTION;

    SELECT @id_servicio AS id_servicio;
END
GO

/* Alta de vehículo: servicios + vehiculos + una fila de disponibilidad por día de renta. */
CREATE OR ALTER PROCEDURE dbo.pa_alta_vehiculo
    @id_usuario                  BIGINT,
    @id_proveedor                BIGINT,
    @nombre                      NVARCHAR(150),
    @precio_base                 DECIMAL(12,2),
    @id_ubicacion_retiro         BIGINT,
    @categoria                   NVARCHAR(50),
    @marca                       NVARCHAR(50),
    @modelo                      NVARCHAR(60),
    @inventario_desde            DATE,
    @inventario_cupo             INT,                   -- vehículos por día
    @inventario_hasta            DATE          = NULL,
    @inventario_precio           DECIMAL(12,2) = NULL,
    @capacidad_pasajeros         TINYINT       = 5,
    @descripcion                 NVARCHAR(2000) = NULL,
    @moneda                      NVARCHAR(3)   = N'MXN',
    @permite_cancelacion         BIT           = 1,
    @horas_reembolso_total       INT           = 48,
    @porcentaje_reembolso_tardio DECIMAL(5,2)  = 50,
    @politica_cancelacion        NVARCHAR(500) = NULL,
    @imagen_ruta                 NVARCHAR(260) = NULL,
    @id_servicio                 BIGINT        = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXEC dbo.pa_interno_validar_rol @id_usuario, 'ADMINISTRADOR,SA';

    SET @inventario_hasta = COALESCE(@inventario_hasta, @inventario_desde);
    EXEC dbo.pa_interno_validar_inventario @inventario_desde, @inventario_hasta, @inventario_cupo;
    IF NOT EXISTS (SELECT 1 FROM dbo.ubicaciones WHERE id = @id_ubicacion_retiro AND activo = 1)
        THROW 51031, N'La ubicación de retiro no existe o está inactiva.', 1;

    BEGIN TRANSACTION;
        EXEC dbo.pa_interno_insertar_servicio @id_proveedor, N'VEHICULO', @nombre, @descripcion, @precio_base, @moneda,
             @permite_cancelacion, @horas_reembolso_total, @porcentaje_reembolso_tardio, @politica_cancelacion,
             @imagen_ruta, @id_servicio OUTPUT;

        INSERT INTO dbo.vehiculos (id_servicio, id_ubicacion_retiro, categoria, marca, modelo, capacidad_pasajeros)
        VALUES (@id_servicio, @id_ubicacion_retiro, LTRIM(RTRIM(@categoria)), LTRIM(RTRIM(@marca)), LTRIM(RTRIM(@modelo)),
                @capacidad_pasajeros);

        DECLARE @precio DECIMAL(12,2) = COALESCE(@inventario_precio, @precio_base);
        EXEC dbo.pa_interno_generar_disponibilidad @id_servicio, @inventario_desde, @inventario_hasta, @inventario_cupo, @precio;

        DECLARE @detalle NVARCHAR(500) = CONCAT(N'Alta de vehículo "', @nombre, N'" con cupo ', @inventario_cupo,
            N' del ', FORMAT(@inventario_desde, 'dd/MM/yyyy'), N' al ', FORMAT(@inventario_hasta, 'dd/MM/yyyy'), N'.');
        EXEC dbo.pa_interno_registrar_bitacora @id_usuario, N'CREAR', N'servicios', @id_servicio, @detalle;
    COMMIT TRANSACTION;

    SELECT @id_servicio AS id_servicio;
END
GO

/* Alta de tour / evento: servicios (EVENTO) + disponibilidad. Su ciudad es la ubicación de su proveedor. */
CREATE OR ALTER PROCEDURE dbo.pa_alta_evento
    @id_usuario                  BIGINT,
    @id_proveedor                BIGINT,
    @nombre                      NVARCHAR(150),
    @precio_base                 DECIMAL(12,2),
    @inventario_desde            DATE,
    @inventario_cupo             INT,                   -- lugares por fecha
    @inventario_hasta            DATE          = NULL,
    @inventario_precio           DECIMAL(12,2) = NULL,
    @descripcion                 NVARCHAR(2000) = NULL,
    @moneda                      NVARCHAR(3)   = N'MXN',
    @permite_cancelacion         BIT           = 1,
    @horas_reembolso_total       INT           = 24,
    @porcentaje_reembolso_tardio DECIMAL(5,2)  = 0,
    @politica_cancelacion        NVARCHAR(500) = NULL,
    @id_servicio                 BIGINT        = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXEC dbo.pa_interno_validar_rol @id_usuario, 'ADMINISTRADOR,SA';

    SET @inventario_hasta = COALESCE(@inventario_hasta, @inventario_desde);
    EXEC dbo.pa_interno_validar_inventario @inventario_desde, @inventario_hasta, @inventario_cupo;
    IF NOT EXISTS (SELECT 1 FROM dbo.proveedores WHERE id = @id_proveedor AND id_ubicacion IS NOT NULL)
        THROW 51032, N'El proveedor no tiene ubicación y el tour se muestra en su ciudad. Asígnale una ubicación primero.', 1;

    BEGIN TRANSACTION;
        EXEC dbo.pa_interno_insertar_servicio @id_proveedor, N'EVENTO', @nombre, @descripcion, @precio_base, @moneda,
             @permite_cancelacion, @horas_reembolso_total, @porcentaje_reembolso_tardio, @politica_cancelacion,
             NULL, @id_servicio OUTPUT;

        DECLARE @precio DECIMAL(12,2) = COALESCE(@inventario_precio, @precio_base);
        EXEC dbo.pa_interno_generar_disponibilidad @id_servicio, @inventario_desde, @inventario_hasta, @inventario_cupo, @precio;

        DECLARE @detalle NVARCHAR(500) = CONCAT(N'Alta de tour "', @nombre, N'" con ', @inventario_cupo,
            N' lugares del ', FORMAT(@inventario_desde, 'dd/MM/yyyy'), N' al ', FORMAT(@inventario_hasta, 'dd/MM/yyyy'), N'.');
        EXEC dbo.pa_interno_registrar_bitacora @id_usuario, N'CREAR', N'servicios', @id_servicio, @detalle;
    COMMIT TRANSACTION;

    SELECT @id_servicio AS id_servicio;
END
GO

/* Alta de vuelo. Cada vuelo es UNA salida (vuelos.fecha_salida); con @repetir se crea una salida por fecha
   (DIARIO o SEMANAL con @dias_semana = '1,5' → lunes y viernes; 0 = domingo … 6 = sábado), cada una con
   su servicio, su vuelo y sus asientos. Máximo 120 salidas por alta. Devuelve las salidas creadas. */
CREATE OR ALTER PROCEDURE dbo.pa_alta_vuelo
    @id_usuario                  BIGINT,
    @id_proveedor                BIGINT,
    @nombre                      NVARCHAR(150),
    @precio_base                 DECIMAL(12,2),
    @id_origen                   BIGINT,
    @id_destino                  BIGINT,
    @codigo_vuelo                NVARCHAR(10),
    @fecha_salida                DATETIME,              -- primera salida
    @fecha_llegada               DATETIME,              -- llegada de la primera salida (define la duración)
    @asientos                    INT,
    @clase                       NVARCHAR(20)  = N'TURISTA',
    @precio_asiento              DECIMAL(12,2) = NULL,  -- NULL = precio base
    @repetir                     NVARCHAR(10)  = N'NO', -- NO | DIARIO | SEMANAL
    @repetir_hasta               DATE          = NULL,
    @dias_semana                 VARCHAR(20)   = NULL,
    @descripcion                 NVARCHAR(2000) = NULL,
    @moneda                      NVARCHAR(3)   = N'MXN',
    @permite_cancelacion         BIT           = 1,
    @horas_reembolso_total       INT           = 24,
    @porcentaje_reembolso_tardio DECIMAL(5,2)  = 0,
    @politica_cancelacion        NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXEC dbo.pa_interno_validar_rol @id_usuario, 'ADMINISTRADOR,SA';

    SET @codigo_vuelo = UPPER(LTRIM(RTRIM(@codigo_vuelo)));
    IF @codigo_vuelo IS NULL OR LEN(@codigo_vuelo) < 2 OR @codigo_vuelo LIKE N'%[^A-Z0-9]%'
        THROW 51040, N'El código de vuelo debe tener de 2 a 10 letras o números (p. ej. QZ123).', 1;
    IF @id_origen = @id_destino
        THROW 51041, N'El destino debe ser distinto del origen.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.ubicaciones WHERE id = @id_origen AND activo = 1)
       OR NOT EXISTS (SELECT 1 FROM dbo.ubicaciones WHERE id = @id_destino AND activo = 1)
        THROW 51042, N'El origen o el destino no existe o está inactivo.', 1;
    IF @fecha_salida <= GETDATE()
        THROW 51043, N'La primera salida debe ser en el futuro.', 1;
    IF @fecha_llegada <= @fecha_salida
        THROW 51044, N'La llegada debe ser posterior a la salida.', 1;
    IF DATEDIFF(MINUTE, @fecha_salida, @fecha_llegada) > 48 * 60
        THROW 51045, N'Un vuelo no puede durar más de 48 horas.', 1;
    IF @asientos IS NULL OR @asientos < 1 OR @asientos > 1000
        THROW 51046, N'Los asientos deben estar entre 1 y 1000.', 1;
    IF @repetir NOT IN (N'NO', N'DIARIO', N'SEMANAL')
        THROW 51047, N'@repetir debe ser NO, DIARIO o SEMANAL.', 1;

    -- Fechas de salida
    DECLARE @salidas TABLE (n INT IDENTITY(1,1), salida DATETIME);
    DECLARE @duracion INT = DATEDIFF(MINUTE, @fecha_salida, @fecha_llegada);
    DECLARE @primer_dia DATE = CAST(@fecha_salida AS DATE);
    DECLARE @hora TIME(0) = CAST(@fecha_salida AS TIME(0));

    IF @repetir = N'NO'
        INSERT INTO @salidas (salida) VALUES (@fecha_salida);
    ELSE
    BEGIN
        IF @repetir_hasta IS NULL OR @repetir_hasta < @primer_dia
            THROW 51048, N'Indica hasta qué fecha se repite (igual o posterior a la primera salida).', 1;
        IF DATEDIFF(DAY, @primer_dia, @repetir_hasta) > 366
            THROW 51049, N'Máximo un año a partir de la primera salida.', 1;

        WITH numeros AS (
            SELECT TOP (DATEDIFF(DAY, @primer_dia, @repetir_hasta) + 1)
                   ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS n
            FROM sys.all_objects AS a CROSS JOIN sys.all_objects AS b
        )
        INSERT INTO @salidas (salida)
        SELECT CAST(DATEADD(DAY, n, @primer_dia) AS DATETIME) + CAST(@hora AS DATETIME)
        FROM numeros
        WHERE @repetir = N'DIARIO'
           -- 1900-01-07 fue domingo: (días desde esa fecha) % 7 = 0 domingo … 6 sábado (no depende de SET DATEFIRST).
           OR CHARINDEX(',' + CAST(DATEDIFF(DAY, '19000107', DATEADD(DAY, n, @primer_dia)) % 7 AS VARCHAR(1)) + ',',
                        ',' + REPLACE(COALESCE(@dias_semana, ''), ' ', '') + ',') > 0
        ORDER BY n;
    END

    DECLARE @total_salidas INT = (SELECT COUNT(*) FROM @salidas);
    IF @total_salidas = 0
        THROW 51050, N'Con esos días de la semana no hay ninguna salida en el periodo.', 1;
    IF @total_salidas > 120
        THROW 51051, N'Serían más de 120 salidas en una sola alta. Acorta el periodo.', 1;

    DECLARE @creados TABLE (id_servicio BIGINT, salida DATETIME, llegada DATETIME);
    DECLARE @i INT = 1, @salida DATETIME, @dia DATE, @id_servicio BIGINT;
    DECLARE @precio DECIMAL(12,2) = COALESCE(@precio_asiento, @precio_base);

    BEGIN TRANSACTION;
        WHILE @i <= @total_salidas
        BEGIN
            SELECT @salida = salida FROM @salidas WHERE n = @i;
            SET @dia = CAST(@salida AS DATE);

            EXEC dbo.pa_interno_insertar_servicio @id_proveedor, N'VUELO', @nombre, @descripcion, @precio_base, @moneda,
                 @permite_cancelacion, @horas_reembolso_total, @porcentaje_reembolso_tardio, @politica_cancelacion,
                 NULL, @id_servicio OUTPUT;

            INSERT INTO dbo.vuelos (id_servicio, id_origen, id_destino, codigo_vuelo, fecha_salida, fecha_llegada, clase)
            VALUES (@id_servicio, @id_origen, @id_destino, @codigo_vuelo, @salida, DATEADD(MINUTE, @duracion, @salida), @clase);

            EXEC dbo.pa_interno_generar_disponibilidad @id_servicio, @dia, @dia, @asientos, @precio;

            INSERT INTO @creados VALUES (@id_servicio, @salida, DATEADD(MINUTE, @duracion, @salida));
            SET @i += 1;
        END

        DECLARE @primero BIGINT = (SELECT MIN(id_servicio) FROM @creados);
        DECLARE @detalle NVARCHAR(500) = CONCAT(N'Alta del vuelo ', @codigo_vuelo, N': ', @total_salidas, N' salida(s) del ',
            FORMAT(@primer_dia, 'dd/MM/yyyy'), N' al ', FORMAT((SELECT MAX(salida) FROM @creados), 'dd/MM/yyyy'),
            N', ', @asientos, N' asientos cada una.');
        EXEC dbo.pa_interno_registrar_bitacora @id_usuario, N'CREAR', N'servicios', @primero, @detalle;
    COMMIT TRANSACTION;

    SELECT id_servicio, salida, llegada FROM @creados ORDER BY salida;
END
GO

/* =====================================================================================
   OPERACIONES DEL NEGOCIO
   ===================================================================================== */

/* Agrega o ajusta el cupo (y opcionalmente el precio) de cada día del rango (roles ADMINISTRADOR o SA).
   - Si la fecha ya existe se actualiza; si no, se crea (nunca se duplica).
   - El cupo NUNCA baja de lo ya vendido: un solo UPDATE condicional (SQL Server calcula el nuevo disponible
     con los valores ANTERIORES de la fila), así no se sobrevende aunque alguien compre en ese instante.
   - Cupo 0 = cerrar esas fechas. @precio NULL = conservar el precio de cada fecha (las nuevas: precio base).
   - Vuelo: solo existe el día de su salida (se ignoran @desde y @hasta). */
CREATE OR ALTER PROCEDURE dbo.pa_ajustar_disponibilidad
    @id_usuario  BIGINT,
    @id_servicio BIGINT,
    @desde       DATE,
    @cupo        INT,
    @hasta       DATE          = NULL,
    @precio      DECIMAL(12,2) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXEC dbo.pa_interno_validar_rol @id_usuario, 'ADMINISTRADOR,SA';

    DECLARE @tipo NVARCHAR(20), @nombre NVARCHAR(150), @precio_base DECIMAL(12,2);
    SELECT @tipo = tipo_servicio, @nombre = nombre, @precio_base = precio_base FROM dbo.servicios WHERE id = @id_servicio;
    IF @tipo IS NULL THROW 51060, N'El servicio no existe.', 1;

    IF @tipo = N'VUELO'
    BEGIN
        SELECT @desde = CAST(fecha_salida AS DATE) FROM dbo.vuelos WHERE id_servicio = @id_servicio;
        IF @@ROWCOUNT = 0 THROW 51061, N'Este vuelo no tiene sus datos de salida.', 1;
        SET @hasta = @desde;
        IF @cupo IS NULL OR @cupo < 0 OR @cupo > 100000 THROW 51062, N'El cupo debe estar entre 0 y 100,000.', 1;
    END
    ELSE
    BEGIN
        SET @hasta = COALESCE(@hasta, @desde);
        EXEC dbo.pa_interno_validar_inventario @desde, @hasta, @cupo, 0;
    END

    DECLARE @existentes INT, @actualizados INT, @nuevos INT;

    BEGIN TRANSACTION;
        SELECT @existentes = COUNT(*)
        FROM dbo.disponibilidad_servicio WITH (UPDLOCK, HOLDLOCK)
        WHERE id_servicio = @id_servicio AND fecha BETWEEN @desde AND @hasta;

        UPDATE dbo.disponibilidad_servicio
        SET cantidad_total      = @cupo,
            cantidad_disponible = @cupo - (cantidad_total - cantidad_disponible),
            precio_fecha        = COALESCE(@precio, precio_fecha),
            actualizado_en      = GETDATE()
        WHERE id_servicio = @id_servicio AND fecha BETWEEN @desde AND @hasta
          AND cantidad_total - cantidad_disponible <= @cupo;
        SET @actualizados = @@ROWCOUNT;

        IF @actualizados <> @existentes
        BEGIN
            DECLARE @fechas NVARCHAR(300) = N'';
            SELECT TOP (5) @fechas = @fechas + CONCAT(FORMAT(fecha, 'dd/MM'), N' (', cantidad_total - cantidad_disponible, N' vendido(s)), ')
            FROM dbo.disponibilidad_servicio
            WHERE id_servicio = @id_servicio AND fecha BETWEEN @desde AND @hasta
              AND cantidad_total - cantidad_disponible > @cupo
            ORDER BY fecha;
            DECLARE @mensaje NVARCHAR(400) = CONCAT(N'No se guardó nada: el cupo no puede ser menor a lo ya vendido en ', @fechas, N'...');
            THROW 51063, @mensaje, 1;
        END

        DECLARE @precio_nuevas DECIMAL(12,2) = COALESCE(@precio, @precio_base);
        EXEC dbo.pa_interno_generar_disponibilidad @id_servicio, @desde, @hasta, @cupo, @precio_nuevas, @nuevos OUTPUT;

        DECLARE @detalle NVARCHAR(500) = CONCAT(N'"', @nombre, N'": cupo ', @cupo, N' del ', FORMAT(@desde, 'dd/MM/yyyy'),
            N' al ', FORMAT(@hasta, 'dd/MM/yyyy'), CASE WHEN @precio IS NULL THEN N'' ELSE CONCAT(N', precio ', FORMAT(@precio, 'N2')) END,
            N' (', @nuevos, N' día(s) nuevos, ', @actualizados, N' ajustados).');
        EXEC dbo.pa_interno_registrar_bitacora @id_usuario, N'DISPONIBILIDAD', N'servicios', @id_servicio, @detalle;
    COMMIT TRANSACTION;

    SELECT @nuevos AS dias_nuevos, @actualizados AS dias_ajustados;
END
GO

/* Cancela una reservación con la política GUARDADA en la compra (R4), igual que Services/ReglasCancelacion.cs:
   - No se cancela si el estado es final, si el servicio ya inició o si la compra no admite cancelación.
   - Reembolso: 100 % si faltan al menos horas_reembolso_total horas para el inicio; si no, el % tardío,
     sobre el pago APROBADO más reciente.
   - Puede hacerlo el dueño (CLIENTE) o el personal (AGENTE, ADMINISTRADOR, SA) a nombre del cliente.
   - Devuelve el cupo (R3) sin pasar de cantidad_total.
   - @reembolsar = 1 del CLIENTE: el reembolso queda SOLICITADO (R8) y lo resuelve el personal (pa_resolver_reembolso).
   - @reembolsar = 1 del PERSONAL: el reembolso se procesa en el mismo paso (PROCESADO + pago REEMBOLSADO), igual
     que Services/CancelacionesService.cs. Con @cortesia = 1 se devuelve el 100 % del pago aunque la política diga
     menos (solo el personal; el motivo es obligatorio).
   - @motivo: obligatorio (10 a 250) al pedir reembolso y siempre que cancela el personal; opcional (máx. 250) si el
     cliente solo cancela. */
CREATE OR ALTER PROCEDURE dbo.pa_cancelar_reservacion
    @id_usuario     BIGINT,
    @id_reservacion BIGINT,
    @reembolsar     BIT           = 0,
    @motivo         NVARCHAR(250) = NULL,
    @cortesia       BIT           = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @dueno BIGINT, @codigo NVARCHAR(30), @id_estado SMALLINT, @es_final BIT, @moneda NVARCHAR(3);
    SELECT @dueno = r.id_usuarioglobal, @codigo = r.codigo_reserva, @id_estado = r.id_estado_reservacion,
           @es_final = e.es_final, @moneda = r.moneda
    FROM dbo.reservaciones AS r
    JOIN dbo.estados_reservacion AS e ON e.id = r.id_estado_reservacion
    WHERE r.id = @id_reservacion;
    IF @dueno IS NULL THROW 51070, N'La reservación no existe.', 1;

    DECLARE @es_personal BIT = 0;
    IF @id_usuario <> @dueno
    BEGIN
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'AGENTE,ADMINISTRADOR,SA';
        SET @es_personal = 1;
    END
    ELSE
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'CLIENTE';

    SET @motivo = NULLIF(LTRIM(RTRIM(@motivo)), N'');
    IF (@reembolsar = 1 OR @es_personal = 1) AND (@motivo IS NULL OR LEN(@motivo) < 10)
        THROW 51071, N'El motivo debe tener entre 10 y 250 caracteres.', 1;
    IF @cortesia = 1 AND (@es_personal = 0 OR @reembolsar = 0)
        THROW 51079, N'El reembolso de cortesía (100 %) solo lo da el personal al cancelar con reembolso.', 1;

    -- Política de la primera línea (en esta versión cada reservación tiene un solo servicio).
    DECLARE @inicio DATETIME, @permite BIT, @horas INT, @pct_tardio DECIMAL(5,2);
    SELECT @inicio = MIN(fecha_inicio) FROM dbo.detalle_reservacion WHERE id_reservacion = @id_reservacion;
    SELECT TOP (1) @permite = permite_cancelacion, @horas = horas_reembolso_total, @pct_tardio = porcentaje_reembolso_tardio
    FROM dbo.detalle_reservacion WHERE id_reservacion = @id_reservacion ORDER BY id;

    IF @inicio IS NULL   THROW 51072, N'La reservación no tiene detalle.', 1;
    IF @es_final = 1     THROW 51073, N'Esta reservación ya está cancelada o terminada.', 1;
    IF @inicio <= GETDATE() THROW 51074, N'El servicio ya inició: ya no se puede cancelar.', 1;
    IF @permite = 0      THROW 51075, N'Esta compra no admite cancelación (tarifa no reembolsable).', 1;

    DECLARE @porcentaje DECIMAL(5,2) =
        CASE WHEN DATEDIFF(MINUTE, GETDATE(), @inicio) >= @horas * 60 THEN 100 ELSE @pct_tardio END;
    DECLARE @id_pago BIGINT, @monto_pagado DECIMAL(12,2);
    SELECT TOP (1) @id_pago = p.id, @monto_pagado = p.monto
    FROM dbo.pagos AS p
    JOIN dbo.estados_pago AS ep ON ep.id = p.id_estado_pago
    WHERE p.id_reservacion = @id_reservacion AND ep.nombre = N'APROBADO'
    ORDER BY p.id DESC;
    IF @cortesia = 1 SET @porcentaje = 100;
    DECLARE @monto_reembolso DECIMAL(12,2) = ROUND(COALESCE(@monto_pagado, 0) * @porcentaje / 100, 2);

    IF @reembolsar = 1 AND @id_pago IS NULL  THROW 51076, N'No hay un pago aprobado que reembolsar.', 1;
    IF @reembolsar = 1 AND @monto_reembolso <= 0
        THROW 51077, N'Según la política de la compra, a estas alturas ya no hay reembolso.', 1;

    DECLARE @id_cancelada SMALLINT = (SELECT id FROM dbo.estados_reservacion WHERE nombre = N'CANCELADA');

    BEGIN TRANSACTION;
        -- a) Estado: UPDATE condicional (si otra petición la cambió al mismo tiempo, no se toca nada).
        UPDATE dbo.reservaciones SET id_estado_reservacion = @id_cancelada
        WHERE id = @id_reservacion AND id_estado_reservacion = @id_estado;
        IF @@ROWCOUNT <> 1 THROW 51078, N'La reservación cambió mientras se cancelaba. Vuelve a consultarla.', 1;

        INSERT INTO dbo.historial_estado_reservacion (id_reservacion, id_estado_reservacion, id_usuario_cambio, fecha_cambio, motivo)
        VALUES (@id_reservacion, @id_cancelada, @id_usuario, GETDATE(),
                COALESCE(@motivo, CASE WHEN @es_personal = 1 THEN N'Cancelada por el personal a nombre del cliente'
                                       WHEN @reembolsar = 1 THEN N'Cancelada por el cliente con reembolso'
                                       ELSE N'Cancelada por el cliente' END));

        -- b) Devolver el cupo: hospedaje y vehículo, de la fecha de inicio a la víspera de la fecha de fin
        --    (noches / días de renta); vuelo, tour y paquete, solo el día de inicio. Nunca por encima del total.
        UPDATE ds
        SET ds.cantidad_disponible = ds.cantidad_disponible + x.unidades,
            ds.actualizado_en      = GETDATE()
        FROM dbo.disponibilidad_servicio AS ds
        JOIN (
            SELECT d.id_servicio,
                   CAST(CEILING(d.cantidad) AS INT) AS unidades,
                   CAST(d.fecha_inicio AS DATE) AS desde,
                   CASE WHEN s.tipo_servicio IN (N'HOSPEDAJE', N'VEHICULO')
                             AND DATEADD(DAY, -1, CAST(d.fecha_fin AS DATE)) >= CAST(d.fecha_inicio AS DATE)
                        THEN DATEADD(DAY, -1, CAST(d.fecha_fin AS DATE))
                        ELSE CAST(d.fecha_inicio AS DATE) END AS hasta
            FROM dbo.detalle_reservacion AS d
            JOIN dbo.servicios AS s ON s.id = d.id_servicio
            WHERE d.id_reservacion = @id_reservacion
        ) AS x ON x.id_servicio = ds.id_servicio AND ds.fecha BETWEEN x.desde AND x.hasta
        WHERE ds.cantidad_disponible + x.unidades <= ds.cantidad_total;

        -- c) Reembolso del pago aprobado (R8) + su bitácora. Del cliente: SOLICITADO (lo resuelve el personal con
        --    pa_resolver_reembolso). Del personal: se procesa aquí mismo (devolución simulada).
        DECLARE @id_reembolso BIGINT = NULL;
        IF @reembolsar = 1
        BEGIN
            DECLARE @ahora DATETIME = GETDATE();
            DECLARE @referencia NVARCHAR(100) =
                CASE WHEN @es_personal = 1 THEN N'RSIM-' + LEFT(REPLACE(CONVERT(NVARCHAR(36), NEWID()), N'-', N''), 16) END;

            INSERT INTO dbo.reembolsos (id_pago, monto, motivo, estado, fecha_solicitud, fecha_resolucion, referencia_externa)
            VALUES (@id_pago, @monto_reembolso, @motivo,
                    CASE WHEN @es_personal = 1 THEN N'PROCESADO' ELSE N'SOLICITADO' END,
                    @ahora, CASE WHEN @es_personal = 1 THEN @ahora END, @referencia);
            SET @id_reembolso = SCOPE_IDENTITY();

            INSERT INTO dbo.historial_estado_reembolso (id_reembolso, id_usuario_cambio, estado, fecha_cambio, detalle)
            VALUES (@id_reembolso, @id_usuario, N'SOLICITADO', @ahora,
                    CONCAT(N'Solicitado ', CASE WHEN @es_personal = 1 THEN N'por el personal' ELSE N'por el cliente' END,
                           N': ', FORMAT(@porcentaje, '0.##'),
                           CASE WHEN @cortesia = 1 THEN N' % de cortesía.' ELSE N' % según la política de la compra.' END));

            IF @es_personal = 1
            BEGIN
                INSERT INTO dbo.historial_estado_reembolso (id_reembolso, id_usuario_cambio, estado, fecha_cambio, detalle)
                VALUES (@id_reembolso, @id_usuario, N'APROBADO', DATEADD(SECOND, 1, @ahora), N'Aprobado al cancelar a nombre del cliente'),
                       (@id_reembolso, @id_usuario, N'PROCESADO', DATEADD(SECOND, 2, @ahora), CONCAT(N'Devolución simulada ', @referencia));

                DECLARE @id_reembolsado SMALLINT = (SELECT id FROM dbo.estados_pago WHERE nombre = N'REEMBOLSADO');
                UPDATE dbo.pagos SET id_estado_pago = @id_reembolsado WHERE id = @id_pago;
                INSERT INTO dbo.historial_estado_pago (id_pago, id_estado_pago, fecha_cambio, detalle)
                VALUES (@id_pago, @id_reembolsado, DATEADD(SECOND, 2, @ahora), CONCAT(N'Reembolso de $', FORMAT(@monto_reembolso, 'N2')));
            END
        END

        IF @es_personal = 1
        BEGIN
            DECLARE @detalle NVARCHAR(500) = CONCAT(N'Cancelación de ', @codigo, N' a nombre del cliente',
                CASE WHEN @reembolsar = 1 THEN CONCAT(N' con reembolso de $', FORMAT(@monto_reembolso, 'N2'), N' ', @moneda,
                                                      CASE WHEN @cortesia = 1 THEN N' (cortesía 100 %)' ELSE N'' END)
                     ELSE N'' END, N': ', @motivo);
            EXEC dbo.pa_interno_registrar_bitacora @id_usuario, N'CANCELAR', N'reservaciones', @id_reservacion, @detalle;
        END
    COMMIT TRANSACTION;

    SELECT @codigo AS codigo_reserva, @porcentaje AS porcentaje_reembolso,
           CASE WHEN @reembolsar = 1 THEN @monto_reembolso ELSE 0 END AS monto_reembolso_solicitado,
           @id_reembolso AS id_reembolso;
END
GO

/* Resuelve un reembolso SOLICITADO (roles AGENTE, ADMINISTRADOR o SA). El monto es el que calculó la política
   y no se edita.
   - Aprobar: reembolso APROBADO -> PROCESADO (devolución simulada) y el pago pasa a REEMBOLSADO.
   - Rechazar: @detalle obligatorio (10 a 250 caracteres): el motivo que verá el cliente. */
CREATE OR ALTER PROCEDURE dbo.pa_resolver_reembolso
    @id_usuario   BIGINT,
    @id_reembolso BIGINT,
    @aprobar      BIT,
    @detalle      NVARCHAR(250) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXEC dbo.pa_interno_validar_rol @id_usuario, 'AGENTE,ADMINISTRADOR,SA';

    DECLARE @estado NVARCHAR(15), @id_pago BIGINT, @monto DECIMAL(12,2);
    SELECT @estado = estado, @id_pago = id_pago, @monto = monto FROM dbo.reembolsos WHERE id = @id_reembolso;
    IF @estado IS NULL THROW 51080, N'El reembolso no existe.', 1;
    IF @estado <> N'SOLICITADO'
    BEGIN
        DECLARE @mensaje NVARCHAR(200) = CONCAT(N'Este reembolso ya fue resuelto (', @estado, N').');
        THROW 51081, @mensaje, 1;
    END

    SET @detalle = NULLIF(LTRIM(RTRIM(@detalle)), N'');
    IF @aprobar = 0 AND (@detalle IS NULL OR LEN(@detalle) < 10)
        THROW 51082, N'Para rechazar escribe el motivo (10 a 250 caracteres).', 1;

    DECLARE @ahora DATETIME = GETDATE();
    DECLARE @referencia NVARCHAR(100) =
        CASE WHEN @aprobar = 1 THEN N'RSIM-' + LEFT(REPLACE(CONVERT(NVARCHAR(36), NEWID()), N'-', N''), 16) END;

    BEGIN TRANSACTION;
        UPDATE dbo.reembolsos
        SET estado = CASE WHEN @aprobar = 1 THEN N'PROCESADO' ELSE N'RECHAZADO' END,
            fecha_resolucion = @ahora,
            referencia_externa = @referencia
        WHERE id = @id_reembolso AND estado = N'SOLICITADO';
        IF @@ROWCOUNT <> 1 THROW 51083, N'Otro usuario resolvió este reembolso al mismo tiempo.', 1;

        IF @aprobar = 1
        BEGIN
            INSERT INTO dbo.historial_estado_reembolso (id_reembolso, id_usuario_cambio, estado, fecha_cambio, detalle)
            VALUES (@id_reembolso, @id_usuario, N'APROBADO', @ahora, COALESCE(@detalle, N'Aprobado por el personal')),
                   (@id_reembolso, @id_usuario, N'PROCESADO', DATEADD(SECOND, 1, @ahora), CONCAT(N'Devolución simulada ', @referencia));

            DECLARE @id_reembolsado SMALLINT = (SELECT id FROM dbo.estados_pago WHERE nombre = N'REEMBOLSADO');
            UPDATE dbo.pagos SET id_estado_pago = @id_reembolsado WHERE id = @id_pago;
            INSERT INTO dbo.historial_estado_pago (id_pago, id_estado_pago, fecha_cambio, detalle)
            VALUES (@id_pago, @id_reembolsado, DATEADD(SECOND, 1, @ahora), CONCAT(N'Reembolso de $', FORMAT(@monto, 'N2')));
        END
        ELSE
            INSERT INTO dbo.historial_estado_reembolso (id_reembolso, id_usuario_cambio, estado, fecha_cambio, detalle)
            VALUES (@id_reembolso, @id_usuario, N'RECHAZADO', @ahora, @detalle);

        DECLARE @texto NVARCHAR(500) = CONCAT(CASE WHEN @aprobar = 1 THEN N'Reembolso aprobado' ELSE N'Reembolso rechazado' END,
            N' por $', FORMAT(@monto, 'N2'), CASE WHEN @aprobar = 1 THEN N'' ELSE CONCAT(N': ', @detalle) END, N'.');
        DECLARE @accion NVARCHAR(30) = CASE WHEN @aprobar = 1 THEN N'APROBAR' ELSE N'RECHAZAR' END;
        EXEC dbo.pa_interno_registrar_bitacora @id_usuario, @accion, N'reembolsos', @id_reembolso, @texto;
    COMMIT TRANSACTION;

    SELECT id, estado, monto, fecha_resolucion, referencia_externa FROM dbo.reembolsos WHERE id = @id_reembolso;
END
GO

/* Toma un ticket ABIERTO sin agente (roles SOPORTE, ADMINISTRADOR o SA). El UPDATE condicional evita que dos
   personas tomen el mismo ticket. */
CREATE OR ALTER PROCEDURE dbo.pa_tomar_ticket
    @id_usuario BIGINT,
    @id_ticket  BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXEC dbo.pa_interno_validar_rol @id_usuario, 'SOPORTE,ADMINISTRADOR,SA';

    BEGIN TRANSACTION;
        UPDATE dbo.solicitudes_soporte
        SET id_agente_asignado = @id_usuario, estado = N'EN_PROCESO', actualizada_en = GETDATE()
        WHERE id = @id_ticket AND id_agente_asignado IS NULL AND estado = N'ABIERTA';

        IF @@ROWCOUNT = 0
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM dbo.solicitudes_soporte WHERE id = @id_ticket)
                THROW 51090, N'El ticket no existe.', 1;
            THROW 51091, N'Otro agente ya tomó este ticket (o ya no está abierto).', 1;
        END

        DECLARE @detalle NVARCHAR(500) = CONCAT(N'Ticket #', @id_ticket, N' tomado.');
        EXEC dbo.pa_interno_registrar_bitacora @id_usuario, N'TOMAR', N'solicitudes_soporte', @id_ticket, @detalle;
    COMMIT TRANSACTION;

    SELECT id, estado, id_agente_asignado, actualizada_en FROM dbo.solicitudes_soporte WHERE id = @id_ticket;
END
GO

/* Agrega un mensaje a la conversación de un ticket (tabla mensajes_soporte).
   - El CLIENTE dueño del ticket responde mientras no esté CERRADO (ABIERTO o EN_PROCESO).
   - El personal solo responde los tickets EN_PROCESO que tiene asignados (un ADMINISTRADOR o SA que quiera
     intervenir primero se lo reasigna con pa_reasignar_ticket). */
CREATE OR ALTER PROCEDURE dbo.pa_responder_ticket
    @id_usuario BIGINT,
    @id_ticket  BIGINT,
    @mensaje    NVARCHAR(2000)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @dueno BIGINT, @agente BIGINT, @estado NVARCHAR(20);
    SELECT @dueno = id_usuarioglobal, @agente = id_agente_asignado, @estado = estado
    FROM dbo.solicitudes_soporte WHERE id = @id_ticket;
    IF @estado IS NULL THROW 51096, N'El ticket no existe.', 1;

    SET @mensaje = NULLIF(LTRIM(RTRIM(@mensaje)), N'');
    IF @mensaje IS NULL OR LEN(@mensaje) < 2 THROW 51097, N'Escribe tu mensaje (2 a 2000 caracteres).', 1;

    DECLARE @es_personal BIT = CASE WHEN @id_usuario = @dueno THEN 0 ELSE 1 END;
    IF @es_personal = 0
    BEGIN
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'CLIENTE';
        IF @estado = N'CERRADA' THROW 51098, N'El ticket está cerrado: abre uno nuevo.', 1;
    END
    ELSE
    BEGIN
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'SOPORTE,ADMINISTRADOR,SA';
        IF @estado <> N'EN_PROCESO' OR @agente <> @id_usuario
            THROW 51099, N'Solo quien atiende el ticket (EN_PROCESO) puede responderlo.', 1;
    END

    BEGIN TRANSACTION;
        -- UPDATE condicional: si el ticket se cerró o se reasignó mientras tanto, no se agrega el mensaje.
        UPDATE dbo.solicitudes_soporte SET actualizada_en = GETDATE()
        WHERE id = @id_ticket
          AND ((@es_personal = 0 AND estado <> N'CERRADA')
            OR (@es_personal = 1 AND estado = N'EN_PROCESO' AND id_agente_asignado = @id_usuario));
        IF @@ROWCOUNT <> 1 THROW 51100, N'El ticket cambió mientras escribías. Vuelve a abrirlo.', 1;

        INSERT INTO dbo.mensajes_soporte (id_solicitud, id_usuario, es_personal, mensaje, creado_en)
        VALUES (@id_ticket, @id_usuario, @es_personal, @mensaje, GETDATE());
    COMMIT TRANSACTION;

    SELECT id, id_solicitud, es_personal, mensaje, creado_en FROM dbo.mensajes_soporte WHERE id = SCOPE_IDENTITY();
END
GO

/* Reasigna un ticket que no está cerrado (roles ADMINISTRADOR o SA) a una cuenta activa de SOPORTE, ADMINISTRADOR
   o SA. Si estaba ABIERTO pasa a EN_PROCESO. */
CREATE OR ALTER PROCEDURE dbo.pa_reasignar_ticket
    @id_usuario      BIGINT,
    @id_ticket       BIGINT,
    @id_agente_nuevo BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXEC dbo.pa_interno_validar_rol @id_usuario, 'ADMINISTRADOR,SA';

    DECLARE @estado NVARCHAR(20), @anterior BIGINT;
    SELECT @estado = estado, @anterior = id_agente_asignado FROM dbo.solicitudes_soporte WHERE id = @id_ticket;
    IF @estado IS NULL       THROW 51101, N'El ticket no existe.', 1;
    IF @estado = N'CERRADA'  THROW 51102, N'El ticket ya está cerrado.', 1;
    IF NOT EXISTS (
        SELECT 1 FROM dbo.usuarios AS u JOIN dbo.roles_usuarios AS r ON r.id = u.id_rol
        WHERE u.id = @id_agente_nuevo AND u.activo = 1 AND r.nombre IN ('SOPORTE', 'ADMINISTRADOR', 'SA'))
        THROW 51103, N'Elige una cuenta activa de SOPORTE, ADMINISTRADOR o SA.', 1;
    IF @anterior = @id_agente_nuevo THROW 51104, N'El ticket ya está asignado a esa persona.', 1;

    BEGIN TRANSACTION;
        UPDATE dbo.solicitudes_soporte
        SET id_agente_asignado = @id_agente_nuevo, estado = N'EN_PROCESO', actualizada_en = GETDATE()
        WHERE id = @id_ticket AND estado <> N'CERRADA'
          AND (id_agente_asignado = @anterior OR (id_agente_asignado IS NULL AND @anterior IS NULL));
        IF @@ROWCOUNT <> 1 THROW 51105, N'El ticket cambió mientras se reasignaba.', 1;

        DECLARE @detalle NVARCHAR(500) = CONCAT(N'Ticket #', @id_ticket, N' reasignado de ',
            COALESCE(CAST(@anterior AS NVARCHAR(20)), N'(sin agente)'), N' a ', @id_agente_nuevo, N'.');
        EXEC dbo.pa_interno_registrar_bitacora @id_usuario, N'REASIGNAR', N'solicitudes_soporte', @id_ticket, @detalle;
    COMMIT TRANSACTION;

    SELECT id, estado, id_agente_asignado, actualizada_en FROM dbo.solicitudes_soporte WHERE id = @id_ticket;
END
GO

/* Cierra un ticket EN_PROCESO. Solo quien lo atiende (un ADMINISTRADOR o SA primero se lo reasigna). */
CREATE OR ALTER PROCEDURE dbo.pa_cerrar_ticket
    @id_usuario BIGINT,
    @id_ticket  BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXEC dbo.pa_interno_validar_rol @id_usuario, 'SOPORTE,ADMINISTRADOR,SA';

    DECLARE @estado NVARCHAR(20), @agente BIGINT;
    SELECT @estado = estado, @agente = id_agente_asignado FROM dbo.solicitudes_soporte WHERE id = @id_ticket;
    IF @estado IS NULL       THROW 51092, N'El ticket no existe.', 1;
    IF @estado = N'CERRADA'  THROW 51093, N'El ticket ya está cerrado.', 1;
    IF @estado <> N'EN_PROCESO' OR @agente <> @id_usuario
        THROW 51094, N'Solo quien atiende el ticket puede cerrarlo.', 1;

    BEGIN TRANSACTION;
        UPDATE dbo.solicitudes_soporte
        SET estado = N'CERRADA', actualizada_en = GETDATE()
        WHERE id = @id_ticket AND estado = N'EN_PROCESO' AND id_agente_asignado = @id_usuario;
        IF @@ROWCOUNT <> 1 THROW 51095, N'El ticket cambió mientras se cerraba.', 1;

        DECLARE @detalle NVARCHAR(500) = CONCAT(N'Ticket #', @id_ticket, N' cerrado.');
        EXEC dbo.pa_interno_registrar_bitacora @id_usuario, N'CERRAR', N'solicitudes_soporte', @id_ticket, @detalle;
    COMMIT TRANSACTION;

    SELECT id, estado, id_agente_asignado, actualizada_en FROM dbo.solicitudes_soporte WHERE id = @id_ticket;
END
GO

/* =====================================================================================
   USUARIOS (solo SA), igual que Services/SAUsuariosService.cs:
   - El SA no puede cambiarse el rol ni desactivarse a sí mismo.
   - Siempre debe quedar al menos un SA activo.
   ===================================================================================== */

/* Cambia el rol de una cuenta (CLIENTE, SOPORTE, AGENTE, ADMINISTRADOR o SA). */
CREATE OR ALTER PROCEDURE dbo.pa_cambiar_rol_usuario
    @id_usuario BIGINT,          -- el SA que hace el cambio
    @id_cuenta  BIGINT,          -- la cuenta que cambia
    @rol        VARCHAR(30)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXEC dbo.pa_interno_validar_rol @id_usuario, 'SA';

    IF @id_cuenta = @id_usuario THROW 51110, N'No puedes cambiar tu propio rol.', 1;

    DECLARE @id_rol BIGINT = (SELECT id FROM dbo.roles_usuarios WHERE nombre = @rol AND activo = 1);
    IF @id_rol IS NULL THROW 51111, N'El rol no existe o está inactivo.', 1;

    DECLARE @rol_actual VARCHAR(30), @activo BIT;
    SELECT @rol_actual = r.nombre, @activo = u.activo
    FROM dbo.usuarios AS u JOIN dbo.roles_usuarios AS r ON r.id = u.id_rol
    WHERE u.id = @id_cuenta;
    IF @rol_actual IS NULL THROW 51112, N'La cuenta no existe.', 1;
    IF @rol_actual = @rol  THROW 51113, N'La cuenta ya tiene ese rol.', 1;

    BEGIN TRANSACTION;
        UPDATE dbo.usuarios SET id_rol = @id_rol WHERE id = @id_cuenta;

        -- Si era SA activo, debe quedar otro SA activo.
        IF @rol_actual = 'SA' AND @activo = 1 AND NOT EXISTS (
            SELECT 1 FROM dbo.usuarios AS u JOIN dbo.roles_usuarios AS r ON r.id = u.id_rol
            WHERE r.nombre = 'SA' AND u.activo = 1)
            THROW 51114, N'Debe quedar al menos un SA activo.', 1;

        DECLARE @detalle NVARCHAR(500) = CONCAT(N'Rol de la cuenta ', @id_cuenta, N': ', @rol_actual, N' -> ', @rol, N'.');
        EXEC dbo.pa_interno_registrar_bitacora @id_usuario, N'CAMBIAR_ROL', N'usuarios', @id_cuenta, @detalle;
    COMMIT TRANSACTION;

    SELECT u.id, u.correo, r.nombre AS rol, u.activo
    FROM dbo.usuarios AS u JOIN dbo.roles_usuarios AS r ON r.id = u.id_rol WHERE u.id = @id_cuenta;
END
GO

/* Activa o desactiva una cuenta (una cuenta inactiva no inicia sesión; nunca se borra). */
CREATE OR ALTER PROCEDURE dbo.pa_cambiar_estado_usuario
    @id_usuario BIGINT,
    @id_cuenta  BIGINT,
    @activo     BIT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXEC dbo.pa_interno_validar_rol @id_usuario, 'SA';

    IF @id_cuenta = @id_usuario AND @activo = 0 THROW 51115, N'No puedes desactivar tu propia cuenta.', 1;

    DECLARE @actual BIT = (SELECT activo FROM dbo.usuarios WHERE id = @id_cuenta);
    IF @actual IS NULL     THROW 51116, N'La cuenta no existe.', 1;
    IF @actual = @activo   THROW 51117, N'La cuenta ya está en ese estado.', 1;

    BEGIN TRANSACTION;
        UPDATE dbo.usuarios SET activo = @activo WHERE id = @id_cuenta AND activo = @actual;
        IF @@ROWCOUNT <> 1 THROW 51118, N'La cuenta cambió mientras se actualizaba.', 1;

        IF @activo = 0 AND NOT EXISTS (
            SELECT 1 FROM dbo.usuarios AS u JOIN dbo.roles_usuarios AS r ON r.id = u.id_rol
            WHERE r.nombre = 'SA' AND u.activo = 1)
            THROW 51114, N'Debe quedar al menos un SA activo.', 1;

        DECLARE @accion NVARCHAR(30) = CASE WHEN @activo = 1 THEN N'REACTIVAR' ELSE N'BAJA' END;
        DECLARE @detalle NVARCHAR(500) = CONCAT(N'Cuenta ', @id_cuenta, CASE WHEN @activo = 1 THEN N' activada.' ELSE N' desactivada.' END);
        EXEC dbo.pa_interno_registrar_bitacora @id_usuario, @accion, N'usuarios', @id_cuenta, @detalle;
    COMMIT TRANSACTION;

    SELECT id, correo, activo FROM dbo.usuarios WHERE id = @id_cuenta;
END
GO

/* =====================================================================================
   COMPRA: reservación + pago simulado en UNA transacción (rol CLIENTE)
   Mismas reglas que Services/CatalogoService.Cotizar y Services/ReservacionService.PagarAsync:
       HOSPEDAJE  @desde = entrada, @hasta = salida (noches = días entre ambas); cantidad = @habitaciones;
                  huéspedes <= capacidad × habitaciones; horario de entrada y salida del hotel.
       VEHICULO   @desde = retiro, @hasta = devolución (días de renta); cantidad = 1; pasajeros <= capacidad; 10:00.
       VUELO      la fecha es la de la salida (se ignoran @desde/@hasta); cantidad = @personas (asientos).
       PAQUETE    @desde = primer día; cantidad = @personas, entre el mínimo y el máximo del paquete.
       EVENTO     @desde = fecha del tour; cantidad = @personas.
   precio_unitario = suma de precio_fecha de cada día; subtotal = precio_unitario × cantidad (R4).
   Pago (R5): una tarjeta guardada (@id_tarjeta) o los datos TOKENIZADOS de una nueva (token, marca, últimos 4,
   vencimiento): el número completo y el CVV nunca llegan a la base de datos. Pago simulado: una tarjeta
   terminada en 0002 se rechaza y NO se crea nada. Si se envía @total_esperado y el total cambió, no se cobra.
   ===================================================================================== */
CREATE OR ALTER PROCEDURE dbo.pa_crear_reservacion_pago
    @id_usuario        BIGINT,
    @id_servicio       BIGINT,
    @id_metodo_pago    SMALLINT,
    @desde             DATE          = NULL,
    @hasta             DATE          = NULL,
    @personas          INT           = 1,
    @habitaciones      INT           = 1,
    @codigo_promocion  NVARCHAR(30)  = NULL,
    @id_tarjeta        BIGINT        = NULL,   -- tarjeta guardada del cliente
    @token_tarjeta     NVARCHAR(100) = NULL,   -- o tarjeta nueva, ya tokenizada por la pasarela
    @marca             NVARCHAR(20)  = NULL,   -- VISA | MASTERCARD | AMEX
    @ultimos4          NVARCHAR(4)   = NULL,
    @mes_expiracion    TINYINT       = NULL,
    @anio_expiracion   SMALLINT      = NULL,
    @guardar_tarjeta   BIT           = 0,      -- 0 = queda oculta (activo = 0), pero el pago la necesita
    @alias             NVARCHAR(40)  = NULL,
    @total_esperado    DECIMAL(12,2) = NULL,
    @codigo_reserva    NVARCHAR(30)  = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    EXEC dbo.pa_interno_validar_rol @id_usuario, 'CLIENTE';

    DECLARE @hoy DATE = CAST(GETDATE() AS DATE);
    IF @personas IS NULL OR @personas < 1 OR @personas > 20         THROW 51100, N'Las personas deben ser entre 1 y 20.', 1;
    IF @habitaciones IS NULL OR @habitaciones < 1 OR @habitaciones > 10 THROW 51101, N'Las habitaciones deben ser entre 1 y 10.', 1;

    -- ---------- Servicio (activo y de un proveedor activo) ----------
    DECLARE @tipo NVARCHAR(20), @nombre NVARCHAR(150), @moneda NVARCHAR(3), @permite BIT, @horas INT,
            @pct DECIMAL(5,2), @politica NVARCHAR(500);
    SELECT @tipo = s.tipo_servicio, @nombre = s.nombre, @moneda = s.moneda, @permite = s.permite_cancelacion,
           @horas = s.horas_reembolso_total, @pct = s.porcentaje_reembolso_tardio, @politica = s.politica_cancelacion
    FROM dbo.servicios AS s
    JOIN dbo.proveedores AS p ON p.id = s.id_proveedor
    WHERE s.id = @id_servicio AND s.activo = 1 AND p.activo = 1;
    IF @tipo IS NULL THROW 51102, N'El servicio no existe o ya no está disponible.', 1;

    -- ---------- Fechas que ocupa, cantidad y horario (igual que Cotizar) ----------
    DECLARE @fechas TABLE (fecha DATE PRIMARY KEY);
    DECLARE @cantidad INT, @inicio DATETIME, @fin DATETIME, @resumen NVARCHAR(150), @noches INT;

    IF @tipo IN (N'HOSPEDAJE', N'VEHICULO')
    BEGIN
        IF @desde IS NULL OR @hasta IS NULL       THROW 51103, N'Indica la fecha inicial y la final.', 1;
        IF @desde < @hoy                          THROW 51104, N'La fecha inicial no puede ser en el pasado.', 1;
        IF @desde > DATEADD(DAY, 365, @hoy)       THROW 51105, N'Solo se reserva con hasta 365 días de anticipación.', 1;
        IF @hasta <= @desde                       THROW 51106, N'La fecha final debe ser posterior a la inicial.', 1;
        IF DATEDIFF(DAY, @desde, @hasta) > 30     THROW 51107, N'El máximo es de 30 días.', 1;

        SET @noches = DATEDIFF(DAY, @desde, @hasta);
        WITH numeros AS (
            SELECT TOP (@noches) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS n FROM sys.all_objects
        )
        INSERT INTO @fechas (fecha) SELECT DATEADD(DAY, n, @desde) FROM numeros;
    END

    IF @tipo = N'HOSPEDAJE'
    BEGIN
        DECLARE @capacidad INT, @hora_entrada TIME(0), @hora_salida TIME(0);
        SELECT @capacidad = capacidad_huespedes, @hora_entrada = hora_entrada, @hora_salida = hora_salida
        FROM dbo.hospedajes WHERE id_servicio = @id_servicio;
        IF @capacidad IS NULL THROW 51108, N'El hospedaje no tiene sus datos completos.', 1;
        IF @personas > @capacidad * @habitaciones
            THROW 51109, N'Hay más huéspedes que la capacidad de las habitaciones: agrega habitaciones.', 1;

        SELECT @cantidad = @habitaciones,
               @inicio = CAST(@desde AS DATETIME) + CAST(@hora_entrada AS DATETIME),
               @fin    = CAST(@hasta AS DATETIME) + CAST(@hora_salida AS DATETIME),
               @resumen = CONCAT(@noches, CASE WHEN @noches = 1 THEN N' noche' ELSE N' noches' END, N' · ',
                                 @habitaciones, CASE WHEN @habitaciones = 1 THEN N' habitación' ELSE N' habitaciones' END, N' · ',
                                 @personas, CASE WHEN @personas = 1 THEN N' huésped' ELSE N' huéspedes' END);
    END
    ELSE IF @tipo = N'VEHICULO'
    BEGIN
        DECLARE @pasajeros INT = (SELECT capacidad_pasajeros FROM dbo.vehiculos WHERE id_servicio = @id_servicio);
        IF @pasajeros IS NULL THROW 51110, N'El vehículo no tiene sus datos completos.', 1;
        IF @personas > @pasajeros THROW 51111, N'Hay más pasajeros que la capacidad del vehículo.', 1;

        SELECT @cantidad = 1,
               @inicio = DATEADD(HOUR, 10, CAST(@desde AS DATETIME)),
               @fin    = DATEADD(HOUR, 10, CAST(@hasta AS DATETIME)),
               @resumen = CONCAT(@noches, CASE WHEN @noches = 1 THEN N' día' ELSE N' días' END, N' de renta · ',
                                 @personas, CASE WHEN @personas = 1 THEN N' pasajero' ELSE N' pasajeros' END);
    END
    ELSE IF @tipo = N'VUELO'
    BEGIN
        SELECT @inicio = fecha_salida, @fin = fecha_llegada FROM dbo.vuelos WHERE id_servicio = @id_servicio;
        IF @inicio IS NULL      THROW 51112, N'El vuelo no tiene sus datos completos.', 1;
        IF @inicio <= GETDATE() THROW 51113, N'Este vuelo ya salió.', 1;
        INSERT INTO @fechas (fecha) VALUES (CAST(@inicio AS DATE));
        SELECT @cantidad = @personas,
               @resumen = CONCAT(@personas, CASE WHEN @personas = 1 THEN N' pasajero' ELSE N' pasajeros' END);
    END
    ELSE
    BEGIN
        -- PAQUETE y EVENTO: un día de inicio.
        IF @desde IS NULL                    THROW 51114, N'Indica la fecha.', 1;
        IF @desde < @hoy                     THROW 51104, N'La fecha inicial no puede ser en el pasado.', 1;
        IF @desde > DATEADD(DAY, 365, @hoy)  THROW 51105, N'Solo se reserva con hasta 365 días de anticipación.', 1;
        INSERT INTO @fechas (fecha) VALUES (@desde);
        SELECT @cantidad = @personas, @inicio = CAST(@desde AS DATETIME);

        IF @tipo = N'PAQUETE'
        BEGIN
            DECLARE @duracion INT, @minimo INT, @maximo INT;
            SELECT @duracion = duracion_dias, @minimo = min_personas, @maximo = max_personas
            FROM dbo.paquete WHERE id_servicio = @id_servicio;
            IF @duracion IS NULL THROW 51115, N'El paquete no tiene sus datos completos.', 1;
            IF @personas < @minimo OR @personas > @maximo
            BEGIN
                DECLARE @msg_personas NVARCHAR(200) = CONCAT(N'Este paquete es para ', @minimo, N' a ', @maximo, N' personas.');
                THROW 51116, @msg_personas, 1;
            END
            SELECT @fin = DATEADD(MINUTE, 23 * 60 + 59, CAST(DATEADD(DAY, CASE WHEN @duracion > 1 THEN @duracion - 1 ELSE 0 END, @desde) AS DATETIME)),
                   @resumen = CONCAT(@duracion, CASE WHEN @duracion = 1 THEN N' día' ELSE N' días' END, N' · ',
                                     @personas, CASE WHEN @personas = 1 THEN N' persona' ELSE N' personas' END);
        END
        ELSE
            SELECT @fin = DATEADD(MINUTE, 23 * 60 + 59, CAST(@desde AS DATETIME)),
                   @resumen = CONCAT(@personas, CASE WHEN @personas = 1 THEN N' persona' ELSE N' personas' END);
    END

    -- ---------- Precio y cupo de cada día ----------
    DECLARE @dias INT = (SELECT COUNT(*) FROM @fechas);
    DECLARE @falla DATE = (
        SELECT TOP (1) f.fecha
        FROM @fechas AS f
        LEFT JOIN dbo.disponibilidad_servicio AS d ON d.id_servicio = @id_servicio AND d.fecha = f.fecha
        WHERE d.id IS NULL OR d.cantidad_disponible < @cantidad
        ORDER BY f.fecha);
    IF @falla IS NOT NULL
    BEGIN
        DECLARE @msg_cupo NVARCHAR(200) = CONCAT(N'Sin cupo suficiente el ', FORMAT(@falla, 'dd/MM/yyyy'), N'.');
        THROW 51117, @msg_cupo, 1;
    END

    DECLARE @precio_unitario DECIMAL(12,2) = (
        SELECT SUM(d.precio_fecha) FROM @fechas AS f
        JOIN dbo.disponibilidad_servicio AS d ON d.id_servicio = @id_servicio AND d.fecha = f.fecha);
    DECLARE @subtotal DECIMAL(12,2) = @precio_unitario * @cantidad;

    -- ---------- Promoción (un código por reservación) ----------
    DECLARE @id_promocion BIGINT = NULL, @descuento DECIMAL(12,2) = 0;
    SET @codigo_promocion = UPPER(NULLIF(LTRIM(RTRIM(@codigo_promocion)), N''));
    IF @codigo_promocion IS NOT NULL
    BEGIN
        DECLARE @tipo_descuento NVARCHAR(15), @valor DECIMAL(12,2), @minimo_compra DECIMAL(12,2);
        SELECT @id_promocion = id, @tipo_descuento = tipo_descuento, @valor = valor, @minimo_compra = minimo_compra
        FROM dbo.promociones
        WHERE codigo = @codigo_promocion AND activo = 1 AND GETDATE() BETWEEN fecha_inicio AND fecha_fin;
        IF @id_promocion IS NULL THROW 51118, N'El código no existe o ya no está vigente.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.promocion_servicio WHERE id_promocion = @id_promocion AND id_servicio = @id_servicio)
            THROW 51119, N'Este código no aplica a este servicio.', 1;
        IF @subtotal < @minimo_compra
        BEGIN
            DECLARE @msg_minimo NVARCHAR(200) = CONCAT(N'Este código aplica en compras desde $', FORMAT(@minimo_compra, 'N2'), N'.');
            THROW 51120, @msg_minimo, 1;
        END
        SET @descuento = CASE WHEN @tipo_descuento = N'PORCENTAJE' THEN ROUND(@subtotal * @valor / 100, 2) ELSE @valor END;
        IF @descuento > @subtotal SET @descuento = @subtotal;
    END

    DECLARE @total DECIMAL(12,2) = @subtotal - @descuento;
    IF @total <= 0 THROW 51121, N'El total debe ser mayor a cero.', 1;
    IF @total_esperado IS NOT NULL AND @total_esperado <> @total
    BEGIN
        DECLARE @msg_total NVARCHAR(200) = CONCAT(N'El precio cambió: ahora el total es $', FORMAT(@total, 'N2'), N'. No se cobró nada.');
        THROW 51122, @msg_total, 1;
    END

    -- ---------- Método y tarjeta (R5) ----------
    IF NOT EXISTS (SELECT 1 FROM dbo.metodos_pago WHERE id = @id_metodo_pago AND activo = 1)
        THROW 51123, N'El método de pago no existe o no está disponible.', 1;

    DECLARE @ultimos_cobro NVARCHAR(4), @mes TINYINT, @anio SMALLINT;
    IF @id_tarjeta IS NOT NULL
    BEGIN
        SELECT @ultimos_cobro = ultimos4, @mes = mes_expiracion, @anio = anio_expiracion
        FROM dbo.tarjetas_usuarios WHERE id = @id_tarjeta AND id_usuariog = @id_usuario AND activo = 1;
        IF @ultimos_cobro IS NULL THROW 51124, N'Esa tarjeta no está en tu cuenta.', 1;
    END
    ELSE
    BEGIN
        IF @token_tarjeta IS NULL OR @marca IS NULL OR @marca NOT IN (N'VISA', N'MASTERCARD', N'AMEX')
           OR @ultimos4 IS NULL OR @ultimos4 NOT LIKE N'[0-9][0-9][0-9][0-9]'
           OR @mes_expiracion IS NULL OR @mes_expiracion NOT BETWEEN 1 AND 12 OR @anio_expiracion IS NULL
            THROW 51125, N'Indica una tarjeta guardada o los datos tokenizados de una nueva (token, marca, últimos 4 y vencimiento).', 1;
        SELECT @ultimos_cobro = @ultimos4, @mes = @mes_expiracion, @anio = @anio_expiracion;
    END
    IF NOT (@anio > YEAR(@hoy) OR (@anio = YEAR(@hoy) AND @mes >= MONTH(@hoy)))
        THROW 51126, N'La tarjeta está vencida.', 1;
    IF @ultimos_cobro = N'0002'
        THROW 51127, N'El banco rechazó el cobro (tarjeta de prueba terminada en 0002). No se creó nada.', 1;

    -- ---------- Guardar todo ----------
    DECLARE @id_pendiente SMALLINT  = (SELECT id FROM dbo.estados_reservacion WHERE nombre = N'PENDIENTE');
    DECLARE @id_confirmada SMALLINT = (SELECT id FROM dbo.estados_reservacion WHERE nombre = N'CONFIRMADA');
    DECLARE @id_pago_pendiente SMALLINT = (SELECT id FROM dbo.estados_pago WHERE nombre = N'PENDIENTE');
    DECLARE @id_pago_aprobado SMALLINT  = (SELECT id FROM dbo.estados_pago WHERE nombre = N'APROBADO');
    DECLARE @ahora DATETIME = GETDATE();
    DECLARE @id_reservacion BIGINT, @id_pago BIGINT;
    DECLARE @descripcion NVARCHAR(250) = LEFT(CONCAT(@nombre, N' — ', @resumen), 250);
    DECLARE @letras CHAR(32) = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';   -- sin O, I, 0 ni 1 (se confunden)

    BEGIN TRANSACTION;
        -- a) Descontar cupo (R3): solo donde alcanza; si una fecha ya no alcanza, se deshace todo.
        UPDATE d
        SET d.cantidad_disponible = d.cantidad_disponible - @cantidad, d.actualizado_en = @ahora
        FROM dbo.disponibilidad_servicio AS d
        JOIN @fechas AS f ON f.fecha = d.fecha
        WHERE d.id_servicio = @id_servicio AND d.cantidad_disponible >= @cantidad;
        IF @@ROWCOUNT <> @dias THROW 51128, N'Se agotó el cupo mientras pagabas. No se cobró nada.', 1;

        -- b) Tarjeta nueva (tokenizada).
        IF @id_tarjeta IS NULL
        BEGIN
            INSERT INTO dbo.tarjetas_usuarios (id_usuariog, id_institucion, token_referencia, marca, ultimos4,
                                               mes_expiracion, anio_expiracion, alias, activo, creado_en)
            VALUES (@id_usuario, NULL, @token_tarjeta, @marca, @ultimos4, @mes_expiracion, @anio_expiracion,
                    NULLIF(LTRIM(RTRIM(@alias)), N''), @guardar_tarjeta, @ahora);
            SET @id_tarjeta = SCOPE_IDENTITY();
        END

        -- c) Código legible y único: FV-AAMMDD-XXXX.
        DECLARE @intento INT = 0;
        WHILE @intento < 10
        BEGIN
            SET @codigo_reserva = CONCAT(N'FV-', FORMAT(@ahora, 'yyMMdd'), N'-',
                SUBSTRING(@letras, 1 + ABS(CHECKSUM(NEWID())) % 32, 1), SUBSTRING(@letras, 1 + ABS(CHECKSUM(NEWID())) % 32, 1),
                SUBSTRING(@letras, 1 + ABS(CHECKSUM(NEWID())) % 32, 1), SUBSTRING(@letras, 1 + ABS(CHECKSUM(NEWID())) % 32, 1));
            IF NOT EXISTS (SELECT 1 FROM dbo.reservaciones WHERE codigo_reserva = @codigo_reserva) BREAK;
            SET @intento += 1;
        END
        IF @intento = 10 THROW 51129, N'No se pudo generar un código de reserva único.', 1;

        -- d) Reservación PENDIENTE con totales históricos y detalle con snapshot (R4).
        INSERT INTO dbo.reservaciones (id_usuarioglobal, id_estado_reservacion, codigo_reserva, moneda, subtotal, descuento_total, total, creada_en)
        VALUES (@id_usuario, @id_pendiente, @codigo_reserva, @moneda, @subtotal, @descuento, @total, @ahora);
        SET @id_reservacion = SCOPE_IDENTITY();

        INSERT INTO dbo.detalle_reservacion (id_reservacion, id_servicio, id_promocion, cantidad, fecha_inicio, fecha_fin,
                                             precio_unitario, descuento_aplicado, subtotal, descripcion_snapshot,
                                             permite_cancelacion, horas_reembolso_total, porcentaje_reembolso_tardio, politica_cancelacion)
        VALUES (@id_reservacion, @id_servicio, @id_promocion, @cantidad, @inicio, @fin,
                @precio_unitario, @descuento, @subtotal, @descripcion,
                @permite, @horas, @pct, @politica);

        INSERT INTO dbo.historial_estado_reservacion (id_reservacion, id_estado_reservacion, id_usuario_cambio, fecha_cambio, motivo)
        VALUES (@id_reservacion, @id_pendiente, @id_usuario, @ahora, N'Reservación creada');

        -- e) Pago aprobado (simulado) + su bitácora (R6).
        INSERT INTO dbo.pagos (id_reservacion, id_metodo_pago, id_tarjeta, id_estado_pago, monto, moneda, referencia_externa, fecha_pago, creado_en)
        VALUES (@id_reservacion, @id_metodo_pago, @id_tarjeta, @id_pago_aprobado, @total, @moneda,
                N'SIM-' + LEFT(REPLACE(CONVERT(NVARCHAR(36), NEWID()), N'-', N''), 16), @ahora, @ahora);
        SET @id_pago = SCOPE_IDENTITY();

        INSERT INTO dbo.historial_estado_pago (id_pago, id_estado_pago, fecha_cambio, detalle)
        VALUES (@id_pago, @id_pago_pendiente, @ahora, N'Cobro enviado a la pasarela (simulada)'),
               (@id_pago, @id_pago_aprobado, DATEADD(SECOND, 1, @ahora), N'Cobro aprobado (simulado)');

        -- f) Reservación CONFIRMADA (cambio automático: id_usuario_cambio = NULL).
        UPDATE dbo.reservaciones SET id_estado_reservacion = @id_confirmada WHERE id = @id_reservacion;
        INSERT INTO dbo.historial_estado_reservacion (id_reservacion, id_estado_reservacion, id_usuario_cambio, fecha_cambio, motivo)
        VALUES (@id_reservacion, @id_confirmada, NULL, DATEADD(SECOND, 1, @ahora), N'Pago aprobado');
    COMMIT TRANSACTION;

    SELECT @codigo_reserva AS codigo_reserva, @descripcion AS descripcion, @inicio AS inicio, @fin AS fin,
           @cantidad AS cantidad, @precio_unitario AS precio_unitario, @subtotal AS subtotal,
           @descuento AS descuento, @total AS total, @moneda AS moneda;
END
GO

/* =====================================================================================
   CONSULTAS Y REPORTES (solo leen; no requieren rol)
   ===================================================================================== */

/* Busca servicios de un tipo con cupo para unas fechas (misma lógica de destino y fechas que el catálogo):
     HOSPEDAJE / VEHICULO  @desde = entrada/retiro, @hasta = salida/devolución (noches o días entre ambas).
     VUELO                 salidas entre @desde y @hasta (NULL = solo ese día) que todavía no salen.
     PAQUETE / EVENTO      el día @desde.
   Destino: hospedaje = su ubicación; vehículo = punto de retiro; vuelo = destino; tour = ciudad del proveedor;
            paquete = su destino o, si no tiene, la ciudad de su hospedaje o el destino de su vuelo incluidos.
   @cantidad = unidades por día (habitaciones, vehículos, asientos o personas). */
CREATE OR ALTER PROCEDURE dbo.pa_buscar_disponibilidad
    @tipo_servicio NVARCHAR(20),
    @desde         DATE,
    @hasta         DATE   = NULL,
    @id_destino    BIGINT = NULL,
    @cantidad      INT    = 1
AS
BEGIN
    SET NOCOUNT ON;
    IF @tipo_servicio NOT IN (N'VUELO', N'HOSPEDAJE', N'VEHICULO', N'PAQUETE', N'EVENTO')
        THROW 51200, N'Tipo de servicio no válido (VUELO, HOSPEDAJE, VEHICULO, PAQUETE o EVENTO).', 1;
    IF @desde IS NULL THROW 51201, N'Indica la fecha inicial.', 1;
    IF @tipo_servicio IN (N'HOSPEDAJE', N'VEHICULO') AND (@hasta IS NULL OR @hasta <= @desde OR DATEDIFF(DAY, @desde, @hasta) > 30)
        THROW 51202, N'Para hospedaje y vehículo indica una fecha final posterior a la inicial (máximo 30 días).', 1;
    IF @cantidad IS NULL OR @cantidad < 1 SET @cantidad = 1;

    WITH candidatos AS (
        SELECT s.id, s.nombre, s.tipo_servicio, s.moneda, s.precio_base, p.nombre AS proveedor, v.fecha_salida,
               CASE s.tipo_servicio
                    WHEN N'VUELO'     THEN v.id_destino
                    WHEN N'HOSPEDAJE' THEN h.id_ubicacion
                    WHEN N'VEHICULO'  THEN ve.id_ubicacion_retiro
                    WHEN N'EVENTO'    THEN p.id_ubicacion
                    ELSE COALESCE(pq.id_ubicacion_destino, hotel.id_ubicacion, avion.id_destino)
               END AS id_ubicacion
        FROM dbo.servicios AS s
        JOIN dbo.proveedores AS p ON p.id = s.id_proveedor AND p.activo = 1
        LEFT JOIN dbo.vuelos AS v      ON v.id_servicio = s.id
        LEFT JOIN dbo.hospedajes AS h  ON h.id_servicio = s.id
        LEFT JOIN dbo.vehiculos AS ve  ON ve.id_servicio = s.id
        LEFT JOIN dbo.paquete AS pq    ON pq.id_servicio = s.id
        OUTER APPLY (SELECT TOP (1) h2.id_ubicacion FROM dbo.paquete_servicio AS ps
                     JOIN dbo.hospedajes AS h2 ON h2.id_servicio = ps.id_servicio_incluido
                     WHERE ps.id_servicio = s.id ORDER BY ps.orden) AS hotel
        OUTER APPLY (SELECT TOP (1) v2.id_destino FROM dbo.paquete_servicio AS ps
                     JOIN dbo.vuelos AS v2 ON v2.id_servicio = ps.id_servicio_incluido
                     WHERE ps.id_servicio = s.id ORDER BY ps.orden) AS avion
        WHERE s.activo = 1 AND s.tipo_servicio = @tipo_servicio
    ),
    filtrados AS (
        SELECT c.*
        FROM candidatos AS c
        WHERE (@id_destino IS NULL OR c.id_ubicacion = @id_destino)
          AND (c.tipo_servicio <> N'VUELO'
               OR (c.fecha_salida > GETDATE()
                   AND CAST(c.fecha_salida AS DATE) BETWEEN @desde AND COALESCE(@hasta, @desde)))
    ),
    numeros AS (
        SELECT TOP (31) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS n FROM sys.all_objects
    ),
    requeridas AS (
        SELECT f.id,
               CASE WHEN f.tipo_servicio = N'VUELO' THEN CAST(f.fecha_salida AS DATE) ELSE DATEADD(DAY, n.n, @desde) END AS fecha
        FROM filtrados AS f
        JOIN numeros AS n
          ON n.n < CASE WHEN f.tipo_servicio IN (N'HOSPEDAJE', N'VEHICULO') THEN DATEDIFF(DAY, @desde, @hasta) ELSE 1 END
    ),
    calculo AS (
        SELECT r.id,
               COUNT(*) AS dias_requeridos,
               SUM(CASE WHEN d.cantidad_disponible >= @cantidad THEN 1 ELSE 0 END) AS dias_con_cupo,
               SUM(CASE WHEN d.id IS NULL THEN 1 ELSE 0 END) AS dias_sin_cargar,
               MIN(d.cantidad_disponible) AS disponible_minimo,
               SUM(d.precio_fecha) AS precio_por_unidad
        FROM requeridas AS r
        LEFT JOIN dbo.disponibilidad_servicio AS d ON d.id_servicio = r.id AND d.fecha = r.fecha
        GROUP BY r.id
    )
    SELECT f.id AS id_servicio, f.nombre, f.proveedor, u.municipio_ciudad AS ciudad, u.estado, f.fecha_salida,
           c.dias_requeridos, c.dias_con_cupo, c.dias_sin_cargar, COALESCE(c.disponible_minimo, 0) AS disponible_minimo,
           c.precio_por_unidad, c.precio_por_unidad * @cantidad AS total_estimado, f.moneda,
           CAST(CASE WHEN c.dias_con_cupo = c.dias_requeridos THEN 1 ELSE 0 END AS BIT) AS disponible
    FROM filtrados AS f
    JOIN calculo AS c ON c.id = f.id
    LEFT JOIN dbo.ubicaciones AS u ON u.id = f.id_ubicacion
    ORDER BY disponible DESC, total_estimado, f.nombre;
END
GO

/* Comprobante completo de una reservación: cabecera, detalle, pagos, historial de estados y reembolsos. */
CREATE OR ALTER PROCEDURE dbo.pa_detalle_reservacion
    @codigo_reserva NVARCHAR(30)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @id BIGINT = (SELECT id FROM dbo.reservaciones WHERE codigo_reserva = @codigo_reserva);
    IF @id IS NULL THROW 51210, N'No existe una reservación con ese código.', 1;

    -- 1) Cabecera
    SELECT r.codigo_reserva, e.nombre AS estado, CONCAT(u.nombre, N' ', u.apellido_paterno) AS cliente, u.correo,
           r.creada_en, r.subtotal, r.descuento_total, r.total, r.moneda
    FROM dbo.reservaciones AS r
    JOIN dbo.estados_reservacion AS e ON e.id = r.id_estado_reservacion
    JOIN dbo.usuarios AS u ON u.id = r.id_usuarioglobal
    WHERE r.id = @id;

    -- 2) Detalle (snapshot de la compra, R4)
    SELECT s.nombre AS servicio, s.tipo_servicio, d.descripcion_snapshot, d.fecha_inicio, d.fecha_fin, d.cantidad,
           d.precio_unitario, d.descuento_aplicado, d.subtotal, pr.codigo AS codigo_promocion,
           d.permite_cancelacion, d.horas_reembolso_total, d.porcentaje_reembolso_tardio, d.politica_cancelacion
    FROM dbo.detalle_reservacion AS d
    JOIN dbo.servicios AS s ON s.id = d.id_servicio
    LEFT JOIN dbo.promociones AS pr ON pr.id = d.id_promocion
    WHERE d.id_reservacion = @id
    ORDER BY d.id;

    -- 3) Pagos (tarjeta enmascarada, R5)
    SELECT p.fecha_pago, m.nombre AS metodo, CONCAT(t.marca, N' •••• ', t.ultimos4) AS tarjeta, ep.nombre AS estado,
           p.monto, p.moneda, p.referencia_externa
    FROM dbo.pagos AS p
    JOIN dbo.metodos_pago AS m ON m.id = p.id_metodo_pago
    JOIN dbo.tarjetas_usuarios AS t ON t.id = p.id_tarjeta
    JOIN dbo.estados_pago AS ep ON ep.id = p.id_estado_pago
    WHERE p.id_reservacion = @id
    ORDER BY p.fecha_pago;

    -- 4) Historial de estados (R6)
    SELECT h.fecha_cambio, e.nombre AS estado, h.motivo,
           CASE WHEN u.id IS NULL THEN N'Sistema' ELSE CONCAT(u.nombre, N' ', u.apellido_paterno, N' (', ro.nombre, N')') END AS quien
    FROM dbo.historial_estado_reservacion AS h
    JOIN dbo.estados_reservacion AS e ON e.id = h.id_estado_reservacion
    LEFT JOIN dbo.usuarios AS u ON u.id = h.id_usuario_cambio
    LEFT JOIN dbo.roles_usuarios AS ro ON ro.id = u.id_rol
    WHERE h.id_reservacion = @id
    ORDER BY h.fecha_cambio, h.id;

    -- 5) Reembolsos
    SELECT rb.id AS id_reembolso, rb.fecha_solicitud, rb.monto, rb.estado, rb.motivo, rb.fecha_resolucion, rb.referencia_externa
    FROM dbo.reembolsos AS rb
    JOIN dbo.pagos AS p ON p.id = rb.id_pago
    WHERE p.id_reservacion = @id
    ORDER BY rb.fecha_solicitud;
END
GO

/* Reservaciones de un cliente (por id o por correo), de la más reciente a la más antigua. */
CREATE OR ALTER PROCEDURE dbo.pa_reservaciones_cliente
    @id_cliente BIGINT       = NULL,
    @correo     VARCHAR(150) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @id_cliente IS NULL
        SET @id_cliente = (SELECT id FROM dbo.usuarios WHERE correo = LOWER(LTRIM(RTRIM(@correo))));
    IF @id_cliente IS NULL THROW 51220, N'Indica el id o el correo de un cliente existente.', 1;

    SELECT r.codigo_reserva, e.nombre AS estado, linea.servicio, linea.tipo_servicio, linea.inicio, linea.fin,
           r.total, r.moneda, r.creada_en, reemb.estado AS estado_reembolso, reemb.monto AS monto_reembolso
    FROM dbo.reservaciones AS r
    JOIN dbo.estados_reservacion AS e ON e.id = r.id_estado_reservacion
    OUTER APPLY (SELECT TOP (1) s.nombre AS servicio, s.tipo_servicio, d.fecha_inicio AS inicio, d.fecha_fin AS fin
                 FROM dbo.detalle_reservacion AS d JOIN dbo.servicios AS s ON s.id = d.id_servicio
                 WHERE d.id_reservacion = r.id ORDER BY d.id) AS linea
    OUTER APPLY (SELECT TOP (1) rb.estado, rb.monto FROM dbo.reembolsos AS rb
                 JOIN dbo.pagos AS p ON p.id = rb.id_pago
                 WHERE p.id_reservacion = r.id ORDER BY rb.fecha_solicitud DESC) AS reemb
    WHERE r.id_usuarioglobal = @id_cliente
    ORDER BY r.creada_en DESC;
END
GO

/* Ventas de un periodo (por la fecha del primer pago aprobado de cada reservación):
   1) por tipo de servicio  2) por día  3) los 10 servicios que más venden.
   "Reembolsado" = reembolsos PROCESADOS en el periodo. */
CREATE OR ALTER PROCEDURE dbo.pa_reporte_ventas
    @desde DATE,
    @hasta DATE
AS
BEGIN
    SET NOCOUNT ON;
    IF @desde IS NULL OR @hasta IS NULL OR @hasta < @desde THROW 51230, N'Indica un periodo válido.', 1;
    DECLARE @fin DATETIME = DATEADD(DAY, 1, CAST(@hasta AS DATETIME));

    CREATE TABLE #ventas (id_reservacion BIGINT PRIMARY KEY, fecha DATE, tipo_servicio NVARCHAR(20), id_servicio BIGINT,
                          servicio NVARCHAR(150), subtotal DECIMAL(12,2), descuento DECIMAL(12,2), total DECIMAL(12,2));
    INSERT INTO #ventas
    SELECT r.id, CAST(pp.fecha AS DATE), linea.tipo_servicio, linea.id_servicio, linea.nombre, r.subtotal, r.descuento_total, r.total
    FROM dbo.reservaciones AS r
    CROSS APPLY (SELECT MIN(p.fecha_pago) AS fecha FROM dbo.pagos AS p
                 JOIN dbo.estados_pago AS ep ON ep.id = p.id_estado_pago
                 WHERE p.id_reservacion = r.id AND ep.nombre IN (N'APROBADO', N'REEMBOLSADO')) AS pp
    OUTER APPLY (SELECT TOP (1) s.tipo_servicio, s.id AS id_servicio, s.nombre FROM dbo.detalle_reservacion AS d
                 JOIN dbo.servicios AS s ON s.id = d.id_servicio
                 WHERE d.id_reservacion = r.id ORDER BY d.id) AS linea
    WHERE pp.fecha >= @desde AND pp.fecha < @fin;

    CREATE TABLE #reembolsos (tipo_servicio NVARCHAR(20), monto DECIMAL(12,2));
    INSERT INTO #reembolsos
    SELECT linea.tipo_servicio, rb.monto
    FROM dbo.reembolsos AS rb
    JOIN dbo.pagos AS p ON p.id = rb.id_pago
    OUTER APPLY (SELECT TOP (1) s.tipo_servicio FROM dbo.detalle_reservacion AS d
                 JOIN dbo.servicios AS s ON s.id = d.id_servicio
                 WHERE d.id_reservacion = p.id_reservacion ORDER BY d.id) AS linea
    WHERE rb.estado = N'PROCESADO' AND rb.fecha_resolucion >= @desde AND rb.fecha_resolucion < @fin;

    -- 1) Por tipo de servicio
    SELECT t.tipo_servicio,
           COALESCE(v.reservaciones, 0) AS reservaciones,
           COALESCE(v.subtotal, 0) AS subtotal, COALESCE(v.descuentos, 0) AS descuentos, COALESCE(v.vendido, 0) AS vendido,
           COALESCE(rb.reembolsado, 0) AS reembolsado,
           COALESCE(v.vendido, 0) - COALESCE(rb.reembolsado, 0) AS neto
    FROM (SELECT tipo_servicio FROM #ventas UNION SELECT tipo_servicio FROM #reembolsos) AS t
    LEFT JOIN (SELECT tipo_servicio, COUNT(*) AS reservaciones, SUM(subtotal) AS subtotal, SUM(descuento) AS descuentos,
                      SUM(total) AS vendido
               FROM #ventas GROUP BY tipo_servicio) AS v ON v.tipo_servicio = t.tipo_servicio
    LEFT JOIN (SELECT tipo_servicio, SUM(monto) AS reembolsado FROM #reembolsos GROUP BY tipo_servicio) AS rb
           ON rb.tipo_servicio = t.tipo_servicio
    ORDER BY vendido DESC;

    -- 2) Por día
    SELECT fecha, COUNT(*) AS reservaciones, SUM(total) AS vendido
    FROM #ventas GROUP BY fecha ORDER BY fecha;

    -- 3) Los 10 servicios que más venden
    SELECT TOP (10) id_servicio, servicio, tipo_servicio, COUNT(*) AS reservaciones, SUM(total) AS vendido
    FROM #ventas GROUP BY id_servicio, servicio, tipo_servicio ORDER BY vendido DESC;
END
GO

/* Ocupación por servicio en un periodo: cupo total, vendido, % de ocupación e ingreso estimado. */
CREATE OR ALTER PROCEDURE dbo.pa_reporte_ocupacion
    @desde         DATE,
    @hasta         DATE,
    @tipo_servicio NVARCHAR(20) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @desde IS NULL OR @hasta IS NULL OR @hasta < @desde THROW 51240, N'Indica un periodo válido.', 1;

    SELECT s.id AS id_servicio, s.nombre, s.tipo_servicio, p.nombre AS proveedor, s.activo,
           COUNT(*) AS dias,
           SUM(d.cantidad_total) AS cupo_total,
           SUM(d.cantidad_total - d.cantidad_disponible) AS vendidos,
           CAST(100.0 * SUM(d.cantidad_total - d.cantidad_disponible) / NULLIF(SUM(d.cantidad_total), 0) AS DECIMAL(5,1)) AS ocupacion_pct,
           SUM((d.cantidad_total - d.cantidad_disponible) * d.precio_fecha) AS ingreso_estimado,
           SUM(CASE WHEN d.cantidad_total > 0 AND d.cantidad_disponible = 0 THEN 1 ELSE 0 END) AS dias_agotados
    FROM dbo.disponibilidad_servicio AS d
    JOIN dbo.servicios AS s ON s.id = d.id_servicio
    JOIN dbo.proveedores AS p ON p.id = s.id_proveedor
    WHERE d.fecha BETWEEN @desde AND @hasta
      AND (@tipo_servicio IS NULL OR s.tipo_servicio = @tipo_servicio)
    GROUP BY s.id, s.nombre, s.tipo_servicio, p.nombre, s.activo
    ORDER BY ocupacion_pct DESC, s.nombre;
END
GO

/* Reembolsos que esperan respuesta del personal (estado SOLICITADO), del más antiguo al más nuevo. */
CREATE OR ALTER PROCEDURE dbo.pa_reembolsos_pendientes
AS
BEGIN
    SET NOCOUNT ON;
    SELECT rb.id AS id_reembolso, rb.fecha_solicitud, DATEDIFF(DAY, rb.fecha_solicitud, GETDATE()) AS dias_esperando,
           r.codigo_reserva, CONCAT(u.nombre, N' ', u.apellido_paterno) AS cliente, u.correo,
           rb.monto AS monto_reembolso, p.monto AS monto_pagado, p.moneda, rb.motivo
    FROM dbo.reembolsos AS rb
    JOIN dbo.pagos AS p ON p.id = rb.id_pago
    JOIN dbo.reservaciones AS r ON r.id = p.id_reservacion
    JOIN dbo.usuarios AS u ON u.id = r.id_usuarioglobal
    WHERE rb.estado = N'SOLICITADO'
    ORDER BY rb.fecha_solicitud;
END
GO

/* Tickets de soporte (creados en el periodo; NULL = todos): 1) por agente  2) por tipo y estado. */
CREATE OR ALTER PROCEDURE dbo.pa_reporte_tickets
    @desde DATE = NULL,
    @hasta DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @ini DATETIME = COALESCE(CAST(@desde AS DATETIME), '19000101');
    DECLARE @fin DATETIME = COALESCE(DATEADD(DAY, 1, CAST(@hasta AS DATETIME)), '99991231');

    -- 1) Por agente (los ABIERTOS sin agente salen como "(sin asignar)")
    SELECT CASE WHEN u.id IS NULL THEN N'(sin asignar)' ELSE CONCAT(u.nombre, N' ', u.apellido_paterno) END AS agente,
           SUM(CASE WHEN t.estado = N'ABIERTA' THEN 1 ELSE 0 END) AS abiertos,
           SUM(CASE WHEN t.estado = N'EN_PROCESO' THEN 1 ELSE 0 END) AS en_proceso,
           SUM(CASE WHEN t.estado = N'CERRADA' THEN 1 ELSE 0 END) AS cerrados,
           CAST(AVG(CASE WHEN t.estado = N'CERRADA' THEN DATEDIFF(MINUTE, t.creada_en, t.actualizada_en) / 60.0 END) AS DECIMAL(8,1))
               AS horas_promedio_cierre
    FROM dbo.solicitudes_soporte AS t
    LEFT JOIN dbo.usuarios AS u ON u.id = t.id_agente_asignado
    WHERE t.creada_en >= @ini AND t.creada_en < @fin
    GROUP BY u.id, u.nombre, u.apellido_paterno
    ORDER BY SUM(CASE WHEN t.estado <> N'CERRADA' THEN 1 ELSE 0 END) DESC;

    -- 2) Por tipo y estado
    SELECT tipo, estado, COUNT(*) AS tickets
    FROM dbo.solicitudes_soporte
    WHERE creada_en >= @ini AND creada_en < @fin
    GROUP BY tipo, estado
    ORDER BY tipo, estado;
END
GO

/* Bitácora del panel con filtros (los más recientes primero). */
CREATE OR ALTER PROCEDURE dbo.pa_consultar_bitacora
    @desde      DATE         = NULL,
    @hasta      DATE         = NULL,
    @id_autor   BIGINT       = NULL,   -- quién hizo el cambio
    @entidad    NVARCHAR(40) = NULL,   -- proveedores, servicios, reembolsos, solicitudes_soporte…
    @accion     NVARCHAR(30) = NULL,   -- CREAR, ACTUALIZAR, BAJA, REACTIVAR, DISPONIBILIDAD, CANCELAR, APROBAR…
    @maximo     INT          = 200
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (CASE WHEN @maximo BETWEEN 1 AND 5000 THEN @maximo ELSE 200 END)
           b.fecha, CASE WHEN u.id IS NULL THEN N'Sistema' ELSE CONCAT(u.nombre, N' ', u.apellido_paterno) END AS usuario, r.nombre AS rol,
           b.accion, b.entidad, b.id_entidad, b.detalle
    FROM dbo.bitacora AS b
    LEFT JOIN dbo.usuarios AS u ON u.id = b.id_usuario
    LEFT JOIN dbo.roles_usuarios AS r ON r.id = u.id_rol
    WHERE (@desde IS NULL OR b.fecha >= @desde)
      AND (@hasta IS NULL OR b.fecha < DATEADD(DAY, 1, CAST(@hasta AS DATETIME)))
      AND (@id_autor IS NULL OR b.id_usuario = @id_autor)
      AND (@entidad IS NULL OR b.entidad = @entidad)
      AND (@accion IS NULL OR b.accion = @accion)
    ORDER BY b.fecha DESC, b.id DESC;
END
GO

/* ---------- Verificación: procedimientos creados ---------- */
SELECT name AS procedimiento, create_date, modify_date
FROM sys.procedures
WHERE name LIKE N'pa[_]%'
ORDER BY name;
GO

/* =====================================================================================
   EJEMPLOS DE USO (con los datos de 03_seed_demo.sql). Quita el comentario y ejecuta en partes.
   ===================================================================================== */
/*
DECLARE @admin BIGINT   = (SELECT id FROM dbo.usuarios WHERE correo = 'admin.prueba@flyvibemex.test');
DECLARE @agente BIGINT  = (SELECT id FROM dbo.usuarios WHERE correo = 'agente.prueba@flyvibemex.test');
DECLARE @soporte BIGINT = (SELECT id FROM dbo.usuarios WHERE correo = 'soporte.prueba@flyvibemex.test');
DECLARE @cliente BIGINT = (SELECT id FROM dbo.usuarios WHERE correo = 'cliente.prueba@flyvibemex.test');
DECLARE @cancun BIGINT  = (SELECT id FROM dbo.ubicaciones WHERE municipio_ciudad = N'Cancún');
DECLARE @cdmx BIGINT    = (SELECT id FROM dbo.ubicaciones WHERE municipio_ciudad = N'Ciudad de México');
DECLARE @aerolinea BIGINT = (SELECT id FROM dbo.proveedores WHERE tipo_proveedor = N'AEROLINEA');
DECLARE @hotel BIGINT   = (SELECT id FROM dbo.servicios WHERE nombre LIKE N'Hotel Playa Azul%');
DECLARE @manana DATE    = DATEADD(DAY, 1, CAST(GETDATE() AS DATE));
DECLARE @salida DATE    = DATEADD(DAY, 2, @manana);   -- 2 noches
DECLARE @en7 DATE       = DATEADD(DAY, 6, @manana);
DECLARE @en30 DATE      = DATEADD(DAY, 29, @manana);   -- (los parámetros de EXEC no aceptan expresiones)

-- Buscar hospedaje en Cancún 2 noches para 1 habitación
EXEC dbo.pa_buscar_disponibilidad @tipo_servicio = N'HOSPEDAJE', @desde = @manana, @hasta = @salida, @id_destino = @cancun;

-- Comprar 2 noches con una tarjeta nueva TOKENIZADA (4242 = aprobada; con @ultimos4 = '0002' se rechaza)
DECLARE @codigo NVARCHAR(30);
EXEC dbo.pa_crear_reservacion_pago @id_usuario = @cliente, @id_servicio = @hotel, @id_metodo_pago = 1,
     @desde = @manana, @hasta = @salida, @personas = 2, @habitaciones = 1,
     @token_tarjeta = N'tok_sim_ejemplo_0001', @marca = N'VISA', @ultimos4 = N'4242',
     @mes_expiracion = 12, @anio_expiracion = 2030, @codigo_reserva = @codigo OUTPUT;
EXEC dbo.pa_detalle_reservacion @codigo_reserva = @codigo;

-- Cancelar con reembolso (como el cliente) y aprobar el reembolso (como agente)
DECLARE @id_res BIGINT = (SELECT id FROM dbo.reservaciones WHERE codigo_reserva = @codigo);
EXEC dbo.pa_cancelar_reservacion @id_usuario = @cliente, @id_reservacion = @id_res, @reembolsar = 1,
     @motivo = N'Cambié mis planes de viaje.';
EXEC dbo.pa_reembolsos_pendientes;
DECLARE @id_reemb BIGINT = (SELECT MAX(id) FROM dbo.reembolsos WHERE estado = N'SOLICITADO');
EXEC dbo.pa_resolver_reembolso @id_usuario = @agente, @id_reembolso = @id_reemb, @aprobar = 1;

-- Catálogo (como administrador)
EXEC dbo.pa_ajustar_disponibilidad @id_usuario = @admin, @id_servicio = @hotel, @desde = @manana,
     @hasta = @en7, @cupo = 10, @precio = 2600;
EXEC dbo.pa_alta_vuelo @id_usuario = @admin,
     @id_proveedor = @aerolinea, @nombre = N'Vuelo CDMX → Cancún (lunes y viernes)', @precio_base = 2200,
     @id_origen = @cdmx, @id_destino = @cancun, @codigo_vuelo = N'QZ777',
     @fecha_salida = '2026-12-01T07:30', @fecha_llegada = '2026-12-01T10:05', @asientos = 150,
     @repetir = N'SEMANAL', @repetir_hasta = '2026-12-31', @dias_semana = '1,5';

-- Soporte
DECLARE @ticket BIGINT = (SELECT MIN(id) FROM dbo.solicitudes_soporte WHERE estado = N'ABIERTA');
EXEC dbo.pa_tomar_ticket @id_usuario = @soporte, @id_ticket = @ticket;
EXEC dbo.pa_responder_ticket @id_usuario = @soporte, @id_ticket = @ticket, @mensaje = N'Claro, con gusto te ayudo.';
EXEC dbo.pa_reasignar_ticket @id_usuario = @admin, @id_ticket = @ticket, @id_agente_nuevo = @admin;
EXEC dbo.pa_cerrar_ticket @id_usuario = @admin, @id_ticket = @ticket;

-- Cancelar a nombre del cliente con reembolso de cortesía (100 %): se procesa en el mismo paso
DECLARE @id_res2 BIGINT = (SELECT id FROM dbo.reservaciones WHERE codigo_reserva LIKE N'FV-%' AND id_estado_reservacion =
                           (SELECT id FROM dbo.estados_reservacion WHERE nombre = N'CONFIRMADA') AND id_usuarioglobal = @cliente);
EXEC dbo.pa_cancelar_reservacion @id_usuario = @agente, @id_reservacion = @id_res2, @reembolsar = 1,
     @motivo = N'El hotel cerró por mantenimiento.', @cortesia = 1;

-- Usuarios (como SA): pasar a soporte.prueba a AGENTE y regresarlo
DECLARE @sa BIGINT = (SELECT id FROM dbo.usuarios WHERE correo = 'sa.prueba@flyvibemex.test');
EXEC dbo.pa_cambiar_rol_usuario @id_usuario = @sa, @id_cuenta = @soporte, @rol = 'AGENTE';
EXEC dbo.pa_cambiar_rol_usuario @id_usuario = @sa, @id_cuenta = @soporte, @rol = 'SOPORTE';
EXEC dbo.pa_cambiar_estado_usuario @id_usuario = @sa, @id_cuenta = @cliente, @activo = 0;
EXEC dbo.pa_cambiar_estado_usuario @id_usuario = @sa, @id_cuenta = @cliente, @activo = 1;

-- Reportes
EXEC dbo.pa_reporte_ventas @desde = '2026-10-01', @hasta = '2026-12-31';
EXEC dbo.pa_reporte_ocupacion @desde = @manana, @hasta = @en30;
EXEC dbo.pa_reporte_tickets;
EXEC dbo.pa_consultar_bitacora @maximo = 50;
*/

SET NOEXEC OFF;
GO
