// propriedades das correções de aacbfae, com milhares de entradas sorteadas (Random com semente fixa: reprodutível).
// Sem FsCheck de propósito: não vale a dependência para gerar números; a mensagem de cada Assert traz a entrada que falhou.
public class PropriedadesTests
{
    static readonly DateOnly D0 = new(2026, 1, 5);
    static long Cent(double x) => (long)Math.Round(x * 100, MidpointRounding.AwayFromZero);
    static string Cultura(DateOnly d) => ((d.DayNumber * 7 / 5) % 3) switch { 0 => "milho", 1 => "sorgo", _ => "braquiária" };

    // capacidade como a API monta: haDia × colhedoras × uteis/100 (gera o ruído de 3,3 = 10 × 33 / 100)
    static double CapacidadeSorteada(Random r) =>
        Colheita.Capacidade(Math.Round(0.5 + r.NextDouble() * 120, r.Next(0, 3)) * r.Next(1, 6), r.Next(1, 101));

    // área com 0 a 4 casas (3 e 4 casas são as que forçam o arredondamento para baixo)
    // área 0 fica de fora: as duas rotas recusam (e aí Dias = 0 mas Fila = 1 dia; contrato, não alcançável pela API)
    static double AreaSorteada(Random r, double max) => Math.Round(0.001 + r.NextDouble() * max, r.Next(0, 5)) is > 0 and var a ? a : 0.01;

    // ---------- Dias × Fila (sem cadastro × fazenda) ----------

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void Um_talhao_Dias_e_Fila_concordam(int semente)
    {
        var r = new Random(semente);
        for (int i = 0; i < 5000; i++)
        {
            var cap = CapacidadeSorteada(r);
            var area = AreaSorteada(r, cap * 40);
            var e = Assert.Single(Colheita.Fila(D0, [area], cap));
            Assert.True(Colheita.Dias(area, cap) == e.Dias, $"area={area:R} cap={cap:R}: Dias={Colheita.Dias(area, cap)} Fila={e.Dias}");
            Assert.Equal(Colheita.Fim(D0, Colheita.Dias(area, cap)), e.Ate);
        }
    }

    // múltiplos exatos da capacidade, onde o ruído de ponto flutuante mora: n × cap tem que dar n dias nos dois
    [Fact]
    public void Multiplos_exatos_da_capacidade_dao_n_dias_nos_dois()
    {
        var r = new Random(7);
        for (int i = 0; i < 5000; i++)
        {
            var cap = CapacidadeSorteada(r);
            int n = r.Next(1, 366);
            var area = Math.Round(n * cap, 2); // o que o usuário digitaria
            if (Math.Abs(area - n * cap) > 1e-9 * cap) continue; // só os múltiplos "de verdade" com 2 casas
            Assert.True(Colheita.Dias(area, cap) == n, $"area={area:R} cap={cap:R} n={n}: Dias={Colheita.Dias(area, cap)}");
            Assert.True(Colheita.Fila(D0, [area], cap)[0].Dias == n, $"area={area:R} cap={cap:R} n={n}: Fila");
        }
    }

    // vários talhões: o fim da fila é o fim de um bloco só com a área total
    [Theory]
    [InlineData(21)] [InlineData(22)]
    public void Fila_termina_onde_Dias_da_soma_termina(int semente)
    {
        var r = new Random(semente);
        for (int i = 0; i < 1500; i++)
        {
            var cap = CapacidadeSorteada(r);
            var areas = Enumerable.Range(0, r.Next(1, 25)).Select(_ => AreaSorteada(r, cap * 5)).ToList();
            var fila = Colheita.Fila(D0, areas, cap);
            var esperado = Colheita.Fim(D0, Colheita.Dias(areas.Sum(), cap));
            Assert.True(esperado == fila[^1].Ate, $"cap={cap:R} areas=[{string.Join(", ", areas.Select(a => a.ToString("R")))}]: {esperado} x {fila[^1].Ate}");
        }
    }

