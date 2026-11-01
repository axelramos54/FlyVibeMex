using System.Text;
using FlyVibeMex.Models;

namespace FlyVibeMex.Bot;

/// <summary>
/// Parte pública del bot (sin vincular): explorar el catálogo con los MISMOS datos y reglas que la web
/// (<see cref="Services.ICatalogoService"/>: solo servicios activos de proveedores activos, cupo de los próximos 30 días),
/// ficha con política de cancelación y promociones, próximas fechas, y la ayuda (contenido que edita el SA).
/// Para comprar, el bot lleva a la web: ahí se recalcula precio y cupo.
/// </summary>
public sealed partial class ManejadorActualizaciones
{
    private const int ServiciosPorPagina = 5;

    /// <summary>Orden de las secciones (igual que el menú del sitio).</summary>
    private static readonly string[] TiposCatalogo =
    {
        TipoCatalogo.Vuelo, TipoCatalogo.Hospedaje, TipoCatalogo.Vehiculo, TipoCatalogo.Evento, TipoCatalogo.Paquete
    };

    private async Task CategoriasAsync(ContextoBot ctx, CancellationToken ct)
    {
        var teclado = new Teclado()
            .Fila(("✈️ Vuelos", $"cat:{TipoCatalogo.Vuelo}:1"), ("🏨 Hoteles", $"cat:{TipoCatalogo.Hospedaje}:1"))
            .Fila(("🚗 Vehículos", $"cat:{TipoCatalogo.Vehiculo}:1"), ("🎟️ Tours", $"cat:{TipoCatalogo.Evento}:1"))
            .Fila(("🧳 Paquetes", $"cat:{TipoCatalogo.Paquete}:1"), ("↩ Menú", "menu"));

        await EnviarAsync(ctx.ChatId, "<b>¿Qué quieres explorar?</b>\nTe muestro lo disponible en los próximos 30 días con su precio \"desde\".",
            teclado, ct);
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

        var sb = new StringBuilder()
            .Linea($"<b>{seccion}</b> · {FormatoBot.Html(nombreDestino ?? "todos los destinos")}");
        var teclado = new Teclado();

        if (resultados.Count == 0)
        {
            sb.Linea().Linea($"No hay {seccion.ToLowerInvariant()} disponibles{(nombreDestino is null ? "" : " en ese destino")} en los próximos 30 días.");
        }
        else
        {
            if (paginas > 1) sb.Linea($"Página {pagina} de {paginas} · {resultados.Count} opciones");
            var n = (pagina - 1) * ServiciosPorPagina;
            foreach (var r in resultados.Skip((pagina - 1) * ServiciosPorPagina).Take(ServiciosPorPagina))
            {
                n++;
                var precio = r.agotado
                    ? "Agotado"
                    : r.precio_unitario is decimal p ? $"desde {FormatoBot.Dinero(p, r.moneda)} por {r.Unidad}" : "Consulta el precio en la web";
                sb.Linea()
                  .Linea($"<b>{n}. {FormatoBot.HtmlRecortado(r.nombre, 80)}</b>")
                  .Linea($"   📍 {FormatoBot.HtmlRecortado(r.ubicacion, 80)}{(r.fecha_texto is null ? "" : " · " + FormatoBot.Html(r.fecha_texto))}")
                  .Linea($"   {FormatoBot.Html(precio)}");
                teclado.Boton($"{n}. {FormatoBot.Recortar(r.nombre, 40)}", $"srv:{r.id}");
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
            teclado.Fila(("📍 Elegir destino", $"dst:{tipo}"), ("↩ Secciones", "exp"));
        else
            teclado.Boton("↩ Secciones", "exp");

        var ruta = $"/Home/{TipoCatalogo.AccionSeccion(tipo)}" + (destino is null ? "" : $"?destino={destino}");
        EnlaceWeb(teclado, sb, $"Buscar {seccion.ToLowerInvariant()} con fechas en la web", ruta);
        await EnviarAsync(ctx.ChatId, sb.ToString(), teclado, ct);
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
        teclado.Fila(("Todos los destinos", $"cat:{tipo}:1"), ("↩ Secciones", "exp"));

        var texto = busqueda.Destinos.Count == 0
            ? $"Por ahora no hay destinos con {TipoCatalogo.NombreSeccion(tipo).ToLowerInvariant()}."
            : $"<b>{TipoCatalogo.NombreSeccion(tipo)}</b> · ¿a qué destino?";
        await EnviarAsync(ctx.ChatId, texto, teclado, ct);
    }

    private async Task FichaAsync(ContextoBot ctx, ulong idServicio, CancellationToken ct)
    {
        var detalle = await DetalleServicioAsync(idServicio, ct);
        if (detalle is null)
        {
            await EnviarAsync(ctx.ChatId, "Ese servicio ya no está disponible.", new Teclado().Boton("↩ Secciones", "exp"), ct);
            return;
        }

        var sb = new StringBuilder()
            .Linea($"<b>{FormatoBot.HtmlRecortado(detalle.nombre, 150)}</b>")
            .Linea($"{TipoCatalogo.NombreTipo(detalle.tipo)} · {FormatoBot.HtmlRecortado(detalle.ubicacion, 100)}");
        if (!string.IsNullOrWhiteSpace(detalle.descripcion))
            sb.Linea().Linea(FormatoBot.HtmlRecortado(detalle.descripcion, 500).Replace("\r", "").Replace("\n", " "));

        sb.Linea();
        foreach (var dato in detalle.Datos)
            sb.Linea($"• <b>{FormatoBot.Html(dato.etiqueta)}:</b> {FormatoBot.HtmlRecortado(dato.valor, 120)}");

        if (detalle.Incluidos.Count > 0)
        {
            sb.Linea().Linea("<b>Incluye</b>");
            foreach (var incluido in detalle.Incluidos)
                sb.Linea($"• {FormatoBot.HtmlRecortado(incluido.nombre, 90)} ({TipoCatalogo.NombreTipo(incluido.tipo_servicio)} × {incluido.cantidad:0.##})");
        }

        sb.Linea().Linea("<b>Política de cancelación</b>")
          .Linea(FormatoBot.HtmlRecortado(detalle.politica, 600).Replace("\r", "").Replace("\n", " "));

        if (detalle.Promociones.Count > 0)
        {
            sb.Linea().Linea("<b>Promociones vigentes</b> (escribe el código al pagar en la web)");
            foreach (var p in detalle.Promociones)
            {
                var valor = p.tipo_descuento == "PORCENTAJE" ? $"{p.valor:0.##} %" : FormatoBot.Dinero(p.valor, detalle.moneda);
                var minimo = p.minimo_compra > 0 ? $" en compras desde {FormatoBot.Dinero(p.minimo_compra, detalle.moneda)}" : "";
                sb.Linea($"• <code>{FormatoBot.Html(p.codigo)}</code> — {FormatoBot.Html(valor)} de descuento{FormatoBot.Html(minimo)}");
            }
        }

        var teclado = new Teclado();
        if (detalle.tipo != TipoCatalogo.Vuelo) teclado.Boton("📅 Próximas fechas y precios", $"fch:{detalle.id}");
        teclado.Fila(($"↩ {TipoCatalogo.NombreSeccion(detalle.tipo)}", $"cat:{detalle.tipo}:1"), ("Menú", "menu"));
        EnlaceWeb(teclado, sb, "Ver y reservar en la web", $"/Catalogo/Detalle/{detalle.id}");
        await EnviarAsync(ctx.ChatId, sb.ToString(), teclado, ct);
    }

    private async Task FechasAsync(ContextoBot ctx, ulong idServicio, CancellationToken ct)
    {
        var detalle = await DetalleServicioAsync(idServicio, ct);
        if (detalle is null)
        {
            await EnviarAsync(ctx.ChatId, "Ese servicio ya no está disponible.", new Teclado().Boton("↩ Secciones", "exp"), ct);
            return;
        }

        var unidad = TipoCatalogo.Unidad(detalle.tipo);
        var sb = new StringBuilder()
            .Linea($"📅 <b>{FormatoBot.HtmlRecortado(detalle.nombre, 120)}</b>")
            .Linea($"Precio por {unidad} y lugares disponibles (próximos días):")
            .Linea();

        var dias = detalle.ProximasFechas.Where(d => d.existe).Take(14).ToList();
        if (dias.Count == 0)
        {
            sb.Linea("No hay fechas cargadas en los próximos 30 días.");
        }
        foreach (var d in dias)
        {
            var cupo = d.disponible > 0 ? $"{d.disponible} disponible{(d.disponible == 1 ? "" : "s")}" : "agotado";
            sb.Linea($"• {FormatoBot.DiaCorto(d.fecha)} — {FormatoBot.Dinero(d.precio, detalle.moneda)} · {cupo}");
        }
        sb.Linea().Linea("<i>Precio y cupo informativos: se confirman en la web al reservar.</i>");

        var teclado = new Teclado().Fila(("↩ Ficha", $"srv:{detalle.id}"), ("Menú", "menu"));
        EnlaceWeb(teclado, sb, "Reservar en la web", $"/Catalogo/Detalle/{detalle.id}");
        await EnviarAsync(ctx.ChatId, sb.ToString(), teclado, ct);
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
            .Fila(("⌨️ Comandos", "ayu:cmd"), ("↩ Menú", "menu"));
        await EnviarAsync(ctx.ChatId, "<b>Ayuda</b> · ¿sobre qué tema?", teclado, ct);
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
                sb.Linea("<b>Cancelaciones y reembolsos</b>")
                  .Linea(FormatoBot.HtmlRecortado(contenido.Texto(ContenidoSitioClaves.PoliticasCancelaciones), 2500))
                  .Linea()
                  .Linea("Cada servicio tiene su propia política: la ves en su ficha (/explorar) y queda guardada en tu compra. " +
                         "Para cancelar o pedir un reembolso entra a la web › Mis reservaciones.");
                EnlaceWeb(teclado, sb, "Ver las políticas en la web", "/Otros/Politicas");
                break;
            }
            case "pag":
            {
                var metodos = await _consultas.MetodosPagoActivosAsync(ct);
                sb.Linea("<b>Métodos de pago</b>");
                foreach (var (nombre, descripcion) in metodos)
                    sb.Linea($"• <b>{FormatoBot.Html(nombre)}</b>{(string.IsNullOrWhiteSpace(descripcion) ? "" : " — " + FormatoBot.HtmlRecortado(descripcion, 150))}");
                sb.Linea().Linea("🔒 El cobro se hace solo en la web. El bot nunca te pedirá datos de tarjeta ni el CVV.");
                break;
            }
            case "con":
            {
                var contenido = await _contenido.ObtenerAsync(ct);
                sb.Linea($"<b>{FormatoBot.HtmlRecortado(contenido.Texto(ContenidoSitioClaves.ContactoTitulo), 100)}</b>")
                  .Linea(FormatoBot.HtmlRecortado(contenido.Texto(ContenidoSitioClaves.ContactoTexto), 1000));
                var correo = contenido.Texto(ContenidoSitioClaves.PieCorreo);
                var telefono = contenido.Texto(ContenidoSitioClaves.PieTelefono);
                if (!string.IsNullOrWhiteSpace(correo)) sb.Linea($"✉️ {FormatoBot.Html(correo)}");
                if (!string.IsNullOrWhiteSpace(telefono)) sb.Linea($"📞 {FormatoBot.Html(telefono)}");
                sb.Linea().Linea("También puedes escribirle a un asesor desde aquí con /asesoria.");
                break;
            }
            case "seg":
                sb.Linea("<b>Tu seguridad</b>")
                  .Linea("• Nunca te pediré números de tarjeta, CVV ni tu contraseña. Si alguien te los pide en nombre de FlyVibeMex, no los des.")
                  .Linea("• Los pagos, cancelaciones y reembolsos se hacen solo en la web, con tu sesión iniciada.")
                  .Linea("• Para ver tus reservaciones aquí, vincula tu cuenta con un código de un solo uso (Mi cuenta › Telegram).")
                  .Linea("• Si pierdes tu teléfono, desvincula Telegram desde la web: Mi cuenta › Telegram.");
                break;
            default:
                sb.Linea("<b>Comandos</b>")
                  .Linea("/menu — menú principal")
                  .Linea("/explorar — vuelos, hoteles, vehículos, tours y paquetes")
                  .Linea("/reservas — tus próximas reservaciones*")
                  .Linea("/tickets — tus tickets de soporte*")
                  .Linea("/asesoria — pedir ayuda a un asesor*")
                  .Linea("/problema — reportar un problema con una reservación*")
                  .Linea("/vincular — vincular tu cuenta · /desvincular")
                  .Linea("/cancelar — salir de lo que estés haciendo")
                  .Linea()
                  .Linea("<i>* Requieren tu cuenta vinculada.</i>");
                break;
        }

        teclado.Fila(("↩ Ayuda", "ayu"), ("Menú", "menu"));
        await EnviarAsync(ctx.ChatId, sb.ToString(), teclado, ct);
    }
}
