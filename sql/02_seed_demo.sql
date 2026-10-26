/* =====================================================================================
   FlyVibe Mex · 02_seed_demo.sql
   -------------------------------------------------------------------------------------
   Datos de DEMOSTRACIÓN coherentes para probar la web, el panel y el bot de Telegram:
       proveedor -> servicio -> subtipo -> disponibilidad -> promoción -> reservación
       -> detalle (snapshot) -> pago -> reembolso -> bitácoras -> tickets de soporte.

   Requisitos: haber ejecutado sql/00_db_prueba_conexion.sql y sql/01_esquema.sql.
   Se puede ejecutar varias veces: VACÍA las 24 tablas de 01_esquema.sql y las vuelve a llenar.
   No borra usuarios: solo agrega 2 si no existen (agente.prueba y ana.lopez).

   Las fechas son RELATIVAS al día en que se ejecuta (disponibilidad de hoy a +29 días,
   vuelos en +3 y +7 días, viaje "urgente" en +2 días), salvo FV-260901 (12 al 15 de diciembre
   de 2026), que es el ejemplo de docs/06_ANALISIS_Y_DISENO_BOT_TELEGRAM.md.

   Usuarios para probar (contraseña de TODOS: FlyVibe#Prueba2026, solo para esta base):
       cliente.prueba@flyvibemex.test   CLIENTE        reservación FV-260902 (viaje en 2 días)
       ana.lopez@flyvibemex.test        CLIENTE        FV-260901 (confirmada) y FV-260903 (cancelada, reembolso)
       agente.prueba@flyvibemex.test    AGENTE         atiende el ticket EN_PROCESO
       admin.prueba@flyvibemex.test     ADMINISTRADOR
   Los nombres de proveedores y bancos son ficticios. Las tarjetas son tokens falsos: NO hay
   números de tarjeta ni CVV (regla R5).

   Convención de importes en detalle_reservacion:
       subtotal = cantidad * precio_unitario (ANTES de descuento); descuento_aplicado aparte.
       reservaciones.subtotal = suma de subtotales; total = subtotal - descuento_total.
   ===================================================================================== */

USE FlyVibeMex_Test;
GO

IF DB_NAME() <> N'FlyVibeMex_Test'
BEGIN
    RAISERROR(N'Este script solo debe ejecutarse sobre FlyVibeMex_Test. Ejecución detenida.', 16, 1);
    SET NOEXEC ON;
END
GO
IF OBJECT_ID(N'dbo.solicitudes_soporte', N'U') IS NULL OR OBJECT_ID(N'dbo.vuelos', N'U') IS NULL
BEGIN
    RAISERROR(N'Faltan tablas. Ejecuta primero sql/00_db_prueba_conexion.sql y sql/01_esquema.sql.', 16, 1);
    SET NOEXEC ON;
END
GO

SET XACT_ABORT ON;   -- cualquier error deshace TODO el script
BEGIN TRANSACTION;

/* ---------- Vaciar (de hijas a padres) ---------- */
DELETE FROM dbo.solicitudes_soporte;
DELETE FROM dbo.historial_estado_reembolso;
DELETE FROM dbo.reembolsos;
DELETE FROM dbo.historial_estado_pago;
DELETE FROM dbo.pagos;
DELETE FROM dbo.estados_pago;
DELETE FROM dbo.tarjetas_usuarios;
DELETE FROM dbo.instituciones_financieras;
DELETE FROM dbo.metodos_pago;
DELETE FROM dbo.historial_estado_reservacion;
DELETE FROM dbo.detalle_reservacion;
DELETE FROM dbo.reservaciones;
DELETE FROM dbo.estados_reservacion;
DELETE FROM dbo.promocion_servicio;
DELETE FROM dbo.promociones;
DELETE FROM dbo.disponibilidad_servicio;
DELETE FROM dbo.paquete_servicio;
DELETE FROM dbo.paquete;
DELETE FROM dbo.vehiculos;
DELETE FROM dbo.hospedajes;
DELETE FROM dbo.vuelos;
DELETE FROM dbo.servicios;
DELETE FROM dbo.proveedores;
DELETE FROM dbo.ubicaciones;

DECLARE @hoy   DATE     = CAST(GETDATE() AS DATE);
DECLARE @ahora DATETIME = GETDATE();

/* =====================================================================================
   Usuarios extra (los otros 3 vienen de 00_db_prueba_conexion.sql)
   Hashes PasswordHasher V3 (PBKDF2-HMAC-SHA512, 100 000 iteraciones, sal propia) de
   "FlyVibe#Prueba2026".
   ===================================================================================== */
