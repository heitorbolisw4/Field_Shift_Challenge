// Baixa a umidade do solo da zona de raízes (0–100 cm) do SMAP L4 (SPL4SMGP, v008, grade EASE 2.0 de 9 km)
// para as cidades do app e grava um CSV por cidade em data/smap/<chave do IMERG>.csv ("2016-01-01,0.2129").
// Também grava data/smap/constantes.csv com o ponto de murcha e a porosidade do modelo (SPL4SMLM).
//
// Uso (na raiz do repo):  dotnet run --project tools/Farm.Smap
// Precisa da variável de ambiente EARTHDATA_TOKEN (o mesmo token do Farm.Imerg).
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

var inv = CultureInfo.InvariantCulture;
var token = Environment.GetEnvironmentVariable("EARTHDATA_TOKEN")
            ?? throw new Exception("defina EARTHDATA_TOKEN antes de rodar");

// mesmas cidades do front (sede do município) e a célula (linha y, coluna x) da grade EASE 2.0 de 9 km.
// As células foram achadas lendo /cell_lat e /cell_lon de um granule (OPeNDAP .dap.csv) e pegando
// o centro mais próximo da sede; a grade é fixa, então não muda entre granules.
(string Nome, double Lat, double Lon, int Y, int X)[] cidades =
    [("Indianópolis", -19.03, -47.92, 1076, 1414),   // centro -19.0148, -47.9409
     ("Uberlândia",   -18.92, -48.28, 1075, 1410),   // centro -18.9403, -48.3143
     ("Araguari",     -18.65, -48.19, 1071, 1411),   // centro -18.6423, -48.2210
     ("Uberaba",      -19.75, -47.93, 1086, 1414)];  // centro -19.7623, -47.9409

// a chave do arquivo reaproveita o nome do pixel do IMERG (data/imerg), assim a API acha os dois arquivos do mesmo jeito
string Chave(double lat, double lon) =>
    $"{(Math.Floor((lat + 90) / 0.1) * 0.1 - 89.95).ToString("F2", inv)}_{(Math.Floor((lon + 180) / 0.1) * 0.1 - 179.95).ToString("F2", inv)}.csv";

// uma caixa que cobre as 4 células: 1 pedido por dia traz todas.
// Cuidado: no .dap.csv do Hyrax um recorte de largura 1 vem com valores em branco, então garante largura >= 2.
int y0 = cidades.Min(c => c.Y), y1 = Math.Max(cidades.Max(c => c.Y), y0 + 1);
int x0 = cidades.Min(c => c.X), x1 = Math.Max(cidades.Max(c => c.X), x0 + 1);
string Caixa(string v) => $"{v}%5B{y0}:1:{y1}%5D%5B{x0}:1:{x1}%5D";

// só a safra (1/nov a 31/mar) dos 10 anos completos, como no IMERG
var ultimoAno = DateTime.Today.Year - 1;
var dias = Enumerable.Range(0, (new DateOnly(ultimoAno, 12, 31).DayNumber - new DateOnly(ultimoAno - 9, 1, 1).DayNumber) + 1)
                     .Select(i => new DateOnly(ultimoAno - 9, 1, 1).AddDays(i))
                     .Where(d => d.Month is >= 11 or <= 3).ToList();

var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

// lê um recorte em .dap.csv: "/Grupo/var[k], v0, v1, ..." (k = linha a partir de y0, valores a partir de x0)
async Task<Dictionary<(int Y, int X), double>> Recorte(string url, string variavel, CancellationToken ct)
{
    var r = await http.GetAsync($"{url}.dap.csv?dap4.ce={Caixa(variavel)}", ct);
    if (r.StatusCode == HttpStatusCode.NotFound) throw new FileNotFoundException(url);
    r.EnsureSuccessStatusCode();
    var texto = await r.Content.ReadAsStringAsync(ct);
    if (texto.TrimStart().StartsWith('<')) throw new Exception("resposta em HTML: token inválido ou login pedido");
    var res = new Dictionary<(int, int), double>();
    foreach (var l in texto.Split('\n').Where(l => l.StartsWith(variavel + "[")))
    {
        var k = int.Parse(l[(variavel.Length + 1)..l.IndexOf(']')], inv);
        var vals = l.Split(',').Skip(1).ToArray();
        for (var j = 0; j < vals.Length; j++)
            if (double.TryParse(vals[j].Trim(), inv, out var v) && v >= 0) res[(y0 + k, x0 + j)] = v;  // fill = -9999
    }
    return res;
}

// 1) retoma: carrega os CSVs já baixados e só pede os dias que faltam em alguma cidade
var pasta = Path.Combine("data", "smap");
var theta = cidades.ToDictionary(c => (c.Y, c.X), _ => new ConcurrentDictionary<DateOnly, double>());
foreach (var c in cidades)
{
    var arq = Path.Combine(pasta, Chave(c.Lat, c.Lon));
    if (!File.Exists(arq)) continue;
    foreach (var p in File.ReadLines(arq).Skip(1).Select(l => l.Split(',')).Where(p => p.Length == 2))
        theta[(c.Y, c.X)][DateOnly.Parse(p[0], inv)] = double.Parse(p[1], inv);
}
var faltam = dias.Where(d => cidades.Any(c => !theta[(c.Y, c.X)].ContainsKey(d))).ToList();
Console.WriteLine($"faltam {faltam.Count} de {dias.Count} dias");

