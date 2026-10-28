namespace FlyVibeMex.Models
{
    /// <summary>
    /// Campos de la política de cancelación que captura el gestor. Los implementan
    /// <see cref="ServicioFormViewModel"/> (alta / actualizar servicio) y <see cref="ServicioFormBase"/>
    /// (asociar hospedaje / vehículo / evento), para usar un solo parcial: <c>_PoliticaCancelacionCampos.cshtml</c>.
    /// </summary>
    public interface IPoliticaCancelacionForm
    {
        bool permite_cancelacion { get; set; }
        int horas_reembolso_total { get; set; }
        decimal porcentaje_reembolso_tardio { get; set; }
        string? politica_cancelacion { get; set; }
    }

    /// <summary>Reglas y textos de la política de cancelación, compartidos por el panel y el sitio público.</summary>
    public static class PoliticaCancelacion
    {
        public const int HorasMaximas = 8760;   // 1 año (mismo CHECK que la tabla)

        /// <summary>
        /// Texto automático de la política (se usa si el gestor no escribió uno).
        /// Ej.: "Reembolso del 100 % hasta 48 h antes del inicio; después, 50 %."
        /// </summary>
        public static string Describir(bool permite, int horas, decimal porcentajeTardio)
        {
            if (!permite) return "No admite cancelación (tarifa no reembolsable).";

            var tardio = porcentajeTardio switch
            {
                >= 100 => "después también se reembolsa el 100 %",
                <= 0 => "después no hay reembolso",
                _ => $"después se reembolsa el {porcentajeTardio:0.##} %"
            };
            return horas == 0
                ? porcentajeTardio switch
                {
                    >= 100 => "Cancelación con reembolso del 100 % en cualquier momento antes del inicio.",
                    <= 0 => "Se puede cancelar antes del inicio, pero sin reembolso.",
                    _ => $"Cancelación antes del inicio: se reembolsa el {porcentajeTardio:0.##} %."
                }
                : $"Reembolso del 100 % cancelando al menos {horas} h antes del inicio; {tardio}.";
        }

        /// <summary>
        /// Normaliza los valores ANTES de guardar en la BD (llamarlo en el POST, después de ModelState.IsValid):
        /// recorta el texto y, si no se permite cancelar, deja horas y % en 0.
        /// </summary>
        public static void Normalizar(IPoliticaCancelacionForm p)
        {
            p.politica_cancelacion = string.IsNullOrWhiteSpace(p.politica_cancelacion) ? null : p.politica_cancelacion.Trim();
            if (!p.permite_cancelacion)
            {
                p.horas_reembolso_total = 0;
                p.porcentaje_reembolso_tardio = 0;
            }
        }
    }
}