IF NOT EXISTS (SELECT 1 FROM dbo.usuarios WHERE correo = 'agente.prueba@flyvibemex.test')
    INSERT INTO dbo.usuarios (id_rol, nombre, apellido_paterno, apellido_materno, correo, telefono, password_hash, activo)
    VALUES ((SELECT id FROM dbo.roles_usuarios WHERE nombre = 'AGENTE'),
            'Agente', 'Prueba', 'FlyVibe', 'agente.prueba@flyvibemex.test', '9610000004',
            'AQAAAAIAAYagAAAAEKrTa4pzbnSHVkqn1+0EcRQQhceq2uQeDEnxY6z6Axl4Obp+Xg5gjSKkHMhQeEL0Zw==', 1);

IF NOT EXISTS (SELECT 1 FROM dbo.usuarios WHERE correo = 'ana.lopez@flyvibemex.test')
    INSERT INTO dbo.usuarios (id_rol, nombre, apellido_paterno, apellido_materno, correo, telefono, password_hash, activo)
    VALUES ((SELECT id FROM dbo.roles_usuarios WHERE nombre = 'CLIENTE'),
            'Ana', 'Lopez', 'Ruiz', 'ana.lopez@flyvibemex.test', '9610000005',
            'AQAAAAIAAYagAAAAEFOZtB5u1GmUdv3ypelMpxtc7Yn2ySjw42CtVd4gHq5Cmft87R75yodkMWkaCAEd4g==', 1);

DECLARE @uCliente BIGINT = (SELECT id FROM dbo.usuarios WHERE correo = 'cliente.prueba@flyvibemex.test');
DECLARE @uAna     BIGINT = (SELECT id FROM dbo.usuarios WHERE correo = 'ana.lopez@flyvibemex.test');
DECLARE @uAgente  BIGINT = (SELECT id FROM dbo.usuarios WHERE correo = 'agente.prueba@flyvibemex.test');

IF @uCliente IS NULL OR @uAna IS NULL OR @uAgente IS NULL
    THROW 50001, N'No se encontraron los usuarios de prueba. Ejecuta de nuevo sql/00_db_prueba_conexion.sql.', 1;

/* =====================================================================================
   Ubicaciones
   ===================================================================================== */
DECLARE @lCdmx BIGINT, @lCun BIGINT, @lTgz BIGINT, @lScl BIGINT, @lOax BIGINT;

INSERT INTO dbo.ubicaciones (pais, estado, municipio_ciudad, codigo_postal) VALUES (N'México', N'Ciudad de México', N'Ciudad de México', N'06000');
SET @lCdmx = SCOPE_IDENTITY();
INSERT INTO dbo.ubicaciones (pais, estado, municipio_ciudad, codigo_postal) VALUES (N'México', N'Quintana Roo', N'Cancún', N'77500');
SET @lCun = SCOPE_IDENTITY();
INSERT INTO dbo.ubicaciones (pais, estado, municipio_ciudad, codigo_postal) VALUES (N'México', N'Chiapas', N'Tuxtla Gutiérrez', N'29000');
SET @lTgz = SCOPE_IDENTITY();
INSERT INTO dbo.ubicaciones (pais, estado, municipio_ciudad, codigo_postal) VALUES (N'México', N'Chiapas', N'San Cristóbal de las Casas', N'29200');
SET @lScl = SCOPE_IDENTITY();
INSERT INTO dbo.ubicaciones (pais, estado, municipio_ciudad, codigo_postal) VALUES (N'México', N'Oaxaca', N'Oaxaca de Juárez', N'68000');
SET @lOax = SCOPE_IDENTITY();
INSERT INTO dbo.ubicaciones (pais, estado, municipio_ciudad, codigo_postal, activo) VALUES (N'México', N'Jalisco', N'Guadalajara', N'44100', 0);  -- inactiva

/* =====================================================================================
   Proveedores (ficticios)
   ===================================================================================== */
DECLARE @pAerolinea BIGINT, @pHotelCaribe BIGINT, @pHotelJovel BIGINT, @pRentadora BIGINT, @pFlyVibe BIGINT;

