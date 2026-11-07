/* =====================================================================================
   FlyVibe Mex · 09_huespedes_etiquetas_favoritos_resenas.sql           (aditivo, v2)
   -------------------------------------------------------------------------------------
   Tablas nuevas (5): tipos_huesped, detalle_reservacion_huesped, hospedaje_etiquetas,
                      favoritos, resenas.   NO modifica ninguna tabla existente.
   Procedimientos (4): pa_alternar_favorito, pa_crear_resena, pa_listar_resenas, pa_moderar_resena
   Requisitos : 01 (tablas), 04 (LogError), 02 (pa_interno_*). Mismas convenciones que el 06.
   Somee      : NO lleva USE. Selecciona la base ANTES de ejecutar. Se puede correr varias veces.
   Errores    : rango 514xx (libre).
   Etiquetas  : la lista válida vive en C# (Models/EtiquetasHospedaje.cs); aquí solo se guardan.
                Agregar una etiqueta nueva NO requiere cambios en la base.
   Huéspedes especiales y etiquetas del hotel se escriben con EF desde C# (dentro de las transacciones
   que ya existen), por eso no tienen procedimiento.
   ===================================================================================== */
SET NOCOUNT ON;
GO

IF DB_NAME() IN (N'master', N'tempdb', N'model', N'msdb')
BEGIN
    RAISERROR(N'Estás en una base del sistema. Selecciona la base de FlyVibeMex y repite.', 16, 1);
    SET NOEXEC ON;
END
GO
IF OBJECT_ID(N'dbo.hospedajes', N'U') IS NULL OR OBJECT_ID(N'dbo.detalle_reservacion', N'U') IS NULL
   OR OBJECT_ID(N'dbo.LogError', N'U') IS NULL OR OBJECT_ID(N'dbo.pa_interno_validar_rol', N'P') IS NULL
BEGIN
    RAISERROR(N'Faltan tablas o ayudantes. Ejecuta antes los scripts 01, 04 y 02.', 16, 1);
    SET NOEXEC ON;
END
GO
PRINT N'Base de datos destino: ' + DB_NAME();
GO

/* =====================================================================================
   1 · TABLAS
   ===================================================================================== */

/* Catálogo de huéspedes con necesidades específicas. */
IF OBJECT_ID(N'dbo.tipos_huesped', N'U') IS NULL
CREATE TABLE dbo.tipos_huesped(
    id          SMALLINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_tipos_huesped PRIMARY KEY,
    codigo      VARCHAR(20)   NOT NULL CONSTRAINT uq_tipos_huesped_codigo UNIQUE,
    nombre      NVARCHAR(40)  NOT NULL,
    descripcion NVARCHAR(150) NULL,
    activo      BIT           NOT NULL CONSTRAINT df_tipos_huesped_activo DEFAULT(1)
);
GO

/* Huéspedes con necesidades específicas de UNA línea de reserva (snapshot de la compra). */
IF OBJECT_ID(N'dbo.detalle_reservacion_huesped', N'U') IS NULL
CREATE TABLE dbo.detalle_reservacion_huesped(
    id_detalle      BIGINT   NOT NULL CONSTRAINT fk_drh_detalle REFERENCES dbo.detalle_reservacion(id),
    id_tipo_huesped SMALLINT NOT NULL CONSTRAINT fk_drh_tipo    REFERENCES dbo.tipos_huesped(id),
    cantidad        SMALLINT NOT NULL CONSTRAINT ck_drh_cantidad CHECK (cantidad BETWEEN 1 AND 20),
    CONSTRAINT pk_detalle_reservacion_huesped PRIMARY KEY (id_detalle, id_tipo_huesped)
);
GO

