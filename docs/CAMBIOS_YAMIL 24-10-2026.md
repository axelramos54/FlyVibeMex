# Cambios: proveedores, servicios, inicio de sesión y crear usuario

Copia la carpeta `FlyVibeMex/` de este zip encima de tu proyecto (reemplaza los archivos).

## Archivos NUEVOS
- Controllers/GestorController.ProveedoresServicios.cs  — listado de proveedores + alta/baja/actualizar/reactivar servicios
- Controllers/CrearusuarioController.cs                 — registro de clientes (/Crearusuario)
- Models/Admin/ServicioViewModels.cs
- Models/Auth/CuentaViewModels.cs                        — LoginViewModel y CrearUsuarioViewModel
- Views/Admin/GestorVentas/Proveedores.cshtml            — /Admin/GestorVentas/Proveedores/Lista
- Views/Admin/GestorVentas/Servicios.cshtml              — /Admin/GestorVentas/Servicios
- Views/Admin/GestorVentas/AltaServicio.cshtml           — /Admin/GestorVentas/Servicios/Alta
- Views/Admin/GestorVentas/ActualizarServicio.cshtml     — /Admin/GestorVentas/Servicios/{id}/Actualizar
- Views/Admin/GestorVentas/DarDeBajaServicio.cshtml      — /Admin/GestorVentas/Servicios/{id}/Baja (doble confirmación)
- Views/Admin/GestorVentas/_ServicioFormCampos.cshtml
- Views/Admin/GestorVentas/_ServicioResumen.cshtml
- Views/Home/_FormSesion.cshtml                          — formulario + botones "Iniciar sesión" y "Crear nueva cuenta"
- Views/Home/IniciarSesion.cshtml                        — página completa de login (se muestra si el login falla)
- Views/Crearusuario/Index.cshtml                        — formulario "Crear nueva cuenta"
- Views/Crearusuario/Creado.cshtml                       — confirmación
- wwwroot/assets/css/style_C.css                         — estilos del registro (letra C)

## Archivos MODIFICADOS
- Controllers/GestorController.cs      — ahora es `partial`; los stubs sin BD usan datos de prueba
- Controllers/HomeController.cs        — GET/POST IniciarSesion y POST CerrarSesion (cookie)
- Controllers/Miusuariocontroller.cs   — la clase se llamaba `Miusuariocontroller`; MVC no quitaba el sufijo y la ruta /Miusuario no existía
- Program.cs                           — AddAuthentication().AddCookie(...) y app.UseAuthentication()
- Data/FlyVibeMexDbContext.cs          — quitado el conversor string<->DateTime de ubicaciones (rompía el modelo de EF) y columna `tipo_prooverdor`
- Views/Home/sesion.cshtml             — usa _FormSesion.cshtml
- Views/Admin/GestorVentas/Index.cshtml y _GestorLayout.cshtml — tarjeta y menú de Servicios / Proveedores
- wwwroot/assets/css/style_s.css       — estilos de los botones nuevos

## Pendiente / a revisar
- Views/Admin/Soporte/Proveedores.cshtml y Servicios.cshtml son copias de NuevoTicket y no tienen acción: se pueden borrar.
- Gestor de ventas sigue con `ModoSinBD = true` (datos de prueba). Para guardar en la BD hay que cambiar los métodos
  marcados con `TODO BD` (el código EF ya está escrito en el comentario).
- El DbContext mapea UsuariosGlobales a la tabla `usuarios` (BD de prueba). En el script real la tabla es `UsuariosGlobales`;
  con "DatabaseMode": "Real" el login y el registro fallarán hasta que se unifique el nombre.