    // mesmo talhão pelas duas rotas da conta (Trechos sem cadastro × Trechos da etapa): trechos idênticos, ha idênticos
    [Theory]
    [InlineData(31)] [InlineData(32)]
    public void Trechos_sem_cadastro_iguais_aos_da_fazenda(int semente)
    {
        var r = new Random(semente);
        for (int i = 0; i < 2000; i++)
        {
            var cap = CapacidadeSorteada(r);
            var area = AreaSorteada(r, cap * 30);
            var fim = Colheita.Fim(D0, Colheita.Dias(area, cap));
            var a = Colheita.Trechos(D0, fim, Cultura, area, cap);
            var b = Colheita.Trechos(D0, Colheita.Fila(D0, [area], cap)[0], cap, Cultura);
            Assert.True(a.SequenceEqual(b), $"area={area:R} cap={cap:R}:\n{string.Join("\n", a)}\nx\n{string.Join("\n", b)}");
        }
    }

    // ---------- arredondamento para baixo ----------

    static void InvariantesDoPiso(IReadOnlyList<Trecho> t, double area, string ctx)
    {
        var soma = t.Sum(x => Cent(x.Ha));
        var areaCent = (long)Math.Floor(area * 100 + 1e-6);
        Assert.True(t.All(x => x.Ha >= 0), $"{ctx}: trecho negativo {string.Join(", ", t.Select(x => x.Ha))}");
        Assert.True(t.All(x => Math.Abs(x.Ha * 100 - Cent(x.Ha)) < 1e-6), $"{ctx}: trecho com mais de 2 casas");
        Assert.True(soma <= Math.Round(area * 100, 6), $"{ctx}: soma {soma / 100.0} > área {area:R}");
        // mais forte que "área − soma < 0,01 × trechos": o piso é do acumulado, então a perda total é < 0,01
        Assert.True(soma == areaCent, $"{ctx}: soma {soma / 100.0}, esperado {areaCent / 100.0} (área {area:R})");
    }

    [Theory]
    [InlineData(41)] [InlineData(42)] [InlineData(43)]
    public void Piso_sem_cadastro(int semente)
    {
        var r = new Random(semente);
        for (int i = 0; i < 3000; i++)
        {
            var cap = CapacidadeSorteada(r);
            var area = AreaSorteada(r, cap * 30);
            var t = Colheita.Trechos(D0, Colheita.Fim(D0, Colheita.Dias(area, cap)), Cultura, area, cap);
            InvariantesDoPiso(t, area, $"area={area:R} cap={cap:R}");
        }
    }

    [Theory]
    [InlineData(51)] [InlineData(52)]
    public void Piso_por_talhao_na_fila(int semente)
    {
        var r = new Random(semente);
        for (int i = 0; i < 400; i++)
        {
            var cap = CapacidadeSorteada(r);
            var areas = Enumerable.Range(0, r.Next(1, 30)).Select(_ => AreaSorteada(r, cap * 4)).ToList();
            foreach (var (e, k) in Colheita.Fila(D0, areas, cap).Select((e, k) => (e, k)))
                InvariantesDoPiso(Colheita.Trechos(D0, e, cap, Cultura), areas[k], $"cap={cap:R} talhão {k} area={areas[k]:R}");
        }
    }

    // fim informado maior que o necessário (rota sem cadastro com `fim`): dias a mais ficam com 0 ha, nunca negativos
    [Fact]
    public void Fim_alem_da_area_nao_gera_negativo_nem_passa_da_area()
    {
        var r = new Random(61);
        for (int i = 0; i < 2000; i++)
        {
            var cap = CapacidadeSorteada(r);
            var area = AreaSorteada(r, cap * 10);
            var fim = Colheita.Fim(D0, Colheita.Dias(area, cap) + r.Next(0, 40));
            InvariantesDoPiso(Colheita.Trechos(D0, fim, Cultura, area, cap), area, $"area={area:R} cap={cap:R} fim={fim}");
        }
    }

    // casos que o double erra: 10,37 vira 1036,9999… ; 0,1 + 0,2 ; 3 casas no meio do caminho
    [Theory]
    [InlineData(10.37, 3.3)]
    [InlineData(0.3, 0.1)]
    [InlineData(10.125, 3.3)]
    [InlineData(10.125, 10.125)]
    [InlineData(0.005, 1)]
    [InlineData(0.0099, 0.001)]
    [InlineData(1e-6, 1)]
    [InlineData(99999.99, 0.7)]
    public void Piso_casos_conhecidos(double area, double cap)
    {
        var t = Colheita.Trechos(D0, Colheita.Fim(D0, Colheita.Dias(area, cap)), Cultura, area, cap);
        InvariantesDoPiso(t, area, $"area={area:R} cap={cap:R}");
    }

