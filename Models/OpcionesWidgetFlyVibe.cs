// ███ ESTE ARCHIVO VA EN EL PROYECTO DE FLYVIBEMEX ███  (no en GoldStay)
// Ruta: Models/OpcionesWidgetFlyVibe.cs

namespace FlyVibeMex.Models;

/// <summary>
/// Opciones de la sección "WidgetFlyVibe" de appsettings.json: datos para incrustar el widget de RESERVA DE
/// HABITACIONES de GoldStay dentro de FlyVibeMex, y para firmar el token de inicio de sesión transparente.
///
/// ============================  OJO CON LA DIRECCIÓN  ============================
/// Aquí FlyVibeMex es el ANFITRIÓN: firma un token con la sesión de su cliente y abre el iframe de GoldStay.
/// Esta clase NO tiene nada que ver con <see cref="OpcionesIntegracion"/>, que es la dirección contraria
/// (GoldStay incrustando el catálogo de FlyVibeMex). Las dos direcciones conviven y cada una tiene:
///   · su propia llave de firma (una llave por dirección: si se filtra una, la otra sigue sirviendo);
///   · su propio emisor y audiencia, invertidos.
///
///   Dirección                      iss           aud           llave
///   GoldStay → FlyVibeMex (invitado) goldstay      flyvibemex    Integracion:LlaveFirmaSso
///   FlyVibeMex → GoldStay (anfitrión) flyvibemex    goldstay      WidgetFlyVibe:LlaveFirma   ← esta clase
/// ================================================================================
///
/// Los valores tienen que coincidir con la configuración de GoldStay (su sección JwtSettings):
///   LlaveFirma == JwtSettings:SecretKey · Emisor == JwtSettings:Emisor · Audiencia == JwtSettings:Audiencia
/// </summary>
public sealed class OpcionesWidgetFlyVibe
{
    public const string Seccion = "WidgetFlyVibe";

    /// <summary>Dirección base del sitio de GoldStay, sin diagonal final: "https://goldstayprb.somee.com".</summary>
    public string UrlBase { get; set; } = string.Empty;

    /// <summary>
    /// Llave compartida con GoldStay para firmar el token (HMAC-SHA256). Mínimo 32 caracteres.
    ///
    /// NUNCA en appsettings.json ni en el repositorio: es una contraseña. Con ella se puede entrar a GoldStay
    /// como cualquiera de sus clientes. Se pone con:
    ///   dotnet user-secrets set "WidgetFlyVibe:LlaveFirma" "…"
    /// o como variable de entorno en el servidor (WidgetFlyVibe__LlaveFirma).
    /// </summary>
    public string? LlaveFirma { get; set; }

    /// <summary>Quién firma (claim <c>iss</c>). En esta dirección lo firma FlyVibeMex.</summary>
    public string Emisor { get; set; } = "flyvibemex";

    /// <summary>Para quién es (claim <c>aud</c>). En esta dirección lo valida GoldStay.</summary>
    public string Audiencia { get; set; } = "goldstay";

    /// <summary>
    /// Minutos de vida del token. GoldStay acepta 5 como máximo. Corto a propósito: el token viaja en la URL
    /// del iframe, así que queda en el historial, en el Referer y en los logs; y es de un solo uso.
    /// </summary>
    public int VidaTokenMinutos { get; set; } = 5;

    /// <summary>Ruta de GoldStay que recibe el token y abre la sesión.</summary>
    public string RutaLoginSso { get; set; } = "/Autenticacion/LoginSSO";

    /// <summary>Ruta de GoldStay para un visitante SIN sesión (ve habitaciones, inicia sesión al reservar).</summary>
    public string RutaWidgetPublico { get; set; } = "/Widget/Reserva";

    /// <summary>
    /// Origen de GoldStay (esquema + dominio, sin ruta ni diagonal final). Es el valor que el receptor de
    /// JavaScript exige en <c>event.origin</c>. Se deriva de <see cref="UrlBase"/> para no mantener dos listas.
    /// </summary>
    public string Origen =>
        Uri.TryCreate(UrlBase, UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Authority)
            : string.Empty;

    /// <summary>
    /// ¿Se puede mostrar el widget con sesión transferida? Sin llave suficiente no se firma nada: el widget se
    /// muestra en modo visitante, que sigue siendo útil.
    /// </summary>
    public bool PuedeFirmar =>
        !string.IsNullOrWhiteSpace(UrlBase) &&
        !string.IsNullOrWhiteSpace(LlaveFirma) &&
        LlaveFirma!.Length >= 32;

    /// <summary>¿Hay al menos dirección de GoldStay para mostrar el widget público?</summary>
    public bool EstaConfigurado => !string.IsNullOrWhiteSpace(UrlBase);
}
