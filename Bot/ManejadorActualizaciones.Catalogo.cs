using System.Text;
using FlyVibeMex.Models;

namespace FlyVibeMex.Bot;

/// <summary>Pestañas de la ficha de un servicio: cada una reescribe el mismo mensaje (texto o pie de la foto).</summary>
public enum PestanaFicha
{
    Resumen,
    Detalles,
    Politica,
    Promos,
    Fechas
}

/// <summary>
/// Parte pública del bot (sin vincular): explorar el catálogo con los MISMOS datos y reglas que la web
/// (<see cref="Services.ICatalogoService"/>: solo servicios activos de proveedores activos, cupo de los próximos 30 días),
/// ficha con pestañas (resumen, detalles, política, promociones, fechas) y foto si el servicio tiene, y la ayuda
/// (contenido que edita el SA). Para comprar, el bot lleva a la web: ahí se recalcula precio y cupo.
/// </summary>
public sealed partial class ManejadorActualizaciones
{
    private const int ServiciosPorPagina = 5;
    private const string RutaExplorar = "🔎 Explorar";

    /// <summary>Orden de las secciones (igual que el menú del sitio).</summary>
    private static readonly string[] TiposCatalogo =
    {
        TipoCatalogo.Vuelo, TipoCatalogo.Hospedaje, TipoCatalogo.Vehiculo, TipoCatalogo.Evento, TipoCatalogo.Paquete
    };

    private static string Icono(string tipo) => tipo switch
    {
        TipoCatalogo.Vuelo => "✈️",
        TipoCatalogo.Hospedaje => "🏨",
        TipoCatalogo.Vehiculo => "🚗",
        TipoCatalogo.Evento => "🎟️",
        _ => "🧳"
    };

    private static string Seccion(string tipo) => $"{Icono(tipo)} {TipoCatalogo.NombreSeccion(tipo)}";

    private async Task CategoriasAsync(ContextoBot ctx, CancellationToken ct)
    {
        var teclado = new Teclado()
            .Fila((Seccion(TipoCatalogo.Vuelo), $"cat:{TipoCatalogo.Vuelo}:1"), (Seccion(TipoCatalogo.Hospedaje), $"cat:{TipoCatalogo.Hospedaje}:1"))
            .Fila((Seccion(TipoCatalogo.Vehiculo), $"cat:{TipoCatalogo.Vehiculo}:1"), (Seccion(TipoCatalogo.Evento), $"cat:{TipoCatalogo.Evento}:1"))
            .Boton(Seccion(TipoCatalogo.Paquete), $"cat:{TipoCatalogo.Paquete}:1")
            .Navegacion();

        var sb = new StringBuilder()
            .Linea(FormatoBot.Ruta(RutaExplorar))
            .Linea()
            .Linea("<b>¿Qué quieres explorar?</b>")
            .Linea("Te muestro lo disponible en los próximos 30 días con su precio \"desde\".")
            .Linea()
            .Linea("💡 También puedes escribirme un destino, por ejemplo «Cancún».");
        await MostrarAsync(ctx, sb.ToString(), teclado, ct);
    }

