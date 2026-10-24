using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using FlyVibeMex.Models;

namespace FlyVibeMex.Controllers;

/// <summary>
/// Segunda parte de <see cref="GestorController"/> (<c>/Admin/GestorVentas</c>):
/// listado general de proveedores y alta / baja / actualización de servicios.
/// </summary>
/// <remarks>
/// Hereda de la otra parte de la clase el atributo [Route("Admin/GestorVentas")],
/// [AutoValidateAntiforgeryToken], <c>ModoSinBD</c> y los ayudantes <c>Vista</c>, <c>ObtenerProveedorOPrueba</c>…
/// Mismas reglas: los GET solo consultan, todo lo que modifica datos es POST, y el id siempre sale de la URL.
/// Las vistas están en Views/Admin/GestorVentas/ (Proveedores, Servicios, AltaServicio, ActualizarServicio,
/// DarDeBajaServicio y los parciales _ServicioFormCampos / _ServicioResumen).
/// </remarks>
public partial class GestorController
{
    // ============================== LISTADO DE PROVEEDORES ==============================

    // GET /Admin/GestorVentas/Proveedores/Lista?nombre=&tipo=&incluirInactivos=true
    [HttpGet("Proveedores/Lista")]
    public IActionResult ListaProveedores(string? nombre, string? tipo, bool incluirInactivos = false)
    {
        var vm = new ListaProveedoresViewModel
        {
            Nombre = Recortar(nombre, 120),
            Tipo = ListaProveedoresViewModel.NormalizarTipo(tipo),
            IncluirInactivos = incluirInactivos
        };

        vm.Resultados = ObtenerProveedores(vm.Nombre, vm.Tipo, vm.IncluirInactivos);
        return Vista("Proveedores", vm);
    }

    // POST /Admin/GestorVentas/Proveedores/5/Reactivar   → deshace una baja lógica
    [HttpPost("Proveedores/{id}/Reactivar")]
    public IActionResult ReactivarProveedor(ulong id)
    {
        var proveedor = ObtenerProveedorOPrueba(id);
        if (proveedor is null) return NotFound();

        // TODO BD: UPDATE proveedores SET activo = 1, actualizado_en = CURRENT_TIMESTAMP WHERE id = @id
        //   _db.Proveedores.Where(p => p.id == id).ExecuteUpdate(s => s
        //        .SetProperty(p => p.activo, true).SetProperty(p => p.actualizado_en, DateTime.Now));
        TempData["Exito"] = $"\"{proveedor.nombre}\" se reactivará cuando se conecte la base de datos.";
        return RedirectToAction(nameof(ListaProveedores), new { incluirInactivos = true });
    }

    // ============================== LISTADO DE SERVICIOS ==============================

    // GET /Admin/GestorVentas/Servicios?nombre=&tipo=&idProveedor=&incluirInactivos=true
    [HttpGet("Servicios")]
    public IActionResult ListaServicios(string? nombre, string? tipo, ulong? idProveedor, bool incluirInactivos = false)
    {
        var vm = new ListaServiciosViewModel
        {
            Nombre = Recortar(nombre, 150),
            Tipo = ServicioFormViewModel.NormalizarTipo(tipo),
            IdProveedor = idProveedor is > 0 ? idProveedor : null,
            IncluirInactivos = incluirInactivos
        };

        vm.Resultados = ObtenerServicios(vm.Nombre, vm.Tipo, vm.IdProveedor, vm.IncluirInactivos);
        vm.NombresProveedor = ObtenerProveedores(null, null, incluirInactivos: true)
            .ToDictionary(p => p.id, p => p.nombre);

        CargarProveedores(soloActivos: false);
        return Vista("Servicios", vm);
    }

    // ============================== ALTA DE SERVICIO ==============================

    // GET /Admin/GestorVentas/Servicios/Alta?idProveedor=5   (idProveedor es opcional: preselecciona el proveedor)
    [HttpGet("Servicios/Alta")]
    public IActionResult AltaServicio(ulong? idProveedor)
    {
        CargarProveedores(soloActivos: true);
        return Vista("AltaServicio", new ServicioFormViewModel { id_proveedor = idProveedor is > 0 ? idProveedor : null });
    }

