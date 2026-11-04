namespace FlyVibeMex.Models;

/// <summary>
/// Un aviso que el widget manda a la página que lo contiene (GoldStay) con <c>window.parent.postMessage</c>.
/// Fase 4 de la integración.
///
/// Se usa desde una vista con el parcial _EventoWidget:
///   &lt;partial name="_EventoWidget" model='new EventoWidget("reserva.confirmada", new { codigo = Model.codigo_reserva })' /&gt;
///
/// REGLA: en <see cref="Datos"/> va lo MÍNIMO para que el anfitrión actualice su pantalla (un código, un total).
/// Nada de datos personales del cliente, números de tarjeta ni nada que no deba quedar en manos del anfitrión:
/// el mensaje sale del iframe hacia otra página y, a partir de ahí, el código de GoldStay hace lo que quiera
/// con él. Tampoco se manda nada que GoldStay no pueda ya consultar por su cuenta.
/// </summary>
/// <param name="Tipo">Nombre del evento, en minúsculas y con punto: "reserva.confirmada", "pago.rechazado".</param>
/// <param name="Datos">Objeto anónimo con los campos del evento. Se serializa a JSON.</param>
public sealed record EventoWidget(string Tipo, object? Datos = null);
