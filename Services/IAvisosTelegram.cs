namespace FlyVibeMex.Services;

/// <summary>
/// Avisos por Telegram que disparan los servicios de soporte (docs/06): al personal (grupo privado de agentes) cuando
/// se abre un ticket, y al cliente vinculado cuando un agente le responde o cierra su ticket.
/// </summary>
/// <remarks>
/// Los métodos solo FORMAN el aviso (no esperan a Telegram): <c>Bot/AvisosTelegram.cs</c> los manda en segundo plano,
/// así un problema de red con Telegram nunca hace lenta ni rompe una pantalla de la web. Llamarlos SOLO después de
/// que el cambio se guardó (fuera de la transacción). Si el bot está desactivado se usa <see cref="AvisosTelegramDesactivados"/>.
/// </remarks>
public interface IAvisosTelegram
{
    /// <summary>Se abrió un ticket (desde la web o desde el bot): aviso al grupo de agentes.</summary>
    void TicketCreado(ulong idTicket);

    /// <summary>El personal respondió en el ticket: aviso al cliente (si vinculó su cuenta).</summary>
    void RespuestaDelPersonal(ulong idTicket);

    /// <summary>El personal cerró el ticket: aviso al cliente (si vinculó su cuenta), con estrellas para calificar.</summary>
    void TicketCerrado(ulong idTicket);

    /// <summary>
    /// El ticket cambió (lo tomaron, lo reasignaron, el cliente lo calificó…): se reescribe su mensaje en el grupo de
    /// agentes con el estado actual. Respuestas y cierres del personal ya lo hacen por su cuenta.
    /// </summary>
    void TicketActualizado(ulong idTicket);

    /// <summary>
    /// El cliente respondió en su ticket (desde la web o el bot): se reescribe su mensaje en el grupo y, si ya lo
    /// atiende un agente, sale además un aviso corto que sí suena ("💬 El cliente respondió en #12").
    /// </summary>
    void ClienteRespondio(ulong idTicket);

    /// <summary>
    /// El cliente desvinculó Telegram desde la web (Mi cuenta › Telegram): aviso de seguridad a ese chat.
    /// Los avisos de reservaciones y recordatorios no pasan por aquí: los manda <c>Bot/VigilanteViajes.cs</c>.
    /// </summary>
    void CuentaDesvinculada(long chatId);
}

/// <summary>Bot desactivado (sin token): los avisos no hacen nada.</summary>
public sealed class AvisosTelegramDesactivados : IAvisosTelegram
{
    public void TicketCreado(ulong idTicket) { }
    public void RespuestaDelPersonal(ulong idTicket) { }
    public void TicketCerrado(ulong idTicket) { }
    public void TicketActualizado(ulong idTicket) { }
    public void ClienteRespondio(ulong idTicket) { }
    public void CuentaDesvinculada(long chatId) { }
}
