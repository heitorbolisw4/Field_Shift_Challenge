using System.Globalization;
using System.Text.Json;

// atalhos para conferir a API contra ela mesma (/recomendacao) e contra o Farm.Core
public static class Apoio
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // mês-dia no ano-modelo: 29/02 vira 28/02, como a API faz por dentro (e /recomendacao?inicio=02-29 dá 500)
    public static string Md(DateOnly d) => Calendario.NoAnoModelo(d).ToString("MM-dd", Inv);

    // decêndio (texto "11/02 a 20/02") que contém a data real d
    public static string DecendioDe(DateOnly d) => Decendio.De(Calendario.NoAnoModelo(d)).ToString();

    public static DateOnly Data(this JsonElement e, string campo) => DateOnly.Parse(e.GetProperty(campo).GetString()!, Inv);

    // /recomendacao na janela [de, ate] → decêndio → (cultura, risco)
    public static async Task<Dictionary<string, JsonElement>> Recomendacao(HttpClient c, double lat, double lon, DateOnly de, DateOnly ate, double cad, double riscoMax)
    {
        var j = await c.GetOk($"/recomendacao?lat={Http.Q(lat)}&lon={Http.Q(lon)}&inicio={Md(de)}&fim={Md(ate)}&cad={Http.Q(cad)}&riscoMax={Http.Q(riscoMax)}");
        return j.GetProperty("decendios").EnumerateArray().ToDictionary(d => d.GetProperty("decendio").GetString()!, d => d);
    }

    public static IEnumerable<DateOnly> Dias(DateOnly de, DateOnly ate)
    {
        for (var d = de; d <= ate; d = d.AddDays(1)) yield return d;
    }

    // anos que o Power.cs pede: os 10 últimos completos
    public static List<Dia> ClimaDaApi() => ClimaFalso.Dias(DateTime.Today.Year - 10, DateTime.Today.Year - 1);
}