    // POST /Admin/GestorVentas/Servicios/Alta
    [HttpPost("Servicios/Alta")]
    public IActionResult AltaServicio(
        [Bind("id_proveedor,tipo_servicio,nombre,descripcion,precio_base,moneda")] ServicioFormViewModel datos)
    {
        ValidarProveedorDelServicio(datos.id_proveedor, nameof(datos.id_proveedor));

        if (!ModelState.IsValid)
        {
            CargarProveedores(soloActivos: true);
            return Vista("AltaServicio", datos);
        }

        // TODO BD: INSERT INTO servicios (id_proveedor, tipo_servicio, nombre, descripcion, precio_base, Moneda,
        //                                 activo, creado_en, actualizado_en)
        //   _db.Servicios.Add(new Servicios {
        //       id_proveedor = datos.id_proveedor!.Value, tipo_servicio = datos.tipo_servicio, nombre = datos.nombre.Trim(),
        //       descripcion = datos.descripcion, precio_base = datos.precio_base, moneda = datos.moneda,
        //       activo = true, creado_en = DateTime.Now, actualizado_en = DateTime.Now });   // fechas explícitas: la
        //   _db.SaveChanges();                                                               // columna es datetime
        TempData["Exito"] = $"Los datos del servicio \"{datos.nombre}\" son válidos. El guardado se activará al conectar la base de datos.";
        return RedirectToAction(nameof(ListaServicios), new { nombre = datos.nombre });
    }

    // ============================== ACTUALIZAR SERVICIO ==============================

    // GET /Admin/GestorVentas/Servicios/5/Actualizar
    [HttpGet("Servicios/{id}/Actualizar")]
    public IActionResult ActualizarServicio(ulong id)
    {
        var servicio = ObtenerServicioOPrueba(id);
        if (servicio is null) return NotFound();

        CargarProveedores(soloActivos: false);
        return Vista("ActualizarServicio", ServicioFormViewModel.Desde(servicio));
    }

    // POST /Admin/GestorVentas/Servicios/5/Actualizar
    [HttpPost("Servicios/{id}/Actualizar")]
    public IActionResult ActualizarServicio(ulong id,
        [Bind("id_proveedor,tipo_servicio,nombre,descripcion,precio_base,moneda,activo")] ServicioFormViewModel datos)
    {
        var servicio = ObtenerServicioOPrueba(id);
        if (servicio is null) return NotFound();

        datos.id = id; // el id sale de la URL, no del formulario

        // Regla R1: el subtipo (hospedajes, vehiculos, paquete…) cuelga de este servicio,
        // así que no se permite cambiar el tipo de un servicio que ya existe.
        if (!string.IsNullOrEmpty(servicio.tipo_servicio) && datos.tipo_servicio != servicio.tipo_servicio)
        {
            ModelState.AddModelError(nameof(datos.tipo_servicio),
                "No se puede cambiar el tipo de un servicio existente. Da de baja este y crea uno nuevo.");
        }

        // Solo se valida que el proveedor esté activo si se cambió (un servicio viejo puede tener un proveedor dado de baja).
        if (datos.id_proveedor != servicio.id_proveedor)
            ValidarProveedorDelServicio(datos.id_proveedor, nameof(datos.id_proveedor));

        if (!ModelState.IsValid)
        {
            CargarProveedores(soloActivos: false);
            return Vista("ActualizarServicio", datos);
        }

        // TODO BD: UPDATE servicios SET id_proveedor, nombre, descripcion, precio_base, Moneda, activo,
        //          actualizado_en = CURRENT_TIMESTAMP WHERE id = @id
        //   (precio_base es solo de referencia: NO cambia compras pasadas, regla R4)
        TempData["Exito"] = $"Los cambios de \"{datos.nombre}\" son válidos. Se guardarán cuando se conecte la base de datos.";
        return RedirectToAction(nameof(ListaServicios), new { nombre = datos.nombre, incluirInactivos = !datos.activo });
    }

    // ============================== DAR DE BAJA SERVICIO (doble confirmación) ==============================

    // GET /Admin/GestorVentas/Servicios/5/Baja          → confirmación 1
    // GET /Admin/GestorVentas/Servicios/5/Baja?paso=2   → confirmación 2 (formulario POST)
    [HttpGet("Servicios/{id}/Baja")]
    public IActionResult DarDeBajaServicio(ulong id, int paso = 1)
    {
        var servicio = ObtenerServicioOPrueba(id);
        if (servicio is null) return NotFound();

        ViewData["Paso"] = paso == 2 ? 2 : 1;
        return Vista("DarDeBajaServicio", DetalleServicio(servicio));
    }

