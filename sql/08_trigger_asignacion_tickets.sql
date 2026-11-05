/* =====================================================================================
   FlyVibe Mex · 08_trigger_asignacion_tickets.sql                     (script 8, aditivo)
   -------------------------------------------------------------------------------------
   Trigger AFTER INSERT en dbo.solicitudes_soporte: cada ticket nuevo que llega ABIERTA y sin agente se
   asigna en automático a la cuenta de SOPORTE activa con MENOS tickets activos (no cerrados) y pasa a
   EN_PROCESO. Queda registro en dbo.bitacora con id_usuario = NULL (el panel lo muestra como "Sistema").

   ⚠ ALCANCE (decisión del 05/11/2026): este script es para la ENTREGA DE BASE DE DATOS.
       NO se corre en somee ni en la base de la feria. Con el trigger activo, la "fila libre" de tickets
       ABIERTA desaparece: el panel ya no tiene qué "tomar" (SoportePanelService, pa_tomar_ticket), el bot
       ya no avisa "Abierto · sin agente" y /pendientes nunca cuenta tickets sin agente.
       Para apagarlo sin borrarlo:  DISABLE TRIGGER dbo.tr_solicitudes_soporte_asignar_agente ON dbo.solicitudes_soporte;
       Para quitarlo:               DROP TRIGGER IF EXISTS dbo.tr_solicitudes_soporte_asignar_agente;

   La aplicación YA está preparada para convivir con él: Data/FlyVibeMexDbContext.cs declara el trigger con
   HasTrigger. Sin esa declaración, EF Core inserta con "OUTPUT INSERTED.id" y SQL Server lo rechaza en cuanto
   la tabla tiene un trigger (error 334): crear un ticket desde la web fallaría.

   Requisitos: sql/01_crear_base_datos.sql (tablas) y dbo.LogError (01, o sql/04_log_errores.sql).
   Orden: 01 → 02 → 03 → 06 → 08. Va DESPUÉS del seed (03): si el trigger existiera al cargar el seed, el
   ticket ABIERTA de demostración se asignaría solo. Volver a correr 01 borra la tabla y con ella el trigger:
   hay que volver a correr este script.
   Se puede ejecutar varias veces (CREATE OR ALTER). NO lleva USE: en SSMS selecciona antes la base.

   Reglas que respeta (archivo y línea en el repositorio):
     · Solo cuentas de rol SOPORTE, con la cuenta y el rol activos. ADMINISTRADOR y SA pueden atender
       tickets (pa_tomar_ticket, 02:990), pero no se les reparten en automático.
     · "Tickets activos" = asignados al agente y con estado <> CERRADA.
     · Asignado ⇒ EN_PROCESO: lo exige ck_solicitudes_en_proceso (01:480) y lo esperan pa_responder_ticket
       (02:1054) y pa_cerrar_ticket (02:1150).
     · Solo toca tickets que llegan ABIERTA y sin agente. Uno que ya trae agente (alta hecha por el personal)
       se respeta tal cual.
     · Sin ninguna cuenta de SOPORTE activa, el ticket se queda ABIERTA y sin agente: nunca bloquea el alta.
     · Varios tickets en un solo INSERT: se reparten en round-robin, empezando por el agente con menos carga.
     · Dos altas al mismo tiempo no leen la misma carga: sp_getapplock pone en fila a las ejecuciones del
       trigger (solo entre ellas; no bloquea a nadie más). Si la espera pasa de 5 s, el ticket se queda
       ABIERTA, que es un estado válido: alguien lo toma desde el panel.
     · Errores: mismo esquema que los procedimientos (sql/02). El CATCH deshace la transacción, guarda una fila
       en dbo.LogError (con error_procedimiento = el nombre del trigger) y relanza el error con THROW. El INSERT
       que lo disparó falla igual que antes; ahora queda registrado.
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

IF OBJECT_ID(N'dbo.solicitudes_soporte', N'U') IS NULL OR OBJECT_ID(N'dbo.bitacora', N'U') IS NULL
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

PRINT N'Base de datos destino: ' + DB_NAME();
GO

CREATE OR ALTER TRIGGER dbo.tr_solicitudes_soporte_asignar_agente
ON dbo.solicitudes_soporte
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    -- Nada que hacer si ningún ticket nuevo llegó ABIERTA y sin agente.
    IF NOT EXISTS (SELECT 1 FROM inserted WHERE id_agente_asignado IS NULL AND estado = N'ABIERTA')
        RETURN;

    BEGIN TRY
        -- Fila entre ejecuciones del trigger, ligada a la transacción del INSERT: se suelta sola al terminar.
        DECLARE @candado INT;
        EXEC @candado = sys.sp_getapplock @Resource = N'flyvibemex:asignar_ticket', @LockMode = N'Exclusive',
                                          @LockOwner = N'Transaction', @LockTimeout = 5000;
        IF @candado < 0
            RETURN;   -- no se consiguió turno a tiempo: el ticket queda ABIERTA (válido) y se toma desde el panel

        -- 1. Agentes de SOPORTE activos, de menos a más tickets activos (desempate: el id más bajo).
        DECLARE @agentes TABLE (
            orden     INT IDENTITY(1,1) PRIMARY KEY,
            id_agente BIGINT NOT NULL,
            activos   INT    NOT NULL
        );

        -- Con INSERT … SELECT … ORDER BY, SQL Server asigna el IDENTITY en el orden del ORDER BY.
        -- Se cuenta con SUM(CASE …) y no con COUNT(s.id): COUNT sobre el LEFT JOIN manda el aviso "Null value is
        -- eliminated by an aggregate" en CADA alta de ticket (inofensivo, pero ensucia la salida de SSMS).
        INSERT INTO @agentes (id_agente, activos)
        SELECT u.id, SUM(CASE WHEN s.id IS NULL THEN 0 ELSE 1 END)
        FROM dbo.usuarios AS u
        JOIN dbo.roles_usuarios AS r ON r.id = u.id_rol
        LEFT JOIN dbo.solicitudes_soporte AS s
               ON s.id_agente_asignado = u.id AND s.estado <> N'CERRADA'
        WHERE r.nombre = 'SOPORTE' AND r.activo = 1 AND u.activo = 1
        GROUP BY u.id
        ORDER BY SUM(CASE WHEN s.id IS NULL THEN 0 ELSE 1 END) ASC, u.id ASC;

        DECLARE @total_agentes INT = @@ROWCOUNT;
        IF @total_agentes = 0
            RETURN;   -- sin SOPORTE activo: el ticket queda ABIERTA y sin agente

        -- 2. Cada ticket nuevo con su agente: round-robin empezando por el de menos carga.
        DECLARE @asignaciones TABLE (
            id_ticket BIGINT NOT NULL PRIMARY KEY,
            id_agente BIGINT NOT NULL
        );

        WITH tickets_nuevos AS (
            SELECT i.id AS id_ticket, ROW_NUMBER() OVER (ORDER BY i.id) AS rn
            FROM inserted AS i
            WHERE i.id_agente_asignado IS NULL AND i.estado = N'ABIERTA'
        )
        INSERT INTO @asignaciones (id_ticket, id_agente)
        SELECT t.id_ticket, a.id_agente
        FROM tickets_nuevos AS t
        JOIN @agentes AS a ON a.orden = ((t.rn - 1) % @total_agentes) + 1;

        -- 3. Asignar y pasar a EN_PROCESO (la condición repite el filtro por seguridad).
        UPDATE s
        SET s.id_agente_asignado = asig.id_agente,
            s.estado             = N'EN_PROCESO',
            s.actualizada_en     = GETDATE()
        FROM dbo.solicitudes_soporte AS s
        JOIN @asignaciones AS asig ON asig.id_ticket = s.id
        WHERE s.id_agente_asignado IS NULL AND s.estado = N'ABIERTA';

        -- 4. Bitácora del panel: id_usuario = NULL ("Sistema").
        INSERT INTO dbo.bitacora (fecha, id_usuario, accion, entidad, id_entidad, detalle)
        SELECT GETDATE(), NULL, N'ASIGNAR_AUTO', N'solicitudes_soporte', asig.id_ticket,
               CONCAT(N'Ticket #', asig.id_ticket, N' asignado automáticamente al agente de soporte #', asig.id_agente, N'.')
        FROM @asignaciones AS asig;
    END TRY
    BEGIN CATCH
        -- La transacción es la del INSERT que disparó el trigger y queda inservible: se deshace ANTES de escribir
        -- (si no, el INSERT en LogError fallaría y el registro se perdería).
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        INSERT INTO dbo.LogError (error_numero, error_severidad, error_estado, error_procedimiento,
                                  error_linea, error_mensaje, fecha)
        VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(),
                ERROR_LINE(), ERROR_MESSAGE(), GETDATE());
        THROW;   -- el INSERT que lo disparó falla igual que antes: solo queda registrado
    END CATCH
END;
GO

/* ---------- Verificación ---------- */
SELECT t.name AS trig, OBJECT_NAME(t.parent_id) AS tabla,
       CASE WHEN t.is_disabled = 1 THEN N'DESACTIVADO' ELSE N'ACTIVO' END AS estado
