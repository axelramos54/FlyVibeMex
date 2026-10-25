using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using FlyVibeMex.Models;

namespace FlyVibeMex.Controllers;

/// <summary>
/// Panel del gestor de ventas: <c>/Admin/GestorVentas</c>. Las vistas están en <c>Views/Admin/GestorVentas/</c>.
/// </summary>
/// <remarks>
/// Reglas de seguridad que sigue este controlador:
/// - Los GET solo consultan; todo lo que modifica datos es POST.
/// - [AutoValidateAntiforgeryToken]: todo POST exige el token que agrega el &lt;form&gt; tag helper.
/// - Los formularios se reciben en ViewModels (solo los campos permitidos) y se validan en el servidor.
/// - El id del proveedor siempre sale de la URL y se busca en la BD; nunca se confía en un campo oculto.
/// </remarks>
[Route("Admin/GestorVentas")]
[AutoValidateAntiforgeryToken]
// TODO (cuando exista el login): [Authorize(Roles = "GestorVentas,SA")]
// NOTA: es "partial" porque el listado de proveedores y el CRUD de servicios están en
//       Controllers/GestorController.ProveedoresServicios.cs (misma clase, otro archivo).
public partial class GestorController : Controller
{
    private const string Carpeta = "~/Views/Admin/GestorVentas/";

    // Mientras no haya BD, las pantallas de un proveedor se abren con un "proveedor de prueba".
    // Cuando conectes la BD cámbialo a false: un id que no existe devolverá 404.
    private static readonly bool ModoSinBD = true;

    // Imágenes: máximo 2 MB y solo JPG / PNG / WEBP.
    private const long MaxImagenBytes = 2 * 1024 * 1024;

    private IActionResult Vista(string nombre, object? modelo = null) => View(Carpeta + nombre + ".cshtml", modelo);

    // ============================== INICIO ==============================

    // GET /Admin/GestorVentas
    [HttpGet("")]
    public IActionResult Index() => Vista("Index");

    // ============================== ALTA DE PROVEEDOR ==============================

    // GET /Admin/GestorVentas/Proveedores/Alta
    [HttpGet("Proveedores/Alta")]
    public IActionResult AltaProveedor()
    {
        CargarUbicaciones();
        return Vista("AltaProveedor", new ProveedorFormViewModel());
    }

    // POST /Admin/GestorVentas/Proveedores/Alta
    [HttpPost("Proveedores/Alta")]
    public IActionResult AltaProveedor(
        [Bind("id_ubicacion,tipo_proveedor,nombre,telefono,correo")] ProveedorFormViewModel datos)
    {
        // TODO BD: validar que teléfono y correo no existan ya (son UNIQUE) y que la ubicación exista.
        //   if (existeCorreo) ModelState.AddModelError(nameof(datos.correo), "Ya hay un proveedor con ese correo.");

        if (!ModelState.IsValid)
        {
            CargarUbicaciones();
            return Vista("AltaProveedor", datos);
        }

        // TODO BD: INSERT INTO proveedores (id_ubicacion, tipo_proveedor, nombre, telefono, correo)
        //          activo, creado_en y actualizado_en los pone la BD con sus DEFAULT.
        TempData["Exito"] = $"Los datos de \"{datos.nombre}\" son válidos. El guardado se activará al conectar la base de datos.";
        return RedirectToAction(nameof(GestionarProveedores), new { nombre = datos.nombre });
    }

    // ============================== GESTIONAR PROVEEDORES ==============================

    // GET /Admin/GestorVentas/Proveedores?nombre=xxx
    [HttpGet("Proveedores")]
    public IActionResult GestionarProveedores(string? nombre)
    {
        var vm = new GestionarProveedoresViewModel { Nombre = nombre?.Trim() };

        if (!string.IsNullOrEmpty(vm.Nombre))
        {
            if (vm.Nombre.Length > 120)
            {
                ModelState.AddModelError("nombre", "El nombre no puede tener más de 120 caracteres.");
            }
            else
            {
                vm.Buscado = true;
                vm.Resultados = BuscarProveedoresPorNombre(vm.Nombre);
            }
        }

        return Vista("GestionarProveedores", vm);
    }

    // ----- Actualizar -----