    // POST /Admin/GestorVentas/Servicios/5/Baja   → ejecuta la baja
    [HttpPost("Servicios/{id}/Baja")]
    public IActionResult ConfirmarBajaServicio(ulong id, bool confirmo)
    {
        var servicio = ObtenerServicioOPrueba(id);
        if (servicio is null) return NotFound();

        // La segunda confirmación se valida en el servidor (no basta con el "required" del navegador).
        if (!confirmo)
        {
            ModelState.AddModelError("confirmo", "Marca la casilla para confirmar la baja.");
            ViewData["Paso"] = 2;
            return Vista("DarDeBajaServicio", DetalleServicio(servicio));
        }

        // TODO BD: baja LÓGICA, nunca DELETE (detalle_reservacion, paquete_servicio, promocion_servicio y
        //          disponibilidad_servicio hacen referencia al servicio):
        //          UPDATE servicios SET activo = 0, actualizado_en = CURRENT_TIMESTAMP WHERE id = @id
        TempData["Exito"] = $"La baja de \"{servicio.nombre}\" fue confirmada. Se aplicará cuando se conecte la base de datos.";
        return RedirectToAction(nameof(ListaServicios), new { incluirInactivos = true });
    }

    // POST /Admin/GestorVentas/Servicios/5/Reactivar
    [HttpPost("Servicios/{id}/Reactivar")]
    public IActionResult ReactivarServicio(ulong id)
    {
        var servicio = ObtenerServicioOPrueba(id);
        if (servicio is null) return NotFound();

        // TODO BD: UPDATE servicios SET activo = 1, actualizado_en = CURRENT_TIMESTAMP WHERE id = @id
        TempData["Exito"] = $"\"{servicio.nombre}\" se reactivará cuando se conecte la base de datos.";
        return RedirectToAction(nameof(ListaServicios), new { incluirInactivos = true });
    }

    // ============================== AYUDANTES ==============================

    private static string? Recortar(string? texto, int maximo)
    {
        var t = texto?.Trim();
        if (string.IsNullOrEmpty(t)) return null;
        return t.Length > maximo ? t[..maximo] : t;
    }

    private ServicioDetalleViewModel DetalleServicio(Servicios servicio) => new()
    {
        Servicio = servicio,
        NombreProveedor = ObtenerProveedor(servicio.id_proveedor)?.nombre
                          ?? (ModoSinBD ? "Proveedor de prueba" : $"Proveedor #{servicio.id_proveedor}")
    };

    /// <summary>El proveedor elegido debe existir y estar activo (no se asocian servicios a proveedores dados de baja).</summary>
    private void ValidarProveedorDelServicio(ulong? idProveedor, string campo)
    {
        if (idProveedor is null or 0) return; // ya lo marca [Required]

        var proveedor = ObtenerProveedor(idProveedor.Value);
        if (proveedor is null)
        {
            if (!ModoSinBD) ModelState.AddModelError(campo, "El proveedor seleccionado no existe.");
            return;
        }
        if (!proveedor.activo)
            ModelState.AddModelError(campo, "El proveedor está dado de baja. Reactívalo antes de asociarle servicios.");
    }

    /// <summary>Lista de proveedores para el &lt;select&gt; del formulario y del filtro.</summary>
    private void CargarProveedores(bool soloActivos)
    {
        ViewData["Proveedores"] = ObtenerProveedores(null, null, incluirInactivos: !soloActivos)
            .Select(p => new SelectListItem(p.activo ? p.nombre : $"{p.nombre} (inactivo)", p.id.ToString()))
            .ToList();
    }

    private Servicios? ObtenerServicioOPrueba(ulong id)
    {
        var servicio = ObtenerServicio(id);
        if (servicio is null && ModoSinBD)
        {
            ViewData["SinBD"] = true;
            servicio = ServiciosDePrueba().FirstOrDefault(s => s.id == id) ?? new Servicios
            {
                id = id,
                id_proveedor = 1,
                tipo_servicio = "HOSPEDAJE",
                nombre = "Servicio de prueba",
                descripcion = "Servicio ficticio para probar las pantallas sin base de datos.",
                precio_base = 1500m,
                moneda = "MXN",
                activo = true
            };
        }
        return servicio;
    }

    // ===================== Acceso a datos (pendiente de conectar a la BD) =====================
    // Mientras ModoSinBD = true devuelven datos de PRUEBA (el layout muestra el aviso "Modo prueba").
    // Cuando conectes la BD solo cambias estos métodos; usa LINQ (consultas parametrizadas), nunca SQL concatenado.

