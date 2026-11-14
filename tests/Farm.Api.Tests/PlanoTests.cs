using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

[Collection("api")]
public class PlanoTests(ApiFabrica api)
{
    const double Lat = ApiFabrica.Lat, Lon = ApiFabrica.Lon;

    // fazenda com colhedoras somando `haDia` e diasUteisPct `uteis`; talhões (área, solo) na ordem dada
    async Task<(HttpClient C, int F, List<int> T)> Fazenda(double uteis, double[] colhedoras, params (double Area, string Solo)[] talhoes)
    {
        var c = await api.Cliente();
        var f = (await c.PostOk("/fazendas", new { nome = "F", lat = Lat, lon = Lon, diasUteisPct = uteis, riscoMax = 0.2 })).Id();
        foreach (var h in colhedoras) await c.PostOk($"/fazendas/{f}/colhedoras", new { nome = "C", haDia = h });
        var ids = new List<int>();
        foreach (var (a, s) in talhoes) ids.Add((await c.PostOk($"/fazendas/{f}/talhoes", new { nome = $"T{ids.Count + 1}", areaHa = a, classeSolo = s })).Id());
        return (c, f, ids);
    }

    // cada dia do trecho tem a cultura que /recomendacao dá para o decêndio do dia, com a CAD do talhão
    static async Task ConfereCulturas(HttpClient c, JsonElement talhao, double cad, double riscoMax, double lat = Lat, double lon = Lon)
    {
        var de = talhao.Data("de"); var ate = talhao.Data("ate");
        var rec = await Apoio.Recomendacao(c, lat, lon, de, ate, cad, riscoMax);
        foreach (var tr in talhao.GetProperty("trechos").EnumerateArray())
            foreach (var d in Apoio.Dias(tr.Data("de"), tr.Data("ate")))
                Assert.Equal(rec[Apoio.DecendioDe(d)].GetProperty("recomendacao").GetString(), tr.GetProperty("cultura").GetString());
    }

    static double SomaHa(JsonElement trechos) => trechos.EnumerateArray().Sum(x => x.GetProperty("ha").GetDouble());

    // ---------- plano da fazenda ----------

    [Fact]
    public async Task Sequencia_capacidade_culturas_e_totais()
    {
        // capacidade = (60 + 40) × 50% = 50 ha/dia
        var (c, f, ids) = await Fazenda(50, [60, 40], (500, "AD3"), (375, "AD5"), (150, "AD1"));
        var p = await c.GetOk($"/fazendas/{f}/plano-colheita?inicio=2026-01-05");

        Assert.Equal(50, p.GetProperty("capacidadeHaDia").GetDouble());
        var ts = p.GetProperty("talhoes").EnumerateArray().ToList();
        Assert.Equal(ids, ts.Select(t => t.GetProperty("talhaoId").GetInt32()));       // ordem de cadastro

        // 500/50 = 10 dias, 375/50 = 7,5 → 8, 150/50 = 3; um começa no dia seguinte ao fim do outro
        Assert.Equal([10, 8, 3], ts.Select(t => t.GetProperty("dias").GetInt32()));
        Assert.Equal(new DateOnly(2026, 1, 5), ts[0].Data("de"));
        for (int i = 1; i < ts.Count; i++) Assert.Equal(ts[i - 1].Data("ate").AddDays(1), ts[i].Data("de"));
        Assert.Equal(new DateOnly(2026, 1, 25), p.Data("fim"));
        foreach (var t in ts) Assert.Equal(t.Data("ate").DayNumber - t.Data("de").DayNumber + 1, t.GetProperty("dias").GetInt32());

        // trechos cobrem o talhão sem buraco, ha = dias × capacidade (o último leva o resto) e a soma é a área
        foreach (var t in ts)
        {
            var tr = t.GetProperty("trechos").EnumerateArray().ToList();
            Assert.Equal(t.Data("de"), tr[0].Data("de"));
            Assert.Equal(t.Data("ate"), tr[^1].Data("ate"));
            for (int i = 1; i < tr.Count; i++) Assert.Equal(tr[i - 1].Data("ate").AddDays(1), tr[i].Data("de"));
            for (int i = 0; i < tr.Count - 1; i++) Assert.Equal(tr[i].GetProperty("dias").GetInt32() * 50.0, tr[i].GetProperty("ha").GetDouble());
            Assert.Equal(t.GetProperty("areaHa").GetDouble(), SomaHa(t.GetProperty("trechos")), 6);
        }

        // cultura de cada dia = /recomendacao com a CAD do solo de cada talhão
        await ConfereCulturas(c, ts[0], 30, 0.2);
        await ConfereCulturas(c, ts[1], 61, 0.2);
        await ConfereCulturas(c, ts[2], 17, 0.2);

        // totais por cultura = soma dos trechos, e somam a área da fazenda
        var totais = p.GetProperty("totais").EnumerateArray().ToDictionary(x => x.GetProperty("cultura").GetString()!, x => x.GetProperty("ha").GetDouble());
        var porCultura = ts.SelectMany(t => t.GetProperty("trechos").EnumerateArray())
            .GroupBy(x => x.GetProperty("cultura").GetString()!).ToDictionary(g => g.Key, g => Math.Round(g.Sum(x => x.GetProperty("ha").GetDouble()), 1));
        Assert.Equal(porCultura.OrderBy(x => x.Key), totais.OrderBy(x => x.Key));
        Assert.Equal(1025, totais.Values.Sum(), 6);
        Assert.True(totais.Count >= 2, "janela escolhida para pegar mais de uma cultura: " + string.Join(",", totais.Keys));
        Assert.Equal(totais.Values.OrderByDescending(x => x), totais.Values); // maior primeiro
    }

