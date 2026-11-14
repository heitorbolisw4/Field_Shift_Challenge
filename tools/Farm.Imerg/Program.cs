// Baixa a chuva diária do IMERG Final (GPM_3IMERGDF, v07, pixel 0,1°) para as cidades do app
// e grava um CSV por pixel em data/imerg/<lat>_<lon>.csv ("2016-01-01,12.3").
//
// Uso (na raiz do repo):  dotnet run --project tools/Farm.Imerg
// Precisa da variável de ambiente EARTHDATA_TOKEN (Earthdata Login > Generate Token)
// e do app "NASA GESDISC DATA ARCHIVE" autorizado no seu perfil do Earthdata.
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

var inv = CultureInfo.InvariantCulture;
var token = Environment.GetEnvironmentVariable("EARTHDATA_TOKEN")
            ?? throw new Exception("defina EARTHDATA_TOKEN antes de rodar");

// mesmas cidades do front (sede do município)
(string Nome, double Lat, double Lon)[] cidades =
    [("Indianópolis", -19.03, -47.92), ("Uberlândia", -18.92, -48.28),
     ("Araguari", -18.65, -48.19), ("Uberaba", -19.75, -47.93)];

// índice na grade do IMERG: lon -179,95..179,95 (3600), lat -89,95..89,95 (1800)
int IdxLon(double lon) => (int)Math.Floor((lon + 180) / 0.1);
int IdxLat(double lat) => (int)Math.Floor((lat + 90) / 0.1);

// uma caixa que cobre todas as cidades: 1 pedido por dia traz todos os pixels
int lon0 = cidades.Min(c => IdxLon(c.Lon)), lon1 = cidades.Max(c => IdxLon(c.Lon));
int lat0 = cidades.Min(c => IdxLat(c.Lat)), lat1 = cidades.Max(c => IdxLat(c.Lat));

var ultimoAno = DateTime.Today.Year - 1;   // mesmos 10 anos completos do POWER
var dias = Enumerable.Range(0, (new DateOnly(ultimoAno, 12, 31).DayNumber - new DateOnly(ultimoAno - 9, 1, 1).DayNumber) + 1)
                     .Select(i => new DateOnly(ultimoAno - 9, 1, 1).AddDays(i)).ToList();

var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(60) };
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

string Url(DateOnly d, string versao, string sufixo) =>
    $"https://gpm1.gesdisc.eosdis.nasa.gov/opendap/GPM_L3/GPM_3IMERGDF.07/{d:yyyy}/{d:MM}/" +
    $"3B-DAY.MS.MRG.3IMERG.{d:yyyyMMdd}-S000000-E235959.{versao}.nc4{sufixo}";

// 1) confere a ordem das dimensões antes de baixar 3653 dias (lon e lat trocados dariam dado errado sem erro)
var dds = await http.GetStringAsync(Url(dias[0], "V07B", ".dds"));
var linha = dds.Split('\n').First(l => l.Contains("precipitation["));
Console.WriteLine("DDS: " + linha.Trim());
if (!(linha.IndexOf("lon") < linha.IndexOf("lat") && linha.IndexOf("time") < linha.IndexOf("lon")))
    throw new Exception("ordem das dimensões inesperada; ajuste o índice antes de continuar");

// 2) retoma: carrega os CSVs já baixados e só pede os dias que faltam em algum pixel das cidades
var chuva = new ConcurrentDictionary<(int Lon, int Lat), ConcurrentDictionary<DateOnly, double>>();
var pasta = Path.Combine("data", "imerg");
string Nome((int Lon, int Lat) px) =>
    $"{(px.Lat * 0.1 - 89.95).ToString("F2", inv)}_{(px.Lon * 0.1 - 179.95).ToString("F2", inv)}.csv";