    // GET /Admin/GestorVentas/Proveedores/5/Actualizar
    [HttpGet("Proveedores/{id}/Actualizar")]
    public IActionResult Actualizar(ulong id)
    {
        var proveedor = ObtenerProveedorOPrueba(id);
        if (proveedor is null) return NotFound();

        CargarUbicaciones();
        return Vista("Actualizar", ProveedorFormViewModel.Desde(proveedor));
    }

    // POST /Admin/GestorVentas/Proveedores/5/Actualizar
    [HttpPost("Proveedores/{id}/Actualizar")]
    public IActionResult Actualizar(ulong id,
        [Bind("id_ubicacion,tipo_proveedor,nombre,telefono,correo,activo")] ProveedorFormViewModel datos)
    {
        var proveedor = ObtenerProveedorOPrueba(id);
        if (proveedor is null) return NotFound();

        datos.id = id; // el id sale de la URL, no del formulario

        // TODO BD: validar que teléfono y correo no los tenga OTRO proveedor (UNIQUE).

        if (!ModelState.IsValid)
        {
            CargarUbicaciones();
            return Vista("Actualizar", datos);
        }

        // TODO BD: UPDATE proveedores SET ..., actualizado_en = CURRENT_TIMESTAMP WHERE id = @id
        TempData["Exito"] = $"Los cambios de \"{datos.nombre}\" son válidos. Se guardarán cuando se conecte la base de datos.";
        return RedirectToAction(nameof(GestionarProveedores), new { nombre = datos.nombre });
    }

    // ----- Dar de baja (doble confirmación) -----

    // GET /Admin/GestorVentas/Proveedores/5/Baja          → confirmación 1
    // GET /Admin/GestorVentas/Proveedores/5/Baja?paso=2   → confirmación 2 (formulario POST)
    [HttpGet("Proveedores/{id}/Baja")]
    public IActionResult DarDeBaja(ulong id, int paso = 1)
    {
        var proveedor = ObtenerProveedorOPrueba(id);
        if (proveedor is null) return NotFound();

        ViewData["Paso"] = paso == 2 ? 2 : 1;
        return Vista("DarDeBaja", proveedor);
    }

    // POST /Admin/GestorVentas/Proveedores/5/Baja   → ejecuta la baja
    [HttpPost("Proveedores/{id}/Baja")]
    public IActionResult ConfirmarBaja(ulong id, bool confirmo)
    {
        var proveedor = ObtenerProveedorOPrueba(id);
        if (proveedor is null) return NotFound();

        // La segunda confirmación se valida en el servidor (no basta con un confirm() de JavaScript).
        if (!confirmo)
        {
            ModelState.AddModelError("confirmo", "Marca la casilla para confirmar la baja.");
            ViewData["Paso"] = 2;
            return Vista("DarDeBaja", proveedor);
        }

        // TODO BD: baja LÓGICA, no DELETE (la tabla servicios hace referencia al proveedor):
        //          UPDATE proveedores SET activo = 0, actualizado_en = CURRENT_TIMESTAMP WHERE id = @id
        TempData["Exito"] = $"La baja de \"{proveedor.nombre}\" fue confirmada. Se aplicará cuando se conecte la base de datos.";
        return RedirectToAction(nameof(GestionarProveedores), new { nombre = proveedor.nombre });
    }

    // ----- Asociar (hospedajes, vehículos, eventos) -----

    // GET /Admin/GestorVentas/Proveedores/5/Asociar?tipo=hospedaje
    [HttpGet("Proveedores/{id}/Asociar")]
    public IActionResult Asociar(ulong id, string? tipo)
    {
        var proveedor = ObtenerProveedorOPrueba(id);
        if (proveedor is null) return NotFound();

        tipo = AsociarViewModel.Normalizar(tipo);   // solo hospedaje | vehiculo | evento
        ServicioFormBase? formulario = tipo switch
        {
            "hospedaje" => new HospedajeFormViewModel(),
            "vehiculo" => new VehiculoFormViewModel(),
            "evento" => new EventoFormViewModel(),
            _ => null
        };

        return VistaAsociar(proveedor, tipo, formulario);
    }

