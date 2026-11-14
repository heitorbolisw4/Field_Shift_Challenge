// balanço hídrico (balde + Ks), ISNA do ZARC e faixas de risco
public class ModeloTests
{
    // Kc = 1 o ciclo todo, 20 dias: ETc = ET0
    static readonly Cultura Plana = new("plana", 5, 5, 5, 5, 1, 1, 1, Ky: 1.25);
    static readonly DateOnly Ini = new(2001, 2, 1);
    const double Lat = -19;

    // clima constante do Cerrado; chuva por dia vem da função
    static Dictionary<DateOnly, Dia> Clima(int dias, Func<int, double> chuva) =>
        Enumerable.Range(0, dias).ToDictionary(
            n => Ini.AddDays(n),
            n => new Dia(Ini.AddDays(n), 30, 18, chuva(n), 20, 65, 2, 92));

    static Safra Rodar(Cultura c, Func<int, double> chuva, double cad, double fator = 1, bool pm = true) =>
        Modelo.Simular(c, Clima(c.Ciclo, chuva), Ini, Lat, cad, fator, pm)!;

    [Fact]
    public void ChuvaSobrando_SemDeficit()
    {
        var s = Rodar(Plana, _ => 50, cad: 30);
        Assert.Equal(0, s.Deficit, 9);
        Assert.Equal(0, s.DiasSede);
        Assert.Equal(1, s.IsnaI, 9);
        Assert.Equal(1, s.IsnaIII, 9);
    }

    // balde nunca abaixo de 0: sem chuva, a planta não consome mais que a CAD inicial
    [Theory]
    [InlineData(10)]
    [InlineData(30)]
    [InlineData(60)]
    public void SemChuva_ConsumoNaoPassaDaCad(double cad)
    {
        var s = Rodar(Plana, _ => 0, cad);
        var consumido = s.Etc - s.Deficit;
        Assert.True(consumido <= cad + 1e-9, $"consumiu {consumido} com CAD {cad}");
        Assert.True(s.Deficit >= 0);
    }

    // balde nunca acima da CAD: um temporal no dia 0 transborda e não vira estoque
    [Fact]
    public void Temporal_Transborda()
    {
        const double cad = 30;
        var s = Rodar(Plana, n => n == 0 ? 500 : 0, cad);
        Assert.True(s.Etc - s.Deficit <= cad + 1e-9);
        Assert.Equal(500, s.Chuva, 9);
    }

    // com chuva qualquer, consumo <= CAD + chuva e déficit <= ETc
    [Fact]
    public void ChuvaIrregular_LimitesDoBalde()
    {
        const double cad = 40;
        var s = Rodar(Plana, n => n % 7 == 0 ? 12 : 0, cad);
        var consumido = s.Etc - s.Deficit;
        Assert.InRange(consumido, 0, cad + s.Chuva + 1e-9);
        Assert.InRange(s.Deficit, 0, s.Etc);
        Assert.InRange(s.IsnaI, 0, 1);
        Assert.InRange(s.IsnaIII, 0, 1);
    }

    // CAD enorme: o balde nunca desce de (1-p)·CAD, Ks = 1 sempre
    [Fact]
    public void CadEnorme_SemEstresse()
    {
        var s = Rodar(Plana, _ => 0, cad: 1e6);
        Assert.Equal(0, s.Deficit, 9);
        Assert.Equal(0, s.DiasSede);
    }

    // CAD = 1 mm sem chuva: dia 0 bebe 1 mm (Ks = 1), depois Ks = 0 até o fim
    [Fact]
    public void CadMinima_SedeDoDia1AteOFim()
    {
        var s = Rodar(Plana, _ => 0, cad: 1);
        Assert.Equal(Plana.Ciclo - 1, s.DiasSede);
        Assert.Equal(s.Etc - 1, s.Deficit, 9);
    }