    private async Task ListaAsync(ContextoBot ctx, string? tipo, ulong? destino, int pagina, CancellationToken ct)
    {
        if (tipo is null || !TiposCatalogo.Contains(tipo))
        {
            await CategoriasAsync(ctx, ct);
            return;
        }

        var busqueda = new BusquedaCatalogoViewModel { tipo = tipo, destino = destino };
        await _catalogo.LlenarSeccionAsync(busqueda, aplicarFechas: false, ct);

        var seccion = TipoCatalogo.NombreSeccion(tipo);
        var nombreDestino = destino is null ? null : busqueda.Destinos.FirstOrDefault(d => d.Value == destino.ToString())?.Text;
        var resultados = busqueda.Resultados;
        var paginas = Math.Max(1, (int)Math.Ceiling(resultados.Count / (double)ServiciosPorPagina));
        pagina = Math.Clamp(pagina, 1, paginas);

        var sb = new StringBuilder().Linea(nombreDestino is null
            ? FormatoBot.Ruta(RutaExplorar, Seccion(tipo))
            : FormatoBot.Ruta(RutaExplorar, Seccion(tipo), $"📍 {FormatoBot.HtmlRecortado(nombreDestino, 40)}"));
        var teclado = new Teclado();

        if (resultados.Count == 0)
        {
            sb.Linea().Linea($"No hay {seccion.ToLowerInvariant()} disponibles{(nombreDestino is null ? "" : " en ese destino")} en los próximos 30 días.");
        }
        else
        {
            sb.Linea(paginas > 1 ? $"Página {pagina} de {paginas} · {resultados.Count} opciones" : $"{resultados.Count} opción{(resultados.Count == 1 ? "" : "es")}");
            var n = (pagina - 1) * ServiciosPorPagina;
            foreach (var r in resultados.Skip((pagina - 1) * ServiciosPorPagina).Take(ServiciosPorPagina))
            {
                n++;
                sb.Linea();
                LineasServicio(sb, n, r, mostrarSeccion: false);
                teclado.Boton($"{n}. {FormatoBot.Recortar(r.nombre, 40)}", $"srv:{r.id}:{destino ?? 0}:{pagina}");
            }

            var anterior = pagina > 1 ? Pagina(tipo, destino, pagina - 1) : null;
            var siguiente = pagina < paginas ? Pagina(tipo, destino, pagina + 1) : null;
            if (anterior is not null && siguiente is not null)
                teclado.Fila(("◀ Anterior", anterior), ("Siguiente ▶", siguiente));
            else if (anterior is not null)
                teclado.Boton("◀ Anterior", anterior);
            else if (siguiente is not null)
                teclado.Boton("Siguiente ▶", siguiente);
        }

        if (busqueda.Destinos.Count > 1 || destino is not null)
            teclado.Boton(destino is null ? "📍 Elegir destino" : "📍 Cambiar destino", $"dst:{tipo}");

        var ruta = $"/Home/{TipoCatalogo.AccionSeccion(tipo)}" + (destino is null ? "" : $"?destino={destino}");
        EnlaceWeb(teclado, sb, $"Buscar {seccion.ToLowerInvariant()} con fechas en la web", ruta);
        teclado.Navegacion("Explorar", "exp");
        await MostrarAsync(ctx, sb.ToString(), teclado, ct);
    }

    /// <summary>Renglones de un servicio en una lista (catálogo o búsqueda).</summary>
    private static void LineasServicio(StringBuilder sb, int numero, TarjetaServicioViewModel r, bool mostrarSeccion)
    {
        var precio = r.agotado
            ? "Agotado"
            : r.precio_unitario is decimal p ? $"desde {FormatoBot.Dinero(p, r.moneda)} por {r.Unidad}" : "Consulta el precio en la web";
        var seccion = mostrarSeccion ? $"{Icono(r.tipo)} " : "";
        sb.Linea($"<b>{numero}. {seccion}{FormatoBot.HtmlRecortado(r.nombre, 80)}</b>")
          .Linea($"   📍 {FormatoBot.HtmlRecortado(r.ubicacion, 80)}{(r.fecha_texto is null ? "" : " · " + FormatoBot.Html(r.fecha_texto))}")
          .Linea($"   {FormatoBot.Html(precio)}");
    }

    private static string Pagina(string tipo, ulong? destino, int pagina) =>
        destino is null ? $"cat:{tipo}:{pagina}" : $"catd:{tipo}:{destino}:{pagina}";

    private async Task DestinosAsync(ContextoBot ctx, string? tipo, CancellationToken ct)
    {
        if (tipo is null || !TiposCatalogo.Contains(tipo))
        {
            await CategoriasAsync(ctx, ct);
            return;
        }

        var busqueda = new BusquedaCatalogoViewModel { tipo = tipo };
        await _catalogo.LlenarSeccionAsync(busqueda, aplicarFechas: false, ct);

        var teclado = new Teclado();
        // Dos destinos por renglón (máximo 20 para no hacer un teclado enorme).
        foreach (var par in busqueda.Destinos.Take(20).Chunk(2))
            teclado.Fila(par.Select(d => (FormatoBot.Recortar(d.Text, 28), $"catd:{tipo}:{d.Value}:1")).ToArray());
        teclado.Navegacion($"{TipoCatalogo.NombreSeccion(tipo)} (todos)", $"cat:{tipo}:1");

        var sb = new StringBuilder()
            .Linea(FormatoBot.Ruta(RutaExplorar, Seccion(tipo), "📍 Destino"))
            .Linea()
            .Linea(busqueda.Destinos.Count == 0
                ? $"Por ahora no hay destinos con {TipoCatalogo.NombreSeccion(tipo).ToLowerInvariant()}."
                : "<b>¿A qué destino?</b>");
        await MostrarAsync(ctx, sb.ToString(), teclado, ct);
    }

    // =============================================================================================
    // Ficha con pestañas
    // =============================================================================================