INSERT INTO dbo.proveedores (id_ubicacion, tipo_proveedor, nombre, telefono, correo)
VALUES (@lCdmx, N'AEROLINEA', N'Aerolínea Quetzal (demo)', N'5500000001', N'reservas@quetzal-demo.test');
SET @pAerolinea = SCOPE_IDENTITY();
INSERT INTO dbo.proveedores (id_ubicacion, tipo_proveedor, nombre, telefono, correo)
VALUES (@lCun, N'HOTEL', N'Hoteles Caribe Azul (demo)', N'9980000001', N'contacto@caribeazul-demo.test');
SET @pHotelCaribe = SCOPE_IDENTITY();
INSERT INTO dbo.proveedores (id_ubicacion, tipo_proveedor, nombre, telefono, correo)
VALUES (@lScl, N'HOTEL', N'Hotel Colonial Jovel (demo)', N'9670000001', N'recepcion@jovel-demo.test');
SET @pHotelJovel = SCOPE_IDENTITY();
INSERT INTO dbo.proveedores (id_ubicacion, tipo_proveedor, nombre, telefono, correo)
VALUES (@lCun, N'RENTADORA', N'Rentadora Sureste (demo)', N'9980000002', N'autos@sureste-demo.test');
SET @pRentadora = SCOPE_IDENTITY();
INSERT INTO dbo.proveedores (id_ubicacion, tipo_proveedor, nombre, telefono, correo)
VALUES (@lCdmx, N'OTRO', N'FlyVibe Mex (paquetes propios)', N'9610000000', N'paquetes@flyvibemex.test');
SET @pFlyVibe = SCOPE_IDENTITY();

/* =====================================================================================
   Servicios (supertipo) + subtipos
   ===================================================================================== */
DECLARE @sVueloCun BIGINT, @sVueloTgz BIGINT, @sHotelCun BIGINT, @sHotelScl BIGINT, @sHostalOax BIGINT,
        @sAutoCun BIGINT, @sSuvTgz BIGINT, @sPaqCun BIGINT, @sPaqChis BIGINT;

INSERT INTO dbo.servicios (id_proveedor, tipo_servicio, nombre, descripcion, precio_base)
VALUES (@pAerolinea, N'VUELO', N'Vuelo Ciudad de México → Cancún', N'Vuelo directo en clase turista con una maleta de mano.', 2350.00);
SET @sVueloCun = SCOPE_IDENTITY();
INSERT INTO dbo.servicios (id_proveedor, tipo_servicio, nombre, descripcion, precio_base)
VALUES (@pAerolinea, N'VUELO', N'Vuelo Tuxtla Gutiérrez → Ciudad de México', N'Vuelo matutino directo.', 1890.00);
SET @sVueloTgz = SCOPE_IDENTITY();
INSERT INTO dbo.servicios (id_proveedor, tipo_servicio, nombre, descripcion, precio_base)
VALUES (@pHotelCaribe, N'HOSPEDAJE', N'Hotel Playa Azul · Habitación doble', N'Frente al mar, desayuno incluido. Precio por noche.', 2450.00);
SET @sHotelCun = SCOPE_IDENTITY();
INSERT INTO dbo.servicios (id_proveedor, tipo_servicio, nombre, descripcion, precio_base)
VALUES (@pHotelJovel, N'HOSPEDAJE', N'Hotel Colonial Jovel · Habitación sencilla', N'A dos calles del andador. Precio por noche.', 1200.00);
SET @sHotelScl = SCOPE_IDENTITY();
INSERT INTO dbo.servicios (id_proveedor, tipo_servicio, nombre, descripcion, precio_base, activo)
VALUES (@pHotelJovel, N'HOSPEDAJE', N'Hostal Centro Oaxaca (fuera de catálogo)', N'Servicio dado de baja: no debe aparecer en búsquedas.', 800.00, 0);
SET @sHostalOax = SCOPE_IDENTITY();
INSERT INTO dbo.servicios (id_proveedor, tipo_servicio, nombre, descripcion, precio_base)
VALUES (@pRentadora, N'VEHICULO', N'Renta de auto compacto automático (Cancún)', N'Kilometraje libre y seguro básico. Precio por día.', 660.00);
SET @sAutoCun = SCOPE_IDENTITY();
INSERT INTO dbo.servicios (id_proveedor, tipo_servicio, nombre, descripcion, precio_base)
VALUES (@pRentadora, N'VEHICULO', N'Renta de SUV 7 pasajeros (Tuxtla Gutiérrez)', N'Ideal para grupos. Precio por día.', 1150.00);
SET @sSuvTgz = SCOPE_IDENTITY();
INSERT INTO dbo.servicios (id_proveedor, tipo_servicio, nombre, descripcion, precio_base)
VALUES (@pFlyVibe, N'PAQUETE', N'Cancún 4 días con vuelo, hotel y auto', N'Vuelo redondo desde CDMX, 3 noches en Hotel Playa Azul y auto por 3 días. Precio por persona.', 9800.00);
SET @sPaqCun = SCOPE_IDENTITY();
INSERT INTO dbo.servicios (id_proveedor, tipo_servicio, nombre, descripcion, precio_base)
VALUES (@pFlyVibe, N'PAQUETE', N'Chiapas mágico 3 días', N'San Cristóbal de las Casas y Cañón del Sumidero, con hotel y SUV. Precio por persona.', 6500.00);
SET @sPaqChis = SCOPE_IDENTITY();

