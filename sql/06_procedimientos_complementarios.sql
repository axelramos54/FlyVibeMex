/* =====================================================================================
   FlyVibe Mex · 05_procedimientos_complementarios.sql                  (script 5, aditivo)
   -------------------------------------------------------------------------------------
   Los procedimientos que FALTABAN: las altas, las ediciones, las bajas lógicas y las consultas
   que la aplicación sí hace y que no estaban en sql/02_procedimientos_almacenados.sql.
   Con este script, toda operación de la aplicación tiene su procedimiento equivalente.

   Requisitos: sql/01_crear_base_datos.sql (tablas), sql/04_log_errores.sql (dbo.LogError) y
   sql/02_procedimientos_almacenados.sql (usa sus ayudantes pa_interno_*).

   Dónde se ejecuta: en cualquiera de las dos bases. NO lleva USE (somee no lo acepta), así que
   en SSMS hay que SELECCIONAR ANTES la base de FlyVibeMex en el desplegable. El script imprime
   al inicio el nombre de la base destino para que se pueda comprobar.

   Se puede ejecutar las veces que haga falta: todo es CREATE OR ALTER, no crea ni borra tablas.
   Requiere SQL Server 2016 SP1 o posterior (usa STRING_SPLIT).

   Convenciones: las mismas del script 02.
       - Prefijo pa_. Todo procedimiento que modifica datos recibe @id_usuario (quién lo hace),
         valida su ROL, trabaja en UNA transacción y deja registro en la tabla bitacora.
       - Los errores de negocio se lanzan con THROW; aquí se usa el rango 513xx, libre, para no
         chocar con los códigos del script 02.
       - El cuerpo va dentro de BEGIN TRY y el BEGIN CATCH deshace la transacción, guarda el error
         en dbo.LogError y lo vuelve a lanzar con THROW, igual que en el script 02.
       - Las listas de ids viajan como texto separado por comas, igual que @dias_semana en
         pa_alta_vuelo: '12,15,18'. En los servicios de un paquete, cada elemento puede llevar su
         cantidad después de dos puntos: '12:2,15:1' (sin cantidad, se asume 1).

   Índice:
       Altas       pa_alta_ubicacion, pa_alta_banco, pa_alta_usuario, pa_alta_paquete,
                   pa_alta_promocion, pa_crear_ticket, pa_alta_tarjeta, pa_guardar_contenido_sitio
       Ediciones   pa_editar_proveedor, pa_editar_servicio, pa_editar_paquete, pa_editar_promocion,
                   pa_editar_ubicacion, pa_editar_metodo_pago, pa_editar_banco, pa_editar_usuario,
                   pa_cambiar_password_usuario, pa_editar_descripcion_rol, pa_editar_alias_tarjeta,
                   pa_calificar_ticket
       Bajas       pa_cambiar_estado_proveedor, pa_cambiar_estado_servicio, pa_cambiar_estado_promocion,
       lógicas     pa_cambiar_estado_ubicacion, pa_cambiar_estado_metodo_pago, pa_cambiar_estado_banco,
                   pa_baja_tarjeta, pa_restaurar_contenido_sitio
       Consultas   pa_catalogo_servicios, pa_detalle_servicio, pa_listar_proveedores,
                   pa_listar_servicios, pa_listar_paquetes, pa_listar_promociones,
                   pa_listar_usuarios, pa_listar_tickets, pa_listar_reservaciones
   Al final hay ejemplos de uso (comentados).
   ===================================================================================== */

SET NOCOUNT ON;
GO

/* Candado: nunca una base del sistema. */
IF DB_NAME() IN (N'master', N'tempdb', N'model', N'msdb')
BEGIN
    DECLARE @aviso NVARCHAR(300) = CONCAT(N'Estás conectado a la base del sistema "', DB_NAME(),
                                          N'". Selecciona la base de FlyVibeMex y repite.');
    RAISERROR(@aviso, 16, 1);
    SET NOEXEC ON;
END
GO

IF OBJECT_ID(N'dbo.servicios', N'U') IS NULL OR OBJECT_ID(N'dbo.solicitudes_soporte', N'U') IS NULL
BEGIN
    RAISERROR(N'Esta base no tiene las tablas de FlyVibeMex. Ejecuta primero sql/01_crear_base_datos.sql.', 16, 1);
    SET NOEXEC ON;
END
GO

IF OBJECT_ID(N'dbo.LogError', N'U') IS NULL
BEGIN
    RAISERROR(N'Falta la tabla dbo.LogError. Ejecuta sql/04_log_errores.sql y repite este script.', 16, 1);
    SET NOEXEC ON;
END
GO

/* Este script usa los ayudantes del 02 (validar rol, registrar bitácora, inventario, disponibilidad). */
IF OBJECT_ID(N'dbo.pa_interno_validar_rol', N'P') IS NULL
    OR OBJECT_ID(N'dbo.pa_interno_registrar_bitacora', N'P') IS NULL
    OR OBJECT_ID(N'dbo.pa_interno_insertar_servicio', N'P') IS NULL
BEGIN
    RAISERROR(N'Faltan los ayudantes pa_interno_*. Ejecuta primero sql/02_procedimientos_almacenados.sql.', 16, 1);
    SET NOEXEC ON;
END
GO

PRINT N'Base de datos destino: ' + DB_NAME();
GO

/* =====================================================================================
   1 · ALTAS
   ===================================================================================== */

/* Alta de una ciudad del catálogo de ubicaciones (solo SA). No puede repetirse la misma ciudad
   en el mismo estado y país. */
CREATE OR ALTER PROCEDURE dbo.pa_alta_ubicacion
    @id_usuario       BIGINT,
    @estado           NVARCHAR(60),
    @municipio_ciudad NVARCHAR(100),
    @codigo_postal    NVARCHAR(10),
    @pais             NVARCHAR(60) = N'México',
    @id_ubicacion     BIGINT       = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'SA';

        SELECT @pais = LTRIM(RTRIM(@pais)), @estado = LTRIM(RTRIM(@estado)),
               @municipio_ciudad = LTRIM(RTRIM(@municipio_ciudad)), @codigo_postal = LTRIM(RTRIM(@codigo_postal));
        IF NULLIF(@estado, N'') IS NULL OR NULLIF(@municipio_ciudad, N'') IS NULL OR NULLIF(@codigo_postal, N'') IS NULL
            THROW 51300, N'El estado, la ciudad y el código postal son obligatorios.', 1;
        IF EXISTS (SELECT 1 FROM dbo.ubicaciones
                   WHERE pais = @pais AND estado = @estado AND municipio_ciudad = @municipio_ciudad)
            THROW 51301, N'Ya existe esa ciudad en ese estado.', 1;

        BEGIN TRANSACTION;
            INSERT INTO dbo.ubicaciones (pais, estado, municipio_ciudad, codigo_postal, activo, creado_en, actualizado_en)
            VALUES (@pais, @estado, @municipio_ciudad, @codigo_postal, 1, GETDATE(), GETDATE());
            SET @id_ubicacion = SCOPE_IDENTITY();

            DECLARE @detalle NVARCHAR(500) = CONCAT(@municipio_ciudad, N', ', @estado, N', ', @pais, N' (C.P. ', @codigo_postal, N').');
            EXEC dbo.pa_interno_registrar_bitacora @id_usuario, N'CREAR', N'ubicaciones', @id_ubicacion, @detalle;
        COMMIT TRANSACTION;

        SELECT @id_ubicacion AS id_ubicacion;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Alta de un banco emisor de tarjetas (solo SA). El nombre no se repite; la clave y el código
   internacional tampoco, cuando se indican. */
CREATE OR ALTER PROCEDURE dbo.pa_alta_banco
    @id_usuario           BIGINT,
    @nombre               NVARCHAR(120),
    @clave_institucion    NVARCHAR(10)  = NULL,
    @codigo_internacional NVARCHAR(15)  = NULL,
    @telefono             NVARCHAR(20)  = NULL,
    @id_banco             BIGINT        = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'SA';

        SELECT @nombre = LTRIM(RTRIM(@nombre)),
               @clave_institucion = NULLIF(UPPER(LTRIM(RTRIM(@clave_institucion))), N''),
               @codigo_internacional = NULLIF(UPPER(LTRIM(RTRIM(@codigo_internacional))), N''),
               @telefono = NULLIF(LTRIM(RTRIM(@telefono)), N'');
        IF NULLIF(@nombre, N'') IS NULL
            THROW 51305, N'El nombre del banco es obligatorio.', 1;
        IF EXISTS (SELECT 1 FROM dbo.instituciones_financieras WHERE nombre = @nombre)
            THROW 51306, N'Ya existe un banco con ese nombre.', 1;
        IF @clave_institucion IS NOT NULL
           AND EXISTS (SELECT 1 FROM dbo.instituciones_financieras WHERE clave_institucion = @clave_institucion)
            THROW 51307, N'Esa clave ya la tiene otro banco.', 1;
        IF @codigo_internacional IS NOT NULL
           AND EXISTS (SELECT 1 FROM dbo.instituciones_financieras WHERE codigo_internacional = @codigo_internacional)
            THROW 51308, N'Ese código internacional ya lo tiene otro banco.', 1;

        BEGIN TRANSACTION;
            INSERT INTO dbo.instituciones_financieras (nombre, clave_institucion, codigo_internacional, telefono, activo)
            VALUES (@nombre, @clave_institucion, @codigo_internacional, @telefono, 1);
            SET @id_banco = SCOPE_IDENTITY();

            DECLARE @detalle NVARCHAR(500) = CONCAT(N'Banco "', @nombre, N'" (clave ', COALESCE(@clave_institucion, N'—'),
                                                    N', código ', COALESCE(@codigo_internacional, N'—'), N').');
            EXEC dbo.pa_interno_registrar_bitacora @id_usuario, N'CREAR', N'instituciones_financieras', @id_banco, @detalle;
        COMMIT TRANSACTION;

        SELECT @id_banco AS id_banco;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Alta de una cuenta (solo SA).

   IMPORTANTE sobre la contraseña (R10): este procedimiento recibe el HASH ya calculado, nunca una
   contraseña en texto. El hash lo genera ASP.NET Core (PasswordHasher, PBKDF2-HMAC-SHA512 con sal
   propia) y NO se puede reproducir en T-SQL. Para probar desde SSMS se puede copiar el hash de una
   cuenta existente, con lo que la cuenta nueva queda con esa misma contraseña. El CHECK de la tabla
   exige al menos 60 caracteres, así que una contraseña en texto plano no entra por descuido. */
CREATE OR ALTER PROCEDURE dbo.pa_alta_usuario
    @id_usuario       BIGINT,
    @rol              VARCHAR(30),
    @nombre           VARCHAR(80),
    @apellido_paterno VARCHAR(60),
    @apellido_materno VARCHAR(60),
    @correo           VARCHAR(150),
    @telefono         VARCHAR(20),
    @password_hash    VARCHAR(255),
    @id_cuenta        BIGINT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'SA';

        SELECT @correo = LOWER(LTRIM(RTRIM(@correo))), @telefono = LTRIM(RTRIM(@telefono)),
               @nombre = LTRIM(RTRIM(@nombre)), @apellido_paterno = LTRIM(RTRIM(@apellido_paterno)),
               @apellido_materno = LTRIM(RTRIM(@apellido_materno)), @rol = LTRIM(RTRIM(@rol));

        DECLARE @id_rol BIGINT = (SELECT id FROM dbo.roles_usuarios WHERE nombre = @rol AND activo = 1);
        IF @id_rol IS NULL
            THROW 51310, N'Ese rol no existe o está inactivo.', 1;
        IF LEN(@password_hash) < 60
            THROW 51311, N'El hash de la contraseña no es válido: este procedimiento NO recibe contraseñas en texto.', 1;
        IF EXISTS (SELECT 1 FROM dbo.usuarios WHERE correo = @correo)
            THROW 51312, N'Ya existe una cuenta con ese correo.', 1;
        IF EXISTS (SELECT 1 FROM dbo.usuarios WHERE telefono = @telefono)
            THROW 51313, N'Ese teléfono ya está registrado en otra cuenta.', 1;

        BEGIN TRANSACTION;
            INSERT INTO dbo.usuarios (id_rol, nombre, apellido_paterno, apellido_materno, correo, telefono,
                                      password_hash, activo, creado_en, actualizado_en)
            VALUES (@id_rol, @nombre, @apellido_paterno, @apellido_materno, @correo, @telefono,
                    @password_hash, 1, GETDATE(), GETDATE());
            SET @id_cuenta = SCOPE_IDENTITY();

            DECLARE @detalle NVARCHAR(500) = CONCAT(N'Alta de la cuenta ', @correo, N' con rol ', @rol, N'.');
            EXEC dbo.pa_interno_registrar_bitacora @id_usuario, N'CREAR', N'usuarios', @id_cuenta, @detalle;
        COMMIT TRANSACTION;

        SELECT @id_cuenta AS id_cuenta;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Alta de un PAQUETE (R2): una fila de servicios (tipo PAQUETE), su fila de paquete, los servicios
   que incluye y su disponibilidad por día.

   @servicios_incluidos: ids separados por comas, cada uno con su cantidad opcional después de dos
   puntos. Ejemplo: '12:2,15:1,18'. Un paquete NO puede incluir otro paquete ni incluirse a sí mismo.

   El destino es opcional, salvo en dos casos: cuando el paquete no incluye hospedaje ni vuelo (no
   hay de dónde deducirlo) y cuando los servicios incluidos están en ciudades distintas. */
