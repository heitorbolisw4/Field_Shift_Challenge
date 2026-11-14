// lógica que saiu do front (JS) e do Program.cs: decêndio, calendário, colheita, polígono do talhão
public class DecendioTests
{
    [Theory]
    [InlineData(2001, 2, 1, "01/02 a 10/02")]
    [InlineData(2001, 2, 15, "11/02 a 20/02")]
    [InlineData(2001, 2, 25, "21/02 a 28/02")]
    [InlineData(2004, 2, 29, "21/02 a 29/02")]
    [InlineData(2001, 1, 31, "21/01 a 31/01")]
    public void De_BlocoCerto(int a, int m, int d, string esperado) =>
        Assert.Equal(esperado, Decendio.De(new DateOnly(a, m, d)).ToString());

    [Fact]
    public void Entre_CruzaOAno()
    {
        var (di, df) = Calendario.Janela(new DateOnly(2026, 12, 15), new DateOnly(2027, 1, 5));
        Assert.Equal(new DateOnly(2001, 12, 15), di);
        Assert.Equal(new DateOnly(2002, 1, 5), df);
        var lista = Decendio.Entre(di, df).Select(x => x.ToString()).ToList();
        Assert.Equal(["11/12 a 20/12", "21/12 a 31/12", "01/01 a 10/01"], lista);
    }

    [Fact]
    public void AnoModelo_29DeFevereiroVira28() =>
        Assert.Equal(new DateOnly(2001, 2, 28), Calendario.NoAnoModelo(new DateOnly(2028, 2, 29)));
}

public class CalendarioTests
{
    static Dictionary<DateOnly, Dia> Clima(double chuva)
    {
        var ini = new DateOnly(2001, 1, 1);
        return Enumerable.Range(0, 730).ToDictionary(n => ini.AddDays(n),
            n => new Dia(ini.AddDays(n), 30, 18, chuva, 20, 65, 2, 92));
    }

    static readonly Decendio Fev1 = Decendio.De(new DateOnly(2001, 2, 1));

    [Fact]
    public void ChuvaSobrando_IndicaMilho()
    {
        var dias = Clima(50);
        var r = Calendario.Avaliar(Fev1, dias, Calendario.Anos(dias), new Parametros(-19, 30));
        Assert.Equal(0, r.Risco["milho"]);
        Assert.Equal("milho", r.Recomendacao);
    }

    [Fact]
    public void SemChuva_IndicaBraquiaria()
    {
        var dias = Clima(0);
        var r = Calendario.Avaliar(Fev1, dias, Calendario.Anos(dias), new Parametros(-19, 30));
        Assert.Equal(1, r.Risco["milho"]);
        Assert.Equal(1, r.Risco["sorgo"]);
        Assert.Equal("braquiária", r.Recomendacao);
    }

    [Fact]
    public void SemDados_RiscoUm()
    {
        var r = Calendario.Avaliar(Fev1, new Dictionary<DateOnly, Dia>(), [], new Parametros(-19, 30));
        Assert.Equal(1, r.Risco["milho"]);
    }

    [Theory]
    [InlineData(0.1, 0.1, "milho")]   // os dois cabem: o mais exigente (milho)
    [InlineData(0.3, 0.1, "sorgo")]
    [InlineData(0.2, 0.5, "milho")]   // limite é inclusivo
    [InlineData(0.3, 0.3, "braquiária")]
    public void Escolher(double milho, double sorgo, string esperado) =>
        Assert.Equal(esperado, Calendario.Escolher(new Dictionary<string, double> { ["milho"] = milho, ["sorgo"] = sorgo }, 0.2));

    [Fact]
    public void Calcular_UmPorDecendio()
    {
        var dias = Clima(50);
        var cal = Calendario.Calcular(new DateOnly(2001, 1, 20), new DateOnly(2001, 3, 20), dias, new Parametros(-19, 30));
        Assert.Equal(7, cal.Count); // 11/01, 21/01, 01/02, 11/02, 21/02, 01/03, 11/03
        Assert.Equal("11/01 a 20/01", cal[0].Decendio.ToString());
    }

