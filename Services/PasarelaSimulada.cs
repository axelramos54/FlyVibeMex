using FlyVibeMex.Models;

namespace FlyVibeMex.Services;

/// <summary>
/// Pasarela de pago SIMULADA (no hay banco real). Valida la tarjeta, detecta la marca y decide si el cobro se
/// aprueba. En un sistema real esto lo haría el gateway (Stripe, Conekta, OpenPay…) y la app solo recibiría el token.
/// </summary>
/// <remarks>
/// Tarjetas de prueba:
///   4242 4242 4242 4242 → aprobada
///   4000 0000 0000 0002 → rechazada (cualquier tarjeta cuyos últimos 4 dígitos sean 0002)
/// Seguridad (regla R5): el número completo y el CVV solo existen en memoria durante la validación;
/// de la tarjeta solo se guardan marca, últimos 4 dígitos, vencimiento, alias y un token falso.
/// </remarks>
public static class PasarelaSimulada
{
    /// <summary>Últimos 4 dígitos que simulan un cobro rechazado por el banco.</summary>
    public const string UltimosRechazo = "0002";

    public static string SoloDigitos(string? texto) =>
        new((texto ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

    /// <summary>Algoritmo de Luhn: detecta la mayoría de los errores al teclear un número de tarjeta.</summary>
    public static bool LuhnValido(string digitos)
    {
        if (digitos.Length == 0) return false;
        int suma = 0;
        bool duplicar = false;
        for (int i = digitos.Length - 1; i >= 0; i--)
        {
            int d = digitos[i] - '0';
            if (duplicar)
            {
                d *= 2;
                if (d > 9) d -= 9;
            }
            suma += d;
            duplicar = !duplicar;
        }
        return suma % 10 == 0;
    }

    /// <summary>VISA, MASTERCARD o AMEX según el inicio del número; <c>null</c> si no se acepta.</summary>
    public static string? Marca(string digitos)
    {
        if (digitos.StartsWith('4')) return "VISA";
        if (digitos.Length >= 2 && (digitos.StartsWith("34") || digitos.StartsWith("37"))) return "AMEX";
        if (digitos.Length >= 4)
        {
            int dos = int.Parse(digitos[..2]);
            int cuatro = int.Parse(digitos[..4]);
            if (dos is >= 51 and <= 55 || cuatro is >= 2221 and <= 2720) return "MASTERCARD";
        }
        return null;
    }

    /// <summary>¿La tarjeta sigue vigente? (vence el último día del mes indicado).</summary>
    public static bool Vigente(byte mes, ushort anio, DateOnly hoy) =>
        anio > hoy.Year || (anio == hoy.Year && mes >= hoy.Month);

    /// <summary>Decide el resultado del cobro simulado.</summary>
    public static bool CobroAprobado(string ultimos4) => ultimos4 != UltimosRechazo;

    public static string NuevoToken() => "tok_sim_" + Guid.NewGuid().ToString("N");

    public static string NuevaReferencia() => "SIM-" + Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();

    /// <summary>Valida los datos de una tarjeta nueva. Devuelve los errores por campo del formulario.</summary>
    public static List<(string campo, string mensaje)> ValidarTarjetaNueva(PagoFormViewModel p, DateOnly hoy)
    {
        var errores = new List<(string, string)>();
        var digitos = SoloDigitos(p.numero_tarjeta);
        var marca = Marca(digitos);

        if (digitos.Length is < 13 or > 19)
            errores.Add((nameof(p.numero_tarjeta), "El número debe tener entre 13 y 19 dígitos."));
        else if (!LuhnValido(digitos))
            errores.Add((nameof(p.numero_tarjeta), "El número de tarjeta no es válido. Revisa que esté bien escrito."));
        else if (marca is null)
            errores.Add((nameof(p.numero_tarjeta), "Solo aceptamos Visa, Mastercard o American Express."));

        if (p.mes_expiracion is null or < 1 or > 12)
            errores.Add((nameof(p.mes_expiracion), "Elige el mes de vencimiento."));
        if (p.anio_expiracion is null || p.anio_expiracion < hoy.Year || p.anio_expiracion > hoy.Year + 15)
            errores.Add((nameof(p.anio_expiracion), "Elige el año de vencimiento."));
        else if (p.mes_expiracion is >= 1 and <= 12 && !Vigente(p.mes_expiracion.Value, p.anio_expiracion.Value, hoy))
            errores.Add((nameof(p.mes_expiracion), "La tarjeta está vencida."));

        var cvv = SoloDigitos(p.cvv);
        var largoCvv = marca == "AMEX" ? 4 : 3;
        if (cvv.Length != largoCvv || cvv.Length != (p.cvv ?? string.Empty).Trim().Length)
            errores.Add((nameof(p.cvv), $"El CVV debe tener {largoCvv} dígitos."));

        return errores;
    }
}