CREATE OR ALTER PROCEDURE dbo.pa_alta_paquete
    @id_usuario                  BIGINT,
    @id_proveedor                BIGINT,
    @nombre                      NVARCHAR(150),
    @precio_base                 DECIMAL(12,2),
    @servicios_incluidos         VARCHAR(MAX),
    @inventario_desde            DATE,
    @inventario_cupo             INT,                    -- personas por día
    @inventario_hasta            DATE           = NULL,  -- NULL = solo @inventario_desde
    @inventario_precio           DECIMAL(12,2)  = NULL,  -- NULL = precio base
    @duracion_dias               SMALLINT       = 1,
    @min_personas                SMALLINT       = 1,
    @max_personas                SMALLINT       = 1,
    @id_ubicacion_destino        BIGINT         = NULL,
    @descripcion                 NVARCHAR(2000) = NULL,
    @moneda                      NVARCHAR(3)    = N'MXN',
    @permite_cancelacion         BIT            = 1,
    @horas_reembolso_total       INT            = 48,
    @porcentaje_reembolso_tardio DECIMAL(5,2)   = 50,
    @politica_cancelacion        NVARCHAR(500)  = NULL,
    @imagen_ruta                 NVARCHAR(260)  = NULL,
    @id_servicio                 BIGINT         = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'ADMINISTRADOR,SA';

        SET @inventario_hasta = COALESCE(@inventario_hasta, @inventario_desde);
        EXEC dbo.pa_interno_validar_inventario @inventario_desde, @inventario_hasta, @inventario_cupo;

        IF NOT EXISTS (SELECT 1 FROM dbo.proveedores WHERE id = @id_proveedor AND activo = 1)
            THROW 51320, N'El proveedor no existe o está dado de baja.', 1;
        IF @duracion_dias IS NULL OR @duracion_dias < 1
            THROW 51321, N'La duración del paquete debe ser de al menos un día.', 1;
        IF @min_personas < 1 OR @max_personas < @min_personas
            THROW 51322, N'El máximo de personas debe ser igual o mayor que el mínimo, y el mínimo al menos 1.', 1;

        -- Los servicios incluidos, ya separados: 'id:cantidad' -> una fila por servicio.
        DECLARE @incluidos TABLE (id_servicio BIGINT PRIMARY KEY, cantidad DECIMAL(8,2) NOT NULL, orden SMALLINT NOT NULL);
        INSERT INTO @incluidos (id_servicio, cantidad, orden)
        SELECT x.id, x.cantidad, ROW_NUMBER() OVER (ORDER BY x.id)
        FROM (
            SELECT DISTINCT
                   TRY_CAST(LEFT(t.v, CHARINDEX(':', t.v + ':') - 1) AS BIGINT) AS id,
                   COALESCE(TRY_CAST(NULLIF(SUBSTRING(t.v, CHARINDEX(':', t.v + ':') + 1, 20), '') AS DECIMAL(8,2)), 1) AS cantidad
            FROM (SELECT LTRIM(RTRIM(value)) AS v FROM STRING_SPLIT(COALESCE(@servicios_incluidos, ''), ',')) AS t
            WHERE t.v <> ''
        ) AS x
        WHERE x.id IS NOT NULL;

        IF NOT EXISTS (SELECT 1 FROM @incluidos)
            THROW 51323, N'Indica al menos un servicio incluido (ejemplo: ''12:2,15'').', 1;
        IF EXISTS (SELECT 1 FROM @incluidos WHERE cantidad <= 0)
            THROW 51324, N'Cada servicio incluido necesita una cantidad mayor que 0.', 1;
        IF EXISTS (SELECT 1 FROM @incluidos AS i
                   WHERE NOT EXISTS (SELECT 1 FROM dbo.servicios AS s
                                     WHERE s.id = i.id_servicio AND s.activo = 1
                                       AND s.tipo_servicio IN (N'VUELO', N'HOSPEDAJE', N'VEHICULO', N'EVENTO')))
            THROW 51325, N'Algún servicio incluido no existe, está dado de baja o es otro paquete.', 1;

        IF @id_ubicacion_destino IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM dbo.ubicaciones WHERE id = @id_ubicacion_destino AND activo = 1)
            THROW 51326, N'El destino del paquete no existe o está inactivo.', 1;

        IF @id_ubicacion_destino IS NULL
        BEGIN
            -- Ciudades de los servicios incluidos: la del hospedaje y la de destino del vuelo.
            DECLARE @ciudades INT = (
                SELECT COUNT(DISTINCT c.id_ubicacion) FROM (
                    SELECT h.id_ubicacion FROM @incluidos AS i JOIN dbo.hospedajes AS h ON h.id_servicio = i.id_servicio
                    UNION
                    SELECT v.id_destino   FROM @incluidos AS i JOIN dbo.vuelos     AS v ON v.id_servicio = i.id_servicio
                ) AS c);
            IF NOT EXISTS (SELECT 1 FROM @incluidos AS i JOIN dbo.servicios AS s ON s.id = i.id_servicio
                           WHERE s.tipo_servicio IN (N'HOSPEDAJE', N'VUELO'))
                THROW 51327, N'El paquete no incluye hospedaje ni vuelo: indica su destino.', 1;
            IF @ciudades > 1
                THROW 51328, N'Los servicios incluidos están en ciudades distintas: indica el destino del paquete.', 1;
        END

        BEGIN TRANSACTION;
            EXEC dbo.pa_interno_insertar_servicio @id_proveedor, N'PAQUETE', @nombre, @descripcion, @precio_base, @moneda,
                 @permite_cancelacion, @horas_reembolso_total, @porcentaje_reembolso_tardio, @politica_cancelacion,
                 @imagen_ruta, @id_servicio OUTPUT;

            INSERT INTO dbo.paquete (id_servicio, duracion_dias, min_personas, max_personas, id_ubicacion_destino)
            VALUES (@id_servicio, @duracion_dias, @min_personas, @max_personas, @id_ubicacion_destino);

            INSERT INTO dbo.paquete_servicio (id_servicio, id_servicio_incluido, cantidad, orden)
            SELECT @id_servicio, i.id_servicio, i.cantidad, i.orden FROM @incluidos AS i;

            DECLARE @precio DECIMAL(12,2) = COALESCE(@inventario_precio, @precio_base);
            EXEC dbo.pa_interno_generar_disponibilidad @id_servicio, @inventario_desde, @inventario_hasta, @inventario_cupo, @precio;

            DECLARE @cuantos INT = (SELECT COUNT(*) FROM @incluidos);
            DECLARE @detalle NVARCHAR(500) = CONCAT(N'Alta del paquete "', @nombre, N'" con ', @cuantos,
                N' servicio(s) incluido(s) y cupo ', @inventario_cupo, N' del ', FORMAT(@inventario_desde, 'dd/MM/yyyy'),
                N' al ', FORMAT(@inventario_hasta, 'dd/MM/yyyy'), N'.');
            EXEC dbo.pa_interno_registrar_bitacora @id_usuario, N'CREAR', N'servicios', @id_servicio, @detalle;
        COMMIT TRANSACTION;

        SELECT @id_servicio AS id_servicio;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Alta de una promoción (R13) y los servicios a los que aplica.
   @servicios: ids separados por comas ('12,15,18'). Todos deben existir y estar activos. */
CREATE OR ALTER PROCEDURE dbo.pa_alta_promocion
    @id_usuario     BIGINT,
    @codigo         NVARCHAR(30),
    @tipo_descuento NVARCHAR(15),          -- PORCENTAJE | MONTO
    @valor          DECIMAL(12,2),
    @fecha_inicio   DATETIME,
    @fecha_fin      DATETIME,
    @servicios      VARCHAR(MAX),
    @nombre         NVARCHAR(120)  = NULL,
    @descripcion    NVARCHAR(500)  = NULL,
    @minimo_compra  DECIMAL(12,2)  = 0,
    @id_promocion   BIGINT         = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'ADMINISTRADOR,SA';

        SET @codigo = UPPER(LTRIM(RTRIM(@codigo)));
        IF NULLIF(@codigo, N'') IS NULL
            THROW 51340, N'El código de la promoción es obligatorio.', 1;
        IF EXISTS (SELECT 1 FROM dbo.promociones WHERE codigo = @codigo)
            THROW 51341, N'Ya existe una promoción con ese código.', 1;
        IF @tipo_descuento NOT IN (N'PORCENTAJE', N'MONTO')
            THROW 51342, N'El tipo de descuento debe ser PORCENTAJE o MONTO.', 1;
        IF @valor IS NULL OR @valor <= 0
            THROW 51343, N'El valor del descuento debe ser mayor que 0.', 1;
        IF @tipo_descuento = N'PORCENTAJE' AND @valor > 100
            THROW 51344, N'Un porcentaje no puede pasar de 100.', 1;
        IF @fecha_fin <= @fecha_inicio
            THROW 51345, N'El fin de la vigencia debe ser posterior al inicio.', 1;

        DECLARE @aplica TABLE (id_servicio BIGINT PRIMARY KEY);
        INSERT INTO @aplica (id_servicio)
        SELECT DISTINCT TRY_CAST(LTRIM(RTRIM(value)) AS BIGINT)
        FROM STRING_SPLIT(COALESCE(@servicios, ''), ',')
        WHERE TRY_CAST(LTRIM(RTRIM(value)) AS BIGINT) IS NOT NULL;

        IF NOT EXISTS (SELECT 1 FROM @aplica)
            THROW 51346, N'Marca al menos un servicio: la promoción solo aplica a los servicios elegidos.', 1;
        IF EXISTS (SELECT 1 FROM @aplica AS a
                   WHERE NOT EXISTS (SELECT 1 FROM dbo.servicios AS s WHERE s.id = a.id_servicio AND s.activo = 1))
            THROW 51347, N'Algún servicio marcado no existe o está dado de baja.', 1;

        BEGIN TRANSACTION;
            INSERT INTO dbo.promociones (codigo, nombre, descripcion, tipo_descuento, valor, fecha_inicio, fecha_fin,
                                         activo, minimo_compra)
            VALUES (@codigo, NULLIF(LTRIM(RTRIM(@nombre)), N''), NULLIF(LTRIM(RTRIM(@descripcion)), N''),
                    @tipo_descuento, @valor, @fecha_inicio, @fecha_fin, 1, COALESCE(@minimo_compra, 0));
            SET @id_promocion = SCOPE_IDENTITY();

            INSERT INTO dbo.promocion_servicio (id_promocion, id_servicio)
            SELECT @id_promocion, a.id_servicio FROM @aplica AS a;

            DECLARE @cuantos INT = (SELECT COUNT(*) FROM @aplica);
            DECLARE @detalle NVARCHAR(500) = CONCAT(N'Alta de la promoción ', @codigo, N' (', @tipo_descuento, N' ',
                @valor, N') para ', @cuantos, N' servicio(s), vigente del ', FORMAT(@fecha_inicio, 'dd/MM/yyyy'),
                N' al ', FORMAT(@fecha_fin, 'dd/MM/yyyy'), N'.');
            EXEC dbo.pa_interno_registrar_bitacora @id_usuario, N'CREAR', N'promociones', @id_promocion, @detalle;
        COMMIT TRANSACTION;

        SELECT @id_promocion AS id_promocion;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Crea un ticket de soporte (R9). Lo abre el CLIENTE dueño de la cuenta; el personal puede abrirlo
   a nombre de un cliente, pero nunca a nombre de otro usuario interno.
   Si se liga a una reservación, esa reservación debe ser del mismo cliente. */
CREATE OR ALTER PROCEDURE dbo.pa_crear_ticket
    @id_usuario     BIGINT,            -- quién lo abre
    @asunto         NVARCHAR(120),
    @mensaje        NVARCHAR(2000),
    @tipo           NVARCHAR(20)  = N'ASESORIA',   -- ASESORIA | PROBLEMA
    @id_cliente     BIGINT        = NULL,          -- NULL = el ticket es para quien lo abre
    @id_reservacion BIGINT        = NULL,
    @id_ticket      BIGINT        = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY

        SET @id_cliente = COALESCE(@id_cliente, @id_usuario);
        IF @id_cliente = @id_usuario
            EXEC dbo.pa_interno_validar_rol @id_usuario, 'CLIENTE,SOPORTE,AGENTE,ADMINISTRADOR,SA';
        ELSE
            EXEC dbo.pa_interno_validar_rol @id_usuario, 'SOPORTE,AGENTE,ADMINISTRADOR,SA';

        SELECT @asunto = LTRIM(RTRIM(@asunto)), @mensaje = LTRIM(RTRIM(@mensaje));
        IF NULLIF(@asunto, N'') IS NULL OR NULLIF(@mensaje, N'') IS NULL
            THROW 51370, N'El asunto y el mensaje son obligatorios.', 1;
        IF @tipo NOT IN (N'ASESORIA', N'PROBLEMA')
            THROW 51371, N'El tipo del ticket debe ser ASESORIA o PROBLEMA.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.usuarios WHERE id = @id_cliente AND activo = 1)
            THROW 51372, N'La cuenta del cliente no existe o está inactiva.', 1;
        IF @id_reservacion IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM dbo.reservaciones WHERE id = @id_reservacion AND id_usuarioglobal = @id_cliente)
            THROW 51373, N'Esa reservación no es de ese cliente.', 1;

        BEGIN TRANSACTION;
            INSERT INTO dbo.solicitudes_soporte (id_usuarioglobal, id_reservacion, id_agente_asignado, tipo, asunto,
                                                 mensaje, estado, creada_en, actualizada_en)
            VALUES (@id_cliente, @id_reservacion, NULL, @tipo, @asunto, @mensaje, N'ABIERTA', GETDATE(), GETDATE());
            SET @id_ticket = SCOPE_IDENTITY();

            DECLARE @detalle NVARCHAR(500) = CONCAT(N'Ticket #', @id_ticket, N' (', @tipo, N') abierto: ', @asunto);
            EXEC dbo.pa_interno_registrar_bitacora @id_usuario, N'CREAR', N'solicitudes_soporte', @id_ticket, @detalle;
        COMMIT TRANSACTION;

        SELECT @id_ticket AS id_ticket, N'ABIERTA' AS estado;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Guarda una tarjeta del cliente (R5).
   SOLO el token del proveedor de pagos, la marca, los últimos 4 dígitos y la expiración.
   PROHIBIDO pasar aquí el número completo (PAN) o el CVV: no hay columna para ellos y no debe haberla. */
CREATE OR ALTER PROCEDURE dbo.pa_alta_tarjeta
    @id_usuario       BIGINT,
    @token_referencia NVARCHAR(100),
    @marca            NVARCHAR(20),
    @ultimos4         NVARCHAR(4),
    @mes_expiracion   TINYINT,
    @anio_expiracion  SMALLINT,
    @id_institucion   BIGINT       = NULL,
    @alias            NVARCHAR(40) = NULL,
    @id_tarjeta       BIGINT       = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'CLIENTE,SOPORTE,AGENTE,ADMINISTRADOR,SA';

        IF @ultimos4 NOT LIKE N'[0-9][0-9][0-9][0-9]'
            THROW 51380, N'Los últimos 4 dígitos deben ser exactamente cuatro números.', 1;
        IF @mes_expiracion NOT BETWEEN 1 AND 12
            THROW 51381, N'El mes de expiración debe estar entre 1 y 12.', 1;
        IF @anio_expiracion < YEAR(GETDATE()) OR (@anio_expiracion = YEAR(GETDATE()) AND @mes_expiracion < MONTH(GETDATE()))
            THROW 51382, N'La tarjeta ya está vencida.', 1;
        IF NULLIF(LTRIM(RTRIM(@token_referencia)), N'') IS NULL
            THROW 51383, N'Falta el token del proveedor de pagos.', 1;
        IF EXISTS (SELECT 1 FROM dbo.tarjetas_usuarios WHERE token_referencia = @token_referencia)
            THROW 51384, N'Esa tarjeta ya está registrada.', 1;
        IF @id_institucion IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM dbo.instituciones_financieras WHERE id = @id_institucion AND activo = 1)
            THROW 51385, N'El banco no existe o está dado de baja.', 1;

        BEGIN TRANSACTION;
            INSERT INTO dbo.tarjetas_usuarios (id_usuariog, id_institucion, token_referencia, marca, ultimos4,
                                               mes_expiracion, anio_expiracion, alias, activo, creado_en)
            VALUES (@id_usuario, @id_institucion, LTRIM(RTRIM(@token_referencia)), LTRIM(RTRIM(@marca)), @ultimos4,
                    @mes_expiracion, @anio_expiracion, NULLIF(LTRIM(RTRIM(@alias)), N''), 1, GETDATE());
            SET @id_tarjeta = SCOPE_IDENTITY();
        COMMIT TRANSACTION;

        -- Nunca se devuelve el token: solo lo que el cliente necesita ver.
        SELECT @id_tarjeta AS id_tarjeta, @marca AS marca, @ultimos4 AS ultimos4;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Guarda un texto o una imagen del contenido editable del sitio (solo SA).
   Una fila por clave: si la clave ya tiene fila se actualiza, si no, se crea. Si se borra la fila
   (pa_restaurar_contenido_sitio), el sitio vuelve a mostrar su texto por defecto. */