    /// <param name="destino">Destino de la lista de donde vino (para que "←" regrese a la misma lista).</param>
    /// <param name="pagina">Página de esa lista.</param>
    private async Task FichaAsync(ContextoBot ctx, ulong idServicio, PestanaFicha pestana, ulong? destino, int pagina,
        CancellationToken ct)
    {
        var detalle = await DetalleServicioAsync(idServicio, ct);
        if (detalle is null)
        {
            await MostrarAsync(ctx, "Ese servicio ya no está disponible.", new Teclado().Navegacion("Explorar", "exp"), ct);
            return;
        }

        var tieneFechas = detalle.tipo != TipoCatalogo.Vuelo;
        var tienePromos = detalle.Promociones.Count > 0;
        if ((pestana == PestanaFicha.Promos && !tienePromos) || (pestana == PestanaFicha.Fechas && !tieneFechas))
            pestana = PestanaFicha.Resumen;

        var sb = new StringBuilder()
            .Linea(FormatoBot.Ruta(RutaExplorar, Seccion(detalle.tipo)))
            .Linea($"<b>{FormatoBot.HtmlRecortado(detalle.nombre, 120)}</b>")
            .Linea($"{TipoCatalogo.NombreTipo(detalle.tipo)} · {FormatoBot.HtmlRecortado(detalle.ubicacion, 80)}")
            .Linea();

        switch (pestana)
        {
            case PestanaFicha.Detalles:
                PestanaDetalles(sb, detalle);
                break;
            case PestanaFicha.Politica:
                sb.Linea("<b>📜 Política de cancelación</b>")
                  .Linea(FormatoBot.HtmlRecortado(detalle.politica, 700).Replace("\r", "").Replace("\n", " "))
                  .Linea()
                  .Linea("<i>Es la que queda guardada en tu compra. Para cancelar, entra a la web › Mis reservaciones.</i>");
                break;
            case PestanaFicha.Promos:
                sb.Linea("<b>🏷️ Promociones vigentes</b>")
                  .Linea("<i>Escribe el código al pagar en la web.</i>");
                foreach (var p in detalle.Promociones.Take(6))
                {
                    var valor = p.tipo_descuento == "PORCENTAJE" ? $"{p.valor:0.##} %" : FormatoBot.Dinero(p.valor, detalle.moneda);
                    var minimo = p.minimo_compra > 0 ? $" en compras desde {FormatoBot.Dinero(p.minimo_compra, detalle.moneda)}" : "";
                    sb.Linea($"• <code>{FormatoBot.Html(p.codigo)}</code> — {FormatoBot.Html(valor)} de descuento{FormatoBot.Html(minimo)}");
                }
                break;
            case PestanaFicha.Fechas:
                PestanaFechas(sb, detalle);
                break;
            default:
                PestanaResumen(sb, detalle);
                break;
        }

        // Pestañas: la actual va marcada con • (tocarla de nuevo no hace nada).
        var sufijo = $"{detalle.id}:{destino ?? 0}:{pagina}";
        string Pestana(PestanaFicha p, string etiqueta) => p == pestana ? $"• {etiqueta} •" : etiqueta;
        var teclado = new Teclado()
            .Fila((Pestana(PestanaFicha.Resumen, "ℹ️ Resumen"), $"srv:{sufijo}"),
                  (Pestana(PestanaFicha.Detalles, "📋 Detalles"), $"srvd:{sufijo}"),
                  (Pestana(PestanaFicha.Politica, "📜 Política"), $"srvp:{sufijo}"));
        if (tienePromos && tieneFechas)
            teclado.Fila((Pestana(PestanaFicha.Promos, "🏷️ Promos"), $"srvo:{sufijo}"),
                         (Pestana(PestanaFicha.Fechas, "📅 Fechas y precios"), $"fch:{sufijo}"));
        else if (tienePromos)
            teclado.Boton(Pestana(PestanaFicha.Promos, "🏷️ Promociones"), $"srvo:{sufijo}");
        else if (tieneFechas)
            teclado.Boton(Pestana(PestanaFicha.Fechas, "📅 Fechas y precios"), $"fch:{sufijo}");

        EnlaceWeb(teclado, sb, "🌐 Ver y reservar en la web", $"/Catalogo/Detalle/{detalle.id}");
        teclado.Navegacion(TipoCatalogo.NombreSeccion(detalle.tipo), Pagina(detalle.tipo, destino, pagina));
        await MostrarAsync(ctx, sb.ToString(), teclado, ct, foto: detalle.imagen_ruta);
    }

