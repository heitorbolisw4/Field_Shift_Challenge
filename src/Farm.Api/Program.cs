using System.Globalization;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient(); // registra a fábrica de HttpClient
var app = builder.Build();

// o front fica em <raiz>/web; a API serve esses arquivos na mesma porta (sem CORS)
var web = new PhysicalFileProvider(Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "web")));
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = web });  // "/" abre web/index.html
app.UseStaticFiles(new StaticFileOptions { FileProvider = web });

Imerg.Pasta = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "data", "imerg"));
Smap.Pasta = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "data", "smap"));

// água no balde no dia do plantio: "cheio" (CAD toda) ou "smap" (umidade observada; dia sem dado -> cheio)
// "auto": smap se a cidade tem CSV do SMAP, senão cheio. Devolve o modo usado de fato (null = valor inválido).
string? Agua(string aguaInicial, double lat, double lon) => aguaInicial switch
{
    "auto" => Smap.Fc(lat, lon) is null ? "cheio" : "smap",
    "cheio" or "smap" => aguaInicial,
    _ => null
};
double AguaIni(string aguaInicial, double lat, double lon, DateOnly plantio) =>
    aguaInicial == "smap" ? Smap.Fracao(lat, lon, plantio) ?? 1 : 1;
const string AguaErro = "aguaInicial deve ser auto, cheio ou smap";

// clima diário do POWER; com fonteChuva=imerg, a chuva vem do CSV do IMERG. null = não tem IMERG para esse ponto.
// "auto": IMERG se tiver CSV do pixel, senão POWER. Devolve também a fonte usada de fato.
async Task<(List<Dia>? Dias, string Fonte)> Dados(IHttpClientFactory http, double lat, double lon, string fonte)
{
    var power = await Power.BaixarAsync(http.CreateClient(), lat, lon);
    if (fonte == "power") return (power, "power");
    if (fonte is not ("auto" or "imerg")) return (null, fonte);
    var imerg = Imerg.TrocarChuva(power, lat, lon);
    return fonte == "auto" && imerg is null ? (power, "power") : (imerg, "imerg");
}
IResult SemImerg(double lat, double lon, string fonte) => fonte != "imerg"
    ? Results.BadRequest("fonteChuva deve ser auto, imerg ou power")
    : Results.BadRequest($"sem dados do IMERG para este ponto: falta {Path.GetFileName(Imerg.Arquivo(lat, lon))} em data/imerg");

// passo 4: média por mês, para checar se os dados fazem sentido
app.MapGet("/clima", async (double lat, double lon, IHttpClientFactory http, string fonteChuva = "auto") =>
{
    (var validos, fonteChuva) = await Dados(http, lat, lon, fonteChuva);
    if (validos is null) return SemImerg(lat, lon, fonteChuva);

    var porMes = validos
        .GroupBy(d => d.Data.Month)
        .OrderBy(g => g.Key)
        .Select(g => new {
            mes = g.Key,
            et0Hs = Math.Round(g.Average(d => Clima.Et0(d, lat)), 2),    // Hargreaves, mm/dia
            et0Pm = Math.Round(g.Average(d => Clima.Et0Pm(d, lat)), 2),  // Penman-Monteith, mm/dia
            chuvaMes = Math.Round(g.Sum(d => d.Chuva) / 10, 0)          // mm/mês, média dos anos
        });

    return Results.Ok(new { fonteChuva, validos = validos.Count, porMes });
});

// cad padrão 30: calibrado contra o ZARC com chuva do IMERG
// passo 5/6: déficit de UMA cultura plantada numa data, ano a ano (com balde de solo)
// ex.: /deficit?lat=-19.03&lon=-47.92&plantio=02-15&cultura=milho&cad=30
app.MapGet("/deficit", async (double lat, double lon, string plantio, string cultura, IHttpClientFactory http, double cad = 30, double fatorChuva = 1, string et0 = "pm", string fonteChuva = "auto", string aguaInicial = "auto") =>
{
    if (Agua(aguaInicial, lat, lon) is not { } agua) return Results.BadRequest(AguaErro);
    aguaInicial = agua;
    if (!Culturas.Todas.TryGetValue(cultura, out var c))
        return Results.BadRequest($"cultura desconhecida: {cultura}");

    var md = DateOnly.ParseExact($"2000-{plantio}", "yyyy-MM-dd", CultureInfo.InvariantCulture); // "02-15" vira mês/dia
    (var lista, fonteChuva) = await Dados(http, lat, lon, fonteChuva);
    if (lista is null) return SemImerg(lat, lon, fonteChuva);
    var dias = lista.ToDictionary(d => d.Data);

    var anos = new List<object>();
    foreach (var ano in dias.Keys.Select(d => d.Year).Distinct().Order())
    {
        var ini = new DateOnly(ano, md.Month, Math.Min(md.Day, DateTime.DaysInMonth(ano, md.Month))); // 29/02 em ano normal
        var s = Modelo.Simular(c, dias, ini, lat, cad, fatorChuva, et0 == "pm", AguaIni(aguaInicial, lat, lon, ini));
        if (s is null) continue; // ciclo passou do fim dos dados

        anos.Add(new { ano, etc = Math.Round(s.Etc), chuva = Math.Round(s.Chuva),
                       deficit = Math.Round(s.Deficit), s.DiasSede,
                       perda = Math.Round(Modelo.Perda(c, s), 2),
                       isnaI = Math.Round(s.IsnaI, 2), isnaIII = Math.Round(s.IsnaIII, 2),
                       passaZarc = Modelo.Passa(c, s, "isna") });
    }

    return Results.Ok(new { cultura = c.Nome, plantio, ciclo = c.Ciclo, cad, fatorChuva, et0, fonteChuva, aguaInicial, anos });
});

