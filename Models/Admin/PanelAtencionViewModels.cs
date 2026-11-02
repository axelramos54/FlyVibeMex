using Microsoft.AspNetCore.Mvc.Rendering;

namespace FlyVibeMex.Models
{
    // =============================================================================================
    // Panel de atención (T4 fase C): Soporte (/Admin/Soporte) y Reservaciones y reembolsos (/Admin/Reservaciones).
    // Lo usan SoporteController, ReservacionesPanelController, SoportePanelService y ReservacionesPanelService.
    // =============================================================================================

    /// <summary>Textos y colores de los estados (tickets, reservaciones, pagos y reembolsos) para las vistas.</summary>
    public static class EstadosPanel
    {
        public static string TextoTicket(string estado) => estado switch
        {
            "ABIERTA" => "Abierto",
            "EN_PROCESO" => "En proceso",
            "CERRADA" => "Cerrado",
            _ => estado
        };

        public static string Clase(string? estado) => estado switch
        {
            "ABIERTA" or "SOLICITADO" or "PENDIENTE" => "text-bg-warning",
            "EN_PROCESO" or "CONFIRMADA" or "EN_CURSO" => "text-bg-primary",
            "APROBADO" or "PROCESADO" or "COMPLETADA" => "text-bg-success",
            "RECHAZADO" or "CANCELADA" => "text-bg-danger",
            "REEMBOLSADO" => "text-bg-info",
            _ => "text-bg-secondary"
        };
    }

    // ---------------------------------------------------------------------------------------------
    // Soporte
    // ---------------------------------------------------------------------------------------------

    public class TicketPanelItem
    {
        public ulong id { get; set; }
        public string tipo { get; set; } = string.Empty;
        public string asunto { get; set; } = string.Empty;
        public string estado { get; set; } = string.Empty;
        public ulong id_cliente { get; set; }
        public string cliente { get; set; } = string.Empty;
        public string correo_cliente { get; set; } = string.Empty;
        public ulong? id_agente { get; set; }
        public string? agente { get; set; }
        public ulong? id_reservacion { get; set; }
        public string? codigo_reserva { get; set; }
        public DateTime creada_en { get; set; }
        public DateTime actualizada_en { get; set; }
        /// <summary>Respuestas en la conversación (sin contar el mensaje inicial).</summary>
        public int mensajes { get; set; }
        /// <summary>El ticket sigue abierto y el último mensaje es del cliente: le toca responder al personal.</summary>
        public bool espera_respuesta { get; set; }

        /// <summary>Calificación de la atención (1 a 5) que dio el cliente desde el bot al cerrarse el ticket; null si no calificó.</summary>
        public byte? calificacion { get; set; }
        public DateTime? calificada_en { get; set; }

        public string TipoTexto => tipo == "PROBLEMA" ? "Problema" : "Asesoría";
    }

    /// <summary>Inicio del panel de soporte: lo que hay pendiente.</summary>
    public class ResumenSoporteViewModel
    {
        public int EnFila { get; set; }
        public int MisActivos { get; set; }
        public int EsperanRespuesta { get; set; }
        public bool EsSupervisor { get; set; }
    }

    /// <summary>Detalle de un ticket en el panel (/Admin/Soporte/Ticket/{id}) y pantalla "Atender nuevo ticket".</summary>
    public class TicketDetalleViewModel
    {
        public TicketPanelItem Ticket { get; set; } = new();
        public string? telefono_cliente { get; set; }
        public List<MensajeTicketItem> Mensajes { get; set; } = new();

        /// <summary>Resumen de SOLO LECTURA de la reservación ligada (si la hay).</summary>
        public ResumenReservacionViewModel? Reservacion { get; set; }