    private static void PestanaResumen(StringBuilder sb, DetalleServicioViewModel detalle)
    {
        // "Desde": el precio más bajo de los próximos días con lugares (los mismos que muestra la pestaña Fechas).
        var conCupo = detalle.ProximasFechas.Where(d => d.existe && d.disponible > 0).ToList();
        if (conCupo.Count > 0)
            sb.Linea($"💰 Desde <b>{FormatoBot.Dinero(conCupo.Min(d => d.precio), detalle.moneda)}</b> por {TipoCatalogo.Unidad(detalle.tipo)}");
        else if (detalle.tipo != TipoCatalogo.Vuelo)
            sb.Linea("Sin lugares en los próximos 30 días.");

        if (!string.IsNullOrWhiteSpace(detalle.descripcion))
            sb.Linea().Linea(FormatoBot.HtmlRecortado(detalle.descripcion, 350).Replace("\r", "").Replace("\n", " "));

        if (detalle.Promociones.Count > 0)
            sb.Linea().Linea($"🏷️ {detalle.Promociones.Count} promoción{(detalle.Promociones.Count == 1 ? "" : "es")} vigente{(detalle.Promociones.Count == 1 ? "" : "s")}.");
    }

    private static void PestanaDetalles(StringBuilder sb, DetalleServicioViewModel detalle)
    {
        sb.Linea("<b>📋 Detalles</b>");
        foreach (var dato in detalle.Datos)
            sb.Linea($"• <b>{FormatoBot.Html(dato.etiqueta)}:</b> {FormatoBot.HtmlRecortado(dato.valor, 100)}");

        if (detalle.Incluidos.Count > 0)
        {
            sb.Linea().Linea("<b>Incluye</b>");
            foreach (var incluido in detalle.Incluidos)
                sb.Linea($"• {FormatoBot.HtmlRecortado(incluido.nombre, 70)} ({TipoCatalogo.NombreTipo(incluido.tipo_servicio)} × {incluido.cantidad:0.##})");
        }
        if (detalle.Datos.Count == 0 && detalle.Incluidos.Count == 0)
            sb.Linea("Consulta todos los detalles en la web.");
    }

    private static void PestanaFechas(StringBuilder sb, DetalleServicioViewModel detalle)
    {
        sb.Linea($"<b>📅 Próximas fechas</b> · precio por {TipoCatalogo.Unidad(detalle.tipo)}");
        var dias = detalle.ProximasFechas.Where(d => d.existe).Take(14).ToList();
        if (dias.Count == 0) sb.Linea("No hay fechas cargadas en los próximos 30 días.");
        foreach (var d in dias)
        {
            var cupo = d.disponible > 0 ? $"{d.disponible} disponible{(d.disponible == 1 ? "" : "s")}" : "agotado";
            sb.Linea($"• {FormatoBot.DiaCorto(d.fecha)} — {FormatoBot.Dinero(d.precio, detalle.moneda)} · {cupo}");
        }
        sb.Linea().Linea("<i>Precio y cupo informativos: se confirman en la web al reservar.</i>");
    }

    private async Task<DetalleServicioViewModel?> DetalleServicioAsync(ulong idServicio, CancellationToken ct)
    {
        var tipo = await _catalogo.TipoDeServicioAsync(idServicio, ct);
        if (tipo is null) return null;
        return await _catalogo.DetalleAsync(idServicio, new BusquedaCatalogoViewModel { tipo = tipo }, cotizar: false, ct);
    }

    // =============================================================================================
    // Ayuda (textos del sitio que edita el SA en /Admin/SA › Contenido)
    // =============================================================================================

    private async Task AyudaAsync(ContextoBot ctx, CancellationToken ct)
    {
        var teclado = new Teclado()
            .Fila(("❌ Cancelaciones", "ayu:can"), ("💳 Métodos de pago", "ayu:pag"))
            .Fila(("📞 Contacto", "ayu:con"), ("🔒 Seguridad", "ayu:seg"))
            .Boton("⌨️ Cómo usarme", "ayu:cmd")
            .Navegacion();
        var sb = new StringBuilder()
            .Linea(FormatoBot.Ruta("ℹ️ Ayuda"))
            .Linea()
            .Linea("<b>¿Sobre qué tema?</b>");
        await MostrarAsync(ctx, sb.ToString(), teclado, ct);
    }