    [Fact]
    public void AguaIni_ChamadaComADataDoPlantio()
    {
        var vistos = new List<DateOnly>();
        var dias = Clima(50);
        Calendario.Avaliar(Fev1, dias, Calendario.Anos(dias), new Parametros(-19, 30, AguaIni: d => { vistos.Add(d); return 1; }));
        Assert.Contains(new DateOnly(2002, 2, 10), vistos);
    }
}

public class ColheitaTests
{
    [Fact]
    public void Dias_IgualAoFrontAntigo()
    {
        var cap = Colheita.Capacidade(60 * 1, 70);   // 42 ha por dia de calendário
        Assert.Equal(42, cap, 9);
        Assert.Equal(24, Colheita.Dias(1000, cap));  // 1000/42 = 23,8 -> 24
        Assert.Equal(0, Colheita.Dias(1000, 0));
    }

    [Fact]
    public void Fim_InicioContaComoDia1()
    {
        Assert.Equal(new DateOnly(2026, 2, 24), Colheita.Fim(new DateOnly(2026, 2, 1), 24));
        Assert.Equal(new DateOnly(2026, 2, 1), Colheita.Fim(new DateOnly(2026, 2, 1), 0));
    }

    [Fact]
    public void Trechos_JuntaDiasIguaisECortaNaArea()
    {
        var t = Colheita.Trechos(new DateOnly(2026, 2, 5), new DateOnly(2026, 2, 14),
            d => d.Day <= 10 ? "milho" : "sorgo", area: 500, capacidade: 60);
        Assert.Equal(2, t.Count);
        Assert.Equal(new Trecho("milho", new(2026, 2, 5), new(2026, 2, 10), 6, 360), t[0]);
        Assert.Equal(new Trecho("sorgo", new(2026, 2, 11), new(2026, 2, 14), 4, 140), t[1]); // sobrou só 140
        Assert.Equal(500, t.Sum(x => x.Ha));
    }

    [Fact]
    public void Trechos_AreaQuebradaNaoPassaDoTotal()
    {
        var t = Colheita.Trechos(new DateOnly(2026, 2, 9), new DateOnly(2026, 2, 11), _ => "milho", area: 116.88, capacidade: 42);
        Assert.Equal(116.88, Assert.Single(t).Ha);
    }