        public bool PuedeTomar { get; set; }
        public bool PuedeResponder { get; set; }
        public bool PuedeCerrar { get; set; }
        public bool PuedeReasignar { get; set; }
        /// <summary>Cuentas activas de SOPORTE / ADMINISTRADOR / SA para reasignar (solo supervisores).</summary>
        public List<SelectListItem> Agentes { get; set; } = new();
        public RespuestaTicketForm Respuesta { get; set; } = new();

        /// <summary>Tickets ABIERTOS sin agente en la fila (pantalla "Atender nuevo ticket").</summary>
        public int EnFila { get; set; }
    }

    /// <summary>Todos los tickets (solo ADMINISTRADOR y SA) con filtros.</summary>
    public class ListaTicketsViewModel
    {
        public const int PorPagina = 50;

        public string? Estado { get; set; }
        /// <summary>Filtro de agente: id, 0 = sin asignar, null = todos.</summary>
        public ulong? IdAgente { get; set; }
        public string? Texto { get; set; }
        public int Pagina { get; set; } = 1;
        public int Total { get; set; }
        public List<TicketPanelItem> Resultados { get; set; } = new();
        public List<SelectListItem> Agentes { get; set; } = new();

        public int Paginas => Math.Max(1, (int)Math.Ceiling(Total / (double)PorPagina));

        public static readonly (string valor, string texto)[] Estados =
        {
            ("", "Todos"), ("ABIERTA", "Abiertos"), ("EN_PROCESO", "En proceso"), ("CERRADA", "Cerrados")
        };

        public static string? NormalizarEstado(string? estado) => estado is "ABIERTA" or "EN_PROCESO" or "CERRADA" ? estado : null;
    }

    // ---------------------------------------------------------------------------------------------
    // Reservaciones (resumen y detalle)
    // ---------------------------------------------------------------------------------------------

    public class LineaReservacionItem
    {
        public string servicio { get; set; } = string.Empty;
        public string tipo { get; set; } = string.Empty;
        public string descripcion { get; set; } = string.Empty;
        public DateTime inicio { get; set; }
        public DateTime fin { get; set; }
        public decimal cantidad { get; set; }
        public decimal precio_unitario { get; set; }
        public decimal descuento { get; set; }
        public decimal subtotal { get; set; }
        /// <summary>Política de cancelación GUARDADA en la compra (R4).</summary>
        public string politica { get; set; } = string.Empty;
    }

    /// <summary>Resumen de una reservación: lo usa el ticket de soporte (solo lectura) y el detalle del panel.</summary>
    public class ResumenReservacionViewModel
    {
        public ulong id { get; set; }
        public string codigo_reserva { get; set; } = string.Empty;
        public string estado { get; set; } = string.Empty;
        public DateTime creada_en { get; set; }
        public decimal subtotal { get; set; }
        public decimal descuento_total { get; set; }
        public decimal total { get; set; }
        public string moneda { get; set; } = "MXN";
        public ulong id_cliente { get; set; }
        public string cliente { get; set; } = string.Empty;
        public string correo_cliente { get; set; } = string.Empty;
        public string telefono_cliente { get; set; } = string.Empty;
        public List<LineaReservacionItem> Lineas { get; set; } = new();
        /// <summary>Último pago (estado y monto).</summary>
        public string? estado_pago { get; set; }
        public decimal? monto_pagado { get; set; }
        /// <summary>Último reembolso (estado y monto).</summary>
        public string? estado_reembolso { get; set; }
        public decimal? monto_reembolso { get; set; }

        public DateTime? Inicio => Lineas.Count == 0 ? null : Lineas.Min(l => l.inicio);
        public DateTime? Fin => Lineas.Count == 0 ? null : Lineas.Max(l => l.fin);
    }

    public record HistorialPagoItem(DateTime fecha, string estado, string? detalle);

    public class PagoPanelItem
    {
        public ulong id { get; set; }
        public DateTime fecha { get; set; }
        public decimal monto { get; set; }
        public string moneda { get; set; } = "MXN";
        public string estado { get; set; } = string.Empty;
        public string metodo { get; set; } = string.Empty;
        public string tarjeta { get; set; } = string.Empty;
        public string? referencia { get; set; }
        public List<HistorialPagoItem> Historial { get; set; } = new();
    }