    private async Task AyudaTemaAsync(ContextoBot ctx, string tema, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var teclado = new Teclado();

        switch (tema)
        {
            case "can":
            {
                var contenido = await _contenido.ObtenerAsync(ct);
                sb.Linea(FormatoBot.Ruta("ℹ️ Ayuda", "❌ Cancelaciones"))
                  .Linea()
                  .Linea("<b>Cancelaciones y reembolsos</b>")
                  .Linea(FormatoBot.HtmlRecortado(contenido.Texto(ContenidoSitioClaves.PoliticasCancelaciones), 2500))
                  .Linea()
                  .Linea("Cada servicio tiene su propia política: la ves en su ficha (🔎 Explorar) y queda guardada en tu compra. " +
                         "Para cancelar o pedir un reembolso entra a la web › Mis reservaciones.");
                EnlaceWeb(teclado, sb, "Ver las políticas en la web", "/Otros/Politicas");
                break;
            }
            case "pag":
            {
                var metodos = await _consultas.MetodosPagoActivosAsync(ct);
                sb.Linea(FormatoBot.Ruta("ℹ️ Ayuda", "💳 Métodos de pago"))
                  .Linea()
                  .Linea("<b>Métodos de pago</b>");
                foreach (var (nombre, descripcion) in metodos)
                    sb.Linea($"• <b>{FormatoBot.Html(nombre)}</b>{(string.IsNullOrWhiteSpace(descripcion) ? "" : " — " + FormatoBot.HtmlRecortado(descripcion, 150))}");
                sb.Linea().Linea("🔒 El cobro se hace solo en la web. El bot nunca te pedirá datos de tarjeta ni el CVV.");
                break;
            }
            case "con":
            {
                var contenido = await _contenido.ObtenerAsync(ct);
                sb.Linea(FormatoBot.Ruta("ℹ️ Ayuda", "📞 Contacto"))
                  .Linea()
                  .Linea($"<b>{FormatoBot.HtmlRecortado(contenido.Texto(ContenidoSitioClaves.ContactoTitulo), 100)}</b>")
                  .Linea(FormatoBot.HtmlRecortado(contenido.Texto(ContenidoSitioClaves.ContactoTexto), 1000));
                var correo = contenido.Texto(ContenidoSitioClaves.PieCorreo);
                var telefono = contenido.Texto(ContenidoSitioClaves.PieTelefono);
                if (!string.IsNullOrWhiteSpace(correo)) sb.Linea($"✉️ {FormatoBot.Html(correo)}");
                if (!string.IsNullOrWhiteSpace(telefono)) sb.Linea($"📞 {FormatoBot.Html(telefono)}");
                sb.Linea().Linea("También puedes escribirle a un asesor desde aquí:");
                teclado.Boton("💡 Pedir asesoría", "ase");
                break;
            }
            case "seg":
                sb.Linea(FormatoBot.Ruta("ℹ️ Ayuda", "🔒 Seguridad"))
                  .Linea()
                  .Linea("<b>Tu seguridad</b>")
                  .Linea("• Nunca te pediré números de tarjeta, CVV ni tu contraseña. Si alguien te los pide en nombre de FlyVibeMex, no los des.")
                  .Linea("• Los pagos, cancelaciones y reembolsos se hacen solo en la web, con tu sesión iniciada.")
                  .Linea("• Para ver tus reservaciones aquí, vincula tu cuenta con un código de un solo uso (Mi cuenta › Telegram).")
                  .Linea("• Si pierdes tu teléfono, desvincula Telegram desde la web: Mi cuenta › Telegram.");
                break;
            default:
                sb.Linea(FormatoBot.Ruta("ℹ️ Ayuda", "⌨️ Cómo usarme"))
                  .Linea()
                  .Linea("<b>Cómo usarme</b>")
                  .Linea("• Escríbeme un destino o un servicio («Cancún», «hotel playa») y lo busco.")
                  .Linea("• Usa los accesos directos de abajo del chat o los botones de cada mensaje.")
                  .Linea("• Para salir de algo a medias, toca ✖ Cancelar.")
                  .Linea()
                  .Linea("<b>Comandos</b>")
                  .Linea("/menu — menú principal")
                  .Linea("/explorar — vuelos, hoteles, vehículos, tours y paquetes")
                  .Linea("/reservas — tus reservaciones*")
                  .Linea("/soporte — asesoría, reportar un problema y tus tickets*")
                  .Linea("/ayuda — políticas, métodos de pago y contacto")
                  .Linea()
                  .Linea("<i>* Requieren tu cuenta vinculada (👤 Mi cuenta).</i>");
                break;
        }

        teclado.Navegacion("Ayuda", "ayu");
        await MostrarAsync(ctx, sb.ToString(), teclado, ct);
    }
}