/* Tabla intermedia de etiquetas (tags) del hospedaje: ALBERCA, GRANDE, WIFI... No toca hospedajes ni servicios. */
IF OBJECT_ID(N'dbo.hospedaje_etiquetas', N'U') IS NULL
CREATE TABLE dbo.hospedaje_etiquetas(
    id_servicio BIGINT      NOT NULL CONSTRAINT fk_hospedaje_etiquetas_hospedajes REFERENCES dbo.hospedajes(id_servicio),
    etiqueta    VARCHAR(30) NOT NULL CONSTRAINT ck_hospedaje_etiquetas_etiqueta CHECK (LEN(etiqueta) > 0),
    CONSTRAINT pk_hospedaje_etiquetas PRIMARY KEY (id_servicio, etiqueta)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_hospedaje_etiquetas_etiqueta')
    CREATE INDEX ix_hospedaje_etiquetas_etiqueta ON dbo.hospedaje_etiquetas(etiqueta);
GO

/* Favoritos: cualquier servicio LOCAL (vuelo, hotel, auto, paquete, tour). */
IF OBJECT_ID(N'dbo.favoritos', N'U') IS NULL
CREATE TABLE dbo.favoritos(
    id_usuarioglobal BIGINT   NOT NULL CONSTRAINT fk_favoritos_usuarios  REFERENCES dbo.usuarios(id),
    id_servicio      BIGINT   NOT NULL CONSTRAINT fk_favoritos_servicios REFERENCES dbo.servicios(id),
    creado_en        DATETIME NOT NULL CONSTRAINT df_favoritos_creado_en DEFAULT(CURRENT_TIMESTAMP),
    CONSTRAINT pk_favoritos PRIMARY KEY (id_usuarioglobal, id_servicio)
);
GO

/* Reseñas: una por usuario y servicio. OCULTA = moderada (no se muestra, no se borra). */
IF OBJECT_ID(N'dbo.resenas', N'U') IS NULL
CREATE TABLE dbo.resenas(
    id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_resenas PRIMARY KEY,
    id_usuarioglobal BIGINT         NOT NULL CONSTRAINT fk_resenas_usuarios  REFERENCES dbo.usuarios(id),
    id_servicio      BIGINT         NOT NULL CONSTRAINT fk_resenas_servicios REFERENCES dbo.servicios(id),
    calificacion     TINYINT        NOT NULL CONSTRAINT ck_resenas_calificacion CHECK (calificacion BETWEEN 1 AND 5),
    titulo           NVARCHAR(100)  NULL,
    comentario       NVARCHAR(1000) NOT NULL,
    estado           NVARCHAR(10)   NOT NULL CONSTRAINT df_resenas_estado DEFAULT(N'VISIBLE')
                     CONSTRAINT ck_resenas_estado CHECK (estado IN (N'VISIBLE', N'OCULTA')),
    creada_en        DATETIME       NOT NULL CONSTRAINT df_resenas_creada_en DEFAULT(CURRENT_TIMESTAMP),
    actualizada_en   DATETIME       NOT NULL CONSTRAINT df_resenas_actualizada_en DEFAULT(CURRENT_TIMESTAMP),
    CONSTRAINT uq_resenas_usuario_servicio UNIQUE (id_usuarioglobal, id_servicio)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_resenas_servicio')
    CREATE INDEX ix_resenas_servicio ON dbo.resenas(id_servicio, estado);
GO

/* =====================================================================================
   2 · DATOS INICIALES (idempotentes)
   ===================================================================================== */
INSERT INTO dbo.tipos_huesped (codigo, nombre, descripcion)
SELECT v.codigo, v.nombre, v.descripcion
FROM (VALUES
    ('NINO',         N'Niño',         N'Menor de edad: puede requerir cuna, cama extra o menú infantil.'),
    ('EMBARAZADA',   N'Embarazada',   N'Mujer embarazada: planta baja, cercanía al elevador, almohadas especiales.'),
    ('ADULTO_MAYOR', N'Adulto mayor', N'Persona de 60 años o más: accesibilidad y cama baja.')
) AS v(codigo, nombre, descripcion)
WHERE NOT EXISTS (SELECT 1 FROM dbo.tipos_huesped t WHERE t.codigo = v.codigo);
GO

/* =====================================================================================
   3 · PROCEDIMIENTOS
   ===================================================================================== */

/* Agrega el servicio a favoritos si no estaba; si ya estaba, lo quita. Devuelve el estado final. */
CREATE OR ALTER PROCEDURE dbo.pa_alternar_favorito
    @id_usuario   BIGINT,
    @id_servicio  BIGINT,
    @es_favorito  BIT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'CLIENTE';

        IF NOT EXISTS (SELECT 1 FROM dbo.servicios WHERE id = @id_servicio AND activo = 1)
            THROW 51420, N'El servicio no existe o ya no está disponible.', 1;

        BEGIN TRANSACTION;
            IF EXISTS (SELECT 1 FROM dbo.favoritos WHERE id_usuarioglobal = @id_usuario AND id_servicio = @id_servicio)
            BEGIN
                DELETE FROM dbo.favoritos WHERE id_usuarioglobal = @id_usuario AND id_servicio = @id_servicio;
                SET @es_favorito = 0;
            END
            ELSE
            BEGIN
                INSERT INTO dbo.favoritos (id_usuarioglobal, id_servicio) VALUES (@id_usuario, @id_servicio);
                SET @es_favorito = 1;
            END
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;
    END CATCH
END
GO

/* Crea la reseña. Reglas: solo CLIENTE; reservación CONFIRMADA de ese servicio que ya empezó
   (para relajarlo, quita d.fecha_inicio <= GETDATE()); una reseña por servicio. */
CREATE OR ALTER PROCEDURE dbo.pa_crear_resena
    @id_usuario    BIGINT,
    @id_servicio   BIGINT,
    @calificacion  TINYINT,
    @titulo        NVARCHAR(100) = NULL,
    @comentario    NVARCHAR(1000),
    @id_resena     BIGINT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'CLIENTE';

        SELECT @titulo = NULLIF(LTRIM(RTRIM(@titulo)), N''), @comentario = LTRIM(RTRIM(@comentario));
        IF @calificacion NOT BETWEEN 1 AND 5
            THROW 51430, N'La calificación debe ser de 1 a 5.', 1;
        IF NULLIF(@comentario, N'') IS NULL
            THROW 51431, N'Escribe un comentario.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.servicios WHERE id = @id_servicio)
            THROW 51432, N'El servicio no existe.', 1;
        IF EXISTS (SELECT 1 FROM dbo.resenas WHERE id_usuarioglobal = @id_usuario AND id_servicio = @id_servicio)
            THROW 51433, N'Ya dejaste una reseña de este servicio.', 1;
        IF NOT EXISTS (
            SELECT 1
            FROM dbo.detalle_reservacion d
            JOIN dbo.reservaciones r       ON r.id = d.id_reservacion
            JOIN dbo.estados_reservacion e ON e.id = r.id_estado_reservacion
            WHERE r.id_usuarioglobal = @id_usuario AND d.id_servicio = @id_servicio
              AND e.nombre = N'CONFIRMADA' AND d.fecha_inicio <= GETDATE())
            THROW 51434, N'Solo puedes reseñar un servicio que ya reservaste y disfrutaste.', 1;

        BEGIN TRANSACTION;
            INSERT INTO dbo.resenas (id_usuarioglobal, id_servicio, calificacion, titulo, comentario)
            VALUES (@id_usuario, @id_servicio, @calificacion, @titulo, @comentario);
            SET @id_resena = SCOPE_IDENTITY();

            DECLARE @detalle NVARCHAR(500) = CONCAT(N'Reseña #', @id_resena, N' del servicio #', @id_servicio,
                                                    N' (', @calificacion, N'/5)');
            EXEC dbo.pa_interno_registrar_bitacora @id_usuario, N'CREAR', N'resenas', @id_resena, @detalle;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;
    END CATCH
END
GO

/* Reseñas de un servicio. DOS conjuntos: (1) reseñas, (2) promedio y total de las visibles.
   @incluir_ocultas = 1 solo para quien modera (C# decide con el rol). */
CREATE OR ALTER PROCEDURE dbo.pa_listar_resenas
    @id_servicio     BIGINT,
    @incluir_ocultas BIT = 0
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        SELECT r.id, r.calificacion, r.titulo, r.comentario, r.estado, r.creada_en,
               CONCAT(u.nombre, N' ', LEFT(u.apellido_paterno, 1), N'.') AS autor
        FROM dbo.resenas AS r
        JOIN dbo.usuarios AS u ON u.id = r.id_usuarioglobal
        WHERE r.id_servicio = @id_servicio AND (@incluir_ocultas = 1 OR r.estado = N'VISIBLE')
        ORDER BY r.creada_en DESC;

        SELECT CAST(ISNULL(AVG(CAST(calificacion AS DECIMAL(4,2))), 0) AS DECIMAL(3,2)) AS promedio,
               COUNT(*) AS total
        FROM dbo.resenas WHERE id_servicio = @id_servicio AND estado = N'VISIBLE';
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;
    END CATCH
END
GO

/* Oculta o vuelve a mostrar una reseña. SOPORTE, ADMINISTRADOR y SA. */
CREATE OR ALTER PROCEDURE dbo.pa_moderar_resena
    @id_usuario BIGINT,
    @id_resena  BIGINT,
    @estado     NVARCHAR(10)      -- VISIBLE | OCULTA
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        EXEC dbo.pa_interno_validar_rol @id_usuario, 'SOPORTE,ADMINISTRADOR,SA';

        IF @estado NOT IN (N'VISIBLE', N'OCULTA')
            THROW 51440, N'El estado debe ser VISIBLE u OCULTA.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.resenas WHERE id = @id_resena)
            THROW 51441, N'La reseña no existe.', 1;

        BEGIN TRANSACTION;
            UPDATE dbo.resenas SET estado = @estado, actualizada_en = GETDATE() WHERE id = @id_resena;
            DECLARE @detalle NVARCHAR(500) = CONCAT(N'Reseña #', @id_resena, N' → ', @estado);
            EXEC dbo.pa_interno_registrar_bitacora @id_usuario, N'EDITAR', N'resenas', @id_resena, @detalle;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        IF ERROR_PROCEDURE() IS NULL OR ERROR_PROCEDURE() = OBJECT_NAME(@@PROCID)
            INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                      error_linea, error_mensaje, fecha)
            VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                    ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;
    END CATCH
END
GO

SET NOEXEC OFF;
GO

/* ---------- Pruebas rápidas (descomenta y ajusta ids) ----------
SELECT * FROM dbo.tipos_huesped;
INSERT INTO dbo.hospedaje_etiquetas (id_servicio, etiqueta) VALUES (10, 'ALBERCA'), (10, 'GRANDE');
DECLARE @fav BIT; EXEC dbo.pa_alternar_favorito @id_usuario = 5, @id_servicio = 10, @es_favorito = @fav OUTPUT; SELECT @fav;
EXEC dbo.pa_listar_resenas @id_servicio = 10;
*/
