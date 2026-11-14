using System.Globalization;
using System.Net;
using System.Text.Json;

// testes adversariais do leitor de números da query (Numeros.cs, commit 1cce046) rota por rota:
// cada número de cada rota recebe as mesmas escritas ruins; as boas precisam chegar com o mesmo valor na resposta.
[Collection("api")]
public class NumerosRotasTests(ApiFabrica api)
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly string Lat = Http.Q(ApiFabrica.Lat), Lon = Http.Q(ApiFabrica.Lon);

    // rota → (query base válida, números que a rota lê)
    static readonly Dictionary<string, (string Base, string[] Numeros)> Rotas = new()
    {
        ["/clima"] = ($"lat={Lat}&lon={Lon}", ["lat", "lon"]),
        ["/deficit"] = ($"lat={Lat}&lon={Lon}&plantio=02-15&cultura=milho&cad=30&fatorChuva=1", ["lat", "lon", "cad", "fatorChuva"]),
        ["/recomendacao"] = ($"lat={Lat}&lon={Lon}&inicio=01-01&fim=01-10&cad=30&fatorChuva=1&riscoMax=0.2", ["lat", "lon", "cad", "fatorChuva", "riscoMax"]),
        ["/plano-colheita"] = ($"lat={Lat}&lon={Lon}&inicio=2026-01-05&area=100&haDia=60&colhedoras=1&uteis=70&cad=30&riscoMax=0.2",
                               ["lat", "lon", "area", "haDia", "colhedoras", "uteis", "cad", "riscoMax"]),
    };

    // troca (ou põe) o valor cru de um parâmetro na query base
    static string Url(string rota, string nome, string? cru)
    {
        var partes = Rotas[rota].Base.Split('&').Where(p => !p.StartsWith(nome + "=", StringComparison.Ordinal)).ToList();
        if (cru is not null) partes.Add(cru);
        return $"{rota}?{string.Join('&', partes)}";
    }

    async Task<(HttpStatusCode Status, string Corpo)> Get(string url)
    {
        var r = await api.Anonimo().GetAsync(url);
        return (r.StatusCode, await r.Content.ReadAsStringAsync());
    }

    async Task Da400(string url)
    {
        var (s, corpo) = await Get(url);
        Assert.True(s == HttpStatusCode.BadRequest, $"{url} -> {(int)s}: {corpo}");
    }

    // ---------- escritas ruins, em todo número de toda rota ----------

    // valor cru já codificado para a URL (o que chega ao servidor depois de decodificar está no comentário)
    public static readonly string[] Ruins =
    [
        "",            // vazio
        "%20",         // " "
        "+",           // "+" na query vira espaço
        "1,5",         // vírgula
        "1%2C5",       // vírgula codificada
        "1.000,5",     // milhar pt-BR
        "1,000.5",     // milhar en-US
        "NaN", "-nan", "Infinity", "-INFINITY", "1e309", "-1e309",
        "%E2%88%9E",   // "∞"
        "%E2%88%921",  // "−1" com sinal de menos Unicode
        "%EF%BC%91",   // "１" largura total
        "1%C2%A00",    // "1 0" com espaço inseparável
        "1%200",       // "1 0"
        "0x10", "1e", "e1", "1e1.5", "1..0", "--1", "%2B-1", "1-", "(1)", "abc", "1d", "1_0",
    ];

    public static IEnumerable<object[]> RuinsPorRota() =>
        from r in Rotas from n in r.Value.Numeros from v in Ruins select new object[] { r.Key, n, v };

    [Theory]
    [MemberData(nameof(RuinsPorRota))]
    public async Task Escrita_ruim_da_400_em_toda_rota(string rota, string nome, string cru) =>
        await Da400(Url(rota, nome, $"{nome}={cru}"));

    // parâmetro sem "=" (?cad) chega como vazio
    public static IEnumerable<object[]> NumerosPorRota() =>
        from r in Rotas from n in r.Value.Numeros select new object[] { r.Key, n };

    [Theory]
    [MemberData(nameof(NumerosPorRota))]
    public async Task Parametro_sem_igual_da_400(string rota, string nome) => await Da400(Url(rota, nome, nome));

    // nome codificado na URL é decodificado antes do leitor: "c%61d" = "cad"
    [Theory]
    [MemberData(nameof(NumerosPorRota))]
    public async Task Nome_codificado_e_conferido(string rota, string nome) =>
        await Da400(Url(rota, nome, $"{Uri.EscapeDataString(nome[..1])}%{(int)nome[1]:X2}{nome[2..]}=1,5"));

    [Theory]
    [MemberData(nameof(NumerosPorRota))]
    public async Task Nome_em_maiusculas_e_conferido(string rota, string nome) =>
        await Da400(Url(rota, nome, $"{nome.ToUpperInvariant()}=1,5"));

    // ---------- bordas de cada faixa, em cada rota que lê o número ----------

    public static IEnumerable<object[]> ForaDaFaixa()
    {
        var fora = new Dictionary<string, string[]>
        {
            ["lat"] = ["90.0000001", "-90.0000001", "91", "-1555"],
            ["lon"] = ["180.0000001", "-180.0000001", "-5555"],
            ["cad"] = ["0", "-0", "%2B0", "0e9", "-1", "500.0000001", "5e3"],
            ["riscoMax"] = ["-0.0000001", "1.0000001", "2", "-1", "1e1"],
            ["fatorChuva"] = ["-0.0000001", "5.0000001", "-1", "1e1"],
            ["area"] = ["0", "-0", "-1", "1000000.001", "1e7"],
            ["haDia"] = ["0", "-0", "-60", "1000.0001", "1e4"],
            ["colhedoras"] = ["0", "-1", "101", "1.5", "2147483648"],
            ["uteis"] = ["0", "-0", "-70", "100.0001", "1e3"],
        };
        foreach (var (rota, (_, nums)) in Rotas)
            foreach (var n in nums)
                foreach (var v in fora[n])
                    yield return [rota, n, v];
    }

    [Theory]
    [MemberData(nameof(ForaDaFaixa))]
    public async Task Fora_da_faixa_da_400(string rota, string nome, string cru) =>
        await Da400(Url(rota, nome, $"{nome}={cru}"));

    // ---------- escritas boas chegam com o valor certo (a resposta ecoa o número) ----------

    // (rota, parâmetro, valor cru, campo ecoado, valor esperado)
    public static readonly TheoryData<string, string, string, string, double> Boas = new()
    {
        { "/recomendacao", "cad", "%2B30", "cad", 30 },             // "+30"
        { "/recomendacao", "cad", "+30", "cad", 30 },               // " 30"
        { "/recomendacao", "cad", "%0930%0A", "cad", 30 },          // tab e quebra de linha em volta
        { "/recomendacao", "cad", "3e1", "cad", 30 },
        { "/recomendacao", "cad", "3E%2B1", "cad", 30 },            // "3E+1"
        { "/recomendacao", "cad", "300e-1", "cad", 30 },
        { "/recomendacao", "cad", "30.", "cad", 30 },
        { "/recomendacao", "cad", "0030", "cad", 30 },
        { "/recomendacao", "cad", "500", "cad", 500 },
        { "/recomendacao", "riscoMax", ".25", "riscoMax", 0.25 },
        { "/recomendacao", "riscoMax", "-0", "riscoMax", 0 },
        { "/recomendacao", "riscoMax", "1e0", "riscoMax", 1 },
        { "/recomendacao", "riscoMax", "25e-2", "riscoMax", 0.25 },
        { "/deficit", "fatorChuva", "%2B1.5", "fatorChuva", 1.5 },
        { "/deficit", "fatorChuva", "5", "fatorChuva", 5 },
        { "/deficit", "cad", "4.5e1", "cad", 45 },
        { "/plano-colheita", "area", "1e2", "areaHa", 100 },
        { "/plano-colheita", "area", "%2B100.0", "areaHa", 100 },
        { "/plano-colheita", "riscoMax", "0.30", "riscoMax", 0.3 },
        { "/plano-colheita", "haDia", "6e1", "capacidadeHaDia", 42 },   // 60 × 1 × 70%
        { "/plano-colheita", "uteis", "1e2", "capacidadeHaDia", 60 },
        { "/plano-colheita", "colhedoras", "%2B2", "capacidadeHaDia", 84 },
        { "/plano-colheita", "colhedoras", "100", "capacidadeHaDia", 4200 },
    };

    [Theory]
    [MemberData(nameof(Boas))]
    public async Task Escrita_boa_chega_com_o_valor_certo(string rota, string nome, string cru, string campo, double esperado)
    {
        var (s, corpo) = await Get(Url(rota, nome, $"{nome}={cru}"));
        Assert.True(s == HttpStatusCode.OK, $"{nome}={cru} -> {(int)s}: {corpo}");
        Assert.Equal(esperado, JsonDocument.Parse(corpo).RootElement.GetProperty(campo).GetDouble(), 9);
    }

    // inteiro escrito como decimal: o leitor aceita (2.0 é inteiro) e o binding de int recusa; tem que ser 400, nunca 500
    [Theory]
    [InlineData("2.0")]
    [InlineData("1e1")]
    [InlineData("%2B2.00")]
    [InlineData("1&colhedoras=1")]   // repetido: o int junta "1,1" e recusa (o double não; ver Repetido_nao_vira_outro_numero)
    public async Task Colhedoras_inteiro_com_ponto_da_400_e_nao_500(string cru) =>
        await Da400(Url("/plano-colheita", "colhedoras", $"colhedoras={cru}"));

    // ---------- números minúsculos que o leitor aceita não podem derrubar a rota ----------

    [Theory]
    [InlineData("/recomendacao", "cad", "5e-324")]
    [InlineData("/recomendacao", "cad", "1e-300")]
    [InlineData("/deficit", "cad", "5e-324")]
    [InlineData("/recomendacao", "fatorChuva", "5e-324")]
    [InlineData("/recomendacao", "riscoMax", "5e-324")]
    [InlineData("/plano-colheita", "cad", "5e-324")]
    [InlineData("/plano-colheita", "area", "5e-324")]
    [InlineData("/plano-colheita", "haDia", "5e-324")]
    [InlineData("/plano-colheita", "uteis", "5e-324")]
    [InlineData("/plano-colheita", "riscoMax", "5e-324")]
    public async Task Subnormal_aceito_nao_da_500(string rota, string nome, string cru)
    {
        var (s, corpo) = await Get(Url(rota, nome, $"{nome}={cru}"));
        Assert.True(s is HttpStatusCode.OK or HttpStatusCode.BadRequest, $"{nome}={cru} -> {(int)s}: {corpo}");
    }

    // ---------- parâmetro repetido ----------
    // O binding de um double junta os valores repetidos com vírgula ("30" e "5" viram "30,5")
    // e lê com AllowThousands (305). O leitor confere cada valor sozinho, então a soma passa sem conferência.
    // Certo: 400, ou a rota usar um dos valores informados (nunca um terceiro número).

    public static readonly TheoryData<string, string, string, string, double[]> Repetidos = new()
    {
        { "/recomendacao", "cad", "cad=30&cad=5", "cad", [30, 5] },
        { "/recomendacao", "cad", "cad=400&cad=500", "cad", [400, 500] },      // 400500: muito acima do teto 500
        { "/recomendacao", "riscoMax", "riscoMax=1&riscoMax=0", "riscoMax", [1, 0] },   // 10
        { "/deficit", "fatorChuva", "fatorChuva=1&fatorChuva=5", "fatorChuva", [1, 5] }, // 15
        { "/plano-colheita", "area", "area=100&area=0.5", "areaHa", [100, 0.5] },
        { "/plano-colheita", "haDia", "haDia=10&haDia=5", "capacidadeHaDia", [7, 3.5] },    // 105 → 73.5 ha/dia (capacidade = haDia × 70%)
    };

    [Theory(Skip = "BUG: parâmetro repetido é juntado com vírgula pelo binding (cad=30&cad=5 vira 305) e escapa da faixa do leitor")]
    [MemberData(nameof(Repetidos))]
    public async Task Repetido_nao_vira_outro_numero(string rota, string nome, string crus, string campo, double[] informados)
    {
        var (s, corpo) = await Get(Url(rota, nome, crus));
        if (s == HttpStatusCode.BadRequest) return;
        Assert.True(s == HttpStatusCode.OK, $"{crus} -> {(int)s}: {corpo}");
        var usado = JsonDocument.Parse(corpo).RootElement.GetProperty(campo).GetDouble();
        Assert.True(informados.Any(v => Math.Abs(v - usado) < 1e-9),
            $"{crus} -> rota usou {campo}={usado.ToString(Inv)}");
    }

    // o mesmo valor repetido não pode virar outro número (30 e 30 → "30,30" → 3030)
    [Theory(Skip = "BUG: cad=30&cad=30 chega como 3030 na rota (fora do teto 500 do leitor)")]
    [InlineData("/recomendacao")]
    [InlineData("/deficit")]
    public async Task Mesmo_valor_repetido_nao_muda(string rota)
    {
        var (s, corpo) = await Get(Url(rota, "cad", "cad=30&cad=30"));
        if (s == HttpStatusCode.BadRequest) return;
        Assert.Equal(30, JsonDocument.Parse(corpo).RootElement.GetProperty("cad").GetDouble());
    }

    // lat repetida vira coordenada fora do planeta: o leitor não vê -1555, a rota roda com ela
    [Theory(Skip = "BUG: lat=-15&lat=55 chega como -1555 (sem conferência de faixa depois do leitor)")]
    [InlineData("/clima")]
    [InlineData("/deficit")]
    [InlineData("/recomendacao")]
    [InlineData("/plano-colheita")]
    public async Task Lat_repetida_da_400(string rota) => await Da400(Url(rota, "lat", "lat=-15&lat=55"));

    // repetido com um valor ruim: todos são conferidos (o ruim pode vir primeiro ou depois)
    [Theory]
    [InlineData("cad=abc&cad=30")]
    [InlineData("cad=30&cad=abc")]
    [InlineData("cad=30&cad=")]
    [InlineData("cad=&cad=30")]
    [InlineData("cad=30&cad=NaN")]
    [InlineData("cad=30&cad=0")]
    [InlineData("cad=30&cad=1,5")]
    public async Task Repetido_com_um_ruim_da_400(string crus) => await Da400(Url("/recomendacao", "cad", crus));

    // ---------- rotas da fazenda e rotas que não leem números ----------

    [Theory]
    [InlineData("lat=1,5")]
    [InlineData("cad=NaN")]
    [InlineData("riscoMax=")]
    [InlineData("haDia=1e309")]
    public async Task Plano_da_fazenda_confere_a_query(string q)
    {
        var c = await api.Cliente();
        var f = (await c.PostOk("/fazendas", new { nome = "F", lat = ApiFabrica.Lat, lon = ApiFabrica.Lon })).Id();
        await c.PostOk($"/fazendas/{f}/colhedoras", new { nome = "C", haDia = 50 });
        await c.PostOk($"/fazendas/{f}/talhoes", new { nome = "T", areaHa = 10 });
        var r = await c.GetAsync($"/fazendas/{f}/plano-colheita?inicio=2026-01-05&{q}");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        // número bom de parâmetro que a rota nem lê passa
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"/fazendas/{f}/plano-colheita?inicio=2026-01-05&cad=30")).StatusCode);
    }

    // sem login, a autorização vem antes do leitor: 401, nunca 200
    [Fact]
    public async Task Fazenda_sem_login_com_numero_ruim_nao_passa()
    {
        var r = await api.Anonimo().GetAsync("/fazendas?lat=1,5");
        Assert.True(r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest, $"{(int)r.StatusCode}");
    }

    // o leitor vale em toda rota, até nas que não leem números (comportamento atual, documentado aqui)
    [Theory]
    [InlineData("/solos?cad=abc")]
    [InlineData("/solos?lat=1,5")]
    public async Task Rota_sem_numeros_tambem_recusa(string url) => await Da400(url);

    [Theory]
    [InlineData("/solos?cad=30")]
    [InlineData("/solos?ordem=1,2")]
    [InlineData("/solos?x=1,5")]
    public async Task Rota_sem_numeros_com_query_boa_passa(string url) =>
        Assert.Equal(HttpStatusCode.OK, (await api.Anonimo().GetAsync(url)).StatusCode);
}
