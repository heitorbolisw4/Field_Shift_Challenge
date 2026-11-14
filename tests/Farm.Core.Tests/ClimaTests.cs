// Ra e ET0 contra os exemplos resolvidos da FAO-56.
public class ClimaTests
{
    // FAO-56 exemplo 8: 3 de setembro, 20° S -> Ra = 32,2 MJ/m²/dia
    [Fact]
    public void Ra_Exemplo8_20GrausSul_3Setembro()
    {
        var ra = Clima.Ra(new DateOnly(2001, 9, 3), -20);
        Assert.InRange(ra, 32.1, 32.3);
    }

    // FAO-56 exemplo 18: Bruxelas (50°48' N), 6 de julho -> Ra = 41,09
    [Fact]
    public void Ra_Exemplo18_Bruxelas_6Julho()
    {
        var ra = Clima.Ra(new DateOnly(2001, 7, 6), 50.8);
        Assert.InRange(ra, 40.9, 41.3);
    }

    // inverno no sul tem menos radiação que verão no sul
    [Fact]
    public void Ra_HemisferioSul_VeraoMaiorQueInverno()
    {
        Assert.True(Clima.Ra(new DateOnly(2001, 12, 21), -19) > Clima.Ra(new DateOnly(2001, 6, 21), -19));
    }

    // FAO-56 exemplo 18: Bruxelas, ET0 = 3,9 mm/dia.
    // O exemplo usa URmax/URmin (84/63); o modelo usa a UR média (73,5), então a tolerância é maior.
    [Fact]
    public void Et0Pm_Exemplo18_Bruxelas()
    {
        var d = new Dia(new DateOnly(2001, 7, 6), TMax: 21.5, TMin: 12.3, Chuva: 0,
                        Rad: 22.07, UR: 73.5, Vento: 2.078, Pressao: 100.1);
        var et0 = Clima.Et0Pm(d, 50.8);
        Assert.InRange(et0, 3.7, 4.1);
    }

    // Hargreaves (eq. 52) com Ra do exemplo 8 (32,2): 0,0023 · 40,3 · √15 · 0,408 · 32,2 ≈ 4,72
    [Fact]
    public void Et0Hargreaves_ContaNaMao()
    {
        var d = new Dia(new DateOnly(2001, 9, 3), TMax: 30, TMin: 15, Chuva: 0,
                        Rad: 0, UR: 0, Vento: 0, Pressao: 0);
        Assert.InRange(Clima.Et0(d, -20), 4.67, 4.77);
    }

    // Cerrado na seca: PM e Hargreaves devem dar valores plausíveis e na mesma ordem
    [Fact]
    public void Et0_CerradoPlausivel()
    {
        var d = new Dia(new DateOnly(2001, 3, 15), TMax: 30, TMin: 18, Chuva: 0,
                        Rad: 20, UR: 65, Vento: 2, Pressao: 92);
        var pm = Clima.Et0Pm(d, -19);
        var hs = Clima.Et0(d, -19);
        Assert.InRange(pm, 2, 7);
        Assert.InRange(hs, 2, 7);
        Assert.InRange(pm / hs, 0.6, 1.4);
    }
}