-- Vuelos: salida en +7 días (Cancún) y +3 días (Tuxtla).
INSERT INTO dbo.vuelos (id_servicio, id_origen, id_destino, codigo_vuelo, fecha_salida, fecha_llegada, clase)
VALUES
    (@sVueloCun, @lCdmx, @lCun,  N'QZ301', DATEADD(MINUTE, 7*60+30, CAST(DATEADD(DAY, 7, @hoy) AS DATETIME)),
                                           DATEADD(MINUTE, 10*60+5, CAST(DATEADD(DAY, 7, @hoy) AS DATETIME)), N'TURISTA'),
    (@sVueloTgz, @lTgz,  @lCdmx, N'QZ518', DATEADD(MINUTE, 6*60+45, CAST(DATEADD(DAY, 3, @hoy) AS DATETIME)),
                                           DATEADD(MINUTE, 8*60+10, CAST(DATEADD(DAY, 3, @hoy) AS DATETIME)), N'TURISTA');

INSERT INTO dbo.hospedajes (id_servicio, id_ubicacion, tipo_habitacion, categoria, capacidad_huespedes, hora_entrada, hora_salida)
VALUES
    (@sHotelCun,  @lCun, N'Doble',      N'4 estrellas', 2, '15:00', '12:00'),
    (@sHotelScl,  @lScl, N'Sencilla',   N'Boutique',    1, '14:00', '11:00'),
    (@sHostalOax, @lOax, N'Compartida', NULL,           4, '13:00', '11:00');

INSERT INTO dbo.vehiculos (id_servicio, id_ubicacion_retiro, categoria, marca, modelo, capacidad_pasajeros)
VALUES
    (@sAutoCun, @lCun, N'Compacto', N'Chevrolet', N'Aveo',    5),
    (@sSuvTgz,  @lTgz, N'SUV',      N'Kia',       N'Sorento', 7);

INSERT INTO dbo.paquete (id_servicio, duracion_dias, min_personas, max_personas, politica_cancelacion)
VALUES
    (@sPaqCun,  4, 2, 4, N'Cancelación sin costo hasta 7 días antes de la salida. De 7 días a 48 horas antes se cobra el 30 % del total. A menos de 48 horas no hay reembolso.'),
    (@sPaqChis, 3, 1, 6, N'Cancelación sin costo hasta 72 horas antes del inicio. Después se cobra la primera noche de hotel.');

INSERT INTO dbo.paquete_servicio (id_servicio, id_servicio_incluido, cantidad, orden)
VALUES
    (@sPaqCun,  @sVueloCun, 1, 1),
    (@sPaqCun,  @sHotelCun, 3, 2),
    (@sPaqCun,  @sAutoCun,  3, 3),
    (@sPaqChis, @sHotelScl, 2, 1),
    (@sPaqChis, @sSuvTgz,   3, 2),
    (@sPaqChis, @sVueloTgz, 1, 3);

/* =====================================================================================
   Disponibilidad (R3): hoy .. +29 días (30 días: cubre la entrega aunque el seed se ejecute
   unas semanas antes). Fin de semana +15 %. Un día agotado para probar.
   1900-01-01 fue lunes: (días desde esa fecha) % 7 = 5 sábado, 6 domingo.
   ===================================================================================== */
DECLARE @inventario TABLE (id_servicio BIGINT, total INT, precio DECIMAL(12,2));
INSERT INTO @inventario VALUES
    (@sHotelCun, 8, 2450.00), (@sHotelScl, 6, 1200.00), (@sAutoCun, 5, 660.00),
    (@sSuvTgz, 3, 1150.00), (@sPaqCun, 10, 9800.00), (@sPaqChis, 10, 6500.00);

DECLARE @dias TABLE (d INT);
INSERT INTO @dias VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9),
                         (10),(11),(12),(13),(14),(15),(16),(17),(18),(19),
                         (20),(21),(22),(23),(24),(25),(26),(27),(28),(29);