CREATE OR ALTER PROCEDURE dbo.pa_guardar_contenido_sitio
    @id_usuario  BIGINT,
    @clave       VARCHAR(60),
    @texto       NVARCHAR(4000) = NULL,
    @imagen_ruta NVARCHAR(260)  = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'SA';

        SET @clave = LTRIM(RTRIM(@clave));
        SELECT @texto = NULLIF(LTRIM(RTRIM(@texto)), N''), @imagen_ruta = NULLIF(LTRIM(RTRIM(@imagen_ruta)), N'');
        IF NULLIF(@clave, '') IS NULL
            THROW 51390, N'Indica la clave del contenido (por ejemplo inicio.bienvenida.titulo).', 1;
        IF @texto IS NULL AND @imagen_ruta IS NULL
            THROW 51391, N'Indica el texto o la ruta de la imagen: una fila de contenido no puede quedar vacía.', 1;

        BEGIN TRANSACTION;
            IF EXISTS (SELECT 1 FROM dbo.contenido_sitio WHERE clave = @clave)
                UPDATE dbo.contenido_sitio
                SET texto = COALESCE(@texto, texto),
                    imagen_ruta = COALESCE(@imagen_ruta, imagen_ruta),
                    actualizado_en = GETDATE(),
                    id_usuario = @id_usuario
                WHERE clave = @clave;
            ELSE
                INSERT INTO dbo.contenido_sitio (clave, texto, imagen_ruta, actualizado_en, id_usuario)
                VALUES (@clave, @texto, @imagen_ruta, GETDATE(), @id_usuario);

            DECLARE @detalle NVARCHAR(500) = CONCAT(@clave, N': ', LEFT(COALESCE(@texto, @imagen_ruta), 300));
            EXEC dbo.pa_interno_registrar_bitacora @id_usuario, N'ACTUALIZAR', N'contenido_sitio', NULL, @detalle;
        COMMIT TRANSACTION;

        SELECT clave, texto, imagen_ruta, actualizado_en FROM dbo.contenido_sitio WHERE clave = @clave;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO
/* =====================================================================================
   2 · EDICIONES
   Cambian datos existentes. Ninguna toca las compras ya hechas: lo que compró el cliente quedó
   copiado en detalle_reservacion al pagar (R4), así que editar el catálogo no altera un viaje vendido.
   ===================================================================================== */

/* Datos de un proveedor. El correo y el teléfono siguen siendo únicos entre proveedores. */
CREATE OR ALTER PROCEDURE dbo.pa_editar_proveedor
    @id_usuario     BIGINT,
    @id_proveedor   BIGINT,
    @tipo_proveedor NVARCHAR(20),
    @nombre         NVARCHAR(120),
    @telefono       NVARCHAR(20),
    @correo         NVARCHAR(150),
    @id_ubicacion   BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'ADMINISTRADOR,SA';

        SELECT @nombre = LTRIM(RTRIM(@nombre)), @telefono = LTRIM(RTRIM(@telefono)), @correo = LOWER(LTRIM(RTRIM(@correo)));
        IF NOT EXISTS (SELECT 1 FROM dbo.proveedores WHERE id = @id_proveedor)
            THROW 51315, N'El proveedor no existe.', 1;
        IF NULLIF(@nombre, N'') IS NULL
            THROW 51316, N'El nombre del proveedor es obligatorio.', 1;
        IF @id_ubicacion IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.ubicaciones WHERE id = @id_ubicacion AND activo = 1)
            THROW 51317, N'La ubicación no existe o está inactiva.', 1;
        IF EXISTS (SELECT 1 FROM dbo.proveedores WHERE correo = @correo AND id <> @id_proveedor)
            THROW 51318, N'Ya hay otro proveedor con ese correo.', 1;
        IF EXISTS (SELECT 1 FROM dbo.proveedores WHERE telefono = @telefono AND id <> @id_proveedor)
            THROW 51319, N'Ya hay otro proveedor con ese teléfono.', 1;

        BEGIN TRANSACTION;
            UPDATE dbo.proveedores
            SET id_ubicacion = @id_ubicacion, tipo_proveedor = @tipo_proveedor, nombre = @nombre,
                telefono = @telefono, correo = @correo, actualizado_en = GETDATE()
            WHERE id = @id_proveedor;

            DECLARE @detalle NVARCHAR(500) = CONCAT(N'Datos del proveedor "', @nombre, N'" actualizados.');
            EXEC dbo.pa_interno_registrar_bitacora @id_usuario, N'ACTUALIZAR', N'proveedores', @id_proveedor, @detalle;
        COMMIT TRANSACTION;

        SELECT id, nombre, tipo_proveedor, telefono, correo, id_ubicacion, activo FROM dbo.proveedores WHERE id = @id_proveedor;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Datos COMUNES de cualquier servicio (R1): nombre, descripción, precio, moneda y política de
   cancelación. Los datos propios de cada tipo se cambian con pa_editar_hospedaje, pa_editar_vehiculo
   o pa_editar_vuelo.

   El TIPO no se puede cambiar: su fila de subtipo, su inventario y las compras hechas dependen de él.
   El proveedor solo se exige activo cuando SE CAMBIA: un servicio viejo puede tener un proveedor dado
   de baja y aun así necesitar que se le corrija el nombre. */
CREATE OR ALTER PROCEDURE dbo.pa_editar_servicio
    @id_usuario                  BIGINT,
    @id_servicio                 BIGINT,
    @nombre                      NVARCHAR(150),
    @precio_base                 DECIMAL(12,2),
    @id_proveedor                BIGINT         = NULL,  -- NULL = se queda con el que tiene
    @descripcion                 NVARCHAR(2000) = NULL,
    @moneda                      NVARCHAR(3)    = NULL,
    @permite_cancelacion         BIT            = NULL,
    @horas_reembolso_total       INT            = NULL,
    @porcentaje_reembolso_tardio DECIMAL(5,2)   = NULL,
    @politica_cancelacion        NVARCHAR(500)  = NULL,
    @imagen_ruta                 NVARCHAR(260)  = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'ADMINISTRADOR,SA';

        DECLARE @proveedor_actual BIGINT = (SELECT id_proveedor FROM dbo.servicios WHERE id = @id_servicio);
        IF @proveedor_actual IS NULL
            THROW 51330, N'El servicio no existe.', 1;

        SET @id_proveedor = COALESCE(@id_proveedor, @proveedor_actual);
        IF @id_proveedor <> @proveedor_actual
           AND NOT EXISTS (SELECT 1 FROM dbo.proveedores WHERE id = @id_proveedor AND activo = 1)
            THROW 51331, N'El proveedor nuevo no existe o está dado de baja.', 1;
        IF NULLIF(LTRIM(RTRIM(@nombre)), N'') IS NULL
            THROW 51332, N'El nombre del servicio es obligatorio.', 1;
        IF @precio_base IS NULL OR @precio_base < 0
            THROW 51333, N'El precio base no puede ser negativo.', 1;

        BEGIN TRANSACTION;
            UPDATE dbo.servicios
            SET id_proveedor = @id_proveedor,
                nombre = LTRIM(RTRIM(@nombre)),
                descripcion = COALESCE(NULLIF(LTRIM(RTRIM(@descripcion)), N''), descripcion),
                precio_base = @precio_base,
                moneda = COALESCE(@moneda, moneda),
                permite_cancelacion = COALESCE(@permite_cancelacion, permite_cancelacion),
                horas_reembolso_total = COALESCE(@horas_reembolso_total, horas_reembolso_total),
                porcentaje_reembolso_tardio = COALESCE(@porcentaje_reembolso_tardio, porcentaje_reembolso_tardio),
                politica_cancelacion = COALESCE(NULLIF(LTRIM(RTRIM(@politica_cancelacion)), N''), politica_cancelacion),
                imagen_ruta = COALESCE(NULLIF(LTRIM(RTRIM(@imagen_ruta)), N''), imagen_ruta),
                actualizado_en = GETDATE()
            WHERE id = @id_servicio;

            DECLARE @detalle NVARCHAR(500) = CONCAT(N'Datos de "', LTRIM(RTRIM(@nombre)), N'" actualizados.');
            EXEC dbo.pa_interno_registrar_bitacora @id_usuario, N'ACTUALIZAR', N'servicios', @id_servicio, @detalle;
        COMMIT TRANSACTION;

        SELECT id, tipo_servicio, nombre, precio_base, moneda, activo, actualizado_en
        FROM dbo.servicios WHERE id = @id_servicio;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Datos propios de un hospedaje. */
CREATE OR ALTER PROCEDURE dbo.pa_editar_hospedaje
    @id_usuario          BIGINT,
    @id_servicio         BIGINT,
    @id_ubicacion        BIGINT,
    @tipo_habitacion     NVARCHAR(60),
    @hora_entrada        TIME(0),
    @hora_salida         TIME(0),
    @capacidad_huespedes INT          = 2,
    @categoria           NVARCHAR(40) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'ADMINISTRADOR,SA';

        IF NOT EXISTS (SELECT 1 FROM dbo.servicios WHERE id = @id_servicio AND tipo_servicio = N'HOSPEDAJE')
            THROW 51334, N'Ese servicio no existe o no es un hospedaje.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.ubicaciones WHERE id = @id_ubicacion AND activo = 1)
            THROW 51335, N'La ubicación del hospedaje no existe o está inactiva.', 1;

        BEGIN TRANSACTION;
            IF EXISTS (SELECT 1 FROM dbo.hospedajes WHERE id_servicio = @id_servicio)
                UPDATE dbo.hospedajes
                SET id_ubicacion = @id_ubicacion, tipo_habitacion = LTRIM(RTRIM(@tipo_habitacion)),
                    categoria = NULLIF(LTRIM(RTRIM(@categoria)), N''), capacidad_huespedes = @capacidad_huespedes,
                    hora_entrada = @hora_entrada, hora_salida = @hora_salida
                WHERE id_servicio = @id_servicio;
            ELSE
                INSERT INTO dbo.hospedajes (id_servicio, id_ubicacion, tipo_habitacion, categoria, capacidad_huespedes,
                                            hora_entrada, hora_salida)
                VALUES (@id_servicio, @id_ubicacion, LTRIM(RTRIM(@tipo_habitacion)), NULLIF(LTRIM(RTRIM(@categoria)), N''),
                        @capacidad_huespedes, @hora_entrada, @hora_salida);

            UPDATE dbo.servicios SET actualizado_en = GETDATE() WHERE id = @id_servicio;

            DECLARE @detalle NVARCHAR(500) = CONCAT(N'Datos del hospedaje #', @id_servicio, N' actualizados (',
                                                    LTRIM(RTRIM(@tipo_habitacion)), N').');
            EXEC dbo.pa_interno_registrar_bitacora @id_usuario, N'ACTUALIZAR', N'hospedajes', @id_servicio, @detalle;
        COMMIT TRANSACTION;

        SELECT * FROM dbo.hospedajes WHERE id_servicio = @id_servicio;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Datos propios de un vehículo. */
CREATE OR ALTER PROCEDURE dbo.pa_editar_vehiculo
    @id_usuario           BIGINT,
    @id_servicio          BIGINT,
    @id_ubicacion_retiro  BIGINT,
    @categoria            NVARCHAR(50),
    @marca                NVARCHAR(50),
    @modelo               NVARCHAR(60),
    @capacidad_pasajeros  TINYINT = 4
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'ADMINISTRADOR,SA';

        IF NOT EXISTS (SELECT 1 FROM dbo.servicios WHERE id = @id_servicio AND tipo_servicio = N'VEHICULO')
            THROW 51336, N'Ese servicio no existe o no es un vehículo.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.ubicaciones WHERE id = @id_ubicacion_retiro AND activo = 1)
            THROW 51337, N'La ubicación de retiro no existe o está inactiva.', 1;

        BEGIN TRANSACTION;
            IF EXISTS (SELECT 1 FROM dbo.vehiculos WHERE id_servicio = @id_servicio)
                UPDATE dbo.vehiculos
                SET id_ubicacion_retiro = @id_ubicacion_retiro, categoria = LTRIM(RTRIM(@categoria)),
                    marca = LTRIM(RTRIM(@marca)), modelo = LTRIM(RTRIM(@modelo)), capacidad_pasajeros = @capacidad_pasajeros
                WHERE id_servicio = @id_servicio;
            ELSE
                INSERT INTO dbo.vehiculos (id_servicio, id_ubicacion_retiro, categoria, marca, modelo, capacidad_pasajeros)
                VALUES (@id_servicio, @id_ubicacion_retiro, LTRIM(RTRIM(@categoria)), LTRIM(RTRIM(@marca)),
                        LTRIM(RTRIM(@modelo)), @capacidad_pasajeros);

            UPDATE dbo.servicios SET actualizado_en = GETDATE() WHERE id = @id_servicio;

            DECLARE @detalle NVARCHAR(500) = CONCAT(N'Datos del vehículo #', @id_servicio, N' actualizados (',
                                                    LTRIM(RTRIM(@marca)), N' ', LTRIM(RTRIM(@modelo)), N').');
            EXEC dbo.pa_interno_registrar_bitacora @id_usuario, N'ACTUALIZAR', N'vehiculos', @id_servicio, @detalle;
        COMMIT TRANSACTION;

        SELECT * FROM dbo.vehiculos WHERE id_servicio = @id_servicio;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Datos propios de un vuelo.

   Regla delicada: se puede cambiar la HORA de la salida, pero no el DÍA cuando ya hay asientos
   vendidos; esos clientes compraron esa fecha. Si no se ha vendido nada, al mover el día se mueve
   también la fila de disponibilidad, para que el inventario siga correspondiendo a la salida. */