    private List<Proveedores> ObtenerProveedores(string? nombre, string? tipo, bool incluirInactivos)
    {
        // TODO BD:
        //   var q = _db.Proveedores.AsNoTracking().AsQueryable();
        //   if (!incluirInactivos) q = q.Where(p => p.activo);
        //   if (tipo is not null)  q = q.Where(p => p.tipo_proveedor == tipo);
        //   if (nombre is not null) q = q.Where(p => p.nombre.Contains(nombre));
        //   return q.OrderBy(p => p.nombre).Take(200).ToList();
        if (!ModoSinBD) return new List<Proveedores>();

        ViewData["SinBD"] = true;
        IEnumerable<Proveedores> q = ProveedoresDePrueba();
        if (!incluirInactivos) q = q.Where(p => p.activo);
        if (tipo is not null) q = q.Where(p => p.tipo_proveedor == tipo);
        if (nombre is not null) q = q.Where(p => p.nombre.Contains(nombre, StringComparison.OrdinalIgnoreCase));
        return q.OrderBy(p => p.nombre).ToList();
    }

    private Servicios? ObtenerServicio(ulong id)
    {
        // TODO BD: return _db.Servicios.AsNoTracking().FirstOrDefault(s => s.id == id);
        if (!ModoSinBD) return null;
        ViewData["SinBD"] = true;
        return ServiciosDePrueba().FirstOrDefault(s => s.id == id);
    }

    private List<Servicios> ObtenerServicios(string? nombre, string? tipo, ulong? idProveedor, bool incluirInactivos)
    {
        // TODO BD:
        //   var q = _db.Servicios.AsNoTracking().AsQueryable();
        //   if (!incluirInactivos)      q = q.Where(s => s.activo);
        //   if (tipo is not null)       q = q.Where(s => s.tipo_servicio == tipo);
        //   if (idProveedor is not null) q = q.Where(s => s.id_proveedor == idProveedor.Value);
        //   if (nombre is not null)     q = q.Where(s => s.nombre.Contains(nombre));
        //   return q.OrderBy(s => s.nombre).Take(200).ToList();
        if (!ModoSinBD) return new List<Servicios>();

        ViewData["SinBD"] = true;
        IEnumerable<Servicios> q = ServiciosDePrueba();
        if (!incluirInactivos) q = q.Where(s => s.activo);
        if (tipo is not null) q = q.Where(s => s.tipo_servicio == tipo);
        if (idProveedor is not null) q = q.Where(s => s.id_proveedor == idProveedor.Value);
        if (nombre is not null) q = q.Where(s => s.nombre.Contains(nombre, StringComparison.OrdinalIgnoreCase));
        return q.OrderBy(s => s.nombre).ToList();
    }

    // ----- Datos de prueba (solo se usan con ModoSinBD = true; bórralos al conectar la BD) -----

    private static List<Proveedores> ProveedoresDePrueba() => new()
    {
        new() { id = 1, nombre = "Hotel Selva Maya (prueba)", tipo_proveedor = "HOTEL", telefono = "9161234567", correo = "contacto@selvamaya.test", activo = true, creado_en = new DateTime(2026, 9, 1) },
        new() { id = 2, nombre = "Aerolínea Sureste (prueba)", tipo_proveedor = "AEROLINEA", telefono = "9617654321", correo = "ventas@aerosureste.test", activo = true, creado_en = new DateTime(2026, 9, 3) },
        new() { id = 3, nombre = "Rentadora Chiapas (prueba)", tipo_proveedor = "RENTADORA", telefono = "9615550101", correo = "renta@chiapas.test", activo = false, creado_en = new DateTime(2026, 9, 10) }
    };

    private static List<Servicios> ServiciosDePrueba() => new()
    {
        new() { id = 1, id_proveedor = 1, tipo_servicio = "HOSPEDAJE", nombre = "Cabaña en Palenque (prueba)", descripcion = "Cabaña rústica para 2 personas.", precio_base = 1850m, moneda = "MXN", activo = true },
        new() { id = 2, id_proveedor = 2, tipo_servicio = "VUELO", nombre = "Vuelo TGZ - CUN (prueba)", descripcion = "Vuelo sencillo Tuxtla Gutiérrez → Cancún.", precio_base = 2390m, moneda = "MXN", activo = true },
        new() { id = 3, id_proveedor = 3, tipo_servicio = "VEHICULO", nombre = "Sedán compacto (prueba)", descripcion = "Renta por día.", precio_base = 650m, moneda = "MXN", activo = false }
    };
}
