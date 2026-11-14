using System.Collections.Concurrent;
using System.Globalization;

// cliente do NASA POWER: a única parte que fala com a rede
static class Power
{
    // cache em memória por coordenada: a NASA é chamada uma vez por ponto; depois a resposta é instantânea
    static readonly ConcurrentDictionary<(double, double), List<Dia>> cache = new();

    public static async Task<List<Dia>> BaixarAsync(HttpClient http, double lat, double lon)
    {
        if (cache.TryGetValue((lat, lon), out var pronto)) return pronto;
        var dias = await BaixarDaNasaAsync(http, lat, lon);
        cache[(lat, lon)] = dias;
        return dias;
    }

    // baixa os últimos 10 anos completos do POWER e descarta os dias com -999
    static async Task<List<Dia>> BaixarDaNasaAsync(HttpClient http, double lat, double lon)
    {
        var inv = CultureInfo.InvariantCulture;           // força ponto decimal
        var ultimoAno = DateTime.Today.Year - 1;          // só anos completos
        var url = "https://power.larc.nasa.gov/api/temporal/daily/point" +
                  "?parameters=T2M_MAX,T2M_MIN,PRECTOTCORR,ALLSKY_SFC_SW_DWN,RH2M,WS2M,PS&community=AG" +
                  $"&latitude={lat.ToString(inv)}&longitude={lon.ToString(inv)}" +
                  $"&start={ultimoAno - 9}0101&end={ultimoAno}1231&format=JSON";

        var resp = await http.GetFromJsonAsync<PowerResponse>(url);
        var p = resp!.Properties.Parameter;

        return p["T2M_MAX"].Keys
            .Select(k => new Dia(
                DateOnly.ParseExact(k, "yyyyMMdd", inv),
                p["T2M_MAX"][k], p["T2M_MIN"][k], p["PRECTOTCORR"][k],
                p["ALLSKY_SFC_SW_DWN"][k], p["RH2M"][k], p["WS2M"][k], p["PS"][k]))
            .Where(d => d.TMax != -999 && d.TMin != -999 && d.Chuva != -999 &&
                        d.Rad != -999 && d.UR != -999 && d.Vento != -999 && d.Pressao != -999) // fill_value do POWER
            .OrderBy(d => d.Data)
            .ToList();
    }
}

// só os campos que interessam do JSON do POWER
record PowerResponse(PowerProps Properties);
record PowerProps(Dictionary<string, Dictionary<string, double>> Parameter);