CREATE OR ALTER PROCEDURE dbo.pa_editar_vuelo
    @id_usuario    BIGINT,
    @id_servicio   BIGINT,
    @id_origen     BIGINT,
    @id_destino    BIGINT,
    @codigo_vuelo  NVARCHAR(10),
    @fecha_salida  DATETIME,
    @fecha_llegada DATETIME,
    @clase         NVARCHAR(20) = N'TURISTA'   -- TURISTA | PREMIUM | PRIMERA
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'ADMINISTRADOR,SA';

        IF NOT EXISTS (SELECT 1 FROM dbo.servicios WHERE id = @id_servicio AND tipo_servicio = N'VUELO')
            THROW 51350, N'Ese servicio no existe o no es un vuelo.', 1;
        IF @clase NOT IN (N'TURISTA', N'PREMIUM', N'PRIMERA')
            THROW 51338, N'La clase del vuelo debe ser TURISTA, PREMIUM o PRIMERA.', 1;
        IF @id_origen = @id_destino
            THROW 51351, N'El origen y el destino no pueden ser la misma ciudad.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.ubicaciones WHERE id = @id_origen AND activo = 1)
            OR NOT EXISTS (SELECT 1 FROM dbo.ubicaciones WHERE id = @id_destino AND activo = 1)
            THROW 51352, N'El origen o el destino no existe o está inactivo.', 1;
        IF @fecha_llegada <= @fecha_salida
            THROW 51353, N'La llegada debe ser posterior a la salida.', 1;

        DECLARE @dia_anterior DATE = (SELECT CAST(fecha_salida AS DATE) FROM dbo.vuelos WHERE id_servicio = @id_servicio);
        DECLARE @dia_nuevo    DATE = CAST(@fecha_salida AS DATE);
        DECLARE @vendidos     INT  = (SELECT COALESCE(SUM(cantidad_total - cantidad_disponible), 0)
                                      FROM dbo.disponibilidad_servicio WHERE id_servicio = @id_servicio);

        IF @dia_anterior IS NOT NULL AND @dia_anterior <> @dia_nuevo AND @vendidos > 0
        BEGIN
            DECLARE @mensaje NVARCHAR(300) = CONCAT(N'Esta salida ya tiene ', @vendidos,
                N' asiento(s) vendido(s): puedes cambiar la hora, pero no el día.');
            THROW 51354, @mensaje, 1;
        END

        BEGIN TRANSACTION;
            IF EXISTS (SELECT 1 FROM dbo.vuelos WHERE id_servicio = @id_servicio)
                UPDATE dbo.vuelos
                SET id_origen = @id_origen, id_destino = @id_destino, codigo_vuelo = UPPER(LTRIM(RTRIM(@codigo_vuelo))),
                    fecha_salida = @fecha_salida, fecha_llegada = @fecha_llegada, clase = @clase
                WHERE id_servicio = @id_servicio;
            ELSE
                INSERT INTO dbo.vuelos (id_servicio, id_origen, id_destino, codigo_vuelo, fecha_salida, fecha_llegada, clase)
                VALUES (@id_servicio, @id_origen, @id_destino, UPPER(LTRIM(RTRIM(@codigo_vuelo))), @fecha_salida,
                        @fecha_llegada, @clase);

            IF @dia_anterior IS NOT NULL AND @dia_anterior <> @dia_nuevo
                UPDATE dbo.disponibilidad_servicio
                SET fecha = @dia_nuevo, actualizado_en = GETDATE()
                WHERE id_servicio = @id_servicio;

            UPDATE dbo.servicios SET actualizado_en = GETDATE() WHERE id = @id_servicio;

            DECLARE @detalle NVARCHAR(500) = CONCAT(N'Datos del vuelo ', UPPER(LTRIM(RTRIM(@codigo_vuelo))),
                N' actualizados (salida ', FORMAT(@fecha_salida, 'dd/MM/yyyy HH:mm'), N').');
            EXEC dbo.pa_interno_registrar_bitacora @id_usuario, N'ACTUALIZAR', N'vuelos', @id_servicio, @detalle;
        COMMIT TRANSACTION;

        SELECT * FROM dbo.vuelos WHERE id_servicio = @id_servicio;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Datos propios de un paquete y los servicios que incluye.

   Los incluidos se REEMPLAZAN completos (se borran y se vuelven a insertar). Es seguro porque las
   compras pasadas no dependen de paquete_servicio: lo que compró el cliente quedó copiado en
   detalle_reservacion al pagar (R4). Si @servicios_incluidos llega NULL, no se tocan. */
CREATE OR ALTER PROCEDURE dbo.pa_editar_paquete
    @id_usuario           BIGINT,
    @id_servicio          BIGINT,
    @duracion_dias        SMALLINT     = NULL,
    @min_personas         SMALLINT     = NULL,
    @max_personas         SMALLINT     = NULL,
    @id_ubicacion_destino BIGINT       = NULL,
    @servicios_incluidos  VARCHAR(MAX) = NULL     -- NULL = no se tocan; '12:2,15' = reemplaza la lista
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'ADMINISTRADOR,SA';

        IF NOT EXISTS (SELECT 1 FROM dbo.servicios WHERE id = @id_servicio AND tipo_servicio = N'PAQUETE')
            THROW 51360, N'Ese servicio no existe o no es un paquete.', 1;

        DECLARE @duracion SMALLINT = COALESCE(@duracion_dias, (SELECT duracion_dias FROM dbo.paquete WHERE id_servicio = @id_servicio), 1);
        DECLARE @minimo   SMALLINT = COALESCE(@min_personas, (SELECT min_personas  FROM dbo.paquete WHERE id_servicio = @id_servicio), 1);
        DECLARE @maximo   SMALLINT = COALESCE(@max_personas, (SELECT max_personas  FROM dbo.paquete WHERE id_servicio = @id_servicio), 1);
        IF @duracion < 1
            THROW 51361, N'La duración del paquete debe ser de al menos un día.', 1;
        IF @minimo < 1 OR @maximo < @minimo
            THROW 51362, N'El máximo de personas debe ser igual o mayor que el mínimo, y el mínimo al menos 1.', 1;
        IF @id_ubicacion_destino IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM dbo.ubicaciones WHERE id = @id_ubicacion_destino AND activo = 1)
            THROW 51363, N'El destino del paquete no existe o está inactivo.', 1;

        DECLARE @incluidos TABLE (id_servicio BIGINT PRIMARY KEY, cantidad DECIMAL(8,2) NOT NULL, orden SMALLINT NOT NULL);
        IF @servicios_incluidos IS NOT NULL
        BEGIN
            INSERT INTO @incluidos (id_servicio, cantidad, orden)
            SELECT x.id, x.cantidad, ROW_NUMBER() OVER (ORDER BY x.id)
            FROM (
                SELECT DISTINCT
                       TRY_CAST(LEFT(t.v, CHARINDEX(':', t.v + ':') - 1) AS BIGINT) AS id,
                       COALESCE(TRY_CAST(NULLIF(SUBSTRING(t.v, CHARINDEX(':', t.v + ':') + 1, 20), '') AS DECIMAL(8,2)), 1) AS cantidad
                FROM (SELECT LTRIM(RTRIM(value)) AS v FROM STRING_SPLIT(@servicios_incluidos, ',')) AS t
                WHERE t.v <> ''
            ) AS x
            WHERE x.id IS NOT NULL;

            IF NOT EXISTS (SELECT 1 FROM @incluidos)
                THROW 51364, N'La lista de servicios incluidos quedó vacía: un paquete necesita al menos uno.', 1;
            IF EXISTS (SELECT 1 FROM @incluidos WHERE id_servicio = @id_servicio)
                THROW 51365, N'Un paquete no puede incluirse a sí mismo.', 1;
            IF EXISTS (SELECT 1 FROM @incluidos WHERE cantidad <= 0)
                THROW 51366, N'Cada servicio incluido necesita una cantidad mayor que 0.', 1;
            IF EXISTS (SELECT 1 FROM @incluidos AS i
                       WHERE NOT EXISTS (SELECT 1 FROM dbo.servicios AS s
                                         WHERE s.id = i.id_servicio AND s.activo = 1
                                           AND s.tipo_servicio IN (N'VUELO', N'HOSPEDAJE', N'VEHICULO', N'EVENTO')))
                THROW 51367, N'Algún servicio incluido no existe, está dado de baja o es otro paquete.', 1;
        END

        BEGIN TRANSACTION;
            IF EXISTS (SELECT 1 FROM dbo.paquete WHERE id_servicio = @id_servicio)
                UPDATE dbo.paquete
                SET duracion_dias = @duracion, min_personas = @minimo, max_personas = @maximo,
                    id_ubicacion_destino = COALESCE(@id_ubicacion_destino, id_ubicacion_destino)
                WHERE id_servicio = @id_servicio;
            ELSE
                INSERT INTO dbo.paquete (id_servicio, duracion_dias, min_personas, max_personas, id_ubicacion_destino)
                VALUES (@id_servicio, @duracion, @minimo, @maximo, @id_ubicacion_destino);

            IF @servicios_incluidos IS NOT NULL
            BEGIN
                DELETE FROM dbo.paquete_servicio WHERE id_servicio = @id_servicio;
                INSERT INTO dbo.paquete_servicio (id_servicio, id_servicio_incluido, cantidad, orden)
                SELECT @id_servicio, i.id_servicio, i.cantidad, i.orden FROM @incluidos AS i;
            END

            UPDATE dbo.servicios SET actualizado_en = GETDATE() WHERE id = @id_servicio;

            DECLARE @cuantos INT = (SELECT COUNT(*) FROM dbo.paquete_servicio WHERE id_servicio = @id_servicio);
            DECLARE @detalle NVARCHAR(500) = CONCAT(N'Paquete #', @id_servicio, N' actualizado: ', @cuantos,
                                                    N' servicio(s) incluido(s), ', @duracion, N' día(s).');
            EXEC dbo.pa_interno_registrar_bitacora @id_usuario, N'ACTUALIZAR', N'servicios', @id_servicio, @detalle;
        COMMIT TRANSACTION;

        SELECT p.*, (SELECT COUNT(*) FROM dbo.paquete_servicio WHERE id_servicio = @id_servicio) AS servicios_incluidos
        FROM dbo.paquete AS p WHERE p.id_servicio = @id_servicio;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Datos de una promoción y los servicios a los que aplica.

   El CÓDIGO no se cambia: las compras pasadas lo muestran, y si cambiara, el historial del cliente
   mencionaría una promoción que ya no existe con ese nombre. Los servicios se reemplazan completos
   (igual que en el paquete, y por la misma razón). Si @servicios llega NULL, no se tocan. */
CREATE OR ALTER PROCEDURE dbo.pa_editar_promocion
    @id_usuario     BIGINT,
    @id_promocion   BIGINT,
    @tipo_descuento NVARCHAR(15)   = NULL,
    @valor          DECIMAL(12,2)  = NULL,
    @fecha_inicio   DATETIME       = NULL,
    @fecha_fin      DATETIME       = NULL,
    @nombre         NVARCHAR(120)  = NULL,
    @descripcion    NVARCHAR(500)  = NULL,
    @minimo_compra  DECIMAL(12,2)  = NULL,
    @servicios      VARCHAR(MAX)   = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'ADMINISTRADOR,SA';

        DECLARE @codigo NVARCHAR(30) = (SELECT codigo FROM dbo.promociones WHERE id = @id_promocion);
        IF @codigo IS NULL
            THROW 51348, N'La promoción no existe.', 1;

        DECLARE @tipo   NVARCHAR(15)  = COALESCE(@tipo_descuento, (SELECT tipo_descuento FROM dbo.promociones WHERE id = @id_promocion));
        DECLARE @monto  DECIMAL(12,2) = COALESCE(@valor,          (SELECT valor          FROM dbo.promociones WHERE id = @id_promocion));
        DECLARE @inicio DATETIME      = COALESCE(@fecha_inicio,   (SELECT fecha_inicio   FROM dbo.promociones WHERE id = @id_promocion));
        DECLARE @fin    DATETIME      = COALESCE(@fecha_fin,      (SELECT fecha_fin      FROM dbo.promociones WHERE id = @id_promocion));

        IF @tipo NOT IN (N'PORCENTAJE', N'MONTO')
            THROW 51349, N'El tipo de descuento debe ser PORCENTAJE o MONTO.', 1;
        IF @monto <= 0
            THROW 51355, N'El valor del descuento debe ser mayor que 0.', 1;
        IF @tipo = N'PORCENTAJE' AND @monto > 100
            THROW 51356, N'Un porcentaje no puede pasar de 100.', 1;
        IF @fin <= @inicio
            THROW 51357, N'El fin de la vigencia debe ser posterior al inicio.', 1;

        DECLARE @aplica TABLE (id_servicio BIGINT PRIMARY KEY);
        IF @servicios IS NOT NULL
        BEGIN
            INSERT INTO @aplica (id_servicio)
            SELECT DISTINCT TRY_CAST(LTRIM(RTRIM(value)) AS BIGINT)
            FROM STRING_SPLIT(@servicios, ',')
            WHERE TRY_CAST(LTRIM(RTRIM(value)) AS BIGINT) IS NOT NULL;

            IF NOT EXISTS (SELECT 1 FROM @aplica)
                THROW 51358, N'La lista de servicios quedó vacía: una promoción necesita al menos uno.', 1;
            -- Se admiten los que YA estaban ligados aunque estén de baja: si no, editar una promoción vieja
            -- fallaría sin que el gestor haya tocado nada.
            IF EXISTS (SELECT 1 FROM @aplica AS a
                       WHERE NOT EXISTS (SELECT 1 FROM dbo.servicios AS s WHERE s.id = a.id_servicio AND s.activo = 1)
                         AND NOT EXISTS (SELECT 1 FROM dbo.promocion_servicio AS ps
                                         WHERE ps.id_promocion = @id_promocion AND ps.id_servicio = a.id_servicio))
                THROW 51359, N'Algún servicio marcado no existe o está dado de baja.', 1;
        END

        BEGIN TRANSACTION;
            UPDATE dbo.promociones
            SET nombre = COALESCE(NULLIF(LTRIM(RTRIM(@nombre)), N''), nombre),
                descripcion = COALESCE(NULLIF(LTRIM(RTRIM(@descripcion)), N''), descripcion),
                tipo_descuento = @tipo, valor = @monto, fecha_inicio = @inicio, fecha_fin = @fin,
                minimo_compra = COALESCE(@minimo_compra, minimo_compra)
            WHERE id = @id_promocion;

            IF @servicios IS NOT NULL
            BEGIN
                DELETE FROM dbo.promocion_servicio WHERE id_promocion = @id_promocion;
                INSERT INTO dbo.promocion_servicio (id_promocion, id_servicio)
                SELECT @id_promocion, a.id_servicio FROM @aplica AS a;
            END

            DECLARE @detalle NVARCHAR(500) = CONCAT(N'Promoción ', @codigo, N' actualizada (', @tipo, N' ', @monto,
                N', del ', FORMAT(@inicio, 'dd/MM/yyyy'), N' al ', FORMAT(@fin, 'dd/MM/yyyy'), N').');
            EXEC dbo.pa_interno_registrar_bitacora @id_usuario, N'ACTUALIZAR', N'promociones', @id_promocion, @detalle;
        COMMIT TRANSACTION;

        SELECT * FROM dbo.promociones WHERE id = @id_promocion;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Datos de una ciudad del catálogo (solo SA). */