INSERT INTO dbo.disponibilidad_servicio (id_servicio, fecha, cantidad_total, cantidad_disponible, precio_fecha)
SELECT i.id_servicio,
       DATEADD(DAY, d.d, @hoy),
       i.total,
       CASE WHEN i.id_servicio = @sHotelCun AND d.d = 5 THEN 0        -- día agotado
            ELSE i.total - (d.d % 3) END,
       CASE WHEN DATEDIFF(DAY, '19000101', DATEADD(DAY, d.d, @hoy)) % 7 IN (5, 6)
            THEN ROUND(i.precio * 1.15, 2) ELSE i.precio END
FROM @inventario AS i CROSS JOIN @dias AS d;

-- Vuelos: solo el día de salida.
INSERT INTO dbo.disponibilidad_servicio (id_servicio, fecha, cantidad_total, cantidad_disponible, precio_fecha)
VALUES (@sVueloCun, DATEADD(DAY, 7, @hoy), 180, 42, 2350.00),
       (@sVueloTgz, DATEADD(DAY, 3, @hoy), 150, 12, 1890.00);

/* =====================================================================================
   Promociones: 2 vigentes y 1 vencida
   ===================================================================================== */
DECLARE @prBienvenida BIGINT, @prCaribe BIGINT, @prVerano BIGINT;

INSERT INTO dbo.promociones (codigo, nombre, descripcion, tipo_descuento, valor, fecha_inicio, fecha_fin, minimo_compra)
VALUES (N'BIENVENIDA10', N'Bienvenida FlyVibe', N'10 % en hospedaje, autos y paquetes.', N'PORCENTAJE', 10,
        DATEADD(DAY, -5, CAST(@hoy AS DATETIME)), DATEADD(DAY, 60, CAST(@hoy AS DATETIME)), 0);
SET @prBienvenida = SCOPE_IDENTITY();
INSERT INTO dbo.promociones (codigo, nombre, descripcion, tipo_descuento, valor, fecha_inicio, fecha_fin, minimo_compra)
VALUES (N'CARIBE500', N'Caribe con descuento', N'$500 de descuento en compras desde $5,000 en Cancún.', N'MONTO', 500,
        DATEADD(DAY, -1, CAST(@hoy AS DATETIME)), DATEADD(DAY, 30, CAST(@hoy AS DATETIME)), 5000);
SET @prCaribe = SCOPE_IDENTITY();
INSERT INTO dbo.promociones (codigo, nombre, descripcion, tipo_descuento, valor, fecha_inicio, fecha_fin, minimo_compra)
VALUES (N'VERANO2026', N'Verano 2026', N'Promoción vencida: no debe mostrarse.', N'PORCENTAJE', 15,
        '2026-06-01T00:00:00', '2026-08-31T23:59:59', 0);
SET @prVerano = SCOPE_IDENTITY();

INSERT INTO dbo.promocion_servicio (id_promocion, id_servicio)
VALUES (@prBienvenida, @sHotelCun), (@prBienvenida, @sHotelScl), (@prBienvenida, @sAutoCun),
       (@prBienvenida, @sSuvTgz),   (@prBienvenida, @sPaqCun),   (@prBienvenida, @sPaqChis),
       (@prCaribe, @sHotelCun),     (@prCaribe, @sPaqCun),
       (@prVerano, @sPaqCun);

/* =====================================================================================
   Catálogos de estados y pagos
   ===================================================================================== */
INSERT INTO dbo.estados_reservacion (nombre, descripcion, es_final)
VALUES (N'PENDIENTE',  N'Creada, en espera de pago', 0),
       (N'CONFIRMADA', N'Pagada y confirmada', 0),
       (N'EN_CURSO',   N'El viaje ya comenzó', 0),
       (N'COMPLETADA', N'El viaje terminó', 1),
       (N'CANCELADA',  N'Cancelada por el cliente o por la agencia', 1);

DECLARE @erPendiente SMALLINT  = (SELECT id FROM dbo.estados_reservacion WHERE nombre = N'PENDIENTE');
DECLARE @erConfirmada SMALLINT = (SELECT id FROM dbo.estados_reservacion WHERE nombre = N'CONFIRMADA');
DECLARE @erCancelada SMALLINT  = (SELECT id FROM dbo.estados_reservacion WHERE nombre = N'CANCELADA');

INSERT INTO dbo.metodos_pago (nombre, descripcion, activo)
VALUES (N'Tarjeta de crédito', N'Visa, Mastercard o American Express. El cobro se hace solo en la web.', 1),
       (N'Tarjeta de débito',  N'Tarjetas de débito de bancos mexicanos. El cobro se hace solo en la web.', 1),
       (N'Transferencia SPEI', N'Próximamente.', 0);

