using System.Net;
using System.Text.Json;

// /plano-colheita (sem cadastro) × /fazendas/{id}/plano-colheita com entradas sorteadas, e as bordas da validação de aacbfae.
// Semente fixa: reprodutível. Cada caso cria uma conta e uma fazenda, então os laços são curtos (dezenas, não milhares);
// a conta pura com milhares de entradas está em Farm.Core.Tests/PropriedadesTests.
[Collection("api")]
public class ConsistenciaTests(ApiFabrica api)
{
    const double Lat = ApiFabrica.Lat, Lon = ApiFabrica.Lon;
    static readonly string Ponto = $"lat={Http.Q(Lat)}&lon={Http.Q(Lon)}";
    const string Inicio = "2026-01-05";
    static long Cent(double x) => (long)Math.Round(x * 100, MidpointRounding.AwayFromZero);

    // fazenda com n colhedoras iguais (haDia cada) e talhões AD3 (cad 30) sem coordenada própria:
    // mesmo ponto, mesma cad e mesmo riscoMax (0,2) que os padrões de /plano-colheita
    async Task<(HttpClient C, int F)> Fazenda(double uteis, double haDia, int colhedoras, params double[] areas)
    {
        var c = await api.Cliente();
        var f = (await c.PostOk("/fazendas", new { nome = "F", lat = Lat, lon = Lon, diasUteisPct = uteis })).Id();
        for (int i = 0; i < colhedoras; i++) await c.PostOk($"/fazendas/{f}/colhedoras", new { nome = $"C{i}", haDia });
        for (int i = 0; i < areas.Length; i++) await c.PostOk($"/fazendas/{f}/talhoes", new { nome = $"T{i}", areaHa = areas[i] });
        return (c, f);
    }

    static string SemCadastro(double area, double haDia, int colhedoras, double uteis) =>
        $"/plano-colheita?{Ponto}&inicio={Inicio}&area={Http.Q(area)}&haDia={Http.Q(haDia)}&colhedoras={colhedoras}&uteis={Http.Q(uteis)}";

    static string Txt(JsonElement t) =>
        $"{t.GetProperty("cultura").GetString()} {t.GetProperty("de").GetString()}..{t.GetProperty("ate").GetString()} " +
        $"{t.GetProperty("dias").GetInt32()}d {Cent(t.GetProperty("ha").GetDouble())}c";

    // ---------- consistência entre as duas rotas ----------

    // um talhão: fim, dias, capacidade e cada trecho (cultura, datas, ha) idênticos nas duas rotas
    [Theory]
    [InlineData(101)] [InlineData(102)] [InlineData(103)]
    public async Task Um_talhao_sem_cadastro_igual_a_fazenda(int semente)
    {
        var r = new Random(semente);
        for (int i = 0; i < 8; i++)
        {
            double haDia = Math.Round(1 + r.NextDouble() * 80, r.Next(0, 3));
            int col = r.Next(1, 4);
            double uteis = Math.Round(5 + r.NextDouble() * 95, r.Next(0, 2));
            double cap = Colheita.Capacidade(haDia * col, uteis);
            double area = Math.Round(0.01 + r.NextDouble() * cap * 40, r.Next(1, 4)) is > 0 and var a ? a : 0.01;
            var ctx = $"area={area:R} haDia={haDia:R} col={col} uteis={uteis:R}";

            var (c, f) = await Fazenda(uteis, haDia, col, area);
            var fz = await c.GetOk($"/fazendas/{f}/plano-colheita?inicio={Inicio}");
            var s = await c.GetOk(SemCadastro(area, haDia, col, uteis));
            var t = Assert.Single(fz.GetProperty("talhoes").EnumerateArray());

            Assert.True(fz.Data("fim") == s.Data("fim"), $"{ctx}: fim {fz.Data("fim")} x {s.Data("fim")}");
            Assert.True(t.GetProperty("dias").GetInt32() == s.GetProperty("dias").GetInt32(), $"{ctx}: dias");
            Assert.Equal(fz.GetProperty("capacidadeHaDia").GetDouble(), s.GetProperty("capacidadeHaDia").GetDouble());
            var a1 = t.GetProperty("trechos").EnumerateArray().Select(Txt).ToList();
            var a2 = s.GetProperty("trechos").EnumerateArray().Select(Txt).ToList();
            Assert.True(a1.SequenceEqual(a2), $"{ctx}:\nfazenda: {string.Join(" | ", a1)}\nsem cad: {string.Join(" | ", a2)}");

            // piso: soma ≤ área, perda < 0,01, nenhum trecho negativo
            var soma = a1.Count == 0 ? 0 : t.GetProperty("trechos").EnumerateArray().Sum(x => Cent(x.GetProperty("ha").GetDouble()));
            Assert.True(t.GetProperty("trechos").EnumerateArray().All(x => x.GetProperty("ha").GetDouble() >= 0), $"{ctx}: trecho negativo");
            Assert.True(soma == (long)Math.Floor(area * 100 + 1e-6), $"{ctx}: soma {soma}c");
        }
    }