var pixels = cidades.Select(c => (IdxLon(c.Lon), IdxLat(c.Lat))).Distinct().ToList();
foreach (var px in pixels)
{
    var arq = Path.Combine(pasta, Nome(px));
    if (!File.Exists(arq)) continue;
    var serie = chuva.GetOrAdd(px, _ => new());
    foreach (var c in File.ReadLines(arq).Skip(1).Select(l => l.Split(',')).Where(c => c.Length == 2))
        serie[DateOnly.Parse(c[0], inv)] = double.Parse(c[1], inv);
}
var faltam = dias.Where(d => pixels.Any(px => !(chuva.GetValueOrDefault(px)?.ContainsKey(d) ?? false))).ToList();
Console.WriteLine($"faltam {faltam.Count} de {dias.Count} dias");

// 3) baixa dia a dia, 4 em paralelo
int feitos = 0;
var falhas = new ConcurrentBag<DateOnly>();
var naoPublicado = new ConcurrentBag<DateOnly>();

await Parallel.ForEachAsync(faltam, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (d, ct) =>
{
    var consulta = $".ascii?precipitation[0:0][{lon0}:{lon1}][{lat0}:{lat1}]";
    for (var tentativa = 1; tentativa <= 6; tentativa++)
    {
        try
        {
            var r = await http.GetAsync(Url(d, "V07B", consulta), ct);
            if (r.StatusCode == HttpStatusCode.NotFound) r = await http.GetAsync(Url(d, "V07A", consulta), ct);
            if (r.StatusCode == HttpStatusCode.NotFound) { naoPublicado.Add(d); break; }  // Final ainda não saiu (~3,5 meses)
            if ((int)r.StatusCode is >= 300 and < 400)
                throw new Exception("redirecionou para login: token inválido ou app GESDISC não autorizado");
            r.EnsureSuccessStatusCode();

            // Hyrax rotula por coordenada:
            //   "precipitation.lat, -19.05, -18.95, ..."
            //   "precipitation.precipitation[precipitation.time=13144][precipitation.lon=-48.25], 12.1, 13.0, ..."
            var linhas = (await r.Content.ReadAsStringAsync(ct)).Split('\n');
            var lats = linhas.First(l => l.StartsWith("precipitation.lat")).Split(',').Skip(1)
                             .Select(s => double.Parse(s.Trim(), inv)).ToArray();
            foreach (var l in linhas.Where(l => l.Contains("lon=")))
            {
                var i = l.IndexOf("lon=") + 4;
                var lon = double.Parse(l[i..l.IndexOf(']', i)], inv);
                var vals = l.Split(',').Skip(1).ToArray();
                for (var k = 0; k < vals.Length && k < lats.Length; k++)
                {
                    var v = double.Parse(vals[k].Trim(), inv);
                    if (v < 0) continue;  // fill value (-9999.9)
                    chuva.GetOrAdd((IdxLon(lon), IdxLat(lats[k])), _ => new())[d] = v;
                }
            }
            break;
        }
        catch (Exception e) when (tentativa < 6 && !e.Message.StartsWith("redirecionou"))
        {
            await Task.Delay(1000 * (1 << tentativa), ct);  // 2, 4, 8, 16, 32 s
        }
        catch (Exception e)
        {
            falhas.Add(d);
            Console.WriteLine($"{d}: {e.Message}");
            if (e.Message.StartsWith("redirecionou")) throw;
            break;
        }
    }
    var n = Interlocked.Increment(ref feitos);
    if (n % 100 == 0) Console.WriteLine($"{n}/{faltam.Count} dias");
});

// 4) um CSV por pixel das cidades (mesmo com falhas: a próxima rodada completa)
Directory.CreateDirectory(pasta);
foreach (var c in cidades)
{
    var px = (IdxLon(c.Lon), IdxLat(c.Lat));
    var serie = chuva.GetValueOrDefault(px) ?? new();
    File.WriteAllLines(Path.Combine(pasta, Nome(px)),
        ["data,mm", .. serie.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key:yyyy-MM-dd},{kv.Value.ToString("0.##", inv)}")]);
    Console.WriteLine($"{c.Nome}: {Nome(px)}, {serie.Count} dias, {serie.Values.Sum() / 10:F0} mm/ano");
}
Console.WriteLine($"não publicado: {naoPublicado.Count} dias");
Console.WriteLine($"falhas: {falhas.Count} dias");