// passo 10: calendário por DECÊNDIO (blocos de 10 dias, como o ZARC), com nível de risco
// ex.: /recomendacao?lat=-19.03&lon=-47.92&inicio=01-20&fim=03-20
app.MapGet("/recomendacao", async (double lat, double lon, string inicio, string fim, IHttpClientFactory http,
                                   double cad = 30, double fatorChuva = 1, string et0 = "pm",
                                   string criterio = "isna", double riscoMax = 0.2, string fonteChuva = "auto",
                                   string aguaInicial = "auto") =>
{
    if (Agua(aguaInicial, lat, lon) is not { } agua) return Results.BadRequest(AguaErro);
    aguaInicial = agua;
    var inv = CultureInfo.InvariantCulture;
    var di = DateOnly.ParseExact($"2001-{inicio}", "yyyy-MM-dd", inv); // 2001: ano qualquer, não bissexto
    var df = DateOnly.ParseExact($"2001-{fim}", "yyyy-MM-dd", inv);
    if (df < di) df = df.AddYears(1);  // janela cruza o ano (ex.: dez -> mar): o fim vai para 2002

    (var lista, fonteChuva) = await Dados(http, lat, lon, fonteChuva);
    if (lista is null) return SemImerg(lat, lon, fonteChuva);
    var dias = lista.ToDictionary(d => d.Data);
    var anos = dias.Keys.Select(d => d.Year).Distinct().Order().ToList();

    var decendios = new List<object>();
    for (var d = di; d <= df; )
    {
        // decêndio: dias 1–10, 11–20 ou 21–fim do mês
        var ini = new DateOnly(d.Year, d.Month, d.Day <= 10 ? 1 : d.Day <= 20 ? 11 : 21);
        var fimDec = d.Day <= 20 ? ini.AddDays(9) : new DateOnly(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));

        // risco = fração de (dia de plantio × ano) em que a cultura falha no critério
        var risco = new Dictionary<string, double>();
        foreach (var c in Culturas.Graos)
        {
            int total = 0, falhas = 0;
            for (var p = ini; p <= fimDec; p = p.AddDays(1))
                foreach (var a in anos)
                {
                    var plantio = new DateOnly(a, p.Month, p.Day); // cada decêndio usa os mesmos 10 anos, com ou sem virada de ano
                    var s = Modelo.Simular(c, dias, plantio, lat, cad, fatorChuva, et0 == "pm", AguaIni(aguaInicial, lat, lon, plantio));
                    if (s is null) continue;                 // ciclo passou do fim dos dados
                    total++;
                    if (!Modelo.Passa(c, s, criterio)) falhas++;
                }
            risco[c.Nome] = total == 0 ? 1 : (double)falhas / total;
        }

        // a cultura mais exigente cujo risco cabe no limite; senão, braquiária
        var escolha = Culturas.Graos.FirstOrDefault(c => risco[c.Nome] <= riscoMax)?.Nome ?? "braquiária";

        decendios.Add(new {
            decendio = $"{ini.ToString("dd/MM", inv)} a {fimDec.ToString("dd/MM", inv)}",
            risco = risco.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value, 2)),
            nivel = risco.ToDictionary(kv => kv.Key, kv => Modelo.Nivel(kv.Value)),
            recomendacao = escolha
        });

        d = fimDec.AddDays(1);
    }

    return Results.Ok(new { cad, et0, criterio, riscoMax, fonteChuva, aguaInicial, decendios });
});

app.Run();
