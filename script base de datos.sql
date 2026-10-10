-- script de bd min y necesario para la entrega de pweb
-- entrega 3

-- use gatito;
-- drop database FlyVideMex;

create database FlyVideMex;

use FlyVideMex;

create table ubicaciones(
	id bigint primary key identity(1,1),
	pais varchar(60) default('Mexico'),
	estado varchar(80) not null,
	municipio_ciudad varchar(100) not null,
	codigo_postal varchar(10) not null,
	correo varchar(150) not null,
	activo bit default(1),
	creado datetime default getdate(),
	actualizado datetime default getdate()
);

create table roles_usuarios(
	id bigint Primary Key identity(1,1),
	nombre varchar(30) unique,
	descripcion varchar(150) default(null),
	activo bit default(1)
);

create table usuarios(
	id bigint primary key identity(1,1),
	id_rol bigint, foreign key (id_rol) references roles_usuarios(id),
	nombre varchar(80) not null,
	apellido_paterno varchar(60) not null,
	apellido_materno varchar(60) not null,
	correo varchar(150) not null unique,
	telefono varchar(20) not null unique,
	password_hash varchar(255) not null,
	activo bit default(1),
	creado_en datetime not null default(current_timestamp),
	imagen varbinary(max)
);

create table proveedores(
	id bigint primary key identity(1,1),
	id_ubicacion bigint, foreign key (id_ubicacion) references ubicaciones(id),
	tipo_proveerdor varchar(20) not null, check(tipo_proveerdor = 'AEROLINEA' or tipo_proveerdor = 'HOTEL' or tipo_proveerdor = 'RENTADORA' or tipo_proveerdor = 'OTRO'),
	nombre varchar(120) not null,
	telefono varchar(20) not null unique,
	correo varchar(150) not null unique,
	activo bit default(1) not null,
	creado_en datetime not null default(current_timestamp), 
	actualizado_en datetime not null default(current_timestamp)
);

create table servicios(
	id bigint primary key identity(1,1),
	id_proveedor bigint, foreign key (id_proveedor) references proveedores(id),
	tipo_servicio varchar(20) not null,
	nombre varchar(150) not null,
	descripcion text default(null),
	precio_base decimal(10,2) not null default(0.00),
	Moneda varchar(3) not null default('MXN'),
	activo bit default(1) not null,
	creado_en datetime not null default(current_timestamp), 
	actualizado_en datetime not null default(current_timestamp)
);

create table hospedajes(
	id bigint primary key identity(1,1),
	id_ubicacion bigint, foreign key (id_ubicacion) references ubicaciones(id),
	tipo_ubicacion varchar(60) not null,
	categoria varchar(40),
	capacidad_huespedes int not null default(1),
	hora_entrada time default(null),
	hora_salida time,
	imagen varbinary(max)
);

create table vehiculos(
	id bigint primary key identity(1,1),
	id_ubicacion_retiro bigint, foreign key (id_ubicacion_retiro) references ubicaciones(id),
	categoria varchar(50) not null,
	marca varchar(50) not null,
	modelo varchar(60) not null,
	capacidad_pasajeros tinyint not null,
	imagen varbinary(max)
);

create table paquete(
	id_servicio bigint, foreign key (id_servicio) references servicios(id),
	id bigint primary key(id, id_servicio),
	duracion_dias smallint not null default(1),
	min_personas smallint not null default(1),
	max_personas smallint not null default(1),
	politica_cancelacion text default(null),
	imagen varbinary(max)
);

create table paquete_servicio(
	
	id BIGINT NOT NULL,
	id_servicio BIGINT NOT NULL,
	id_servicio_incluido BIGINT NOT NULL,

	PRIMARY KEY (id, id_servicio),
	FOREIGN KEY (id_servicio) REFERENCES servicios(id),
	FOREIGN KEY (id_servicio_incluido) REFERENCES servicios(id),

	cantidad decimal(8,2) not null,
	orden smallint default(null), check(orden >= 0)
);

create table disponibilidad_servicio(
	id bigint primary key identity(1,1),
	id_servicio bigint, foreign key (id_servicio) references servicios(id),
	fecha date not null,
	cantidad_total int not null default(0), check(cantidad_total >= 0),
	cantidad_disponible int not null default(0), check(cantidad_disponible >= 0),
	precio_fecha decimal(12,2) default(0.00) not null,
	actualizado_en datetime default(current_timestamp) not null
);

create table promociones(
	id bigint primary key identity(1,1),
	codigo varchar(40) not null unique,
	descripcion varchar(250) default(null),
	tipo_descuento varchar(15) not null,
	valor decimal(12,2) not null default(00.00),
	fecha_inicio datetime not null,
	fecha_fin datetime not null,
	activo bit default(1)
);

create table promocion_servicio(
    id_promocion BIGINT NOT NULL,
    id_servicio BIGINT NOT NULL,

    PRIMARY KEY (id_promocion, id_servicio),

    FOREIGN KEY (id_promocion) REFERENCES promociones(id),
    FOREIGN KEY (id_servicio) REFERENCES servicios(id)
);

create table estados_reservacion(
	id smallint primary key identity(1,1),
	nombre varchar(30) not null unique,
	descripcion varchar(150) default(null),
	es_final bit not null default(0)
);



create table reservaciones(
	id bigint primary key identity(1,1),
	id_usuario bigint not null, foreign key (id_usuario) references usuarios(id),
	id_estado_reservacion smallint, foreign key (id_estado_reservacion) references estados_reservacion(id),
	codigo_reserva varchar(30) not null unique,
	moneda char(3) default('MXN') not null,
	subtotal decimal(12,2) not null default(0.00),
	descuento_total decimal(12,2) not null default(0.00),
	total decimal(12,2) not null default(0.00),
	creada_en datetime not null default(current_timestamp)
);