DECLARE @mpCredito SMALLINT = (SELECT id FROM dbo.metodos_pago WHERE nombre = N'Tarjeta de crédito');
DECLARE @mpDebito  SMALLINT = (SELECT id FROM dbo.metodos_pago WHERE nombre = N'Tarjeta de débito');

DECLARE @ifSureste BIGINT, @ifNacional BIGINT;
INSERT INTO dbo.instituciones_financieras (nombre, clave_institucion, codigo_internacional, telefono)
VALUES (N'Banco Demo del Sureste', N'901', N'BDSUMXM1', N'8000000001');
SET @ifSureste = SCOPE_IDENTITY();
INSERT INTO dbo.instituciones_financieras (nombre, clave_institucion, codigo_internacional, telefono)
VALUES (N'Banco Demo Nacional', N'902', NULL, NULL);
SET @ifNacional = SCOPE_IDENTITY();
INSERT INTO dbo.instituciones_financieras (nombre, clave_institucion, codigo_internacional, telefono)
VALUES (N'Emisor internacional (demo)', NULL, NULL, NULL);   -- prueba de los índices únicos filtrados

INSERT INTO dbo.estados_pago (nombre, descripcion, es_final)
VALUES (N'PENDIENTE',   N'En espera de respuesta del gateway', 0),
       (N'APROBADO',    N'Cobro autorizado', 0),
       (N'RECHAZADO',   N'Cobro rechazado', 1),
       (N'REEMBOLSADO', N'Cobro devuelto al cliente', 1);

DECLARE @epPendiente SMALLINT = (SELECT id FROM dbo.estados_pago WHERE nombre = N'PENDIENTE');
DECLARE @epAprobado  SMALLINT = (SELECT id FROM dbo.estados_pago WHERE nombre = N'APROBADO');

-- Tarjetas TOKENIZADAS (R5): token falso del gateway + últimos 4. Nunca número completo ni CVV.
DECLARE @tCliente BIGINT, @tAna BIGINT;
INSERT INTO dbo.tarjetas_usuarios (id_usuariog, id_institucion, token_referencia, marca, ultimos4, mes_expiracion, anio_expiracion, alias)
VALUES (@uCliente, @ifSureste, N'tok_demo_cliente_0001', N'VISA', N'4242', 12, 2029, N'Mi débito');
SET @tCliente = SCOPE_IDENTITY();
INSERT INTO dbo.tarjetas_usuarios (id_usuariog, id_institucion, token_referencia, marca, ultimos4, mes_expiracion, anio_expiracion, alias)
VALUES (@uAna, @ifNacional, N'tok_demo_ana_0001', N'MASTERCARD', N'4444', 8, 2028, N'Personal');
SET @tAna = SCOPE_IDENTITY();

/* =====================================================================================
   Reservaciones
   ===================================================================================== */
DECLARE @r1 BIGINT, @r2 BIGINT, @r3 BIGINT, @pago1 BIGINT, @pago2 BIGINT, @pago3 BIGINT, @reemb BIGINT;
DECLARE @creada1 DATETIME = DATEADD(DAY, -20, @ahora);
DECLARE @creada2 DATETIME = DATEADD(DAY, -6, @ahora);
DECLARE @creada3 DATETIME = DATEADD(DAY, -4, @ahora);

-- FV-260901 · Ana · CONFIRMADA · ejemplo del doc 06 (Cancún 12 al 15 de diciembre de 2026).
INSERT INTO dbo.reservaciones (id_usuarioglobal, id_estado_reservacion, codigo_reserva, subtotal, descuento_total, total, creada_en)
VALUES (@uAna, @erConfirmada, N'FV-260901', 9330.00, 933.00, 8397.00, @creada1);
SET @r1 = SCOPE_IDENTITY();
INSERT INTO dbo.detalle_reservacion (id_reservacion, id_servicio, id_promocion, cantidad, fecha_inicio, fecha_fin, precio_unitario, descuento_aplicado, subtotal, descripcion_snapshot)
VALUES (@r1, @sHotelCun, @prBienvenida, 3, '2026-12-12T15:00:00', '2026-12-15T12:00:00', 2450.00, 735.00, 7350.00, N'Hotel Playa Azul, Hab. doble — 3 noches'),
       (@r1, @sAutoCun,  @prBienvenida, 3, '2026-12-12T10:00:00', '2026-12-15T10:00:00',  660.00, 198.00, 1980.00, N'Renta Aveo automático — 3 días');

