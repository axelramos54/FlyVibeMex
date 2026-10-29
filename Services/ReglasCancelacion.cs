using FlyVibeMex.Models;

namespace FlyVibeMex.Services;

/// <summary>
/// Regla ÚNICA para saber si una reservación se puede cancelar y cuánto se reembolsa. La usan la lista de
/// "Mis reservaciones" (para mostrar el monto ANTES de confirmar) y la cancelación (que la vuelve a calcular).
/// </summary>
/// <remarks>
/// Siempre usa la política COPIADA en la compra (detalle_reservacion), no la actual del servicio (regla R4):
///   - No se cancela si la reservación ya está en un estado final, si ya inició o si la compra no lo permite.
///   - Reembolso = 100 % si faltan al menos <c>horas_reembolso_total</c> horas para el inicio;
///     si faltan menos, <c>porcentaje_reembolso_tardio</c>. Se calcula sobre el monto del pago aprobado.
/// </remarks>
public static class ReglasCancelacion
{
    public static OpcionesCancelacion Calcular(
        bool estadoFinal,
        DateTime inicio,
        bool permiteCancelacion,
        int horasReembolsoTotal,
        decimal porcentajeTardio,
        string? politica,
        decimal? montoPagado,
        string moneda,
        DateTime ahora)
    {
        var opciones = new OpcionesCancelacion
        {
            moneda = moneda,
            politica = string.IsNullOrWhiteSpace(politica)
                ? PoliticaCancelacion.Describir(permiteCancelacion, horasReembolsoTotal, porcentajeTardio)
                : politica
        };

        if (estadoFinal)
            opciones.motivo_no_cancelar = "Esta reservación ya está cancelada o terminada.";
        else if (inicio <= ahora)
            opciones.motivo_no_cancelar = "El servicio ya inició: ya no se puede cancelar.";
        else if (!permiteCancelacion)
            opciones.motivo_no_cancelar = "Esta compra no admite cancelación (tarifa no reembolsable).";

        opciones.puede_cancelar = opciones.motivo_no_cancelar is null;
        if (!opciones.puede_cancelar)
        {
            opciones.motivo_no_reembolsar = opciones.motivo_no_cancelar;
            return opciones;
        }

        var horasAntes = (inicio - ahora).TotalHours;
        opciones.porcentaje = horasAntes >= horasReembolsoTotal ? 100m : Math.Clamp(porcentajeTardio, 0m, 100m);

        if (montoPagado is not decimal monto || monto <= 0)
        {
            opciones.motivo_no_reembolsar = "No hay un pago aprobado que reembolsar.";
            return opciones;
        }

        opciones.monto_reembolso = Math.Round(monto * opciones.porcentaje / 100m, 2, MidpointRounding.AwayFromZero);
        opciones.puede_reembolsar = opciones.monto_reembolso > 0;
        if (!opciones.puede_reembolsar)
            opciones.motivo_no_reembolsar = "Según la política de tu compra, a estas alturas ya no hay reembolso.";

        return opciones;
    }
}
