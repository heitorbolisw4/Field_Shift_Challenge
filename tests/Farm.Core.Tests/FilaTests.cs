// bordas da fila de colheita (capacidade contínua entre talhões) e do arredondamento a 0,01 ha
public class FilaTests
{
    static readonly DateOnly D0 = new(2026, 1, 5);
    static DateOnly Dia(int k) => D0.AddDays(k);

    static List<(int De, int Ate)> Dias(IEnumerable<Colheita.Etapa> fila) =>
        fila.Select(e => (e.De.DayNumber - D0.DayNumber, e.Ate.DayNumber - D0.DayNumber)).ToList();

    // ---------- Fila ----------

    [Fact]
    public void Talhao_que_fecha_o_dia_exato_empurra_o_proximo_para_o_dia_seguinte()
    {
        var f = Colheita.Fila(D0, [100, 50], 50);
        Assert.Equal([(0, 1), (2, 2)], Dias(f));
        Assert.Equal(100, f[1].HaAntes);
    }

    [Fact]
    public void Talhao_que_fecha_no_meio_do_dia_divide_o_dia_com_o_proximo()
    {
        var f = Colheita.Fila(D0, [75, 50], 50);
        Assert.Equal([(0, 1), (1, 2)], Dias(f));
    }

    [Theory]
    [InlineData(100, 30, 4)]   // 3,33 dias
    [InlineData(90, 30, 3)]    // exato
    [InlineData(0.01, 30, 1)]  // migalha: 1 dia
    [InlineData(30, 30, 1)]
    public void Um_talhao_bate_com_Dias(double area, double cap, int dias)
    {
        var e = Assert.Single(Colheita.Fila(D0, [area], cap));
        Assert.Equal(D0, e.De);
        Assert.Equal(dias, e.Dias);
        Assert.Equal(dias, Colheita.Dias(area, cap));
    }

    // 0,1 somado 30 vezes não dá 3,0 em double; cada talhão de 0,1 ha tem que ficar no próprio dia
    [Fact]
    public void Capacidade_fracionaria_sem_ruido_de_ponto_flutuante()
    {
        var f = Colheita.Fila(D0, Enumerable.Repeat(0.1, 30).ToList(), 0.1);
        Assert.Equal(Enumerable.Range(0, 30).Select(k => (k, k)), Dias(f));

        var cap = Colheita.Capacidade(10, 33); // 3,3 ha/dia
        var g = Colheita.Fila(D0, [3.3, 3.3, 3.3, 1.65, 1.65], cap);
        Assert.Equal([(0, 0), (1, 1), (2, 2), (3, 3), (3, 3)], Dias(g));
    }

    [Fact]
    public void Capacidade_enorme_tudo_no_primeiro_dia()
    {
        var f = Colheita.Fila(D0, [1000, 2000, 0.01], 1e12);
        Assert.All(f, e => { Assert.Equal(D0, e.De); Assert.Equal(D0, e.Ate); });
    }

    // contrato: Fila exige capacidade > 0. Quem chama (FazendaApi) barra capacidade <= 0 antes.
    [Fact]
    public void Capacidade_zero_estoura()
    {
        Assert.ThrowsAny<ArgumentException>(() => Colheita.Fila(D0, [10], 0));
    }

    // capacidade minúscula (mas > 0): os dias passam de int e AddDays estoura.
    // Na API: colhedora com haDia=1e-9 passa na validação (> 0) e o plano dá 500 (ver PlanoBordasTests).
    [Fact]
    public void Capacidade_minuscula_estoura_antes_do_limite_de_365_dias()
    {
        Assert.ThrowsAny<ArgumentException>(() => Colheita.Fila(D0, [10], 1e-9));
    }

    // invariantes em filas sortidas: contínua, sem buraco, sem sobreposição além do dia compartilhado,
    // HaAntes = soma das anteriores, e o fim bate com Dias(total)
    [Theory]
    [InlineData(1, 7, 42.0)]
    [InlineData(2, 25, 3.3)]
    [InlineData(3, 60, 0.7)]
    [InlineData(4, 3, 1000.0)]
    [InlineData(5, 40, 17.5)]
    public void Invariantes_da_fila(int semente, int n, double cap)
    {
        var rnd = new Random(semente);
        var areas = Enumerable.Range(0, n).Select(_ => Math.Round(0.01 + rnd.NextDouble() * 50, 2)).ToList();
        var f = Colheita.Fila(D0, areas, cap);

        Assert.Equal(D0, f[0].De);
        double antes = 0;
        for (int i = 0; i < n; i++)
        {
            Assert.Equal(antes, f[i].HaAntes, 9);
            Assert.True(f[i].Ate >= f[i].De);
            if (i > 0)
            {
                var gap = f[i].De.DayNumber - f[i - 1].Ate.DayNumber;
                Assert.InRange(gap, 0, 1);
                var fechaDia = Math.Abs(antes / cap - Math.Round(antes / cap)) < 1e-9;
                Assert.Equal(fechaDia ? 1 : 0, gap);
            }
            antes += areas[i];
        }
        // o fim da fila tem que ser o mesmo de um bloco só com a área total
        var total = Colheita.Somar(areas);
        Assert.Equal(Colheita.Fim(D0, Colheita.Dias(total, cap)), f[^1].Ate);
    }