    [Fact]
    public async Task Ordem_informada_vem_primeiro_resto_por_cadastro()
    {
        var (c, f, ids) = await Fazenda(100, [100], (100, "AD3"), (100, "AD3"), (100, "AD3"), (100, "AD3"));
        async Task<List<int>> Ordem(string q) =>
            (await c.GetOk($"/fazendas/{f}/plano-colheita?inicio=2026-01-05&ordem={q}"))
                .GetProperty("talhoes").EnumerateArray().Select(t => t.GetProperty("talhaoId").GetInt32()).ToList();

        Assert.Equal([ids[2], ids[0], ids[1], ids[3]], await Ordem($"{ids[2]},{ids[0]}"));
        Assert.Equal([ids[3], ids[0], ids[1], ids[2]], await Ordem($" {ids[3]} , x, ,999999,{ids[3]}"));   // lixo, inexistente e repetido ignorados
        Assert.Equal(ids, await Ordem(""));
    }

    // a ordem muda as datas e, portanto, a cultura: o primeiro talhão sempre pega o começo da janela
    [Fact]
    public async Task Ordem_muda_quem_pega_a_janela_boa()
    {
        var (c, f, ids) = await Fazenda(100, [50], (500, "AD3"), (500, "AD6"));
        var p1 = await c.GetOk($"/fazendas/{f}/plano-colheita?inicio=2026-01-10");
        var p2 = await c.GetOk($"/fazendas/{f}/plano-colheita?inicio=2026-01-10&ordem={ids[1]}");
        Assert.Equal(new DateOnly(2026, 1, 10), p1.GetProperty("talhoes")[0].Data("de"));
        Assert.Equal(ids[1], p2.GetProperty("talhoes")[0].GetProperty("talhaoId").GetInt32());
        Assert.Equal(new DateOnly(2026, 1, 10), p2.GetProperty("talhoes")[0].Data("de"));
        Assert.Equal(p1.Data("fim"), p2.Data("fim"));
    }

    [Fact]
    public async Task Cruza_a_virada_do_ano()
    {
        var (c, f, _) = await Fazenda(100, [50], (600, "AD3"), (400, "AD3"));
        var p = await c.GetOk($"/fazendas/{f}/plano-colheita?inicio=2026-12-20");
        var ts = p.GetProperty("talhoes").EnumerateArray().ToList();
        Assert.Equal(new DateOnly(2026, 12, 31), ts[0].Data("ate"));       // 12 dias: 20/12 a 31/12
        Assert.Equal(new DateOnly(2027, 1, 1), ts[1].Data("de"));
        Assert.Equal(new DateOnly(2027, 1, 8), p.Data("fim"));
        foreach (var t in ts) await ConfereCulturas(c, t, 30, 0.2);
        Assert.Equal(1000, p.GetProperty("totais").EnumerateArray().Sum(x => x.GetProperty("ha").GetDouble()), 6);
    }

