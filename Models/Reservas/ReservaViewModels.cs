using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace FlyVibeMex.Models
{
    // =============================================================================================
    // Compra: resumen + pago (una sola pantalla) y confirmación.
    // Lo usan Controllers/ReservasController.cs y Services/ReservacionService.cs.
    // Regla: el navegador solo manda QUÉ quiere comprar (servicio, fechas, personas, código y tarjeta);
    // el precio SIEMPRE se recalcula en el servidor con ICatalogoService.CotizarAsync.
    // =============================================================================================

    /// <summary>
    /// Datos de la compra. Llegan por GET al resumen (desde el botón "Reservar" del detalle) y por POST al pagar
    /// (campos ocultos + datos de la tarjeta). Los nombres son los mismos que usa el catálogo (desde, hasta…).
    /// </summary>
    public class PagoFormViewModel
    {
        // ---------------- Qué se compra ----------------

        /// <summary>Id del servicio.</summary>
        [Required]
        public ulong id { get; set; }

        [DataType(DataType.Date)]
        public DateOnly? desde { get; set; }

        [DataType(DataType.Date)]
        public DateOnly? hasta { get; set; }

        [Range(1, BusquedaCatalogoViewModel.MaxPersonas, ErrorMessage = "Debe ser entre 1 y 20.")]
        public int personas { get; set; } = 1;

        [Range(1, BusquedaCatalogoViewModel.MaxHabitaciones, ErrorMessage = "Debe ser entre 1 y 10.")]
        public int habitaciones { get; set; } = 1;

        /// <summary>Código de promoción (opcional).</summary>
        [StringLength(30, ErrorMessage = "Máximo 30 caracteres.")]
        public string? codigo { get; set; }

        // ---------------- Cómo se paga (solo POST /Reservas/Pagar) ----------------

        /// <summary>
        /// Total que vio el cliente, con punto decimal ("12345.67"). Si al pagar el servidor calcula otro, no cobra
        /// y avisa. Es texto para no depender de la configuración regional al leer el número.
        /// </summary>
        public string? total_confirmado { get; set; }

        [Display(Name = "Método de pago")]
        public ushort? id_metodo_pago { get; set; }

        /// <summary>Tarjeta guardada elegida; <c>null</c> = tarjeta nueva.</summary>
        public ulong? id_tarjeta { get; set; }

        // Tarjeta nueva. El número completo y el CVV SOLO se usan para validar en memoria: nunca se guardan,
        // ni se registran en logs, ni se regresan al navegador (regla R5).
        public string? numero_tarjeta { get; set; }
        public byte? mes_expiracion { get; set; }
        public ushort? anio_expiracion { get; set; }
        public string? cvv { get; set; }

        [StringLength(40, ErrorMessage = "Máximo 40 caracteres.")]
        public string? alias { get; set; }

        /// <summary>Si es <c>false</c>, la tarjeta se guarda oculta (activo = 0) solo porque el pago necesita su id.</summary>
        public bool guardar_tarjeta { get; set; } = true;

        /// <summary>Convierte la solicitud en la búsqueda que entiende el catálogo.</summary>
        public BusquedaCatalogoViewModel ComoBusqueda() => new()
        {
            desde = desde,
            hasta = hasta,
            personas = personas,
            habitaciones = habitaciones
        };

        /// <summary>Borra los datos sensibles antes de volver a mostrar el formulario.</summary>
        public void LimpiarDatosSensibles()
        {
            numero_tarjeta = null;
            cvv = null;
        }
    }

    public record TarjetaGuardada(ulong id, string marca, string ultimos4, byte mes_expiracion, ushort anio_expiracion, string? alias)
    {
        public string Texto => $"{marca} •••• {ultimos4} (vence {mes_expiracion:00}/{anio_expiracion})" +
                               (string.IsNullOrWhiteSpace(alias) ? "" : $" · {alias}");
    }

    public record PromocionAplicada(ulong id, string codigo, string? nombre, decimal descuento);

    /// <summary>Pantalla /Reservas/Resumen: desglose, código de promoción y formulario de pago.</summary>
    public class ResumenReservaViewModel
    {
        public PagoFormViewModel Solicitud { get; set; } = new();
        public DetalleServicioViewModel Servicio { get; set; } = new();

        /// <summary>Precio y cupo recalculados (null o no disponible = no se puede pagar).</summary>
        public CotizacionServicio? Cotizacion { get; set; }

        public PromocionAplicada? Promocion { get; set; }
        public string? ErrorPromocion { get; set; }

        public decimal Descuento { get; set; }
        public decimal Total { get; set; }

        public List<SelectListItem> Metodos { get; set; } = new();
        public List<TarjetaGuardada> Tarjetas { get; set; } = new();

        /// <summary>Mensaje general (p. ej. "el precio cambió").</summary>
        [BindNever] public string? Aviso { get; set; }

        public bool PuedePagar => Cotizacion is { Disponible: true } && Metodos.Count > 0;
    }

    /// <summary>Resultado de <c>IReservacionService.PagarAsync</c>.</summary>
    public class ResultadoPago
    {
        public string? CodigoReserva { get; init; }
        public List<(string campo, string mensaje)> Errores { get; init; } = new();
        /// <summary>El total o el cupo cambiaron: se vuelve a mostrar el resumen con lo nuevo.</summary>
        public bool CambioPrecioOCupo { get; init; }

        public bool Exitoso => CodigoReserva is not null;

        public static ResultadoPago Error(string campo, string mensaje, bool cambio = false) =>
            new() { Errores = { (campo, mensaje) }, CambioPrecioOCupo = cambio };
    }

    /// <summary>Pantalla /Reservas/Confirmacion/{codigo} (también sirve de comprobante para imprimir).</summary>
    public class ConfirmacionReservaViewModel
    {
        public string codigo_reserva { get; set; } = string.Empty;
        public DateTime creada_en { get; set; }
        public string estado { get; set; } = string.Empty;
        public string servicio { get; set; } = string.Empty;
        public string tipo { get; set; } = string.Empty;
        public ulong id_servicio { get; set; }
        public string descripcion { get; set; } = string.Empty;
        public DateTime inicio { get; set; }
        public DateTime fin { get; set; }
        public decimal cantidad { get; set; }
        public decimal precio_unitario { get; set; }
        public decimal subtotal { get; set; }
        public decimal descuento { get; set; }
        public decimal total { get; set; }
        public string moneda { get; set; } = "MXN";
        public string? codigo_promocion { get; set; }
        public string politica { get; set; } = string.Empty;
        public string? metodo_pago { get; set; }
        public string? tarjeta { get; set; }
        public string? referencia_pago { get; set; }
        public DateTime? fecha_pago { get; set; }
    }
}