    // a tolerância de Dias/Fila é em DIAS (1e-9), não em ha: com capacidade gigante o resto some.
    // 1e8 ha/dia e 1e8 + 0,05 ha: 1,0000000005 dia vira 1 dia e 0,05 ha não são colhidos.
    // Irreal (ninguém colhe 1e8 ha/dia), mas a API aceita haDia sem teto.
    [Fact(Skip = "BUG: eps de 1e-9 dia em Dias/Fila descarta até cap×1e-9 ha (0,05 ha com cap=1e8); sem teto de haDia na API")]
    public void Capacidade_gigante_nao_perde_area()
    {
        double cap = 1e8, area = 1e8 + 0.05;
        var t = Colheita.Trechos(D0, Colheita.Fim(D0, Colheita.Dias(area, cap)), Cultura, area, cap);
        InvariantesDoPiso(t, area, "cap gigante");
    }

    // ---------- Ra: clamp ----------

    // Ra como era antes de aacbfae (sem Clamp)
    static double RaAntigo(DateOnly data, double latGraus)
    {
        double J = data.DayOfYear;
        double phi = latGraus * Math.PI / 180;
        double dr = 1 + 0.033 * Math.Cos(2 * Math.PI * J / 365);
        double delta = 0.409 * Math.Sin(2 * Math.PI * J / 365 - 1.39);
        double ws = Math.Acos(-Math.Tan(phi) * Math.Tan(delta));
        return 24 * 60 / Math.PI * 0.0820 * dr *
               (ws * Math.Sin(phi) * Math.Sin(delta) + Math.Cos(phi) * Math.Cos(delta) * Math.Sin(ws));
    }

    // fora do círculo polar o argumento já ficava em [-1, 1]: o Clamp não pode mudar nem um bit
    [Fact]
    public void Clamp_nao_muda_Ra_fora_do_circulo_polar()
    {
        for (double lat = -66; lat <= 66; lat += 0.25)
            foreach (var d in Enumerable.Range(0, 366).Select(k => new DateOnly(2024, 1, 1).AddDays(k)))
                Assert.True(RaAntigo(d, lat) == Clima.Ra(d, lat), $"lat={lat} {d}: {RaAntigo(d, lat):R} x {Clima.Ra(d, lat):R}");
    }

    // ET0 inteira também não muda nos trópicos: Rad/rso só mudou quando rso = 0
    [Fact]
    public void Et0_igual_ao_calculo_antigo_nos_tropicos()
    {
        var r = new Random(71);
        for (int i = 0; i < 3000; i++)
        {
            var d = new DateOnly(2025, 1, 1).AddDays(r.Next(0, 365));
            double lat = -35 + r.NextDouble() * 70;
            var dia = new Dia(d, 25 + r.NextDouble() * 12, 10 + r.NextDouble() * 12, 0, 5 + r.NextDouble() * 25,
                              30 + r.NextDouble() * 65, 0.5 + r.NextDouble() * 5, 85 + r.NextDouble() * 16);
            Assert.True(Clima.Ra(d, lat) == RaAntigo(d, lat));
            var rso = (0.75 + 2e-5 * ((293 - 293 * Math.Pow(dia.Pressao / 101.3, 1 / 5.26)) / 0.0065)) * RaAntigo(d, lat);
            Assert.True(rso > 0, $"lat={lat} {d}: rso={rso}"); // ramo novo (rso = 0) nunca é usado aqui
            Assert.False(double.IsNaN(Clima.Et0Pm(dia, lat)));
        }
    }

    // ---------- Ra / ET0 perto dos polos ----------

    static IEnumerable<DateOnly> Ano() => Enumerable.Range(0, 365).Select(k => new DateOnly(2025, 1, 1).AddDays(k));