create table detalle_reservacion(
	id bigint primary key identity(1,1),
	id_reservacion bigint, foreign key (id_reservacion) references reservaciones(id),
	id_servicio bigint, foreign key (id_servicio) references servicios(id),
	id_promocion bigint, foreign key (id_promocion) references promociones(id),
	cantidad decimal (8,2) not null default(1.0),
	fecha_inicio datetime default(null),
	fecha_fin datetime default(null),
	precio_unitario decimal(12,2) not null,
	descuento_aplicado decimal(12,2) not null default(0),
	subtotal decimal(12,2) not null default(0),
	descripcion_snapshot varchar(200) not null
);

create table metodos_pago(
	id smallint primary key identity(1,1),
	nombre varchar(40) not null unique,
	descripcion varchar(150) default(null),
	activo bit not null default(1)
);

create table instituciones_financieras(
	id bigint primary key identity(1,1),
	nombre	varchar(120),
	clave_institucion varchar(30) unique default(null),
	codigo_internacional varchar(30) unique default(null),
	telefono varchar(20) default(null),
	activo bit not null default(1)
);

create table tarjetas_usuarios(
	id bigint primary key identity(1,1),
	id_usuario bigint not null, foreign key (id_usuario) references usuarios(id),
	id_institucion bigint default(null), foreign key (id_institucion) references instituciones_financieras(id),
	token_referencia varchar(255) not null unique,
	marca varchar(30) not null,
	ultimos4 char(4) not null,
	mes_expiracion tinyint not null, check(mes_expiracion >= 0),
	anio_expiracion smallint not null, check(anio_expiracion >= 0),
	alias varchar(50) default(null),
	activo	bit default(1),
	creado_en datetime not null default(current_timestamp)
);

create table estados_pago(
	id smallint primary key identity(1,1),
	nombre varchar(30) not null unique,
	descripcion varchar(150) default(null),
	es_final tinyint not null
);

create table pagos(
	id bigint primary key identity(1,1),
	id_reservacion bigint not null, foreign key (id_reservacion) references reservaciones(id),
	id_metodo_pago smallint not null, foreign key (id_metodo_pago) references metodos_pago(id),
	id_tarjeta bigint default(null), foreign key (id_tarjeta) references tarjetas_usuarios(id),
	id_estado_pago smallint default(null), foreign key (id_estado_pago) references estados_pago(id),
	monto decimal(12,2) not null,
	moneda char(3) not null default('MXN'),
	referencia_externa varchar(100) unique default(null),
	fecha_pago datetime default(null),
	creado_en datetime default(current_timestamp) not null
);

create table historial_estado_pago(
	id bigint primary key identity(1,1),
	id_pago bigint not null, foreign key (id_pago) references pagos(id),
	id_estado_pago smallint, foreign key (id_estado_pago) references estados_pago(id),
	fecha_cambio datetime not null default(current_timestamp),
	detalle varchar(250) default(null)
);

create table reembolsos(
	id bigint primary key identity(1,1),
	id_pago bigint, foreign key (id_pago) references pagos(id),
	monto decimal(12,2) not null,
	motivo varchar(250) not null,
	estado varchar(20) not null default('SOLICITADO'), check(estado = 'SOLICITADO' or estado = 'APROBADO' OR estado = 'RECHAZADO' OR estado = 'PROCESADO'),
	fecha_solicitud datetime default(current_timestamp) not null,
	fecha_resolucion datetime default(null),
	referencia_externa varchar(100) default(null)
);

create table historial_estado_reembolso(
	id bigint primary key identity(1,1),
	id_reembolso bigint, foreign key (id_reembolso) references reembolsos(id),
	id_usuario_cambio bigint default(null), foreign key (id_usuario_cambio) references usuarios(id),
	estado varchar(20) not null,
	fecha_cambio datetime default(current_timestamp) not null,
	detalle varchar(250) default(null)
);

create table solicitudes_soporte(
	id bigint primary key identity(1,1),
	id_usuario bigint not null, foreign key (id_usuario) references usuarios(id),
	id_reservacion bigint default(null), foreign key (id_reservacion) references reservaciones(id),
	id_agente_asignado bigint default(null), foreign key (id_agente_asignado) references usuarios(id),
	tipo varchar(20) not null,
	asunto varchar(120) not null,
	mensaje text not null,
	estado varchar(20) not null default('ABIERTA'), check(estado = 'ABIERTA' OR estado = 'EN_PROCESO' or estado = 'CERRADA'),
	creada_en datetime not null default(current_timestamp),
	actualizada_en datetime not null default(current_timestamp)
);



-- Inserción en 'ubicaciones'
INSERT INTO ubicaciones (Pais, Estado, Municipio_ciudad, Codigo_postal, Correo)
VALUES 
('Mexico', 'Chiapas', 'Tuxtla Gutiérrez', '29000', 'contacto@hotelchiapas.com'),
('Mexico', 'Oaxaca', 'Oaxaca de Juárez', '68000', 'info@casaoaxaca.com');

-- Inserción en 'hospedaje' (usando el ID de las ubicaciones creadas)
INSERT INTO hospedaje (id_ubicacion, tipo_ubicacion, categoria, capacidad_huespedes, hora_entrada, hora_salida)
VALUES 
(1, 'Hotel', 'Boutique', 4, '15:00:00', '12:00:00'),
(1, 'Cabaña', 'Rústica', 2, '14:00:00', '11:00:00'),
(2, 'Departamento', 'Lujo', 6, '16:00:00', '11:30:00');