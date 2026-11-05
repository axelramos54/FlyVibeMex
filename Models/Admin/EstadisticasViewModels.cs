namespace FlyVibeMex.Models
{
    // =============================================================================================
    // Panel de estadísticas de ventas del SA: GET /Admin/SA/Estadisticas.
    // Lo usan SAController.Estadisticas.cs y Services/SAEstadisticasService.cs.
    //
    // Los números siguen LAS MISMAS REGLAS que el procedimiento pa_reporte_ventas de la entrega de base
    // de datos, para que el panel y el reporte que se corre en SSMS den exactamente lo mismo:
    //   · Una venta cuenta por la FECHA DEL PRIMER PAGO COBRADO de la reservación. Cobrado = estado
    //     APROBADO o REEMBOLSADO: un cobro que después se devolvió sí fue una venta ese día.
    //   · Los importes son los HISTÓRICOS de la reservación (subtotal, descuento_total, total). No se
    //     recalculan con los precios de hoy (regla R4 del modelo de datos).
    //   · Se restan los reembolsos en estado PROCESADO, por su FECHA DE RESOLUCIÓN (no la de solicitud).
    //   · Cada reservación se atribuye al tipo de servicio de su PRIMERA línea de detalle.
    // =============================================================================================

    /// <summary>Periodo que se consulta. Si no se manda nada: del día 1 del mes en curso a hoy.</summary>
    public class FiltroEstadisticas
    {
        public DateTime? desde { get; set; }
        public DateTime? hasta { get; set; }

        /// <summary>Si el periodo tiene más días que esto, la gráfica agrupa por mes en vez de por día.</summary>
        public const int MaximoDiasEnGrafica = 45;

        /// <summary>
        /// Periodo efectivo: rellena los valores que falten y los devuelve en orden y sin horas.
        /// Si llegan invertidos se intercambian (es más útil que mostrar un error).
        /// </summary>
        public (DateTime desde, DateTime hasta) Periodo()
        {
            var hoy = DateTime.Today;
            var d = (desde ?? new DateTime(hoy.Year, hoy.Month, 1)).Date;
            var h = (hasta ?? hoy).Date;
            return h < d ? (h, d) : (d, h);
        }

        /// <summary>Días que abarca el periodo, contando los dos extremos.</summary>
        public int Dias()
        {
            var (d, h) = Periodo();
            return (h - d).Days + 1;
        }
    }

    /// <summary>Una fila de la tabla por tipo de servicio (VUELO, HOSPEDAJE, VEHICULO, PAQUETE, EVENTO).</summary>
    public class VentasPorTipo
    {
        public string tipo { get; set; } = string.Empty;
        public int reservaciones { get; set; }
        public decimal subtotal { get; set; }
        public decimal descuentos { get; set; }
        public decimal vendido { get; set; }
        public decimal reembolsado { get; set; }

        public decimal neto => vendido - reembolsado;
    }

    /// <summary>Una barra de la gráfica: un día o un mes del periodo.</summary>
    public class VentasPorPeriodo
    {
        public DateTime inicio { get; set; }
        public string etiqueta { get; set; } = string.Empty;
        public string etiquetaLarga { get; set; } = string.Empty;
        public int reservaciones { get; set; }
        public decimal vendido { get; set; }
    }

    /// <summary>Una fila de "lo que más se vendió".</summary>
    public class ServicioVendido
    {
        public ulong? id_servicio { get; set; }
        public string servicio { get; set; } = string.Empty;
        public string tipo { get; set; } = string.Empty;
        public int reservaciones { get; set; }
        public decimal vendido { get; set; }
    }

    /// <summary>Todo lo que muestra la pantalla de estadísticas.</summary>
    public class EstadisticasVentasViewModel
    {
        public const int TopServicios = 10;

        public FiltroEstadisticas Filtro { get; set; } = new();

        /// <summary>Reservaciones con al menos un cobro en el periodo.</summary>
        public int Reservaciones { get; set; }
        public decimal Subtotal { get; set; }
        public decimal Descuentos { get; set; }

        /// <summary>Suma de los totales históricos de esas reservaciones.</summary>
        public decimal Vendido { get; set; }

        /// <summary>Reembolsos PROCESADOS en el periodo.</summary>
        public decimal Reembolsado { get; set; }
        public int ReembolsosProcesados { get; set; }

        /// <summary>Reservaciones del periodo que terminaron canceladas (informativo).</summary>
        public int Canceladas { get; set; }

        public string Moneda { get; set; } = "MXN";

        /// <summary>true si en el periodo hay reservaciones en más de una moneda (los totales no se suman a ciegas).</summary>
        public bool VariasMonedas { get; set; }

        /// <summary>true cuando la gráfica agrupa por mes porque el periodo es largo.</summary>
        public bool AgrupadoPorMes { get; set; }

        public List<VentasPorTipo> PorTipo { get; set; } = new();
        public List<VentasPorPeriodo> PorPeriodo { get; set; } = new();
        public List<ServicioVendido> MasVendidos { get; set; } = new();

        /// <summary>Lo que quedó después de devoluciones. No es margen: ver la nota de la vista.</summary>
        public decimal Neto => Vendido - Reembolsado;

        /// <summary>Venta promedio por reservación.</summary>
        public decimal Promedio => Reservaciones == 0 ? 0m : Vendido / Reservaciones;

        public decimal MaximoPeriodo => PorPeriodo.Count == 0 ? 0m : PorPeriodo.Max(p => p.vendido);

        public bool SinDatos => Reservaciones == 0 && ReembolsosProcesados == 0;
    }
}