CREATE OR ALTER PROCEDURE dbo.pa_editar_ubicacion
    @id_usuario       BIGINT,
    @id_ubicacion     BIGINT,
    @estado           NVARCHAR(60),
    @municipio_ciudad NVARCHAR(100),
    @codigo_postal    NVARCHAR(10),
    @pais             NVARCHAR(60) = N'México'
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'SA';

        SELECT @pais = LTRIM(RTRIM(@pais)), @estado = LTRIM(RTRIM(@estado)),
               @municipio_ciudad = LTRIM(RTRIM(@municipio_ciudad)), @codigo_postal = LTRIM(RTRIM(@codigo_postal));
        IF NOT EXISTS (SELECT 1 FROM dbo.ubicaciones WHERE id = @id_ubicacion)
            THROW 51302, N'La ubicación no existe.', 1;
        IF NULLIF(@estado, N'') IS NULL OR NULLIF(@municipio_ciudad, N'') IS NULL
            THROW 51303, N'El estado y la ciudad son obligatorios.', 1;
        IF EXISTS (SELECT 1 FROM dbo.ubicaciones
                   WHERE pais = @pais AND estado = @estado AND municipio_ciudad = @municipio_ciudad AND id <> @id_ubicacion)
            THROW 51304, N'Ya existe esa ciudad en ese estado.', 1;

        BEGIN TRANSACTION;
            UPDATE dbo.ubicaciones
            SET pais = @pais, estado = @estado, municipio_ciudad = @municipio_ciudad,
                codigo_postal = @codigo_postal, actualizado_en = GETDATE()
            WHERE id = @id_ubicacion;

            DECLARE @detalle NVARCHAR(500) = CONCAT(@municipio_ciudad, N', ', @estado, N', ', @pais, N' (C.P. ', @codigo_postal, N').');
            EXEC dbo.pa_interno_registrar_bitacora @id_usuario, N'ACTUALIZAR', N'ubicaciones', @id_ubicacion, @detalle;
        COMMIT TRANSACTION;

        SELECT * FROM dbo.ubicaciones WHERE id = @id_ubicacion;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Nombre y descripción de un método de pago (solo SA). Los métodos no se dan de alta desde el panel:
   son un catálogo fijo que solo se edita y se activa o desactiva. */
CREATE OR ALTER PROCEDURE dbo.pa_editar_metodo_pago
    @id_usuario     BIGINT,
    @id_metodo_pago SMALLINT,
    @nombre         NVARCHAR(40),
    @descripcion    NVARCHAR(150) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'SA';

        SET @nombre = LTRIM(RTRIM(@nombre));
        IF NOT EXISTS (SELECT 1 FROM dbo.metodos_pago WHERE id = @id_metodo_pago)
            THROW 51386, N'El método de pago no existe.', 1;
        IF NULLIF(@nombre, N'') IS NULL
            THROW 51387, N'El nombre del método de pago es obligatorio.', 1;
        IF EXISTS (SELECT 1 FROM dbo.metodos_pago WHERE nombre = @nombre AND id <> @id_metodo_pago)
            THROW 51388, N'Ya existe un método de pago con ese nombre.', 1;

        BEGIN TRANSACTION;
            UPDATE dbo.metodos_pago
            SET nombre = @nombre, descripcion = NULLIF(LTRIM(RTRIM(@descripcion)), N'')
            WHERE id = @id_metodo_pago;

            DECLARE @detalle NVARCHAR(500) = CONCAT(N'Método de pago "', @nombre, N'": ', COALESCE(@descripcion, N'(sin descripción)'));
            EXEC dbo.pa_interno_registrar_bitacora @id_usuario, N'ACTUALIZAR', N'metodos_pago', @id_metodo_pago, @detalle;
        COMMIT TRANSACTION;

        SELECT * FROM dbo.metodos_pago WHERE id = @id_metodo_pago;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Datos de un banco emisor (solo SA). */
CREATE OR ALTER PROCEDURE dbo.pa_editar_banco
    @id_usuario           BIGINT,
    @id_banco             BIGINT,
    @nombre               NVARCHAR(120),
    @clave_institucion    NVARCHAR(10) = NULL,
    @codigo_internacional NVARCHAR(15) = NULL,
    @telefono             NVARCHAR(20) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'SA';

        SELECT @nombre = LTRIM(RTRIM(@nombre)),
               @clave_institucion = NULLIF(UPPER(LTRIM(RTRIM(@clave_institucion))), N''),
               @codigo_internacional = NULLIF(UPPER(LTRIM(RTRIM(@codigo_internacional))), N''),
               @telefono = NULLIF(LTRIM(RTRIM(@telefono)), N'');
        IF NOT EXISTS (SELECT 1 FROM dbo.instituciones_financieras WHERE id = @id_banco)
            THROW 51392, N'El banco no existe.', 1;
        IF NULLIF(@nombre, N'') IS NULL
            THROW 51393, N'El nombre del banco es obligatorio.', 1;
        IF EXISTS (SELECT 1 FROM dbo.instituciones_financieras WHERE nombre = @nombre AND id <> @id_banco)
            THROW 51394, N'Ya existe otro banco con ese nombre.', 1;
        IF @clave_institucion IS NOT NULL
           AND EXISTS (SELECT 1 FROM dbo.instituciones_financieras WHERE clave_institucion = @clave_institucion AND id <> @id_banco)
            THROW 51395, N'Esa clave ya la tiene otro banco.', 1;
        IF @codigo_internacional IS NOT NULL
           AND EXISTS (SELECT 1 FROM dbo.instituciones_financieras WHERE codigo_internacional = @codigo_internacional AND id <> @id_banco)
            THROW 51396, N'Ese código internacional ya lo tiene otro banco.', 1;

        BEGIN TRANSACTION;
            UPDATE dbo.instituciones_financieras
            SET nombre = @nombre, clave_institucion = @clave_institucion,
                codigo_internacional = @codigo_internacional, telefono = @telefono
            WHERE id = @id_banco;

            DECLARE @detalle NVARCHAR(500) = CONCAT(N'Banco "', @nombre, N'" actualizado (clave ',
                COALESCE(@clave_institucion, N'—'), N', código ', COALESCE(@codigo_internacional, N'—'), N').');
            EXEC dbo.pa_interno_registrar_bitacora @id_usuario, N'ACTUALIZAR', N'instituciones_financieras', @id_banco, @detalle;
        COMMIT TRANSACTION;

        SELECT * FROM dbo.instituciones_financieras WHERE id = @id_banco;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Datos personales de una cuenta. Los puede cambiar su DUEÑO (Mi perfil) o el SA desde el panel.
   El correo NO se cambia aquí: es con lo que se inicia sesión y es único; cambiarlo es otra operación. */
CREATE OR ALTER PROCEDURE dbo.pa_editar_usuario
    @id_usuario       BIGINT,            -- quién hace el cambio
    @id_cuenta        BIGINT,            -- cuenta que se modifica
    @nombre           VARCHAR(80),
    @apellido_paterno VARCHAR(60),
    @apellido_materno VARCHAR(60),
    @telefono         VARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY

        -- Cada quien puede editar lo suyo; para editar la cuenta de otra persona hace falta ser SA.
        IF @id_usuario <> @id_cuenta
            EXEC dbo.pa_interno_validar_rol @id_usuario, 'SA';
        ELSE
            EXEC dbo.pa_interno_validar_rol @id_usuario, 'CLIENTE,SOPORTE,AGENTE,ADMINISTRADOR,SA';

        SELECT @nombre = LTRIM(RTRIM(@nombre)), @apellido_paterno = LTRIM(RTRIM(@apellido_paterno)),
               @apellido_materno = LTRIM(RTRIM(@apellido_materno)), @telefono = LTRIM(RTRIM(@telefono));
        IF NOT EXISTS (SELECT 1 FROM dbo.usuarios WHERE id = @id_cuenta)
            THROW 51400, N'La cuenta no existe.', 1;
        IF NULLIF(@nombre, '') IS NULL OR NULLIF(@apellido_paterno, '') IS NULL
            THROW 51401, N'El nombre y el apellido paterno son obligatorios.', 1;
        IF EXISTS (SELECT 1 FROM dbo.usuarios WHERE telefono = @telefono AND id <> @id_cuenta)
            THROW 51402, N'Ese teléfono ya está registrado en otra cuenta.', 1;

        BEGIN TRANSACTION;
            UPDATE dbo.usuarios
            SET nombre = @nombre, apellido_paterno = @apellido_paterno, apellido_materno = @apellido_materno,
                telefono = @telefono, actualizado_en = GETDATE()
            WHERE id = @id_cuenta;

            DECLARE @detalle NVARCHAR(500) = CONCAT(N'Datos actualizados: ', @nombre, N' ', @apellido_paterno, N' ',
                                                    @apellido_materno, N', tel. ', @telefono, N'.');
            EXEC dbo.pa_interno_registrar_bitacora @id_usuario, N'ACTUALIZAR', N'usuarios', @id_cuenta, @detalle;
        COMMIT TRANSACTION;

        SELECT id, nombre, apellido_paterno, apellido_materno, correo, telefono, activo, actualizado_en
        FROM dbo.usuarios WHERE id = @id_cuenta;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Cambia la contraseña de una cuenta (R10): el dueño desde Mi perfil, o el SA al restablecerla.

   Igual que en el alta: recibe el HASH ya calculado por ASP.NET Core, NUNCA la contraseña en texto.
   En la bitácora queda que se cambió, jamás el valor. */
CREATE OR ALTER PROCEDURE dbo.pa_cambiar_password_usuario
    @id_usuario    BIGINT,
    @id_cuenta     BIGINT,
    @password_hash VARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY

        IF @id_usuario <> @id_cuenta
            EXEC dbo.pa_interno_validar_rol @id_usuario, 'SA';
        ELSE
            EXEC dbo.pa_interno_validar_rol @id_usuario, 'CLIENTE,SOPORTE,AGENTE,ADMINISTRADOR,SA';

        IF NOT EXISTS (SELECT 1 FROM dbo.usuarios WHERE id = @id_cuenta)
            THROW 51403, N'La cuenta no existe.', 1;
        IF LEN(@password_hash) < 60
            THROW 51404, N'El hash de la contraseña no es válido: este procedimiento NO recibe contraseñas en texto.', 1;

        BEGIN TRANSACTION;
            UPDATE dbo.usuarios SET password_hash = @password_hash, actualizado_en = GETDATE() WHERE id = @id_cuenta;

            DECLARE @correo VARCHAR(150) = (SELECT correo FROM dbo.usuarios WHERE id = @id_cuenta);
            DECLARE @detalle NVARCHAR(500) = CONCAT(N'Contraseña cambiada para ', @correo, N'.');
            EXEC dbo.pa_interno_registrar_bitacora @id_usuario, N'RESTABLECER_PASS', N'usuarios', @id_cuenta, @detalle;
        COMMIT TRANSACTION;

        SELECT @id_cuenta AS id_cuenta, N'Contraseña actualizada.' AS resultado;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Descripción de un rol (solo SA). El NOMBRE del rol no se toca: de él dependen los permisos. */
CREATE OR ALTER PROCEDURE dbo.pa_editar_descripcion_rol
    @id_usuario  BIGINT,
    @id_rol      BIGINT,
    @descripcion VARCHAR(150) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'SA';

        DECLARE @rol VARCHAR(30) = (SELECT nombre FROM dbo.roles_usuarios WHERE id = @id_rol);
        IF @rol IS NULL
            THROW 51405, N'El rol no existe.', 1;

        BEGIN TRANSACTION;
            UPDATE dbo.roles_usuarios SET descripcion = NULLIF(LTRIM(RTRIM(@descripcion)), '') WHERE id = @id_rol;

            DECLARE @detalle NVARCHAR(500) = CONCAT(N'Descripción del rol ', @rol, N': ',
                                                    COALESCE(NULLIF(LTRIM(RTRIM(@descripcion)), ''), '(vacía)'));
            EXEC dbo.pa_interno_registrar_bitacora @id_usuario, N'ACTUALIZAR', N'roles_usuarios', @id_rol, @detalle;
        COMMIT TRANSACTION;

        SELECT * FROM dbo.roles_usuarios WHERE id = @id_rol;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Alias de una tarjeta ("la del banco X"). Solo el dueño, y solo si la tarjeta sigue activa. */
CREATE OR ALTER PROCEDURE dbo.pa_editar_alias_tarjeta
    @id_usuario BIGINT,
    @id_tarjeta BIGINT,
    @alias      NVARCHAR(40) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY

        SET @alias = NULLIF(LTRIM(RTRIM(@alias)), N'');
        IF LEN(COALESCE(@alias, N'')) > 40
            THROW 51406, N'El alias puede tener máximo 40 caracteres.', 1;

        BEGIN TRANSACTION;
            UPDATE dbo.tarjetas_usuarios
            SET alias = @alias
            WHERE id = @id_tarjeta AND id_usuariog = @id_usuario AND activo = 1;

            IF @@ROWCOUNT = 0
                THROW 51407, N'No encontramos esa tarjeta en tu cuenta (o ya fue eliminada).', 1;
        COMMIT TRANSACTION;

        SELECT id, marca, ultimos4, alias FROM dbo.tarjetas_usuarios WHERE id = @id_tarjeta;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Calificación de la atención de un ticket (1 a 5 estrellas).
   UPDATE condicional: solo el dueño, solo un ticket CERRADO y solo una vez. */
CREATE OR ALTER PROCEDURE dbo.pa_calificar_ticket
    @id_usuario   BIGINT,
    @id_ticket    BIGINT,
    @calificacion TINYINT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY

        IF @calificacion NOT BETWEEN 1 AND 5
            THROW 51374, N'La calificación debe estar entre 1 y 5 estrellas.', 1;

        BEGIN TRANSACTION;
            UPDATE dbo.solicitudes_soporte
            SET calificacion = @calificacion, calificada_en = GETDATE(), actualizada_en = GETDATE()
            WHERE id = @id_ticket AND id_usuarioglobal = @id_usuario AND estado = N'CERRADA' AND calificacion IS NULL;

            IF @@ROWCOUNT = 0
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM dbo.solicitudes_soporte WHERE id = @id_ticket AND id_usuarioglobal = @id_usuario)
                    THROW 51375, N'Ese ticket no existe o no es tuyo.', 1;
                IF EXISTS (SELECT 1 FROM dbo.solicitudes_soporte WHERE id = @id_ticket AND calificacion IS NOT NULL)
                    THROW 51376, N'Ese ticket ya fue calificado.', 1;
                THROW 51377, N'Solo se puede calificar un ticket cerrado.', 1;
            END
        COMMIT TRANSACTION;

        SELECT id, estado, calificacion, calificada_en FROM dbo.solicitudes_soporte WHERE id = @id_ticket;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO
/* =====================================================================================
   3 · BAJAS LÓGICAS Y REACTIVACIONES
   R11: nada se borra. Usuarios, proveedores, servicios, promociones, tarjetas y catálogos se
   desactivan (activo = 0) y se pueden volver a activar. Así las compras, los pagos y la bitácora
   siguen apuntando a filas que existen.
   El único DELETE real de esta sección es el del contenido del sitio, y es a propósito: borrar esa
   fila es lo que hace que la página vuelva a mostrar su texto por defecto.
   ===================================================================================== */

/* Baja o reactivación de un proveedor. Al darlo de baja, sus servicios dejan de verse en el
   catálogo público (el sitio solo muestra servicios cuyo proveedor está activo). */
CREATE OR ALTER PROCEDURE dbo.pa_cambiar_estado_proveedor
    @id_usuario   BIGINT,
    @id_proveedor BIGINT,
    @activo       BIT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'ADMINISTRADOR,SA';

        DECLARE @nombre NVARCHAR(120), @estado_actual BIT;
        SELECT @nombre = nombre, @estado_actual = activo FROM dbo.proveedores WHERE id = @id_proveedor;
        IF @nombre IS NULL
            THROW 51410, N'El proveedor no existe.', 1;
        IF @estado_actual = @activo
            THROW 51411, N'El proveedor ya estaba en ese estado.', 1;

        DECLARE @servicios INT = (SELECT COUNT(*) FROM dbo.servicios WHERE id_proveedor = @id_proveedor AND activo = 1);

        BEGIN TRANSACTION;
            UPDATE dbo.proveedores SET activo = @activo, actualizado_en = GETDATE()
            WHERE id = @id_proveedor AND activo <> @activo;

            DECLARE @detalle NVARCHAR(500) = CASE WHEN @activo = 1
                THEN CONCAT(N'Proveedor "', @nombre, N'" reactivado.')
                ELSE CONCAT(N'Baja del proveedor "', @nombre, N'": sus ', @servicios,
                            N' servicio(s) activo(s) dejan de mostrarse en el catálogo.') END;
            DECLARE @accion NVARCHAR(30) = CASE WHEN @activo = 1 THEN N'REACTIVAR' ELSE N'BAJA' END;
            EXEC dbo.pa_interno_registrar_bitacora @id_usuario, @accion, N'proveedores', @id_proveedor, @detalle;
        COMMIT TRANSACTION;

        SELECT id, nombre, activo FROM dbo.proveedores WHERE id = @id_proveedor;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Baja o reactivación de un servicio (sirve igual para un paquete: un paquete es un servicio).
   Para REACTIVAR, su proveedor debe estar activo; si no, el servicio quedaría visible colgando de
   un proveedor dado de baja. Las reservaciones ya hechas no cambian. */
CREATE OR ALTER PROCEDURE dbo.pa_cambiar_estado_servicio
    @id_usuario  BIGINT,
    @id_servicio BIGINT,
    @activo      BIT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'ADMINISTRADOR,SA';

        DECLARE @nombre NVARCHAR(150), @estado_actual BIT, @id_proveedor BIGINT, @tipo NVARCHAR(20);
        SELECT @nombre = nombre, @estado_actual = activo, @id_proveedor = id_proveedor, @tipo = tipo_servicio
        FROM dbo.servicios WHERE id = @id_servicio;
        IF @nombre IS NULL
            THROW 51415, N'El servicio no existe.', 1;
        IF @estado_actual = @activo
            THROW 51416, N'El servicio ya estaba en ese estado.', 1;
        IF @activo = 1 AND NOT EXISTS (SELECT 1 FROM dbo.proveedores WHERE id = @id_proveedor AND activo = 1)
            THROW 51417, N'El proveedor de este servicio está dado de baja. Reactívalo primero.', 1;

        BEGIN TRANSACTION;
            UPDATE dbo.servicios SET activo = @activo, actualizado_en = GETDATE()
            WHERE id = @id_servicio AND activo <> @activo;

            DECLARE @detalle NVARCHAR(500) = CASE WHEN @activo = 1
                THEN CONCAT(N'Reactivación de "', @nombre, N'" (', @tipo, N').')
                ELSE CONCAT(N'Baja de "', @nombre, N'" (', @tipo, N'): ya no se puede reservar. Las reservaciones existentes no cambian.') END;
            DECLARE @accion NVARCHAR(30) = CASE WHEN @activo = 1 THEN N'REACTIVAR' ELSE N'BAJA' END;
            EXEC dbo.pa_interno_registrar_bitacora @id_usuario, @accion, N'servicios', @id_servicio, @detalle;
        COMMIT TRANSACTION;

        SELECT id, nombre, tipo_servicio, activo FROM dbo.servicios WHERE id = @id_servicio;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Baja o reactivación de una promoción. Desactivarla impide usar el código de ahí en adelante;
   las compras que ya la usaron conservan su descuento (R4). */
CREATE OR ALTER PROCEDURE dbo.pa_cambiar_estado_promocion
    @id_usuario   BIGINT,
    @id_promocion BIGINT,
    @activo       BIT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'ADMINISTRADOR,SA';

        DECLARE @codigo NVARCHAR(30), @estado_actual BIT;
        SELECT @codigo = codigo, @estado_actual = activo FROM dbo.promociones WHERE id = @id_promocion;
        IF @codigo IS NULL
            THROW 51420, N'La promoción no existe.', 1;
        IF @estado_actual = @activo
            THROW 51421, N'La promoción ya estaba en ese estado.', 1;

        BEGIN TRANSACTION;
            UPDATE dbo.promociones SET activo = @activo WHERE id = @id_promocion AND activo <> @activo;

            DECLARE @detalle NVARCHAR(500) = CONCAT(N'Promoción ', @codigo, N' ',
                                                    CASE WHEN @activo = 1 THEN N'activada.' ELSE N'desactivada: ya no se puede usar.' END);
            DECLARE @accion NVARCHAR(30) = CASE WHEN @activo = 1 THEN N'REACTIVAR' ELSE N'BAJA' END;
            EXEC dbo.pa_interno_registrar_bitacora @id_usuario, @accion, N'promociones', @id_promocion, @detalle;
        COMMIT TRANSACTION;

        SELECT id, codigo, activo, fecha_inicio, fecha_fin FROM dbo.promociones WHERE id = @id_promocion;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Baja o reactivación de una ciudad del catálogo (solo SA). Al desactivarla, los servicios y
   paquetes de esa ciudad dejan de aparecer en el catálogo. */
CREATE OR ALTER PROCEDURE dbo.pa_cambiar_estado_ubicacion
    @id_usuario   BIGINT,
    @id_ubicacion BIGINT,
    @activo       BIT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'SA';

        DECLARE @ciudad NVARCHAR(100), @estado NVARCHAR(60), @estado_actual BIT;
        SELECT @ciudad = municipio_ciudad, @estado = estado, @estado_actual = activo
        FROM dbo.ubicaciones WHERE id = @id_ubicacion;
        IF @ciudad IS NULL
            THROW 51425, N'La ubicación no existe.', 1;
        IF @estado_actual = @activo
            THROW 51426, N'La ubicación ya estaba en ese estado.', 1;

        -- Cuántos servicios dependen de esta ciudad (hospedajes, retiros de vehículo, vuelos y destinos de paquete).
        DECLARE @afectados INT = (
            SELECT COUNT(DISTINCT s.id)
            FROM dbo.servicios AS s
            LEFT JOIN dbo.hospedajes AS h ON h.id_servicio = s.id
            LEFT JOIN dbo.vehiculos  AS v ON v.id_servicio = s.id
            LEFT JOIN dbo.vuelos     AS f ON f.id_servicio = s.id
            LEFT JOIN dbo.paquete    AS p ON p.id_servicio = s.id
            WHERE s.activo = 1
              AND (h.id_ubicacion = @id_ubicacion OR v.id_ubicacion_retiro = @id_ubicacion
                   OR f.id_origen = @id_ubicacion OR f.id_destino = @id_ubicacion
                   OR p.id_ubicacion_destino = @id_ubicacion));

        BEGIN TRANSACTION;
            UPDATE dbo.ubicaciones SET activo = @activo, actualizado_en = GETDATE()
            WHERE id = @id_ubicacion AND activo <> @activo;

            DECLARE @detalle NVARCHAR(500) = CONCAT(@ciudad, N', ', @estado, N' ',
                CASE WHEN @activo = 1 THEN N'activada.' ELSE CONCAT(N'desactivada: ', @afectados,
                     N' servicio(s) dejan de aparecer en el catálogo.') END);
            DECLARE @accion NVARCHAR(30) = CASE WHEN @activo = 1 THEN N'REACTIVAR' ELSE N'BAJA' END;
            EXEC dbo.pa_interno_registrar_bitacora @id_usuario, @accion, N'ubicaciones', @id_ubicacion, @detalle;
        COMMIT TRANSACTION;

        SELECT id, municipio_ciudad, estado, pais, activo, @afectados AS servicios_afectados
        FROM dbo.ubicaciones WHERE id = @id_ubicacion;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Activa o desactiva un método de pago (solo SA).
   Siempre debe quedar al menos UNO activo: si no, nadie podría pagar. */
CREATE OR ALTER PROCEDURE dbo.pa_cambiar_estado_metodo_pago
    @id_usuario     BIGINT,
    @id_metodo_pago SMALLINT,
    @activo         BIT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'SA';

        DECLARE @nombre NVARCHAR(40), @estado_actual BIT;
        SELECT @nombre = nombre, @estado_actual = activo FROM dbo.metodos_pago WHERE id = @id_metodo_pago;
        IF @nombre IS NULL
            THROW 51430, N'El método de pago no existe.', 1;
        IF @estado_actual = @activo
            THROW 51431, N'El método de pago ya estaba en ese estado.', 1;
        IF @activo = 0 AND NOT EXISTS (SELECT 1 FROM dbo.metodos_pago WHERE activo = 1 AND id <> @id_metodo_pago)
            THROW 51432, N'Debe quedar al menos un método de pago activo; si no, nadie podría pagar.', 1;

        BEGIN TRANSACTION;
            UPDATE dbo.metodos_pago SET activo = @activo WHERE id = @id_metodo_pago AND activo <> @activo;

            DECLARE @detalle NVARCHAR(500) = CONCAT(N'Método de pago "', @nombre, N'" ',
                                                    CASE WHEN @activo = 1 THEN N'activado.' ELSE N'desactivado.' END);
            DECLARE @accion NVARCHAR(30) = CASE WHEN @activo = 1 THEN N'REACTIVAR' ELSE N'BAJA' END;
            EXEC dbo.pa_interno_registrar_bitacora @id_usuario, @accion, N'metodos_pago', @id_metodo_pago, @detalle;
        COMMIT TRANSACTION;

        SELECT id, nombre, activo FROM dbo.metodos_pago WHERE id = @id_metodo_pago;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Activa o desactiva un banco emisor (solo SA). Las tarjetas que ya lo tienen no cambian:
   solo deja de poder elegirse al guardar una tarjeta nueva. */
CREATE OR ALTER PROCEDURE dbo.pa_cambiar_estado_banco
    @id_usuario BIGINT,
    @id_banco   BIGINT,
    @activo     BIT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'SA';

        DECLARE @nombre NVARCHAR(120), @estado_actual BIT;
        SELECT @nombre = nombre, @estado_actual = activo FROM dbo.instituciones_financieras WHERE id = @id_banco;
        IF @nombre IS NULL
            THROW 51435, N'El banco no existe.', 1;
        IF @estado_actual = @activo
            THROW 51436, N'El banco ya estaba en ese estado.', 1;

        BEGIN TRANSACTION;
            UPDATE dbo.instituciones_financieras SET activo = @activo WHERE id = @id_banco AND activo <> @activo;

            DECLARE @detalle NVARCHAR(500) = CONCAT(N'Banco "', @nombre, N'" ',
                                                    CASE WHEN @activo = 1 THEN N'activado.' ELSE N'desactivado.' END);
            DECLARE @accion NVARCHAR(30) = CASE WHEN @activo = 1 THEN N'REACTIVAR' ELSE N'BAJA' END;
            EXEC dbo.pa_interno_registrar_bitacora @id_usuario, @accion, N'instituciones_financieras', @id_banco, @detalle;
        COMMIT TRANSACTION;

        SELECT id, nombre, activo FROM dbo.instituciones_financieras WHERE id = @id_banco;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* "Eliminar" una tarjeta del cliente: baja lógica (R5 y R11). La fila se queda porque los pagos
   hechos con esa tarjeta apuntan a ella; lo que desaparece es de la lista del cliente. */
CREATE OR ALTER PROCEDURE dbo.pa_baja_tarjeta
    @id_usuario BIGINT,
    @id_tarjeta BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY

        BEGIN TRANSACTION;
            UPDATE dbo.tarjetas_usuarios SET activo = 0
            WHERE id = @id_tarjeta AND id_usuariog = @id_usuario AND activo = 1;

            IF @@ROWCOUNT = 0
                THROW 51440, N'No encontramos esa tarjeta en tu cuenta (o ya estaba eliminada).', 1;
        COMMIT TRANSACTION;

        SELECT id, marca, ultimos4, activo FROM dbo.tarjetas_usuarios WHERE id = @id_tarjeta;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Devuelve un texto o una imagen del sitio a su valor por defecto (solo SA).
   Aquí sí se BORRA la fila, y es intencional: sin fila, el sitio muestra el texto que trae el
   código. No es información del negocio, es una personalización. */
CREATE OR ALTER PROCEDURE dbo.pa_restaurar_contenido_sitio
    @id_usuario BIGINT,
    @clave      VARCHAR(60)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'SA';

        SET @clave = LTRIM(RTRIM(@clave));
        DECLARE @habia BIT = CASE WHEN EXISTS (SELECT 1 FROM dbo.contenido_sitio WHERE clave = @clave) THEN 1 ELSE 0 END;

        BEGIN TRANSACTION;
            IF @habia = 1
            BEGIN
                DELETE FROM dbo.contenido_sitio WHERE clave = @clave;

                DECLARE @detalle NVARCHAR(500) = CONCAT(@clave, N': vuelve el contenido original.');
                EXEC dbo.pa_interno_registrar_bitacora @id_usuario, N'RESTAURAR', N'contenido_sitio', NULL, @detalle;
            END
        COMMIT TRANSACTION;

        SELECT @clave AS clave,
               CASE WHEN @habia = 1 THEN N'Restaurado al original.' ELSE N'Ya tenía el contenido original.' END AS resultado;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO
/* =====================================================================================
   4 · CONSULTAS
   Las listas y los detalles que la aplicación muestra y que no tenían procedimiento. No modifican
   nada. Las de panel validan el rol; las del sitio público no, porque las ve cualquier visitante.
   La búsqueda del catálogo por fechas ya existe en el script 02 (pa_buscar_disponibilidad) y por eso
   no se repite aquí.
   ===================================================================================== */

/* Ficha completa de un servicio para su página de detalle: datos comunes, proveedor, ciudad, los
   campos propios de su tipo, las promociones vigentes que lo incluyen y su disponibilidad próxima.
   Devuelve TRES conjuntos de resultados: ficha, promociones y disponibilidad. */