-- FV-260902 · Cliente Prueba · CONFIRMADA · viaje en 2 días (sirve para probar la etiqueta URGENTE del bot).
INSERT INTO dbo.reservaciones (id_usuarioglobal, id_estado_reservacion, codigo_reserva, subtotal, descuento_total, total, creada_en)
VALUES (@uCliente, @erConfirmada, N'FV-260902', 13000.00, 1300.00, 11700.00, @creada2);
SET @r2 = SCOPE_IDENTITY();
INSERT INTO dbo.detalle_reservacion (id_reservacion, id_servicio, id_promocion, cantidad, fecha_inicio, fecha_fin, precio_unitario, descuento_aplicado, subtotal, descripcion_snapshot)
VALUES (@r2, @sPaqChis, @prBienvenida, 2,
        DATEADD(HOUR, 9, CAST(DATEADD(DAY, 2, @hoy) AS DATETIME)), DATEADD(HOUR, 18, CAST(DATEADD(DAY, 4, @hoy) AS DATETIME)),
        6500.00, 1300.00, 13000.00, N'Chiapas mágico 3 días — 2 personas');

-- FV-260903 · Ana · CANCELADA · con reembolso SOLICITADO.
INSERT INTO dbo.reservaciones (id_usuarioglobal, id_estado_reservacion, codigo_reserva, subtotal, descuento_total, total, creada_en)
VALUES (@uAna, @erCancelada, N'FV-260903', 2400.00, 0, 2400.00, @creada3);
SET @r3 = SCOPE_IDENTITY();
INSERT INTO dbo.detalle_reservacion (id_reservacion, id_servicio, id_promocion, cantidad, fecha_inicio, fecha_fin, precio_unitario, descuento_aplicado, subtotal, descripcion_snapshot)
VALUES (@r3, @sHotelScl, NULL, 2,
        DATEADD(HOUR, 14, CAST(DATEADD(DAY, 20, @hoy) AS DATETIME)), DATEADD(HOUR, 11, CAST(DATEADD(DAY, 22, @hoy) AS DATETIME)),
        1200.00, 0, 2400.00, N'Hotel Colonial Jovel, Hab. sencilla — 2 noches');

-- Bitácora de estados (R6). id_usuario_cambio NULL = automático (p. ej. al confirmarse el pago).
INSERT INTO dbo.historial_estado_reservacion (id_reservacion, id_estado_reservacion, id_usuario_cambio, fecha_cambio, motivo)
VALUES (@r1, @erPendiente,  NULL, @creada1,                    N'Reservación creada'),
       (@r1, @erConfirmada, NULL, DATEADD(MINUTE, 3, @creada1), N'Pago aprobado'),
       (@r2, @erPendiente,  NULL, @creada2,                    N'Reservación creada'),
       (@r2, @erConfirmada, NULL, DATEADD(MINUTE, 2, @creada2), N'Pago aprobado'),
       (@r3, @erPendiente,  NULL, @creada3,                    N'Reservación creada'),
       (@r3, @erConfirmada, NULL, DATEADD(MINUTE, 4, @creada3), N'Pago aprobado'),
       (@r3, @erCancelada,  @uAna, DATEADD(DAY, 1, @creada3),  N'Cancelada por el cliente desde Mis viajes');

/* =====================================================================================
   Pagos, reembolso y bitácoras
   ===================================================================================== */
INSERT INTO dbo.pagos (id_reservacion, id_metodo_pago, id_tarjeta, id_estado_pago, monto, referencia_externa, fecha_pago, creado_en)
VALUES (@r1, @mpCredito, @tAna, @epAprobado, 8397.00, N'ch_demo_260901', DATEADD(MINUTE, 3, @creada1), @creada1);
SET @pago1 = SCOPE_IDENTITY();
INSERT INTO dbo.pagos (id_reservacion, id_metodo_pago, id_tarjeta, id_estado_pago, monto, referencia_externa, fecha_pago, creado_en)
VALUES (@r2, @mpDebito, @tCliente, @epAprobado, 11700.00, N'ch_demo_260902', DATEADD(MINUTE, 2, @creada2), @creada2);
SET @pago2 = SCOPE_IDENTITY();
INSERT INTO dbo.pagos (id_reservacion, id_metodo_pago, id_tarjeta, id_estado_pago, monto, referencia_externa, fecha_pago, creado_en)
VALUES (@r3, @mpCredito, @tAna, @epAprobado, 2400.00, N'ch_demo_260903', DATEADD(MINUTE, 4, @creada3), @creada3);
SET @pago3 = SCOPE_IDENTITY();