    [Fact]
    public async Task Passa_por_29_de_fevereiro()
    {
        var (c, f, _) = await Fazenda(100, [10], (50, "AD3"));
        var p = await c.GetOk($"/fazendas/{f}/plano-colheita?inicio=2028-02-27");
        var t = p.GetProperty("talhoes")[0];
        Assert.Equal(new DateOnly(2028, 3, 2), t.Data("ate"));
        await ConfereCulturas(c, t, 30, 0.2);
    }

    [Fact]
    public async Task Cada_talhao_usa_a_propria_coordenada()
    {
        var c = await api.Cliente();
        var f = (await c.PostOk("/fazendas", new { nome = "F", lat = Lat, lon = Lon })).Id();
        await c.PostOk($"/fazendas/{f}/colhedoras", new { nome = "C", haDia = 100 });
        await c.PostOk($"/fazendas/{f}/talhoes", new { nome = "longe", areaHa = 700, lat = -17.9, lon = -51.1 });
        var p = await c.GetOk($"/fazendas/{f}/plano-colheita?inicio=2026-01-15");
        await ConfereCulturas(c, p.GetProperty("talhoes")[0], 30, 0.2, -17.9, -51.1);
    }

    [Fact]
    public async Task Erros_do_plano()
    {
        var c = await api.Cliente();
        var f = (await c.PostOk("/fazendas", new { nome = "F", lat = Lat, lon = Lon })).Id();
        var url = $"/fazendas/{f}/plano-colheita?inicio=2026-01-15";

        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync(url)).StatusCode);         // sem colhedora
        await c.PostOk($"/fazendas/{f}/colhedoras", new { nome = "C", haDia = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync(url)).StatusCode);         // sem talhão
        var t = await c.PostOk($"/fazendas/{f}/talhoes", new { nome = "T", areaHa = 100000 });
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync(url)).StatusCode);         // > 365 dias
        await c.PutAsJsonAsync($"/talhoes/{t.Id()}", new { nome = "T", areaHa = 10 });
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/fazendas/" + f + "/plano-colheita")).StatusCode); // sem inicio
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync(url + "&fonteChuva=imerg")).StatusCode);

        // fazenda sem coordenada e talhão sem coordenada
        var g = (await c.PostOk("/fazendas", new { nome = "G" })).Id();
        await c.PostOk($"/fazendas/{g}/colhedoras", new { nome = "C", haDia = 10 });
        await c.PostOk($"/fazendas/{g}/talhoes", new { nome = "T", areaHa = 10 });
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync($"/fazendas/{g}/plano-colheita?inicio=2026-01-15")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/fazendas/999999/plano-colheita?inicio=2026-01-15")).StatusCode);
    }

    // ---------- borda conhecida: sobra de capacidade do último dia se perde ----------

    // 3 talhões de 20 ha, 50 ha/dia: a fazenda colheria 60 ha em 2 dias, mas o plano gasta 3 (um dia por talhão).
    // Documentado como simplificação no docs/api-fazenda.md, mas o efeito cresce com talhões pequenos:
    // 20 talhões de 10 ha com 100 ha/dia = 20 dias no plano contra 2 de capacidade, e os últimos talhões caem
    // em decêndios mais tardios, o que muda a cultura recomendada. Este teste trava o comportamento ATUAL.
    [Fact]
    public async Task Borda_proximo_talhao_so_comeca_no_dia_seguinte()
    {
        var (c, f, _) = await Fazenda(100, [50], (20, "AD3"), (20, "AD3"), (20, "AD3"));
        var p = await c.GetOk($"/fazendas/{f}/plano-colheita?inicio=2026-01-05");
        Assert.Equal(new DateOnly(2026, 1, 7), p.Data("fim"));                       // 3 dias
        Assert.All(p.GetProperty("talhoes").EnumerateArray(), t => Assert.Equal(1, t.GetProperty("dias").GetInt32()));

        // a mesma área sem cadastro (um bloco só) leva 2 dias
        var s = await c.GetOk($"/plano-colheita?lat={Http.Q(Lat)}&lon={Http.Q(Lon)}&inicio=2026-01-05&area=60&haDia=50&uteis=100");
        Assert.Equal(2, s.GetProperty("dias").GetInt32());
    }

    [Fact]
    public async Task Borda_muitos_talhoes_pequenos_esticam_o_plano()
    {
        var talhoes = Enumerable.Range(0, 20).Select(_ => (10.0, "AD3")).ToArray();
        var (c, f, _) = await Fazenda(100, [100], talhoes);
        var p = await c.GetOk($"/fazendas/{f}/plano-colheita?inicio=2026-01-15");
        var dias = p.Data("fim").DayNumber - new DateOnly(2026, 1, 15).DayNumber + 1;
        Assert.Equal(20, dias); // capacidade daria 2
        // e a cultura muda ao longo desses 20 dias (o decêndio de 21/01 em diante já é outro)
        Assert.True(p.GetProperty("totais").GetArrayLength() >= 2);
    }

    [Fact(Skip = "BORDA/BUG de modelo: sobra de capacidade não passa para o próximo talhão (ver teste acima)")]
    public async Task Desejado_plano_usa_a_capacidade_cheia()
    {
        var (c, f, _) = await Fazenda(100, [50], (20, "AD3"), (20, "AD3"), (20, "AD3"));
        var p = await c.GetOk($"/fazendas/{f}/plano-colheita?inicio=2026-01-05");
        Assert.Equal(new DateOnly(2026, 1, 6), p.Data("fim"));
    }

    // ---------- arredondamento: soma dos ha não pode passar da área ----------

    // Colheita.Trechos arredonda o resto do último trecho a 0,1 ha (AwayFromZero): área 10,37 vira 10,4.
    // Polígono dá área com 2 casas, então acontece de verdade. Excesso máximo 0,05 ha por talhão.
    [Fact(Skip = "BUG pequeno: soma dos trechos passa da área por arredondamento (10,37 ha → 10,4)")]
    public async Task Totais_nunca_passam_da_area()
    {
        var (c, f, _) = await Fazenda(100, [50], (10.37, "AD3"), (33.35, "AD3"));
        var p = await c.GetOk($"/fazendas/{f}/plano-colheita?inicio=2026-01-05");
        foreach (var t in p.GetProperty("talhoes").EnumerateArray())
            Assert.True(SomaHa(t.GetProperty("trechos")) <= t.GetProperty("areaHa").GetDouble(), $"talhão {t.GetProperty("areaHa")}");
        Assert.True(p.GetProperty("totais").EnumerateArray().Sum(x => x.GetProperty("ha").GetDouble()) <= 43.72);
    }

    [Fact]
    public async Task Hoje_soma_passa_da_area_por_arredondamento()
    {
        var (c, f, _) = await Fazenda(100, [50], (10.37, "AD3"));
        var p = await c.GetOk($"/fazendas/{f}/plano-colheita?inicio=2026-01-05");
        Assert.Equal(10.4, SomaHa(p.GetProperty("talhoes")[0].GetProperty("trechos")), 6);
    }

    // ---------- /plano-colheita (sem cadastro) ----------

    [Fact]
    public async Task Sem_cadastro_area_capacidade_e_culturas()
    {
        var c = api.Anonimo(); // rota pública
        // 2 × 60 × 70% = 84 ha/dia; 1000 / 84 = 11,9 → 12 dias
        var p = await c.GetOk($"/plano-colheita?lat={Http.Q(Lat)}&lon={Http.Q(Lon)}&inicio=2026-01-12&area=1000&colhedoras=2&haDia=60&uteis=70");
        Assert.Equal(84, p.GetProperty("capacidadeHaDia").GetDouble());
        Assert.Equal(12, p.GetProperty("dias").GetInt32());
        Assert.Equal(new DateOnly(2026, 1, 23), p.Data("fim"));
        Assert.Equal(1000, SomaHa(p.GetProperty("trechos")), 6);

        var rec = await Apoio.Recomendacao(c, Lat, Lon, new(2026, 1, 12), new(2026, 1, 23), 30, 0.2);
        foreach (var tr in p.GetProperty("trechos").EnumerateArray())
            foreach (var d in Apoio.Dias(tr.Data("de"), tr.Data("ate")))
                Assert.Equal(rec[Apoio.DecendioDe(d)].GetProperty("recomendacao").GetString(), tr.GetProperty("cultura").GetString());

        // decêndios do plano = os da /recomendacao na mesma janela
        Assert.Equal(rec.Keys, p.GetProperty("decendios").EnumerateArray().Select(d => d.GetProperty("decendio").GetString()!));
        // ajuste dos grãos: milho e sorgo, % entre 0 e 100
        var graos = p.GetProperty("graos").EnumerateArray().ToList();
        Assert.Equal(["milho", "sorgo"], graos.Select(g => g.GetProperty("cultura").GetString()).Order());
        Assert.All(graos, g => Assert.InRange(g.GetProperty("pct").GetInt32(), 0, 100));
    }

    // mesmo cálculo da fazenda com um talhão só
    [Fact]
    public async Task Sem_cadastro_bate_com_fazenda_de_um_talhao()
    {
        var (c, f, _) = await Fazenda(70, [60, 60], (1000, "AD3"));
        var pf = await c.GetOk($"/fazendas/{f}/plano-colheita?inicio=2026-01-12");
        var ps = await c.GetOk($"/plano-colheita?lat={Http.Q(Lat)}&lon={Http.Q(Lon)}&inicio=2026-01-12&area=1000&colhedoras=2&haDia=60&uteis=70");
        Assert.Equal(ps.GetProperty("trechos").GetRawText(), pf.GetProperty("talhoes")[0].GetProperty("trechos").GetRawText());
        Assert.Equal(ps.Data("fim"), pf.Data("fim"));
    }

    [Fact]
    public async Task Sem_cadastro_com_fim_nao_tem_ha()
    {
        var p = await api.Anonimo().GetOk($"/plano-colheita?lat={Http.Q(Lat)}&lon={Http.Q(Lon)}&inicio=2026-01-12&fim=2026-02-05");
        Assert.Equal(25, p.GetProperty("dias").GetInt32());
        Assert.Equal(JsonValueKind.Null, p.GetProperty("areaHa").ValueKind);
        Assert.All(p.GetProperty("trechos").EnumerateArray(), t => Assert.Equal(JsonValueKind.Null, t.GetProperty("ha").ValueKind));
        Assert.Equal(25, p.GetProperty("trechos").EnumerateArray().Sum(t => t.GetProperty("dias").GetInt32()));
    }

    [Fact]
    public async Task Sem_cadastro_cruza_o_ano()
    {
        var c = api.Anonimo();
        var p = await c.GetOk($"/plano-colheita?lat={Http.Q(Lat)}&lon={Http.Q(Lon)}&inicio=2026-12-20&area=2000&haDia=60&colhedoras=2&uteis=70");
        Assert.Equal(new DateOnly(2027, 1, 12), p.Data("fim"));    // 2000/84 = 23,8 → 24 dias
        Assert.Equal("11/12 a 20/12", p.GetProperty("decendios")[0].GetProperty("decendio").GetString());
        Assert.Equal("11/01 a 20/01", p.GetProperty("decendios").EnumerateArray().Last().GetProperty("decendio").GetString());
        var rec = await Apoio.Recomendacao(c, Lat, Lon, new(2026, 12, 20), new(2027, 1, 12), 30, 0.2);
        foreach (var tr in p.GetProperty("trechos").EnumerateArray())
            foreach (var d in Apoio.Dias(tr.Data("de"), tr.Data("ate")))
                Assert.Equal(rec[Apoio.DecendioDe(d)].GetProperty("recomendacao").GetString(), tr.GetProperty("cultura").GetString());
        Assert.Equal(2000, SomaHa(p.GetProperty("trechos")), 6);
    }

    [Theory]
    [InlineData("")]                                          // sem area e sem fim
    [InlineData("&area=100&uteis=0")]                         // capacidade zero
    [InlineData("&area=100&colhedoras=0")]
    [InlineData("&fim=2026-01-01")]                           // fim antes do início
    [InlineData("&fim=2027-01-12")]                           // 366 dias
    [InlineData("&area=100000&haDia=1")]                      // passa de 365 dias
    [InlineData("&area=100&fonteChuva=imerg")]
    public async Task Sem_cadastro_erros(string extra)
    {
        var r = await api.Anonimo().GetAsync($"/plano-colheita?lat={Http.Q(Lat)}&lon={Http.Q(Lon)}&inicio=2026-01-12{extra}");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }
}