CREATE OR ALTER PROCEDURE dbo.pa_detalle_servicio
    @id_servicio BIGINT,
    @dias        INT = 30            -- cuántos días de disponibilidad devolver, a partir de hoy
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY

        IF NOT EXISTS (SELECT 1 FROM dbo.servicios WHERE id = @id_servicio)
            THROW 51450, N'El servicio no existe.', 1;

        -- 1) La ficha
        SELECT s.id, s.tipo_servicio, s.nombre, s.descripcion, s.precio_base, s.moneda, s.activo, s.imagen_ruta,
               s.permite_cancelacion, s.horas_reembolso_total, s.porcentaje_reembolso_tardio, s.politica_cancelacion,
               p.id AS id_proveedor, p.nombre AS proveedor, p.tipo_proveedor, p.activo AS proveedor_activo,
               u.municipio_ciudad AS ciudad, u.estado AS estado_ciudad,
               h.tipo_habitacion, h.categoria AS categoria_hospedaje, h.capacidad_huespedes, h.hora_entrada, h.hora_salida,
               v.categoria AS categoria_vehiculo, v.marca, v.modelo, v.capacidad_pasajeros,
               f.codigo_vuelo, f.fecha_salida, f.fecha_llegada, f.clase,
               pq.duracion_dias, pq.min_personas, pq.max_personas
        FROM dbo.servicios AS s
        JOIN dbo.proveedores AS p ON p.id = s.id_proveedor
        LEFT JOIN dbo.hospedajes AS h ON h.id_servicio = s.id
        LEFT JOIN dbo.vehiculos  AS v ON v.id_servicio = s.id
        LEFT JOIN dbo.vuelos     AS f ON f.id_servicio = s.id
        LEFT JOIN dbo.paquete    AS pq ON pq.id_servicio = s.id
        LEFT JOIN dbo.ubicaciones AS u
               ON u.id = COALESCE(h.id_ubicacion, v.id_ubicacion_retiro, f.id_destino, pq.id_ubicacion_destino, p.id_ubicacion)
        WHERE s.id = @id_servicio;

        -- 2) Promociones vigentes que aplican a este servicio
        SELECT pr.id, pr.codigo, pr.nombre, pr.tipo_descuento, pr.valor, pr.minimo_compra, pr.fecha_inicio, pr.fecha_fin
        FROM dbo.promociones AS pr
        JOIN dbo.promocion_servicio AS ps ON ps.id_promocion = pr.id
        WHERE ps.id_servicio = @id_servicio AND pr.activo = 1
          AND GETDATE() BETWEEN pr.fecha_inicio AND pr.fecha_fin
        ORDER BY pr.valor DESC;

        -- 3) Disponibilidad y precio de los próximos días
        SELECT d.fecha, d.cantidad_total, d.cantidad_disponible, d.precio_fecha
        FROM dbo.disponibilidad_servicio AS d
        WHERE d.id_servicio = @id_servicio
          AND d.fecha >= CAST(GETDATE() AS DATE)
          AND d.fecha < DATEADD(DAY, COALESCE(@dias, 30), CAST(GETDATE() AS DATE))
        ORDER BY d.fecha;

        -- 4) Si es un paquete, lo que incluye
        SELECT si.id AS id_servicio_incluido, si.nombre, si.tipo_servicio, pqs.cantidad, pqs.orden
        FROM dbo.paquete_servicio AS pqs
        JOIN dbo.servicios AS si ON si.id = pqs.id_servicio_incluido
        WHERE pqs.id_servicio = @id_servicio
        ORDER BY COALESCE(pqs.orden, 32767), si.nombre;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Promociones vigentes hoy, para la sección de promociones del sitio. */
CREATE OR ALTER PROCEDURE dbo.pa_promociones_vigentes
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY

        SELECT p.id, p.codigo, p.nombre, p.descripcion, p.tipo_descuento, p.valor, p.minimo_compra,
               p.fecha_inicio, p.fecha_fin,
               (SELECT COUNT(*) FROM dbo.promocion_servicio AS ps WHERE ps.id_promocion = p.id) AS servicios
        FROM dbo.promociones AS p
        WHERE p.activo = 1 AND GETDATE() BETWEEN p.fecha_inicio AND p.fecha_fin
        ORDER BY p.fecha_fin, p.codigo;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Proveedores del panel, con cuántos servicios tiene cada uno (ADMINISTRADOR o SA).
   @incluir_inactivos = 0 muestra solo los activos. */
CREATE OR ALTER PROCEDURE dbo.pa_listar_proveedores
    @id_usuario        BIGINT,
    @texto             NVARCHAR(120) = NULL,
    @tipo_proveedor    NVARCHAR(20)  = NULL,
    @incluir_inactivos BIT           = 0
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'ADMINISTRADOR,SA';

        SELECT p.id, p.nombre, p.tipo_proveedor, p.telefono, p.correo, p.activo,
               u.municipio_ciudad AS ciudad, u.estado,
               (SELECT COUNT(*) FROM dbo.servicios AS s WHERE s.id_proveedor = p.id)                 AS servicios,
               (SELECT COUNT(*) FROM dbo.servicios AS s WHERE s.id_proveedor = p.id AND s.activo = 1) AS servicios_activos
        FROM dbo.proveedores AS p
        LEFT JOIN dbo.ubicaciones AS u ON u.id = p.id_ubicacion
        WHERE (@incluir_inactivos = 1 OR p.activo = 1)
          AND (@tipo_proveedor IS NULL OR p.tipo_proveedor = @tipo_proveedor)
          AND (@texto IS NULL OR p.nombre LIKE N'%' + @texto + N'%' OR p.correo LIKE N'%' + @texto + N'%')
        ORDER BY p.nombre;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Servicios del panel del gestor, con su proveedor y su inventario próximo (ADMINISTRADOR o SA). */
CREATE OR ALTER PROCEDURE dbo.pa_listar_servicios
    @id_usuario        BIGINT,
    @texto             NVARCHAR(150) = NULL,
    @tipo_servicio     NVARCHAR(20)  = NULL,
    @id_proveedor      BIGINT        = NULL,
    @incluir_inactivos BIT           = 0
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'ADMINISTRADOR,SA';

        DECLARE @hoy DATE = CAST(GETDATE() AS DATE);

        SELECT s.id, s.tipo_servicio, s.nombre, s.precio_base, s.moneda, s.activo, s.actualizado_en,
               p.id AS id_proveedor, p.nombre AS proveedor, p.activo AS proveedor_activo,
               inv.dias_con_cupo, inv.proxima_fecha, inv.cupo_disponible,
               (SELECT COUNT(*) FROM dbo.detalle_reservacion AS d WHERE d.id_servicio = s.id) AS veces_reservado
        FROM dbo.servicios AS s
        JOIN dbo.proveedores AS p ON p.id = s.id_proveedor
        OUTER APPLY (SELECT COUNT(*) AS dias_con_cupo, MIN(d.fecha) AS proxima_fecha, SUM(d.cantidad_disponible) AS cupo_disponible
                     FROM dbo.disponibilidad_servicio AS d
                     WHERE d.id_servicio = s.id AND d.fecha >= @hoy AND d.cantidad_disponible > 0) AS inv
        WHERE (@incluir_inactivos = 1 OR s.activo = 1)
          AND (@tipo_servicio IS NULL OR s.tipo_servicio = @tipo_servicio)
          AND (@id_proveedor IS NULL OR s.id_proveedor = @id_proveedor)
          AND (@texto IS NULL OR s.nombre LIKE N'%' + @texto + N'%')
        ORDER BY s.tipo_servicio, s.nombre;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Paquetes con su destino, su duración y los servicios que incluyen (ADMINISTRADOR o SA).
   Devuelve DOS conjuntos: los paquetes y, después, el contenido de cada uno. */