FROM sys.triggers AS t
WHERE t.name = N'tr_solicitudes_soporte_asignar_agente';
GO

SET NOEXEC OFF;
GO

/* =====================================================================================
   Ejemplo de uso (quitar el comentario para probar)
   =====================================================================================

-- Carga actual de cada cuenta de SOPORTE
SELECT u.id, u.correo, COUNT(s.id) AS tickets_activos
FROM dbo.usuarios AS u
JOIN dbo.roles_usuarios AS r ON r.id = u.id_rol
LEFT JOIN dbo.solicitudes_soporte AS s ON s.id_agente_asignado = u.id AND s.estado <> N'CERRADA'
WHERE r.nombre = 'SOPORTE' AND u.activo = 1
GROUP BY u.id, u.correo
ORDER BY tickets_activos, u.id;

-- Un ticket nuevo: debe quedar EN_PROCESO con el primer agente de la lista anterior
DECLARE @cliente BIGINT = (SELECT id FROM dbo.usuarios WHERE correo = 'cliente.prueba@flyvibemex.test');
DECLARE @nuevo BIGINT;
EXEC dbo.pa_crear_ticket @id_usuario = @cliente, @asunto = N'Prueba de asignación automática',
     @mensaje = N'Verificando el trigger.', @tipo = N'ASESORIA', @id_ticket = @nuevo OUTPUT;

SELECT TOP (5) fecha, accion, id_entidad, detalle FROM dbo.bitacora ORDER BY id DESC;

   ===================================================================================== */
