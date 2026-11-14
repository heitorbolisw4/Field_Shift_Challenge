// curva de Kc e fases (FAO-56, 4 fases)
public class CulturaTests
{
    static readonly Cultura C = new("teste", Ini: 10, Des: 20, Med: 30, Fim: 40, KcIni: 0.3, KcMed: 1.2, KcFim: 0.4);

    [Fact]
    public void Ciclo_SomaDasFases() => Assert.Equal(100, C.Ciclo);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(9, 1)]
    [InlineData(10, 2)]
    [InlineData(29, 2)]
    [InlineData(30, 3)]
    [InlineData(59, 3)]
    [InlineData(60, 4)]
    [InlineData(99, 4)]
    public void Fase_Limites(int dia, int fase) => Assert.Equal(fase, C.Fase(dia));

    [Fact]
    public void Kc_Inicial_Medio_Rampas()
    {
        Assert.Equal(0.3, C.Kc(0), 6);
        Assert.Equal(0.3, C.Kc(9), 6);
        Assert.Equal(0.3, C.Kc(10), 6);              // começo da rampa
        Assert.Equal(0.75, C.Kc(20), 6);             // meio da rampa: (0,3 + 1,2) / 2
        Assert.Equal(1.2, C.Kc(30), 6);
        Assert.Equal(1.2, C.Kc(59), 6);
        Assert.Equal(0.8, C.Kc(80), 6);              // meio da descida: (1,2 + 0,4) / 2
    }

    [Fact]
    public void Kc_SempreEntreMinEMax()
    {
        for (int n = 0; n < C.Ciclo; n++)
            Assert.InRange(C.Kc(n), 0.3, 1.2);
    }

    [Fact]
    public void Graos_MaisExigentePrimeiro()
    {
        Assert.Equal("milho", Culturas.Graos[0].Nome);
        Assert.True(Culturas.Graos[0].Ky > Culturas.Graos[1].Ky);
    }
}