    public class ReembolsoPanelItem
    {
        public ulong id { get; set; }
        public ulong id_reservacion { get; set; }
        public string codigo_reserva { get; set; } = string.Empty;
        public string cliente { get; set; } = string.Empty;
        public string correo_cliente { get; set; } = string.Empty;
        public DateTime fecha_solicitud { get; set; }
        public decimal monto { get; set; }
        public decimal monto_pagado { get; set; }
        public string moneda { get; set; } = "MXN";
        public string motivo { get; set; } = string.Empty;
        public string estado { get; set; } = string.Empty;
        public DateTime? fecha_resolucion { get; set; }
        public string? referencia { get; set; }
        public List<HistorialEstadoItem> Historial { get; set; } = new();

        public int DiasEsperando(DateTime ahora) => Math.Max(0, (int)(ahora - fecha_solicitud).TotalDays);
        public bool Pendiente => estado == "SOLICITADO";
        public decimal Porcentaje => monto_pagado > 0 ? Math.Round(monto * 100m / monto_pagado, 0) : 0;
    }

    /// <summary>Detalle de una reservación en el panel (/Admin/Reservaciones/{codigo}).</summary>
    public class DetalleReservacionPanelViewModel
    {
        public ResumenReservacionViewModel Resumen { get; set; } = new();
        public string? promocion { get; set; }
        public List<HistorialEstadoItem> Historial { get; set; } = new();
        public List<PagoPanelItem> Pagos { get; set; } = new();
        public List<ReembolsoPanelItem> Reembolsos { get; set; } = new();
        public List<TicketPanelItem> Tickets { get; set; } = new();
        /// <summary>Lo que permite la política GUARDADA en la compra hoy (las mismas reglas que el cliente).</summary>
        public OpcionesCancelacion Cancelacion { get; set; } = new();
    }

    // ---------------------------------------------------------------------------------------------
    // Listas del panel de Reservaciones
    // ---------------------------------------------------------------------------------------------

    public class FiltroReservacionesPanel
    {
        public string? codigo { get; set; }
        public string? correo { get; set; }
        public string? estado { get; set; }
        public DateTime? desde { get; set; }
        public DateTime? hasta { get; set; }
        public int pagina { get; set; } = 1;
    }

    public class ReservacionPanelItem
    {
        public ulong id { get; set; }
        public string codigo_reserva { get; set; } = string.Empty;
        public string cliente { get; set; } = string.Empty;
        public string correo_cliente { get; set; } = string.Empty;
        public string estado { get; set; } = string.Empty;
        public DateTime creada_en { get; set; }
        public DateTime? inicio { get; set; }
        public string servicio { get; set; } = string.Empty;
        public decimal total { get; set; }
        public string moneda { get; set; } = "MXN";
        public bool reembolso_pendiente { get; set; }
    }

    public class ListaReservacionesPanelViewModel
    {
        public const int PorPagina = 50;

        public FiltroReservacionesPanel Filtro { get; set; } = new();
        public int Total { get; set; }
        public List<ReservacionPanelItem> Resultados { get; set; } = new();
        public List<SelectListItem> Estados { get; set; } = new();
        public int ReembolsosPendientes { get; set; }

        public int Paginas => Math.Max(1, (int)Math.Ceiling(Total / (double)PorPagina));
    }

    public class ListaReembolsosPanelViewModel
    {
        /// <summary>pendientes (SOLICITADO, del más antiguo al más nuevo) o todos (los más recientes primero).</summary>
        public string Vista { get; set; } = "pendientes";
        public List<ReembolsoPanelItem> Resultados { get; set; } = new();

        public static string NormalizarVista(string? vista) => vista == "todos" ? "todos" : "pendientes";
    }
}