INSERT INTO dbo.historial_estado_pago (id_pago, id_estado_pago, fecha_cambio, detalle)
VALUES (@pago1, @epPendiente, @creada1, N'Cobro enviado al gateway'),
       (@pago1, @epAprobado,  DATEADD(MINUTE, 3, @creada1), N'Autorizado'),
       (@pago2, @epPendiente, @creada2, N'Cobro enviado al gateway'),
       (@pago2, @epAprobado,  DATEADD(MINUTE, 2, @creada2), N'Autorizado'),
       (@pago3, @epPendiente, @creada3, N'Cobro enviado al gateway'),
       (@pago3, @epAprobado,  DATEADD(MINUTE, 4, @creada3), N'Autorizado');

INSERT INTO dbo.reembolsos (id_pago, monto, motivo, estado, fecha_solicitud)
VALUES (@pago3, 2400.00, N'Cancelación con más de 72 horas de anticipación', N'SOLICITADO', DATEADD(DAY, 1, @creada3));
SET @reemb = SCOPE_IDENTITY();

INSERT INTO dbo.historial_estado_reembolso (id_reembolso, id_usuario_cambio, estado, fecha_cambio, detalle)
VALUES (@reemb, @uAna, N'SOLICITADO', DATEADD(DAY, 1, @creada3), N'Solicitado por el cliente al cancelar');

/* =====================================================================================
   Soporte (R9): uno de cada estado
   ===================================================================================== */
INSERT INTO dbo.solicitudes_soporte (id_usuarioglobal, id_reservacion, id_agente_asignado, tipo, asunto, mensaje, estado, creada_en, actualizada_en)
VALUES
    (@uCliente, NULL, NULL, N'ASESORIA', N'Paquete para 6 personas',
     N'¿El paquete Chiapas mágico se puede contratar para 6 personas en diciembre?', N'ABIERTA',
     DATEADD(HOUR, -5, @ahora), DATEADD(HOUR, -5, @ahora)),
    (@uAna, @r3, @uAgente, N'PROBLEMA', N'Reembolso de reservación cancelada',
     N'Cancelé la reservación FV-260903 y quiero saber cuándo llega el reembolso.', N'EN_PROCESO',
     DATEADD(DAY, 1, @creada3), DATEADD(HOUR, -2, @ahora)),
    (@uCliente, @r2, @uAgente, N'ASESORIA', N'Factura de mi compra',
     N'Necesito la factura de la reservación FV-260902.', N'CERRADA',
     DATEADD(DAY, -5, @ahora), DATEADD(DAY, -4, @ahora));

COMMIT TRANSACTION;
GO

/* ---------- Verificación rápida ---------- */
SELECT 'ubicaciones' AS tabla, COUNT(*) AS filas FROM dbo.ubicaciones UNION ALL
SELECT 'proveedores', COUNT(*) FROM dbo.proveedores UNION ALL
SELECT 'servicios', COUNT(*) FROM dbo.servicios UNION ALL
SELECT 'vuelos', COUNT(*) FROM dbo.vuelos UNION ALL
SELECT 'hospedajes', COUNT(*) FROM dbo.hospedajes UNION ALL
SELECT 'vehiculos', COUNT(*) FROM dbo.vehiculos UNION ALL
SELECT 'paquete', COUNT(*) FROM dbo.paquete UNION ALL
SELECT 'paquete_servicio', COUNT(*) FROM dbo.paquete_servicio UNION ALL
SELECT 'disponibilidad_servicio', COUNT(*) FROM dbo.disponibilidad_servicio UNION ALL
SELECT 'promociones', COUNT(*) FROM dbo.promociones UNION ALL
SELECT 'reservaciones', COUNT(*) FROM dbo.reservaciones UNION ALL
SELECT 'detalle_reservacion', COUNT(*) FROM dbo.detalle_reservacion UNION ALL
SELECT 'pagos', COUNT(*) FROM dbo.pagos UNION ALL
SELECT 'reembolsos', COUNT(*) FROM dbo.reembolsos UNION ALL
SELECT 'solicitudes_soporte', COUNT(*) FROM dbo.solicitudes_soporte;
GO

-- Promociones vigentes hoy (deben salir BIENVENIDA10 y CARIBE500).
SELECT codigo, tipo_descuento, valor, minimo_compra
FROM dbo.promociones
WHERE activo = 1 AND GETDATE() BETWEEN fecha_inicio AND fecha_fin;
GO

SET NOEXEC OFF;
GO