    // vários talhões: a fazenda termina no mesmo dia que /plano-colheita com a área total
    [Theory]
    [InlineData(201)] [InlineData(202)]
    public async Task Varios_talhoes_terminam_com_a_area_total(int semente)
    {
        var r = new Random(semente);
        for (int i = 0; i < 6; i++)
        {
            double haDia = Math.Round(5 + r.NextDouble() * 60, r.Next(0, 2));
            int col = r.Next(1, 3);
            double uteis = r.Next(10, 101);
            double cap = Colheita.Capacidade(haDia * col, uteis);
            var areas = Enumerable.Range(0, r.Next(2, 7)).Select(_ => Math.Round(0.01 + r.NextDouble() * cap * 6, 2)).ToArray();

            var (c, f) = await Fazenda(uteis, haDia, col, areas);
            var fz = await c.GetOk($"/fazendas/{f}/plano-colheita?inicio={Inicio}");
            var s = await c.GetOk(SemCadastro(areas.Sum(), haDia, col, uteis));
            Assert.True(fz.Data("fim") == s.Data("fim"),
                $"cap={cap:R} areas=[{string.Join(", ", areas)}]: fazenda {fz.Data("fim")} x sem cadastro {s.Data("fim")}");

            // por talhão e no total: piso exato
            foreach (var (t, a) in fz.GetProperty("talhoes").EnumerateArray().Zip(areas))
                Assert.Equal(Cent(a), t.GetProperty("trechos").EnumerateArray().Sum(x => Cent(x.GetProperty("ha").GetDouble())));
            Assert.Equal(areas.Sum(Cent), fz.GetProperty("totais").EnumerateArray().Sum(x => Cent(x.GetProperty("ha").GetDouble())));
        }
    }

    // ---------- limite de 365 dias: as duas rotas no mesmo lugar ----------

    [Theory]
    [InlineData(365, true)]       // 365 dias exatos
    [InlineData(364.99, true)]
    [InlineData(365.01, false)]
    [InlineData(366, false)]
    public async Task Limite_de_365_dias_igual_nas_duas_rotas(double area, bool cabe)
    {
        var (c, f) = await Fazenda(100, 1, 1, area);
        var fz = await c.GetAsync($"/fazendas/{f}/plano-colheita?inicio={Inicio}");
        var s = await c.GetAsync(SemCadastro(area, 1, 1, 100));
        var esperado = cabe ? HttpStatusCode.OK : HttpStatusCode.BadRequest;
        Assert.Equal(esperado, fz.StatusCode);
        Assert.Equal(esperado, s.StatusCode);
        if (cabe) Assert.Equal(365, (await s.Json()).GetProperty("dias").GetInt32());
    }

    // área que dá 365 dias por Dias (com a tolerância) mas cujo área/capacidade em double passa de 365
    // por ruído (ex.: capacidade 3,3 = 10 × 33 / 100). A checagem nova (área / capacidade > 365) não tem a tolerância.
    [Fact(Skip = "BUG: checagem 'area / capacidade > 365' sem a tolerância de Dias/Fila: 365 dias exatos com capacidade ruidosa dão 400")]
    public async Task Ruido_de_ponto_flutuante_no_limite_de_365()
    {
        var (haDia, uteis, area) = AchaRuidoEm365();
        var (c, f) = await Fazenda(uteis, haDia, 1, area);
        Assert.Equal(365, Colheita.Dias(area, Colheita.Capacidade(haDia, uteis)));
        var ctx = $"haDia={haDia:R} uteis={uteis} area={area:R}";
        Assert.True((await c.GetAsync(SemCadastro(area, haDia, 1, uteis))).StatusCode == HttpStatusCode.OK, $"sem cadastro, {ctx}");
        Assert.True((await c.GetAsync($"/fazendas/{f}/plano-colheita?inicio={Inicio}")).StatusCode == HttpStatusCode.OK, $"fazenda, {ctx}");
    }