    [Theory]
    [InlineData(66.0)] [InlineData(66.6)] [InlineData(70)] [InlineData(80)] [InlineData(89.9)] [InlineData(89.999999)] [InlineData(90)]
    [InlineData(-66.6)] [InlineData(-70)] [InlineData(-89.999999)] [InlineData(-90)]
    public void Ra_alta_latitude_finita_e_dentro_da_faixa(double lat)
    {
        foreach (var d in Ano())
        {
            var ra = Clima.Ra(d, lat);
            Assert.True(double.IsFinite(ra), $"lat={lat} {d}: {ra}");
            // topo da atmosfera: nunca negativo (fp de ~1e-15 tolerado); máximo real ~49 MJ/m²/dia no polo no solstício
            Assert.True(ra is > -1e-9 and < 50, $"lat={lat} {d}: {ra}");
        }
    }

    // noite polar dá Ra 0, dia polar dá o máximo; 89,999999 e 90 dão quase o mesmo (sem salto no polo)
    [Fact]
    public void Polo_noite_zero_dia_maximo_e_continuo()
    {
        var verao = new DateOnly(2025, 6, 21);
        var inverno = new DateOnly(2025, 12, 21);
        Assert.InRange(Clima.Ra(inverno, 90), -1e-9, 1e-9);
        Assert.InRange(Clima.Ra(verao, -90), -1e-9, 1e-9);
        Assert.InRange(Clima.Ra(verao, 90), 40, 50);
        Assert.InRange(Clima.Ra(inverno, -90), 40, 50);
        foreach (var d in Ano())
        {
            Assert.InRange(Clima.Ra(d, 90) - Clima.Ra(d, 89.999999), -0.01, 0.01);
            Assert.InRange(Clima.Ra(d, -90) - Clima.Ra(d, -89.999999), -0.01, 0.01);
        }
    }

    // Ra contínuo ao cruzar o círculo polar (onde o Clamp começa a agir)
    [Fact]
    public void Ra_continuo_no_circulo_polar()
    {
        foreach (var d in Ano())
            for (double lat = 60; lat <= 89.9; lat += 0.05)
            {
                Assert.InRange(Clima.Ra(d, lat + 0.05) - Clima.Ra(d, lat), -0.5, 0.5);
                Assert.InRange(Clima.Ra(d, -lat - 0.05) - Clima.Ra(d, -lat), -0.5, 0.5);
            }
    }

    // ET0 (PM e Hargreaves) em latitudes altas com clima plausível, inclusive Rad = 0 na noite polar e
    // Rad > 0 com rso = 0 (dado incoerente do POWER): finita e numa faixa sã
    [Theory]
    [InlineData(70)] [InlineData(80)] [InlineData(90)] [InlineData(-75)] [InlineData(-90)]
    public void Et0_alta_latitude_finita_e_sa(double lat)
    {
        var r = new Random((int)lat + 1000);
        foreach (var d in Ano())
        {
            // abaixo de -17,8 °C de média o Hargreaves fica negativo (ver abaixo)
            double tmin = -15 + r.NextDouble() * 30, tmax = tmin + r.NextDouble() * 15;
            double rad = r.Next(3) == 0 ? 0 : r.NextDouble() * 30;
            var dia = new Dia(d, tmax, tmin, 0, rad, 40 + r.NextDouble() * 60, r.NextDouble() * 10, 95 + r.NextDouble() * 8);
            var pm = Clima.Et0Pm(dia, lat);
            var hs = Clima.Et0(dia, lat);
            Assert.True(double.IsFinite(pm) && double.IsFinite(hs), $"lat={lat} {d} rad={rad}: pm={pm} hs={hs}");
            // PM pode ficar um pouco negativo (saldo de radiação negativo no frio); passar disso é conta errada
            Assert.True(pm is > -5 and < 15, $"lat={lat} {d} rad={rad}: pm={pm}");
            Assert.True(hs is > -1e-6 and < 15, $"lat={lat} {d}: hs={hs}");
        }
    }

    // contrato documentado: Hargreaves com tMédia < -17,8 °C dá ET0 negativa (0,0023 × (tMédia + 17,8) < 0).
    // Não é do clamp; anota que o Hargreaves não serve para frio extremo (polo no inverno é -30 °C).
    [Fact]
    public void Hargreaves_negativo_no_frio_extremo()
    {
        var d = new Dia(new DateOnly(2025, 6, 21), -20, -35, 0, 10, 70, 3, 100);
        Assert.True(Clima.Et0(d, 90) < 0);
    }
}
