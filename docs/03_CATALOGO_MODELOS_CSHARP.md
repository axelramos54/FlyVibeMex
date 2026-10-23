# 03 · Catálogo de modelos C\# (`Models/`)

> **Para quién es este documento:** quien vaya a usar o modificar las clases de `Models/` (controladores, ViewModels, servicios).

> **Alcance:** solo las clases C\#. La implementación física de la base de datos se documentará en una etapa posterior.

## Índice

1. [Panorama](#1-panorama)

2. [Convenciones actuales de las clases](#2-convenciones-actuales-de-las-clases)

3. [Catálogo por dominio](#3-catálogo-por-dominio)

4. [Diagrama de relaciones lógicas](#4-diagrama-de-relaciones-lógicas)

5. [Clases que faltan](#5-clases-que-faltan)

6. [Pendientes de estandarización](#6-pendientes-de-estandarización)

## 1. Panorama

| Dato | Valor |
| - | - |
| Clases de entidad | **24** |
| Otras clases | `ErrorViewModel` (plantilla ASP.NET) |
| Lógica de negocio en las clases | Ninguna (solo propiedades) |
| Propiedades de navegación | Ninguna (las relaciones son solo por identificadores) |
| Atributos de validación | Ninguno |
| ¿Algún controlador las usa? | Todavía no |
| Namespaces | **3 distintos**: `FlyVibeMex.Models` (4 archivos), `fvm` (13), `fvm.models` (8) |


Cada clase tiene ya comentarios XML (`/// \\\<summary\\\>`) con la regla de negocio que le aplica y comentarios `// FIXME:` donde hay inconsistencias.

## 2. Convenciones actuales de las clases

| Elemento | Convención actual | Ejemplo |
| - | - | - |
| Archivo | snake\_case | `detalle\\\_reservacion.cs` |
| Clase | PascalCase, casi siempre en plural | `DetallesReservacion` |
| Propiedades | snake\_case, igual que el campo del diseño de datos | `id\\\_reservacion`, `precio\\\_unitario` |
| Llave primaria | `id` | `public ulong id \\\{get; set;\\\}` |
| Llaves foráneas | `id\\\_\\\<entidad\\\>` | `id\\\_usuario`, `id\\\_servicio` |
| Textos | `string` inicializado con `string.Empty` | `public string nombre \\\{get; set;\\\} = string.Empty;` |
| Imágenes | `byte\\\[\\\]?` | `public byte\\\[\\\]? imagen \\\{get; set;\\\}` |


**Tipos C\# usados**

| Para… | Tipo C\# |
| - | - |
| Identificadores grandes | `ulong` |
| Identificadores de catálogos pequeños | `ushort` |
| Números pequeños (mes, capacidad) | `byte` |
| Cantidades enteras | `int` |
| Dinero y cantidades con decimales | `decimal` |
| Indicadores sí/no | `bool` |
| Fecha y hora | `DateTime` |
| Solo fecha | `DateOnly` |
| Solo hora | `TimeOnly` |
| Texto | `string` |
| Binarios (imágenes) | `byte\\\[\\\]?` |


> **NOTA:** los tipos sin signo (`ulong`, `ushort`) deberán revisarse cuando se defina la capa de acceso a datos (etapa posterior).

## 3. Catálogo por dominio

Leyenda de namespace: ✅ `FlyVibeMex.Models` (correcto) · ⚠️ `fvm` · ⚠️ `fvm.models`

### 3.1 Usuarios

| Archivo | Clase | Namespace | Representa | Propiedades clave | Notas |
| - | - | - | - | - | - |
| `roles\\\_usuario.cs` | `RolesUsuario` | ✅ | Roles de acceso (CLIENTE, AGENTE, ADMINISTRADOR) | `id`, `nombre` (único), `activo` | `descripcion` debería ser opcional (`string?`) |
| `usuario.cs` | `Usuarios` | ✅ | Cuentas de clientes, agentes y administradores | `id`, `id\\\_rol` → RolesUsuario, `correo` (login), `password\\\_hash` (R10), `activo`, `creado\\\_en` | Falta `actualizado\\\_en`; `imagen` agregada por el equipo |
| `ubicaciones.cs` | `Ubicaciones` | ✅ | Direcciones reutilizables (proveedores, hospedajes, vuelos, retiro de vehículos) | `id`, `pais`, `estado`, `municipio\\\_ciudad`, `codigo\\\_postal`, `activo` | Faltan `colonia`, `calle`, `numero\\\_exterior`, `numero\\\_interior`, `referencias`; `correo` sobra; `creado`/`actualizado` son `string` y deberían ser `DateTime` |


### 3.2 Catálogo / Servicios

| Archivo | Clase | Namespace | Representa | Propiedades clave | Notas |
| - | - | - | - | - | - |
| `proveedores.cs` | `Proveedores` | ✅ | Aerolíneas, hoteles, rentadoras y otros | `id`, `id\\\_ubicacion` → Ubicaciones, `tipo\\\_proveedor` (AEROLINEA, HOTEL, RENTADORA, OTRO), `activo` | `id\\\_ubicacion` debería ser opcional |
| `servicios.cs` | `Servicios` | ✅ | **Supertipo** de todo lo reservable (R1) | `id`, `id\\\_proveedor` → Proveedores, `tipo\\\_servicio` (VUELO, HOSPEDAJE, VEHICULO, PAQUETE), `precio\\\_base`, `moneda` | No existe todavía el subtipo `Vuelos` |
| `hospedajes.cs` | `Hospedajes` | ✅ | **Subtipo** HOSPEDAJE | `id`, `id\\\_ubicacion`, `tipo\\\_ubicacion`, `capacidad\\\_huespedes`, `hora\\\_entrada`, `hora\\\_salida` | Debería identificarse por `id\\\_servicio` (ligado 1:1 a su servicio); `tipo\\\_ubicacion` debería ser `tipo\\\_habitacion` |
| `vehiculos.cs` | `Vehiculos` | ✅ | **Subtipo** VEHICULO | `id`, `id\\\_ubicacion\\\_retiro`, `categoria`, `marca`, `modelo`, `capacidad\\\_pasajeros` | Debería identificarse por `id\\\_servicio`; falta `transmision` |
| `paquete.cs` | `Paquetes` | ✅ | **Subtipo** PAQUETE (R2) | `id`, `id\\\_servicio`, `duracion\\\_dias`, `min\\\_personas`, `max\\\_personas`, `politica\\\_cancelacion` | `id` sobra (la identidad es `id\\\_servicio`); `max\\\_personas` y `politica\\\_cancelacion` deberían ser opcionales |
| `paquete\\\_servicio.cs` | `PaqueteServicio` | ✅ | Servicios incluidos en un paquete (R2) | `id`, `id\\\_servicio`, `id\\\_servicio\\\_incluido`, `cantidad`, `orden` | El diseño usa la pareja (`id\\\_paquete`, `id\\\_servicio\\\_incluido`) |
| `disponibilidad\\\_servicio.cs` | `DisponibilidadServicio` | ✅ | Inventario y precio **por fecha** (R3) | `id`, `id\\\_servicio`, `fecha`, `cantidad\\\_total`, `cantidad\\\_disponible`, `precio\\\_fecha` | — |


### 3.3 Promociones

| Archivo | Clase | Namespace | Representa | Propiedades clave | Notas |
| - | - | - | - | - | - |
| `promociones.cs` | `Promociones` | ✅ | Promociones con vigencia (R13) | `id`, `codigo` (único), `tipo\\\_descuento` (PORCENTAJE, MONTO), `valor`, `fecha\\\_inicio`, `fecha\\\_fin`, `activo` | Faltan `nombre` y `minimo\\\_compra` |
| `promocion\\\_servicio.cs` | `PromocionServicio` | ✅ | Servicios a los que aplica cada promoción | `id\\\_promocion`, `id\\\_servicio` (clave compuesta) | — |


### 3.4 Reservaciones

| Archivo | Clase | Namespace | Representa | Propiedades clave | Notas |
| - | - | - | - | - | - |
| `estados\\\_reservacion.cs` | `EstadosReservacion` | ✅ | Catálogo de estados de una reserva (R6) | `id`, `nombre`, `es\\\_final` | — |
| `reservaciones.cs` | `Reservaciones` | ✅ | Encabezado de la reserva, con totales históricos (R4) | `id`, `id\\\_usuario`, `id\\\_estado\\\_reservacion`, `codigo\\\_reserva` (único), `subtotal`, `descuento\\\_total`, `total` | Falta `actualizada\\\_en` |
| `detalle\\\_reservacion.cs` | `DetallesReservacion` | ✅ | Líneas de la reserva como **snapshot** (R4) | `id`, `id\\\_reservacion`, `id\\\_servicio`, `id\\\_promocion`, `cantidad`, `precio\\\_unitario`, `descuento\\\_aplicado`, `subtotal`, `descripcion\\\_snapshot` | Errata **`fehca\\\_inicio`**; `id\\\_promocion`, fechas deberían ser opcionales |


### 3.5 Pagos y reembolsos

| Archivo | Clase | Namespace | Representa | Propiedades clave | Notas |
| - | - | - | - | - | - |
| `metodos\\\_pago.cs` | `MetodosPago` | ✅ | Formas de pago aceptadas (R7) | `id`, `nombre`, `activo` | — |
| `instituciones\\\_financieras.cs` | `InstitucionesFinancieras` | ✅ | Bancos emisores (R7) | `id`, `nombre`, `clave\\\_institucion`, `codigo\\\_internacional` | Claves y teléfono deberían ser opcionales |
| `tarjetas\\\_usuarios.cs` | `TarjetasUsuarios` | ✅ | Tarjetas **tokenizadas** (R5) | `id`, `id\\\_usuario`, `token\\\_referencia`, `marca`, `ultimos4`, `mes\\\_expiracion`, `anio\\\_expiracion` | ⛔ Nunca agregar PAN ni CVV; `id\\\_institucion` y `alias` deberían ser opcionales |
| `estados\\\_pago.cs` | `EstadosPago` | ✅ | Catálogo de estados de un pago (R6) | `id`, `nombre`, `es\\\_final` | `es\\\_final` es `byte`; debería ser `bool` como en `EstadosReservacion` |
| `pagos.cs` | `Pagos` | ✅ | Intentos/transacciones de pago (R7) | `id`, `id\\\_reservacion`, `id\\\_metodo\\\_pago`, `id\\\_tarjeta`, `id\\\_estado\\\_pago`, `monto`, `referencia\\\_externa` | `id\\\_tarjeta`, `referencia\\\_externa` y `fecha\\\_pago` deberían ser opcionales |
| `historial\\\_estado\\\_pagos.cs` | `HistorialEstadoPago` | ✅ | Bitácora de estados de pago (R6) | `id`, `id\\\_pago`, `id\\\_estado\\\_pago`, `fecha\\\_cambio`, `detalle` | Solo inserción |
| `reembolsos.cs` | `Reembolsos` | ✅ | Devoluciones derivadas de un pago (R8) | `id`, `id\\\_pago`, `monto`, `motivo`, `estado` (SOLICITADO, APROBADO, RECHAZADO, PROCESADO) | `fecha\\\_resolucion` y `referencia\\\_externa` deberían ser opcionales |
| `historial\\\_estado\\\_reembolso.cs` | `HistorialEstadoReembolso` | ✅ | Bitácora de estados de reembolso (R6) | `id`, `id\\\_reembolso`, `id\\\_usuario\\\_cambio`, `estado`, `fecha\\\_cambio` | `id\\\_usuario\\\_cambio` debe poder ser nulo (cambio automático) |


### 3.6 Soporte

| Archivo | Clase | Namespace | Representa | Propiedades clave | Notas |
| - | - | - | - | - | - |
| `solicitudes\\\_soporte.cs` | `SolicitudesSoporte` | ✅ | Tickets unificados de asesoría y problemas (R9) | `id`, `id\\\_usuario`, `id\\\_reservacion`, `id\\\_agente\\\_asignado`, `tipo` (ASESORIA, PROBLEMA), `estado` (ABIERTA, EN\_PROCESO, CERRADA) | `id\\\_reservacion` e `id\\\_agente\\\_asignado` deberían ser opcionales. Punto de integración del futuro bot |


### 3.7 Plantilla

| Archivo | Clase | Namespace | Uso |
| - | - | - | - |
| `ErrorViewModel.cs` | `ErrorViewModel` | ✅ | Modelo de `Views/Shared/Error.cshtml` (`RequestId`, `ShowRequestId`) |


## 4. Diagrama de relaciones lógicas

Relaciones según el diseño del dominio (incluye las dos clases que faltan, marcadas con \*).

```
classDiagram    
    direction LR    
    RolesUsuario "1" --\\\> "\\\*" Usuarios    
    Ubicaciones "1" --\\\> "\\\*" Proveedores    
    Proveedores "1" --\\\> "\\\*" Servicios    
    Servicios "1" --\\\> "0..1" Vuelos\\\_pendiente    
    Servicios "1" --\\\> "0..1" Hospedajes    
    Servicios "1" --\\\> "0..1" Vehiculos    
    Servicios "1" --\\\> "0..1" Paquetes    
    Paquetes "1" --\\\> "\\\*" PaqueteServicio    
    Servicios "1" --\\\> "\\\*" DisponibilidadServicio    
    Promociones "1" --\\\> "\\\*" PromocionServicio    
    Servicios "1" --\\\> "\\\*" PromocionServicio    
    Usuarios "1" --\\\> "\\\*" Reservaciones    
    EstadosReservacion "1" --\\\> "\\\*" Reservaciones    
    Reservaciones "1" --\\\> "\\\*" DetallesReservacion    
    Reservaciones "1" --\\\> "\\\*" HistorialEstadoReservacion\\\_pendiente    
    Servicios "1" --\\\> "\\\*" DetallesReservacion    
    Usuarios "1" --\\\> "\\\*" TarjetasUsuarios    
    InstitucionesFinancieras "1" --\\\> "\\\*" TarjetasUsuarios    
    Reservaciones "1" --\\\> "\\\*" Pagos    
    MetodosPago "1" --\\\> "\\\*" Pagos    
    EstadosPago "1" --\\\> "\\\*" Pagos    
    Pagos "1" --\\\> "\\\*" HistorialEstadoPago    
    Pagos "1" --\\\> "\\\*" Reembolsos    
    Reembolsos "1" --\\\> "\\\*" HistorialEstadoReembolso    
    Usuarios "1" --\\\> "\\\*" SolicitudesSoporte
```

## 5. Clases que faltan

| Clase | Por qué hace falta | Propiedades previstas en el diseño |
| - | - | - |
| `Vuelos` | Subtipo VUELO de `Servicios` (R1). Sin ella, la sección Viajes no tiene modelo | `id\\\_servicio`, `id\\\_origen` → Ubicaciones, `id\\\_destino` → Ubicaciones, `codigo\\\_vuelo`, `fecha\\\_salida`, `fecha\\\_llegada`, `clase` |
| `HistorialEstadoReservacion` | Bitácora de estados de la reserva (R6) | `id`, `id\\\_reservacion`, `id\\\_estado\\\_reservacion`, `id\\\_usuario\\\_cambio` (opcional), `fecha\\\_cambio`, `motivo` (opcional) |


## 6. Pendientes de estandarización

Se recomienda hacerlos **en un solo cambio coordinado** (una rama y un PR), porque afectan a todo el proyecto:

- [x] 

- **Unificar namespaces** en `FlyVibeMex.Models` (21 clases están en `fvm` o `fvm.models`). Hoy `\\\_ViewImports.cshtml` solo importa `FlyVibeMex.Models`.

- [x] 

- **Corregir erratas:** `fehca\\\_inicio` → `fecha\\\_inicio` (`DetallesReservacion`); `tipo\\\_ubicacion` → `tipo\\\_habitacion` (`Hospedajes`).

- [x] 

- **Nulabilidad:** declarar como opcionales (`?`) los campos que el diseño permite vacíos (marcados con `FIXME` en cada clase).

- [x] 

- **Fechas como texto:** `Ubicaciones.creado` y `Ubicaciones.actualizado` deben ser `DateTime`.

- [x] 

- **Subtipos:** `Hospedajes`, `Vehiculos` y `Paquetes` deben identificarse por `id\\\_servicio`.

- [x] 

- **Campos faltantes:** `Usuarios.actualizado\\\_en`, `Reservaciones.actualizada\\\_en`, `Promociones.nombre` y `minimo\\\_compra`, `Vehiculos.transmision`, dirección completa en `Ubicaciones`.

- [x] 

- **Consistencia de tipos:** `EstadosPago.es\\\_final` como `bool`.

- [x] 

- **Nombres de archivo** igual al nombre de la clase (`Usuarios.cs`), o al menos consistentes entre sí (hoy hay `usuario.cs` → `Usuarios`, `historial\\\_estado\\\_pagos.cs` → `HistorialEstadoPago`). Nota Yamil: Espero haber entendido 22-10-2026.---------------

- [ ] 

- **Decidir el estilo de propiedades:** mantener snake\_case o pasar a PascalCase (ver [04 · Estándares](file:///mnt/9C0AF8FD0AF8D56C/Documentos/para%20la%20pc%20gamer%20master%20race%20XDXDXDXD/universidad/5%20semestre/programacion%20web/fvm/FlyVibeMex/docs/04_ESTANDARES_Y_HOJA_DE_RUTA_APP.md#5-decisiones-pendientes-del-equipo)).

- [ ] 

- Crear la clase **`Vuelos`** 

- [x] 

- Crear la clase **`HistorialEstadoReservacion`**.

- [ ] 

- Usar **ViewModels** para vistas y formularios en lugar de exponer estas entidades directamente (sobre todo `Usuarios`, por `password\\\_hash`, y `TarjetasUsuarios`).