    // POST /Admin/GestorVentas/Proveedores/5/Asociar/Hospedaje
    [HttpPost("Proveedores/{id}/Asociar/Hospedaje")]
    [RequestSizeLimit(5_000_000)]
    public IActionResult AsociarHospedaje(ulong id, HospedajeFormViewModel datos)
    {
        var proveedor = ObtenerProveedorOPrueba(id);
        if (proveedor is null) return NotFound();

        ValidarImagen(datos.imagen, nameof(datos.imagen));
        if (datos.hora_entrada is not null && datos.hora_salida is null)
            ModelState.AddModelError(nameof(datos.hora_salida), "Si indicas hora de entrada, indica también la de salida.");

        if (!ModelState.IsValid) return VistaAsociar(proveedor, "hospedaje", datos);

        // TODO BD (en una transacción):
        //   1) INSERT servicios (id_proveedor = @id, tipo_servicio = 'HOSPEDAJE', nombre, descripcion, precio_base, moneda)
        //   2) INSERT hospedajes (...) con la imagen leída de datos.imagen
        return ExitoAsociacion(id, proveedor, "hospedaje", datos.nombre);
    }

    // POST /Admin/GestorVentas/Proveedores/5/Asociar/Vehiculo
    [HttpPost("Proveedores/{id}/Asociar/Vehiculo")]
    [RequestSizeLimit(5_000_000)]
    public IActionResult AsociarVehiculo(ulong id, VehiculoFormViewModel datos)
    {
        var proveedor = ObtenerProveedorOPrueba(id);
        if (proveedor is null) return NotFound();

        ValidarImagen(datos.imagen, nameof(datos.imagen));

        if (!ModelState.IsValid) return VistaAsociar(proveedor, "vehiculo", datos);

        // TODO BD (en una transacción):
        //   1) INSERT servicios (id_proveedor = @id, tipo_servicio = 'VEHICULO', ...)
        //   2) INSERT vehiculos (...)
        return ExitoAsociacion(id, proveedor, "vehiculo", datos.nombre);
    }

    // POST /Admin/GestorVentas/Proveedores/5/Asociar/Evento
    [HttpPost("Proveedores/{id}/Asociar/Evento")]
    public IActionResult AsociarEvento(ulong id, EventoFormViewModel datos)
    {
        var proveedor = ObtenerProveedorOPrueba(id);
        if (proveedor is null) return NotFound();

        if (datos.fecha is not null && datos.fecha.Value.Date < DateTime.Today)
            ModelState.AddModelError(nameof(datos.fecha), "La fecha del evento no puede ser en el pasado.");

        if (!ModelState.IsValid) return VistaAsociar(proveedor, "evento", datos);

        // TODO BD (en una transacción):
        //   1) INSERT servicios (id_proveedor = @id, tipo_servicio = 'EVENTO', ...)
        //   2) INSERT disponibilidad_servicio (id_servicio, fecha, cantidad_total, cantidad_disponible = cantidad_total, precio_fecha)
        return ExitoAsociacion(id, proveedor, "evento", datos.nombre);
    }

    // ============================== PAQUETES Y PROMOCIONES ==============================
    // Pantallas pendientes: por ahora muestran un aviso de "en construcción".

    [HttpGet("Paquetes/Crear")]
    public IActionResult CrearPaquete() => EnConstruccion("Crear paquete");

    [HttpGet("Paquetes")]
    public IActionResult GestionarPaquetes() => EnConstruccion("Gestionar paquetes");

    [HttpGet("Promociones/Crear")]
    public IActionResult Promociones() => EnConstruccion("Promociones");

    [HttpGet("Promociones")]
    public IActionResult GestionarPromociones() => EnConstruccion("Gestionar promociones");

    // ============================== AYUDANTES ==============================

    private IActionResult EnConstruccion(string titulo)
    {
        ViewData["Title"] = titulo;
        return Vista("EnConstruccion");
    }

    private IActionResult VistaAsociar(Proveedores proveedor, string? tipo, ServicioFormBase? formulario)
    {
        if (formulario is not null) formulario.id_proveedor = proveedor.id;
        CargarUbicaciones();

        var vm = new AsociarViewModel
        {
            Proveedor = proveedor,
            Tipo = tipo,
            Formulario = formulario,
            ServiciosAsociados = tipo is null ? new List<Servicios>() : ObtenerServiciosAsociados(proveedor.id, tipo)
        };
        return Vista("Asociar", vm);
    }

