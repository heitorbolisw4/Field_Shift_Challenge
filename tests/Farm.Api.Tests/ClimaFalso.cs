using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

// clima sintético e determinístico no formato do NASA POWER. Nada vai para a rede.
// Estação chuvosa de 10/10 até ~15/04 (o fim varia ±25 dias por ano), seca no resto:
// janeiro dá milho, fevereiro/março misturam sorgo e braquiária. Um dia com -999 testa o filtro do Power.cs.
public static class ClimaFalso
{
    public static readonly DateOnly DiaInvalido = new(2019, 7, 4);

    static int FimDasChuvas(int ano) => 105 + ((ano * 7) % 11 - 5) * 5; // dia do ano: ~15/04 ± 25

    public static Dia Gerar(DateOnly d)
    {
        var doy = d.DayOfYear;
        var chuvoso = doy >= 283 || doy <= FimDasChuvas(d.Year);
        var chuva = chuvoso && d.DayNumber % 2 == 0 ? 12.0 : 0.0;
        return new Dia(d, 30, 18, chuva, chuvoso ? 18 : 21, chuvoso ? 75 : 50, 2, 92);
    }

    // dias que o Power.cs devolve depois de filtrar os -999
    public static List<Dia> Dias(int anoIni, int anoFim)
    {
        var l = new List<Dia>();
        for (var d = new DateOnly(anoIni, 1, 1); d <= new DateOnly(anoFim, 12, 31); d = d.AddDays(1))
            if (d != DiaInvalido) l.Add(Gerar(d));
        return l;
    }

    public static string Json(DateOnly ini, DateOnly fim)
    {
        var inv = CultureInfo.InvariantCulture;
        var nomes = new[] { "T2M_MAX", "T2M_MIN", "PRECTOTCORR", "ALLSKY_SFC_SW_DWN", "RH2M", "WS2M", "PS" };
        var p = nomes.ToDictionary(n => n, _ => new Dictionary<string, double>());
        for (var d = ini; d <= fim; d = d.AddDays(1))
        {
            var x = Gerar(d);
            var k = d.ToString("yyyyMMdd", inv);
            double[] v = [x.TMax, x.TMin, x.Chuva, x.Rad, x.UR, x.Vento, x.Pressao];
            if (d == DiaInvalido) v[2] = -999; // fill_value: o Power.cs tem que descartar o dia
            for (int i = 0; i < nomes.Length; i++) p[nomes[i]][k] = v[i];
        }
        return JsonSerializer.Serialize(new { properties = new { parameter = p } });
    }
}

// responde só ao POWER; qualquer outro host derruba o teste
public class PowerFalso : HttpMessageHandler
{
    public int Chamadas;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        var u = req.RequestUri!;
        if (u.Host != "power.larc.nasa.gov") throw new InvalidOperationException($"rede proibida nos testes: {u}");
        Interlocked.Increment(ref Chamadas);
        var q = u.Query.TrimStart('?').Split('&').Select(s => s.Split('=', 2)).ToDictionary(s => s[0], s => Uri.UnescapeDataString(s[1]));
        var inv = CultureInfo.InvariantCulture;
        var ini = DateOnly.ParseExact(q["start"], "yyyyMMdd", inv);
        var fim = DateOnly.ParseExact(q["end"], "yyyyMMdd", inv);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ClimaFalso.Json(ini, fim), Encoding.UTF8, "application/json")
        });
    }
}