CREATE OR ALTER PROCEDURE dbo.pa_listar_paquetes
    @id_usuario        BIGINT,
    @texto             NVARCHAR(150) = NULL,
    @incluir_inactivos BIT           = 0
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'ADMINISTRADOR,SA';

        DECLARE @hoy DATE = CAST(GETDATE() AS DATE);

        SELECT s.id, s.nombre, s.precio_base, s.moneda, s.activo,
               p.nombre AS proveedor,
               pq.duracion_dias, pq.min_personas, pq.max_personas,
               u.municipio_ciudad AS destino,
               (SELECT COUNT(*) FROM dbo.paquete_servicio AS ps WHERE ps.id_servicio = s.id) AS servicios_incluidos,
               (SELECT COUNT(*) FROM dbo.disponibilidad_servicio AS d
                WHERE d.id_servicio = s.id AND d.fecha >= @hoy AND d.cantidad_disponible > 0) AS dias_con_cupo
        INTO #paquetes
        FROM dbo.servicios AS s
        JOIN dbo.paquete AS pq ON pq.id_servicio = s.id
        JOIN dbo.proveedores AS p ON p.id = s.id_proveedor
        LEFT JOIN dbo.ubicaciones AS u ON u.id = pq.id_ubicacion_destino
        WHERE s.tipo_servicio = N'PAQUETE'
          AND (@incluir_inactivos = 1 OR s.activo = 1)
          AND (@texto IS NULL OR s.nombre LIKE N'%' + @texto + N'%');

        SELECT * FROM #paquetes ORDER BY nombre;

        SELECT ps.id_servicio AS id_paquete, si.id AS id_servicio_incluido, si.nombre, si.tipo_servicio,
               ps.cantidad, ps.orden, si.activo
        FROM dbo.paquete_servicio AS ps
        JOIN dbo.servicios AS si ON si.id = ps.id_servicio_incluido
        WHERE ps.id_servicio IN (SELECT id FROM #paquetes)
        ORDER BY ps.id_servicio, COALESCE(ps.orden, 32767), si.nombre;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Promociones del panel, con cuántas compras usaron cada una (ADMINISTRADOR o SA).
   @estado: 'vigentes', 'programadas', 'vencidas', 'inactivas' o NULL para todas. */
CREATE OR ALTER PROCEDURE dbo.pa_listar_promociones
    @id_usuario BIGINT,
    @codigo     NVARCHAR(30) = NULL,
    @estado     VARCHAR(15)  = NULL
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'ADMINISTRADOR,SA';

        DECLARE @ahora DATETIME = GETDATE();

        SELECT p.id, p.codigo, p.nombre, p.tipo_descuento, p.valor, p.minimo_compra,
               p.fecha_inicio, p.fecha_fin, p.activo,
               CASE WHEN p.activo = 0                 THEN N'INACTIVA'
                    WHEN @ahora < p.fecha_inicio      THEN N'PROGRAMADA'
                    WHEN @ahora > p.fecha_fin         THEN N'VENCIDA'
                    ELSE N'VIGENTE' END AS situacion,
               (SELECT COUNT(*) FROM dbo.promocion_servicio AS ps WHERE ps.id_promocion = p.id) AS servicios,
               (SELECT COUNT(DISTINCT d.id_reservacion) FROM dbo.detalle_reservacion AS d WHERE d.id_promocion = p.id) AS compras
        FROM dbo.promociones AS p
        WHERE (@codigo IS NULL OR p.codigo LIKE N'%' + @codigo + N'%')
          AND (@estado IS NULL
               OR (@estado = 'inactivas'   AND p.activo = 0)
               OR (@estado = 'vigentes'    AND p.activo = 1 AND @ahora BETWEEN p.fecha_inicio AND p.fecha_fin)
               OR (@estado = 'programadas' AND p.activo = 1 AND @ahora < p.fecha_inicio)
               OR (@estado = 'vencidas'    AND p.activo = 1 AND @ahora > p.fecha_fin))
        ORDER BY p.fecha_inicio DESC, p.codigo;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Cuentas del sistema, con su rol (solo SA).
   @estado: 'activos', 'inactivos' o NULL para todas. */
CREATE OR ALTER PROCEDURE dbo.pa_listar_usuarios
    @id_usuario BIGINT,
    @texto      NVARCHAR(150) = NULL,
    @rol        VARCHAR(30)   = NULL,
    @estado     VARCHAR(10)   = NULL
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'SA';

        SELECT u.id, u.nombre, u.apellido_paterno, u.apellido_materno, u.correo, u.telefono,
               r.nombre AS rol, u.activo, u.creado_en,
               (SELECT COUNT(*) FROM dbo.reservaciones AS rs WHERE rs.id_usuarioglobal = u.id)      AS reservaciones,
               (SELECT COUNT(*) FROM dbo.solicitudes_soporte AS t WHERE t.id_usuarioglobal = u.id)  AS tickets
        FROM dbo.usuarios AS u
        JOIN dbo.roles_usuarios AS r ON r.id = u.id_rol
        WHERE (@rol IS NULL OR r.nombre = @rol)
          AND (@estado IS NULL OR (@estado = 'activos' AND u.activo = 1) OR (@estado = 'inactivos' AND u.activo = 0))
          AND (@texto IS NULL OR u.nombre LIKE N'%' + @texto + N'%' OR u.apellido_paterno LIKE N'%' + @texto + N'%'
               OR u.correo LIKE N'%' + @texto + N'%' OR u.telefono LIKE N'%' + @texto + N'%')
        ORDER BY r.nombre, u.apellido_paterno, u.nombre;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Tickets de soporte del panel, con su cliente, su agente y el último movimiento.
   pa_reporte_tickets da los totales; este da la LISTA para trabajar. */
CREATE OR ALTER PROCEDURE dbo.pa_listar_tickets
    @id_usuario BIGINT,
    @estado     NVARCHAR(20) = NULL,    -- ABIERTA | EN_PROCESO | CERRADA
    @tipo       NVARCHAR(20) = NULL,    -- ASESORIA | PROBLEMA
    @id_agente  BIGINT       = NULL,    -- NULL = todos; 0 = sin agente asignado
    @desde      DATE         = NULL,
    @hasta      DATE         = NULL
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'SOPORTE,ADMINISTRADOR,SA';

        DECLARE @fin DATETIME = CASE WHEN @hasta IS NULL THEN NULL ELSE DATEADD(DAY, 1, CAST(@hasta AS DATETIME)) END;

        SELECT t.id, t.tipo, t.asunto, t.estado, t.creada_en, t.actualizada_en, t.calificacion,
               t.id_reservacion, r.codigo_reserva,
               CONCAT(c.nombre, N' ', c.apellido_paterno) AS cliente, c.correo AS correo_cliente,
               CONCAT(a.nombre, N' ', a.apellido_paterno) AS agente,
               DATEDIFF(HOUR, t.creada_en, GETDATE()) AS horas_abierto,
               (SELECT COUNT(*) FROM dbo.mensajes_soporte AS m WHERE m.id_solicitud = t.id) AS respuestas
        FROM dbo.solicitudes_soporte AS t
        JOIN dbo.usuarios AS c ON c.id = t.id_usuarioglobal
        LEFT JOIN dbo.usuarios AS a ON a.id = t.id_agente_asignado
        LEFT JOIN dbo.reservaciones AS r ON r.id = t.id_reservacion
        WHERE (@estado IS NULL OR t.estado = @estado)
          AND (@tipo IS NULL OR t.tipo = @tipo)
          AND (@id_agente IS NULL
               OR (@id_agente = 0 AND t.id_agente_asignado IS NULL)
               OR t.id_agente_asignado = @id_agente)
          AND (@desde IS NULL OR t.creada_en >= @desde)
          AND (@fin IS NULL OR t.creada_en < @fin)
        ORDER BY CASE t.estado WHEN N'ABIERTA' THEN 1 WHEN N'EN_PROCESO' THEN 2 ELSE 3 END, t.creada_en;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Reservaciones del panel, con su cliente, su estado y lo que lleva pagado.
   @estado: nombre del estado (PENDIENTE, CONFIRMADA, EN_CURSO, COMPLETADA, CANCELADA) o NULL. */
CREATE OR ALTER PROCEDURE dbo.pa_listar_reservaciones
    @id_usuario BIGINT,
    @texto      NVARCHAR(150) = NULL,   -- código de reserva, nombre o correo del cliente
    @estado     NVARCHAR(30)  = NULL,
    @desde      DATE          = NULL,
    @hasta      DATE          = NULL
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'AGENTE,ADMINISTRADOR,SA';

        DECLARE @fin DATETIME = CASE WHEN @hasta IS NULL THEN NULL ELSE DATEADD(DAY, 1, CAST(@hasta AS DATETIME)) END;

        SELECT r.id, r.codigo_reserva, r.creada_en, r.actualizada_en, r.subtotal, r.descuento_total, r.total, r.moneda,
               e.nombre AS estado,
               CONCAT(u.nombre, N' ', u.apellido_paterno) AS cliente, u.correo,
               linea.nombre AS servicio_principal, linea.tipo_servicio,
               (SELECT COUNT(*) FROM dbo.detalle_reservacion AS d WHERE d.id_reservacion = r.id) AS lineas,
               pagado.cobrado,
               (SELECT COUNT(*) FROM dbo.reembolsos AS rb
                JOIN dbo.pagos AS p2 ON p2.id = rb.id_pago
                WHERE p2.id_reservacion = r.id AND rb.estado = N'SOLICITADO') AS reembolsos_pendientes
        FROM dbo.reservaciones AS r
        JOIN dbo.estados_reservacion AS e ON e.id = r.id_estado_reservacion
        JOIN dbo.usuarios AS u ON u.id = r.id_usuarioglobal
        OUTER APPLY (SELECT TOP (1) s.nombre, s.tipo_servicio
                     FROM dbo.detalle_reservacion AS d
                     JOIN dbo.servicios AS s ON s.id = d.id_servicio
                     WHERE d.id_reservacion = r.id ORDER BY d.id) AS linea
        OUTER APPLY (SELECT COALESCE(SUM(p.monto), 0) AS cobrado
                     FROM dbo.pagos AS p
                     JOIN dbo.estados_pago AS ep ON ep.id = p.id_estado_pago
                     WHERE p.id_reservacion = r.id AND ep.nombre = N'APROBADO') AS pagado
        WHERE (@estado IS NULL OR e.nombre = @estado)
          AND (@desde IS NULL OR r.creada_en >= @desde)
          AND (@fin IS NULL OR r.creada_en < @fin)
          AND (@texto IS NULL OR r.codigo_reserva LIKE N'%' + @texto + N'%'
               OR u.nombre LIKE N'%' + @texto + N'%' OR u.apellido_paterno LIKE N'%' + @texto + N'%'
               OR u.correo LIKE N'%' + @texto + N'%')
        ORDER BY r.creada_en DESC;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO

/* Tarjetas guardadas de un cliente (R5): nunca se devuelve el token, solo lo que se puede mostrar.
   Las puede consultar el dueño; el personal de soporte también, para atender un problema de pago. */
CREATE OR ALTER PROCEDURE dbo.pa_listar_tarjetas_cliente
    @id_usuario BIGINT,
    @id_cliente BIGINT = NULL          -- NULL = las del propio usuario
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY

        SET @id_cliente = COALESCE(@id_cliente, @id_usuario);
        IF @id_cliente <> @id_usuario
            EXEC dbo.pa_interno_validar_rol @id_usuario, 'SOPORTE,ADMINISTRADOR,SA';

        SELECT t.id, t.marca, t.ultimos4, t.mes_expiracion, t.anio_expiracion, t.alias, t.activo, t.creado_en,
               b.nombre AS banco,
               CASE WHEN t.anio_expiracion < YEAR(GETDATE())
                      OR (t.anio_expiracion = YEAR(GETDATE()) AND t.mes_expiracion < MONTH(GETDATE()))
                    THEN 1 ELSE 0 END AS vencida,
               (SELECT COUNT(*) FROM dbo.pagos AS p WHERE p.id_tarjeta = t.id) AS pagos
        FROM dbo.tarjetas_usuarios AS t
        LEFT JOIN dbo.instituciones_financieras AS b ON b.id = t.id_institucion
        WHERE t.id_usuariog = @id_cliente AND t.activo = 1
        ORDER BY t.creado_en DESC;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- deshacer ANTES de escribir (si no, el INSERT falla)
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el error sigue llegando a quien llamo: el comportamiento no cambia
    END CATCH
END
GO
/* =====================================================================================
   Verificación: los 41 procedimientos de este script deben aparecer creados.
   ===================================================================================== */
SELECT COUNT(*) AS procedimientos_de_este_script   -- 41
FROM sys.procedures
WHERE name IN (
    N'pa_alta_ubicacion', N'pa_alta_banco', N'pa_alta_usuario', N'pa_alta_paquete', N'pa_alta_promocion',
    N'pa_crear_ticket', N'pa_alta_tarjeta', N'pa_guardar_contenido_sitio',
    N'pa_editar_proveedor', N'pa_editar_servicio', N'pa_editar_hospedaje', N'pa_editar_vehiculo', N'pa_editar_vuelo',
    N'pa_editar_paquete', N'pa_editar_promocion', N'pa_editar_ubicacion', N'pa_editar_metodo_pago', N'pa_editar_banco',
    N'pa_editar_usuario', N'pa_cambiar_password_usuario', N'pa_editar_descripcion_rol', N'pa_editar_alias_tarjeta',
    N'pa_calificar_ticket',
    N'pa_cambiar_estado_proveedor', N'pa_cambiar_estado_servicio', N'pa_cambiar_estado_promocion',
    N'pa_cambiar_estado_ubicacion', N'pa_cambiar_estado_metodo_pago', N'pa_cambiar_estado_banco',
    N'pa_baja_tarjeta', N'pa_restaurar_contenido_sitio',
    N'pa_detalle_servicio', N'pa_promociones_vigentes', N'pa_listar_proveedores', N'pa_listar_servicios',
    N'pa_listar_paquetes', N'pa_listar_promociones', N'pa_listar_usuarios', N'pa_listar_tickets',
    N'pa_listar_reservaciones', N'pa_listar_tarjetas_cliente');

SELECT COUNT(*) AS procedimientos_pa_en_total      -- 28 del script 02 + 41 de este = 69
FROM sys.procedures
WHERE name LIKE N'pa[_]%';
GO

SET NOEXEC OFF;
GO

/* =====================================================================================
   EJEMPLOS DE USO
   Quitar el comentario del bloque para probarlos en SSMS. Están en orden: lo que crea algo va
   antes de lo que lo edita o lo da de baja. Todos usan la cuenta SA del seed.
   =====================================================================================

DECLARE @sa     BIGINT = (SELECT id FROM dbo.usuarios WHERE correo = 'sa.prueba@flyvibemex.test');
DECLARE @admin  BIGINT = (SELECT TOP (1) u.id FROM dbo.usuarios AS u JOIN dbo.roles_usuarios AS r ON r.id = u.id_rol
                          WHERE r.nombre = 'ADMINISTRADOR' AND u.activo = 1);
DECLARE @cliente BIGINT = (SELECT TOP (1) u.id FROM dbo.usuarios AS u JOIN dbo.roles_usuarios AS r ON r.id = u.id_rol
                           WHERE r.nombre = 'CLIENTE' AND u.activo = 1);

-- Catálogos base (SA)
DECLARE @ciudad BIGINT;
EXEC dbo.pa_alta_ubicacion @id_usuario = @sa, @estado = N'Chiapas', @municipio_ciudad = N'Comitán',
     @codigo_postal = N'30000', @id_ubicacion = @ciudad OUTPUT;
EXEC dbo.pa_editar_ubicacion @id_usuario = @sa, @id_ubicacion = @ciudad, @estado = N'Chiapas',
     @municipio_ciudad = N'Comitán de Domínguez', @codigo_postal = N'30000';

DECLARE @banco BIGINT;
EXEC dbo.pa_alta_banco @id_usuario = @sa, @nombre = N'Banco de Prueba', @clave_institucion = N'999',
     @id_banco = @banco OUTPUT;
EXEC dbo.pa_cambiar_estado_banco @id_usuario = @sa, @id_banco = @banco, @activo = 0;

-- Paquete: se arma con dos servicios que ya existan (aquí, los dos primeros activos que no sean paquete)
DECLARE @s1 BIGINT = (SELECT MIN(id) FROM dbo.servicios
                      WHERE activo = 1 AND tipo_servicio IN (N'VUELO', N'HOSPEDAJE', N'VEHICULO', N'EVENTO'));
DECLARE @s2 BIGINT = (SELECT MIN(id) FROM dbo.servicios
                      WHERE activo = 1 AND tipo_servicio IN (N'VUELO', N'HOSPEDAJE', N'VEHICULO', N'EVENTO') AND id > @s1);
DECLARE @lista VARCHAR(MAX) = CONCAT(@s1, ':1,', @s2, ':1');
DECLARE @proveedor BIGINT = (SELECT TOP (1) id FROM dbo.proveedores WHERE activo = 1 ORDER BY id);
DECLARE @paquete BIGINT;
EXEC dbo.pa_alta_paquete @id_usuario = @admin, @id_proveedor = @proveedor, @nombre = N'Paquete de prueba',
     @precio_base = 8500, @servicios_incluidos = @lista, @inventario_desde = '2026-12-01',
     @inventario_hasta = '2026-12-15', @inventario_cupo = 10, @duracion_dias = 4, @min_personas = 1,
     @max_personas = 4, @id_servicio = @paquete OUTPUT;
EXEC dbo.pa_editar_paquete @id_usuario = @admin, @id_servicio = @paquete, @duracion_dias = 5;
EXEC dbo.pa_cambiar_estado_servicio @id_usuario = @admin, @id_servicio = @paquete, @activo = 0;

-- Promoción sobre ese paquete
DECLARE @promo BIGINT;
DECLARE @servicios VARCHAR(MAX) = CAST(@paquete AS VARCHAR(20));
EXEC dbo.pa_alta_promocion @id_usuario = @admin, @codigo = N'PRUEBA15', @tipo_descuento = N'PORCENTAJE',
     @valor = 15, @fecha_inicio = '2026-11-01', @fecha_fin = '2026-12-31', @servicios = @servicios,
     @id_promocion = @promo OUTPUT;
EXEC dbo.pa_editar_promocion @id_usuario = @admin, @id_promocion = @promo, @valor = 20;
EXEC dbo.pa_cambiar_estado_promocion @id_usuario = @admin, @id_promocion = @promo, @activo = 0;

-- Soporte: el cliente abre un ticket y, cuando se cierra, lo califica
DECLARE @ticket BIGINT;
EXEC dbo.pa_crear_ticket @id_usuario = @cliente, @asunto = N'Duda con mi reservación',
     @mensaje = N'¿Puedo cambiar la fecha de entrada?', @tipo = N'ASESORIA', @id_ticket = @ticket OUTPUT;
EXEC dbo.pa_tomar_ticket     @id_usuario = @admin, @id_ticket = @ticket;
EXEC dbo.pa_cerrar_ticket    @id_usuario = @admin, @id_ticket = @ticket;
EXEC dbo.pa_calificar_ticket @id_usuario = @cliente, @id_ticket = @ticket, @calificacion = 5;

-- Cuenta interna. El hash NO se escribe a mano: se copia de una cuenta que ya existe, y entonces la
-- cuenta nueva queda con la misma contraseña que esa.
DECLARE @hash VARCHAR(255) = (SELECT password_hash FROM dbo.usuarios WHERE correo = 'sa.prueba@flyvibemex.test');
DECLARE @nueva BIGINT;
EXEC dbo.pa_alta_usuario @id_usuario = @sa, @rol = 'SOPORTE', @nombre = 'Ana', @apellido_paterno = 'Pérez',
     @apellido_materno = 'López', @correo = 'ana.prueba@flyvibemex.test', @telefono = '9611234567',
     @password_hash = @hash, @id_cuenta = @nueva OUTPUT;
EXEC dbo.pa_editar_usuario @id_usuario = @sa, @id_cuenta = @nueva, @nombre = 'Ana María',
     @apellido_paterno = 'Pérez', @apellido_materno = 'López', @telefono = '9611234567';
EXEC dbo.pa_cambiar_estado_usuario @id_usuario = @sa, @id_cuenta = @nueva, @activo = 0;

-- Contenido del sitio
EXEC dbo.pa_guardar_contenido_sitio @id_usuario = @sa, @clave = 'inicio.bienvenida.titulo',
     @texto = N'Tu próximo viaje empieza aquí';
EXEC dbo.pa_restaurar_contenido_sitio @id_usuario = @sa, @clave = 'inicio.bienvenida.titulo';

-- Consultas
EXEC dbo.pa_promociones_vigentes;
EXEC dbo.pa_detalle_servicio @id_servicio = @paquete;
EXEC dbo.pa_listar_proveedores     @id_usuario = @admin, @incluir_inactivos = 1;
EXEC dbo.pa_listar_servicios       @id_usuario = @admin, @tipo_servicio = N'HOSPEDAJE';
EXEC dbo.pa_listar_paquetes        @id_usuario = @admin, @incluir_inactivos = 1;
EXEC dbo.pa_listar_promociones     @id_usuario = @admin;
EXEC dbo.pa_listar_usuarios        @id_usuario = @sa, @estado = 'activos';
EXEC dbo.pa_listar_tickets         @id_usuario = @admin, @estado = N'ABIERTA';
EXEC dbo.pa_listar_reservaciones   @id_usuario = @admin;
EXEC dbo.pa_listar_tarjetas_cliente @id_usuario = @cliente;

-- Y el registro de errores: este EXEC falla a propósito (el rol no alcanza) y deja una fila.
EXEC dbo.pa_alta_ubicacion @id_usuario = @cliente, @estado = N'Jalisco', @municipio_ciudad = N'Tequila',
     @codigo_postal = N'46400';
SELECT TOP (10) * FROM dbo.LogError ORDER BY id DESC;

   ===================================================================================== */