    // ---------- Dias (usado por /plano-colheita sem cadastro) ----------

    // Fila tem tolerância (eps) e Dias não: 9,9 / 3,3 = 3,0000000000000004 → Ceiling = 4.
    // /plano-colheita?area=9.9&haDia=10&uteis=33 diz 4 dias (o 4º com 0 ha); a fazenda com o mesmo talhão diz 3.
    [Theory(Skip = "BUG: Colheita.Dias sem tolerância: Dias(9.9, 3.3) = 4 (Fila dá 3)")]
    [InlineData(10, 33, 9.9, 3)]
    [InlineData(7, 10, 2.1, 3)]
    [InlineData(3, 10, 2.1, 7)]
    public void Dias_tem_a_mesma_tolerancia_da_Fila(double haDia, double uteis, double area, int dias)
    {
        var cap = Colheita.Capacidade(haDia, uteis);
        Assert.Equal(dias, Colheita.Fila(D0, [area], cap)[0].Dias);
        Assert.Equal(dias, Colheita.Dias(area, cap));
    }

    // ---------- arredondamento a 0,01 ----------

    static long Cent(double x) => (long)Math.Round(x * 100, MidpointRounding.AwayFromZero);

    // culturas mudando a cada 3 dias para gerar vários trechos por talhão
    static string Cultura(DateOnly d) => ((d.DayNumber / 3) % 3) switch { 0 => "milho", 1 => "sorgo", _ => "braquiária" };

    [Theory]
    [InlineData(11, 80, 7.77)]
    [InlineData(12, 150, 33.3)]
    [InlineData(13, 40, 0.37)]
    [InlineData(14, 200, 123.45)]
    public void Muitos_talhoes_cada_um_fecha_no_centavo(int semente, int n, double cap)
    {
        var rnd = new Random(semente);
        var areas = Enumerable.Range(0, n).Select(_ => Math.Round(0.01 + rnd.NextDouble() * 20, 2)).ToList();
        var fila = Colheita.Fila(D0, areas, cap);

        long totalCent = 0;
        foreach (var (e, a) in fila.Zip(areas))
        {
            var tr = Colheita.Trechos(D0, e, cap, Cultura);
            Assert.All(tr, t => Assert.True(t.Ha >= 0, $"trecho negativo {t}"));
            Assert.All(tr, t => Assert.Equal(t.Ha, Math.Round(t.Ha, 2), 12));   // sempre 2 casas
            Assert.Equal(Cent(a), tr.Sum(t => Cent(t.Ha)));                      // fecha na área do talhão
            Assert.Equal(e.Dias, tr.Sum(t => t.Dias));
            Assert.Equal(e.De, tr[0].De);
            Assert.Equal(e.Ate, tr[^1].Ate);
            totalCent += tr.Sum(t => Cent(t.Ha));
        }
        Assert.Equal(areas.Sum(Cent), totalCent);
        Assert.Equal(totalCent / 100.0, Colheita.Somar(areas));
    }

    // Somar evita 0,1 + 0,2 = 0,30000000000000004
    [Fact]
    public void Somar_nao_acumula_erro()
    {
        Assert.Equal(0.3, Colheita.Somar([0.1, 0.2]));
        Assert.Equal(100.0, Colheita.Somar(Enumerable.Repeat(0.01, 10000)));
        Assert.Equal(0, Colheita.Somar([]));
    }

    // dias inteiros de capacidade: o dia compartilhado vai metade para cada talhão
    [Fact]
    public void Dia_compartilhado_divide_a_capacidade()
    {
        var f = Colheita.Fila(D0, [75, 50], 50);
        var t1 = Colheita.Trechos(D0, f[1], 50, _ => "milho");
        var t = Assert.Single(t1);
        Assert.Equal(50, t.Ha);
        Assert.Equal(2, t.Dias);
        var t0 = Assert.Single(Colheita.Trechos(D0, f[0], 50, _ => "milho"));
        Assert.Equal(75, t0.Ha);
    }

    // área com 3 casas (a API aceita areaHa=10,125): o arredondamento AwayFromZero leva a soma para 10,13 > área.
    // O comentário de Agrupar diz "nunca passa dela".
    [Fact(Skip = "BUG pequeno: área com 3 casas (10,125) vira 10,13 nos trechos e passa da área")]
    public void Area_com_tres_casas_nao_passa_da_area()
    {
        var e = Assert.Single(Colheita.Fila(D0, [10.125], 50));
        var soma = Colheita.Trechos(D0, e, 50, _ => "milho").Sum(t => t.Ha);
        Assert.True(soma <= 10.125, $"soma {soma}");
    }

    // ---------- Trechos sem cadastro (área + fim informados) ----------

    // último dia parcial, e dias depois de colhida a área viram trechos de 0 ha
    [Fact]
    public void Trechos_sem_cadastro_ultimo_dia_parcial()
    {
        var tr = Colheita.Trechos(D0, Dia(3), _ => "milho", 100, 30);
        var t = Assert.Single(tr);
        Assert.Equal(100, t.Ha);
        Assert.Equal(4, t.Dias);
    }
}