    [Fact]
    public void Trechos_AlternandoNaoJunta()
    {
        var t = Colheita.Trechos(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 4),
            d => d.Day % 2 == 0 ? "a" : "b", area: 1000, capacidade: 10);
        Assert.Equal(["b", "a", "b", "a"], t.Select(x => x.Cultura));
        Assert.All(t, x => Assert.Equal(10, x.Ha));
    }

    [Fact]
    public void Fila_SobraDoDiaPassaParaOProximo()
    {
        var ini = new DateOnly(2026, 1, 5);
        var f = Colheita.Fila(ini, [20, 20, 20], 50);
        Assert.Equal([(ini, ini), (ini, ini), (ini, ini.AddDays(1))], f.Select(e => (e.De, e.Ate))); // 20 + 20 + 10 no dia 1
        Assert.Equal([0.0, 20, 40], f.Select(e => e.HaAntes));
    }

    [Fact]
    public void Fila_TalhaoQueFechaODiaNaoInvadeOSeguinte()
    {
        var ini = new DateOnly(2026, 1, 5);
        var f = Colheita.Fila(ini, [50, 50], 50);
        Assert.Equal(ini, f[0].Ate);
        Assert.Equal(ini.AddDays(1), f[1].De);
    }

    [Fact]
    public void TrechosDaFila_SomamAAreaECortamOsDias()
    {
        var ini = new DateOnly(2026, 1, 5);
        var f = Colheita.Fila(ini, [20, 20, 20], 50);
        var t = Colheita.Trechos(ini, f[2], 50, d => d == ini ? "milho" : "sorgo");
        Assert.Equal([("milho", 10.0), ("sorgo", 10.0)], t.Select(x => (x.Cultura, x.Ha))); // 40 ha do dia 1 já foram
    }

    [Fact]
    public void Trechos_ArredondamentoNaoPassaDaArea()
    {
        // 3 trechos de 3,333... ha: a soma fecha em 10 (não 9,99 nem 10,01)
        var ini = new DateOnly(2026, 1, 1);
        var t = Colheita.Trechos(ini, ini.AddDays(2), d => d.Day.ToString(), area: 10, capacidade: 10.0 / 3);
        Assert.Equal(10, Colheita.Somar(t.Select(x => x.Ha)));
        Assert.All(t, x => Assert.InRange(x.Ha, 3.33, 3.34));
    }

    [Fact]
    public void AjusteDosGraos_PctEMedia()
    {
        var ini = new DateOnly(2026, 2, 1);
        var r = Colheita.AjusteDosGraos(ini, ini.AddDays(4),
            d => d.Day == 5 ? null   // dia fora do calendário não conta
               : new Dictionary<string, double> { ["milho"] = d.Day <= 2 ? 0.1 : 0.5 }, 0.2);
        var m = Assert.Single(r);
        Assert.Equal(new AjusteGrao("milho", 2, 50, 30), m);
    }
}

public class GeoTests
{
    // quadrado de 0,01° × 0,01°
    static List<(double, double)> Quadrado(double lon, double lat) =>
        [(lon, lat), (lon + 0.01, lat), (lon + 0.01, lat + 0.01), (lon, lat + 0.01), (lon, lat)];

    [Fact]
    public void Area_NoEquador() =>
        Assert.Equal(123.6, Geo.AreaHa(Quadrado(0, -0.005)), 1); // lado ~1112 m

    [Fact]
    public void Area_EncolheComALatitude() =>
        Assert.Equal(123.6 * Math.Cos(19 * Math.PI / 180), Geo.AreaHa(Quadrado(-47.9, -19.005)), 0);

    [Fact]
    public void Area_SentidoNaoImporta()
    {
        var q = Quadrado(-47.9, -19);
        Assert.Equal(Geo.AreaHa(q), Geo.AreaHa(q.AsEnumerable().Reverse().ToList()), 9);
    }

    [Fact]
    public void Centroide_DoQuadradoEOCentro()
    {
        var (lon, lat) = Geo.Centroide(Quadrado(-47.9, -19));
        Assert.Equal(-47.895, lon, 6);
        Assert.Equal(-18.995, lat, 6);
    }

    [Fact]
    public void Centroide_DeLNaoEAMediaDosVertices()
    {
        // L: quadrado 2×2 sem o canto de cima à direita; centroide da área = (5/6, 5/6) × 0,01°
        List<(double, double)> l = [(0, 0), (0.02, 0), (0.02, 0.01), (0.01, 0.01), (0.01, 0.02), (0, 0.02)];
        var (lon, lat) = Geo.Centroide(l);
        Assert.Equal(0.01 * 5 / 6, lon, 5);
        Assert.Equal(0.01 * 5 / 6, lat, 5);
    }

    [Fact]
    public void PoligonoDegenerado_AreaZero() =>
        Assert.Equal(0, Geo.AreaHa([(0, 0), (1, 1)]));
}

public class SolosTests
{
    [Theory]
    [InlineData("AD1", 17)]
    [InlineData("ad3", 30)]
    [InlineData("AD6", 80)]
    public void Cad(string classe, double mm) => Assert.Equal(mm, Solos.Cad[classe]);

    [Fact]
    public void ClasseInvalida() => Assert.False(Solos.Valida("AD7"));
}