    // o caso existe (não é teste vazio): acha haDia/uteis/área com 2 casas em que 365 × cap é a área exata
    // mas área / cap > 365 em double
    static (double HaDia, double Uteis, double Area) AchaRuidoEm365()
    {
        for (int h = 1; h <= 2000; h++)
            for (int u = 1; u <= 100; u++)
            {
                double haDia = h / 10.0, cap = Colheita.Capacidade(haDia, u), area = Math.Round(365 * cap, 2);
                if (Math.Abs(area - 365 * cap) < 1e-9 && area / cap > 365) return (haDia, u, area);
            }
        throw new InvalidOperationException("nenhum caso com ruído");
    }

    [Fact]
    public void Existe_caso_de_ruido_em_365() => Assert.True(AchaRuidoEm365().Area > 0);

    // ---------- bordas da validação de /plano-colheita ----------

    [Theory]
    [InlineData("area=0")]
    [InlineData("area=-0")]
    [InlineData("area=-1e-12")]
    [InlineData("area=100&haDia=0")]
    [InlineData("area=100&haDia=-0")]
    [InlineData("area=100&haDia=-1e-300")]
    [InlineData("area=100&colhedoras=0")]
    [InlineData("area=100&uteis=0")]
    [InlineData("area=100&uteis=-0")]
    [InlineData("area=100&uteis=100.000000001")]
    [InlineData("area=100&uteis=1e-300")]     // passa na faixa, mas a capacidade some: > 365 dias
    [InlineData("area=100&haDia=1e-300")]
    [InlineData("area=Infinity")]
    [InlineData("area=1e308&haDia=1e-308")]  // área/capacidade = Infinity
    [InlineData("fim=2026-01-04")]           // fim antes do início
    [InlineData("fim=2027-01-05")]           // 366 dias
    public async Task Bordas_que_dao_400(string q)
    {
        var r = await api.Anonimo().GetAsync($"/plano-colheita?{Ponto}&inicio={Inicio}&{q}");
        Assert.True(r.StatusCode == HttpStatusCode.BadRequest, $"{q} -> {(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
    }

    [Theory]
    [InlineData("area=100&uteis=100")]
    [InlineData("area=100&uteis=99.999999999")]
    [InlineData("area=1e-9")]                  // migalha: 1 dia, 0,00 ha
    [InlineData("area=0.01&haDia=1e9")]        // capacidade enorme: 1 dia
    [InlineData("area=100&colhedoras=1")]
    [InlineData("area=100&uteis=0.5&haDia=60")] // 0,3 ha/dia: 334 dias, cabe
    [InlineData("fim=2026-01-05")]             // 1 dia, sem área
    [InlineData("fim=2027-01-04")]             // 365 dias, sem área
    public async Task Bordas_que_dao_200(string q)
    {
        var r = await api.Anonimo().GetAsync($"/plano-colheita?{Ponto}&inicio={Inicio}&{q}");
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"{q} -> {(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        var j = await r.Json();
        Assert.InRange(j.GetProperty("dias").GetInt32(), 1, 365);
        foreach (var t in j.GetProperty("trechos").EnumerateArray())
            if (t.GetProperty("ha").ValueKind == JsonValueKind.Number) Assert.True(t.GetProperty("ha").GetDouble() >= 0);
    }

    // NaN passa em todas as comparações (NaN <= 0 é falso, NaN > 365 é falso): a validação nova não pega.
    // O binding aceita "NaN" e "Infinity" em double. Resultado esperado: 400, não 500 nem 200 com lixo.
    [Theory(Skip = "BUG: /plano-colheita aceita NaN/Infinity em area/haDia/uteis (comparações com NaN são falsas); vira 500 ou 200 com dados sem sentido")]
    [InlineData("area=NaN")]
    [InlineData("area=100&haDia=NaN")]
    [InlineData("area=100&uteis=NaN")]
    [InlineData("area=100&haDia=Infinity")]
    public async Task NaN_e_Infinity_dao_400(string q)
    {
        var r = await api.Anonimo().GetAsync($"/plano-colheita?{Ponto}&inicio={Inicio}&{q}");
        Assert.True(r.StatusCode == HttpStatusCode.BadRequest, $"{q} -> {(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
    }
}