// 2) o sufixo do nome varia (Vv8010, Vv8012, Vv8013, às vezes com "_xxxx" no fim), então resolve o
//    link OPeNDAP de cada dia pela busca de granules do CMR, um pedido por mês. Usa o granule das 13:30 UTC
//    (meio da janela 12–15 h UTC, manhã no Brasil).
var links = new Dictionary<DateOnly, string>();
foreach (var mes in faltam.Select(d => new DateOnly(d.Year, d.Month, 1)).Distinct())
{
    var fim = mes.AddMonths(1);
    var json = await http.GetStringAsync(
        "https://cmr.earthdata.nasa.gov/search/granules.json?short_name=SPL4SMGP&version=008&page_size=500" +
        $"&temporal={mes:yyyy-MM-dd}T00:00:00Z,{fim:yyyy-MM-dd}T00:00:00Z");
    foreach (var g in JsonDocument.Parse(json).RootElement.GetProperty("feed").GetProperty("entry").EnumerateArray())
    {
        var titulo = g.GetProperty("title").GetString()!;
        var i = titulo.IndexOf("_gph_");
        if (i < 0 || titulo.Substring(i + 13, 7) != "T133000") continue;
        var dia = DateOnly.ParseExact(titulo.Substring(i + 5, 8), "yyyyMMdd", inv);
        var od = g.GetProperty("links").EnumerateArray().Select(l => l.GetProperty("href").GetString()!)
                  .FirstOrDefault(h => h.StartsWith("https://opendap.earthdata.nasa.gov/"));
        if (od != null) links[dia] = od;
    }
}
Console.WriteLine($"{links.Count} granules achados no CMR");

// 3) baixa dia a dia, 4 em paralelo
int feitos = 0;
var falhas = new ConcurrentBag<DateOnly>();
var naoAchado = new ConcurrentBag<DateOnly>();

await Parallel.ForEachAsync(faltam, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (d, ct) =>
{
    if (!links.TryGetValue(d, out var url)) { naoAchado.Add(d); return; }
    for (var tentativa = 1; tentativa <= 6; tentativa++)
    {
        try
        {
            var vals = await Recorte(url, "/Geophysical_Data/sm_rootzone", ct);
            foreach (var c in cidades)
                if (vals.TryGetValue((c.Y, c.X), out var v)) theta[(c.Y, c.X)][d] = v;
            break;
        }
        catch (FileNotFoundException) { naoAchado.Add(d); break; }
        catch (Exception e) when (tentativa < 6 && !e.Message.StartsWith("resposta em HTML"))
        {
            await Task.Delay(1000 * (1 << tentativa), ct);  // 2, 4, 8, 16, 32 s
        }
        catch (Exception e)
        {
            falhas.Add(d);
            Console.WriteLine($"{d}: {e.Message}");
            if (e.Message.StartsWith("resposta em HTML")) throw;
            break;
        }
    }
    var n = Interlocked.Increment(ref feitos);
    if (n % 100 == 0) Console.WriteLine($"{n}/{faltam.Count} dias");
});

// 4) um CSV por cidade (mesmo com falhas: a próxima rodada completa)
Directory.CreateDirectory(pasta);
foreach (var c in cidades)
{
    var serie = theta[(c.Y, c.X)];
    File.WriteAllLines(Path.Combine(pasta, Chave(c.Lat, c.Lon)),
        ["data,theta", .. serie.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key:yyyy-MM-dd},{kv.Value.ToString("F4", inv)}")]);
    var v = serie.Values.DefaultIfEmpty(double.NaN);
    Console.WriteLine($"{c.Nome}: {Chave(c.Lat, c.Lon)}, {serie.Count} dias, theta min {v.Min():F4} média {v.Average():F4} máx {v.Max():F4}");
}
Console.WriteLine($"não achados (404 ou fora do CMR): {naoAchado.Count} dias");
Console.WriteLine($"falhas: {falhas.Count} dias");

// 5) constantes do modelo de superfície (granule único e estático do SPL4SMLM): ponto de murcha e porosidade.
//    O SPL4SMLM não traz capacidade de campo; quem precisar estima entre wp e poros.
var lm = "https://opendap.earthdata.nasa.gov/collections/C3480441264-NSIDC_CPRD/granules/SMAP_L4_SM_lmc_00000000T000000_Vv8013_001.h5_sdu93G0W";
var wp = await Recorte(lm, "/Land-Model-Constants_Data/clsm_wp", CancellationToken.None);
var poros = await Recorte(lm, "/Land-Model-Constants_Data/clsm_poros", CancellationToken.None);
File.WriteAllLines(Path.Combine(pasta, "constantes.csv"),
    ["arquivo,wp,poros", .. cidades.Select(c =>
        $"{Chave(c.Lat, c.Lon)},{wp[(c.Y, c.X)].ToString("F4", inv)},{poros[(c.Y, c.X)].ToString("F4", inv)}")]);
foreach (var c in cidades)
    Console.WriteLine($"{c.Nome}: wp {wp[(c.Y, c.X)]:F4}, poros {poros[(c.Y, c.X)]:F4}");
