namespace FlyVibeMex.Models;

/// <summary>
/// Opciones de la sección "Integracion" de appsettings.json: con qué sitios externos (GoldStay) se permite
/// incrustar páginas de FlyVibeMex dentro de un &lt;iframe&gt;, y QUÉ páginas.
///
/// Por qué existe esta clase y no una constante en el código:
///   · El dominio de GoldStay cambia entre la demostración local, las pruebas y el sitio real; es configuración,
///     no código, y no debe obligar a recompilar.
///   · La lista vive en UN solo lugar: la usan el middleware de cabeceras (Services/CabecerasIntegracion.cs)
///     y la configuración de la cookie de sesión en Program.cs.
///
/// SEGURIDAD: "OrigenesPermitidos" nunca debe contener "*" ni un esquema sin dominio. Permitir que cualquier
/// sitio incruste el flujo de reserva es exactamente el ataque de clickjacking que la cabecera previene:
/// un tercero pondría el iframe transparente sobre sus propios botones y el cliente pagaría sin verlo.
/// Si la lista queda vacía, el middleware bloquea TODA incrustación (frame-ancestors 'none'), que es el
/// comportamiento seguro por omisión.
/// </summary>
public sealed class OpcionesIntegracion
{
    /// <summary>Nombre de la sección en appsettings.json.</summary>
    public const string Seccion = "Integracion";

    /// <summary>
    /// Interruptor general. En false (valor por omisión) el sitio no se puede incrustar en ninguna parte,
    /// sin importar lo que diga el resto de la sección.
    /// </summary>
    public bool Habilitado { get; set; }

    /// <summary>
    /// Orígenes que pueden incrustar las rutas embebibles. Un ORIGEN es esquema + dominio + puerto, sin ruta:
    /// "https://www.goldstay.com", "https://localhost:7200". No se admite "*".
    /// </summary>
    public string[] OrigenesPermitidos { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Prefijos de ruta que se pueden incrustar. Todo lo demás (el panel interno, iniciar sesión, Mi usuario…)
    /// sigue bloqueado aunque la integración esté habilitada: solo se expone el catálogo y el flujo de reserva.
    /// </summary>
    public string[] RutasEmbebibles { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Orígenes realmente utilizables: sin vacíos y sin el comodín "*", que se ignora a propósito
    /// (ver la nota de seguridad de la clase).
    /// </summary>
    public IReadOnlyList<string> OrigenesValidos =>
        OrigenesPermitidos
            .Where(origen => !string.IsNullOrWhiteSpace(origen) && origen.Trim() != "*")
            .Select(origen => origen.Trim().TrimEnd('/'))
            .ToArray();

    /// <summary>La integración solo está activa si además hay al menos un origen válido.</summary>
    public bool EstaActiva => Habilitado && OrigenesValidos.Count > 0;

    /// <summary>¿Esta ruta es una de las que se permite incrustar?</summary>
    public bool EsRutaEmbebible(PathString ruta) =>
        EstaActiva && RutasEmbebibles.Any(prefijo =>
            !string.IsNullOrWhiteSpace(prefijo) &&
            ruta.StartsWithSegments(prefijo, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Valor de la directiva frame-ancestors para una ruta embebible:
    /// "frame-ancestors 'self' https://www.goldstay.com".
    /// </summary>
    public string DirectivaFrameAncestors =>
        "frame-ancestors 'self' " + string.Join(' ', OrigenesValidos);

    // =========================================================================================
    // Fase 3 · Inicio de sesión transparente (SSO) con token JWT
    // =========================================================================================

    /// <summary>
    /// Permite que GoldStay abra el widget con la sesión del cliente ya iniciada, mandando un token firmado
    /// a <c>/Integracion/Entrar</c>. Independiente de <see cref="Habilitado"/>: se puede incrustar el catálogo
    /// sin SSO (el cliente inicia sesión él mismo dentro del iframe).
    /// </summary>
    public bool SsoHabilitado { get; set; }

    /// <summary>Quién firma el token (claim <c>iss</c>). Debe coincidir exactamente con lo que manda GoldStay.</summary>
    public string EmisorSso { get; set; } = "goldstay";

    /// <summary>Para quién es el token (claim <c>aud</c>). Evita reusar en FlyVibeMex un token emitido para otro sistema.</summary>
    public string AudienciaSso { get; set; } = "flyvibemex";

    /// <summary>
    /// Llave compartida con GoldStay para firmar y verificar el token (HMAC-SHA256). Mínimo 32 caracteres.
    ///
    /// NUNCA va en appsettings.json ni en el repositorio: es una CONTRASEÑA. Con ella, cualquiera puede
    /// fabricar un token y entrar como cualquier cliente. Se pone con
    ///   dotnet user-secrets set "Integracion:LlaveFirmaSso" "…"
    /// o como variable de entorno en el servidor (Integracion__LlaveFirmaSso).
    /// </summary>
    public string? LlaveFirmaSso { get; set; }

    /// <summary>
    /// Segundos que el token sigue siendo válido tras emitirse. Corto a propósito: el token solo tiene que
    /// sobrevivir una redirección, y mientras menos dure, menos sirve si se filtra (queda en el historial,
    /// en la cabecera Referer y en los logs del servidor).
    /// </summary>
    public int VidaTokenSegundos { get; set; } = 60;

    /// <summary>Margen por diferencia de reloj entre los dos servidores. Más de unos segundos no es razonable.</summary>
    public int ToleranciaRelojSegundos { get; set; } = 30;

    /// <summary>El SSO solo se activa con la integración activa, el interruptor en true y una llave suficientemente larga.</summary>
    public bool EstaSsoActivo =>
        EstaActiva && SsoHabilitado && !string.IsNullOrWhiteSpace(LlaveFirmaSso) && LlaveFirmaSso!.Length >= 32;
}