    // Ks só entra depois de gastar p·CAD (55%): os primeiros dias não têm sede
    [Fact]
    public void Ks_SoDepoisDeGastarP()
    {
        var et0 = global::Clima.Et0Pm(new Dia(Ini, 30, 18, 0, 20, 65, 2, 92), Lat);
        double cad = et0 * 10 / Modelo.P;        // p·CAD dura ~10 dias de ETc
        var s = Rodar(Plana, _ => 0, cad);
        Assert.InRange(s.DiasSede, 1, Plana.Ciclo - 9);
        Assert.True(s.Deficit > 0);
    }

    // mais CAD, menos déficit
    [Fact]
    public void Deficit_CaiComCad()
    {
        double anterior = double.MaxValue;
        foreach (var cad in new[] { 5.0, 20, 50, 100 })
        {
            var d = Rodar(Plana, n => n % 10 == 0 ? 15 : 0, cad).Deficit;
            Assert.True(d <= anterior + 1e-9, $"CAD {cad}: {d} > {anterior}");
            anterior = d;
        }
    }

    [Fact]
    public void FatorChuva_Multiplica()
    {
        var s = Rodar(Plana, _ => 2, cad: 30, fator: 1.5);
        Assert.Equal(2 * 1.5 * Plana.Ciclo, s.Chuva, 9);
    }

    [Fact]
    public void Hargreaves_TambemRespeitaOBalde()
    {
        var s = Rodar(Plana, _ => 0, cad: 20, pm: false);
        Assert.True(s.Etc - s.Deficit <= 20 + 1e-9);
    }

    [Fact]
    public void FaltaDia_DevolveNull()
    {
        var dias = Clima(Plana.Ciclo, _ => 5);
        dias.Remove(Ini.AddDays(7));
        Assert.Null(Modelo.Simular(Plana, dias, Ini, Lat, 30));
    }

    [Fact]
    public void CicloPassaDoFimDosDados_DevolveNull()
    {
        var dias = Clima(Plana.Ciclo - 1, _ => 5);
        Assert.Null(Modelo.Simular(Plana, dias, Ini, Lat, 30));
    }

    // ISNA do ZARC: fase I >= 0,6 e fase III >= 0,5, as duas
    [Theory]
    [InlineData(0.6, 0.5, true)]
    [InlineData(1.0, 1.0, true)]
    [InlineData(0.59, 0.9, false)]
    [InlineData(0.9, 0.49, false)]
    [InlineData(0.0, 0.0, false)]
    public void Passa_Isna(double isnaI, double isnaIII, bool esperado)
    {
        var s = new Safra(100, 0, 0, 0, isnaI, isnaIII);
        Assert.Equal(esperado, Modelo.Passa(Culturas.Todas["milho"], s, "isna"));
    }

    // critério de perda (FAO-33): Ky · déficit / ETc <= 20%. Milho Ky = 1,25 -> déficit até 16% da ETc.
    [Theory]
    [InlineData(16, true)]
    [InlineData(17, false)]
    public void Passa_Perda(double deficit, bool esperado)
    {
        var s = new Safra(100, 0, deficit, 0, 0, 0);   // ISNA zerado: só a perda importa
        Assert.Equal(esperado, Modelo.Passa(Culturas.Todas["milho"], s, "perda"));
    }

    [Fact]
    public void Perda_KyVezesDeficitRelativo()
    {
        var s = new Safra(200, 0, 40, 0, 1, 1);
        Assert.Equal(0.9 * 0.2, Modelo.Perda(Culturas.Todas["sorgo"], s), 9);
    }

    [Theory]
    [InlineData(0.0, "20%")]
    [InlineData(0.20, "20%")]
    [InlineData(0.21, "30%")]
    [InlineData(0.30, "30%")]
    [InlineData(0.40, "40%")]
    [InlineData(0.41, "inapto")]
    [InlineData(1.0, "inapto")]
    public void Nivel_FaixasDoZarc(double risco, string nivel) => Assert.Equal(nivel, Modelo.Nivel(risco));
}
