namespace FlyVibeMex.Models;
public static class EtiquetasHospedaje
{
    public static readonly IReadOnlyDictionary<string, string> Todas = new Dictionary<string, string>
    {
        ["ALBERCA"] = "Alberca", ["PEQUENA"] = "Pequeña", ["MEDIANA"] = "Mediana", ["GRANDE"] = "Grande",
        ["WIFI"] = "Wi-Fi", ["DESAYUNO_INCLUIDO"] = "Desayuno incluido", ["TODO_INCLUIDO"] = "Todo incluido",
        ["VISTA_AL_MAR"] = "Vista al mar", ["FRENTE_A_LA_PLAYA"] = "Frente a la playa",
        ["AIRE_ACONDICIONADO"] = "Aire acondicionado", ["ESTACIONAMIENTO"] = "Estacionamiento",
        ["ACEPTA_MASCOTAS"] = "Acepta mascotas", ["ACCESIBLE"] = "Accesible", ["SPA"] = "Spa", ["GIMNASIO"] = "Gimnasio"
    };
    public static bool EsValida(string? etiqueta) => etiqueta is not null && Todas.ContainsKey(etiqueta);
    public static string Nombre(string etiqueta) => Todas.TryGetValue(etiqueta, out var n) ? n : etiqueta;
}