    private IActionResult ExitoAsociacion(ulong id, Proveedores proveedor, string tipo, string nombreServicio)
    {
        TempData["Exito"] = $"Los datos de \"{nombreServicio}\" para {proveedor.nombre} son válidos. " +
                            "Se guardarán cuando se conecte la base de datos.";
        return RedirectToAction(nameof(Asociar), new { id, tipo });
    }

    /// <summary>Valida tamaño y tipo REAL de la imagen (revisa los primeros bytes, no solo la extensión).</summary>
    private void ValidarImagen(IFormFile? imagen, string campo)
    {
        if (imagen is null || imagen.Length == 0) return;

        if (imagen.Length > MaxImagenBytes)
        {
            ModelState.AddModelError(campo, "La imagen no debe pesar más de 2 MB.");
            return;
        }

        Span<byte> cabecera = stackalloc byte[12];
        int leidos;
        using (var stream = imagen.OpenReadStream())
        {
            leidos = stream.ReadAtLeast(cabecera, cabecera.Length, throwOnEndOfStream: false);
        }

        bool esJpg = leidos >= 3 && cabecera[0] == 0xFF && cabecera[1] == 0xD8 && cabecera[2] == 0xFF;
        bool esPng = leidos >= 4 && cabecera[0] == 0x89 && cabecera[1] == 0x50 && cabecera[2] == 0x4E && cabecera[3] == 0x47;
        bool esWebp = leidos >= 12 && cabecera[0] == 'R' && cabecera[1] == 'I' && cabecera[2] == 'F' && cabecera[3] == 'F'
                                   && cabecera[8] == 'W' && cabecera[9] == 'E' && cabecera[10] == 'B' && cabecera[11] == 'P';

        if (!esJpg && !esPng && !esWebp)
            ModelState.AddModelError(campo, "Formato no permitido. Usa una imagen JPG, PNG o WEBP.");
    }

    private Proveedores? ObtenerProveedorOPrueba(ulong id)
    {
        var proveedor = ObtenerProveedor(id);
        if (proveedor is null && ModoSinBD)
        {
            ViewData["SinBD"] = true;
            proveedor = new Proveedores
            {
                id = id,
                nombre = "Proveedor de prueba",
                tipo_proveedor = "OTRO",
                telefono = "0000000000",
                correo = "prueba@flyvibemex.com",
                activo = true
            };
        }
        return proveedor;
    }

    private void CargarUbicaciones()
    {
        ViewData["Ubicaciones"] = ObtenerUbicaciones()
            .Select(u => new SelectListItem($"{u.municipio_ciudad}, {u.estado}, {u.pais}", u.id.ToString()))
            .ToList();
    }

    // ===================== Acceso a datos (pendiente de conectar a la BD) =====================
    // Cuando conectes la BD solo cambias estos métodos. Usa SIEMPRE consultas parametrizadas
    // (EF Core lo hace solo); nunca armes el SQL concatenando lo que escribió el usuario.

    private List<Proveedores> BuscarProveedoresPorNombre(string nombre)
    {
        // TODO BD: _db.Proveedores.Where(p => p.nombre.Contains(nombre)).OrderBy(p => p.nombre).Take(20).ToList();
        // Sin BD busca entre los proveedores de prueba (ver GestorController.ProveedoresServicios.cs).
        return ObtenerProveedores(nombre, null, incluirInactivos: true).Take(20).ToList();
    }

    private Proveedores? ObtenerProveedor(ulong id)
    {
        // TODO BD: _db.Proveedores.Find(id);
        if (!ModoSinBD) return null;
        ViewData["SinBD"] = true;
        return ProveedoresDePrueba().FirstOrDefault(p => p.id == id);
    }

    private List<Ubicaciones> ObtenerUbicaciones()
    {
        // TODO BD: _db.Ubicaciones.Where(u => u.activo).OrderBy(u => u.municipio_ciudad).ToList();
        return new List<Ubicaciones>();
    }

    private List<Servicios> ObtenerServiciosAsociados(ulong idProveedor, string tipo)
    {
        // TODO BD: _db.Servicios.Where(s => s.id_proveedor == idProveedor
        //                                && s.tipo_servicio == AsociarViewModel.TipoServicio(tipo)).ToList();
        return new List<Servicios>();
    }
}
