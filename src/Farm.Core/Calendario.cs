// risco de cada grão num decêndio de plantio e a cultura indicada
public record RiscoDecendio(Decendio Decendio, IReadOnlyDictionary<string, double> Risco, string Recomendacao);

// parâmetros do balanço hídrico usados no calendário
// AguaIni: fração da CAD cheia no dia do plantio (null = balde cheio)
public record Parametros(double Lat, double Cad, double FatorChuva = 1, bool UsarPm = true,
                         string Criterio = "isna", double RiscoMax = 0.2, Func<DateOnly, double>? AguaIni = null);

public static class Calendario
{
    // ano-modelo das janelas: 2001 não é bissexto. Uma janela que cruza o ano termina em 2002.
    public const int AnoModelo = 2001;

    // leva uma data real para o ano-modelo (29/02 vira 28/02)
    public static DateOnly NoAnoModelo(DateOnly d) =>
        new(AnoModelo, d.Month, Math.Min(d.Day, DateTime.DaysInMonth(AnoModelo, d.Month)));

    // janela mês/dia no ano-modelo; se o fim vem antes do início, cruza o ano
    public static (DateOnly Di, DateOnly Df) Janela(DateOnly inicio, DateOnly fim)
    {
        var di = NoAnoModelo(inicio);
        var df = NoAnoModelo(fim);
        return (di, df < di ? df.AddYears(1) : df);
    }

    // a cultura mais exigente cujo risco cabe no limite; senão, braquiária
    public static string Escolher(IReadOnlyDictionary<string, double> risco, double riscoMax) =>
        Culturas.Graos.FirstOrDefault(c => risco[c.Nome] <= riscoMax)?.Nome ?? Culturas.Todas["braquiaria"].Nome;

    // risco = fração de (dia de plantio × ano) em que a cultura falha no critério
    public static RiscoDecendio Avaliar(Decendio dec, Dictionary<DateOnly, Dia> dias, IReadOnlyList<int> anos, Parametros p)
    {
        var risco = new Dictionary<string, double>();
        foreach (var c in Culturas.Graos)
        {
            int total = 0, falhas = 0;
            for (var dia = dec.Inicio; dia <= dec.Fim; dia = dia.AddDays(1))
                foreach (var a in anos)
                {
                    var plantio = new DateOnly(a, dia.Month, dia.Day); // cada decêndio usa os mesmos anos, com ou sem virada de ano
                    var s = Modelo.Simular(c, dias, plantio, p.Lat, p.Cad, p.FatorChuva, p.UsarPm, p.AguaIni?.Invoke(plantio) ?? 1);
                    if (s is null) continue;                 // ciclo passou do fim dos dados
                    total++;
                    if (!Modelo.Passa(c, s, p.Criterio)) falhas++;
                }
            risco[c.Nome] = total == 0 ? 1 : (double)falhas / total;
        }
        return new(dec, risco, Escolher(risco, p.RiscoMax));
    }

    public static IReadOnlyList<int> Anos(Dictionary<DateOnly, Dia> dias) =>
        dias.Keys.Select(d => d.Year).Distinct().Order().ToList();

    // calendário por decêndio de uma janela (datas no ano-modelo, ver Janela)
    public static List<RiscoDecendio> Calcular(DateOnly di, DateOnly df, Dictionary<DateOnly, Dia> dias, Parametros p)
    {
        var anos = Anos(dias);
        return Decendio.Entre(di, df).Select(dec => Avaliar(dec, dias, anos, p)).ToList();
    }

    // um só decêndio: o que contém a data (real) informada
    public static RiscoDecendio NaData(DateOnly data, Dictionary<DateOnly, Dia> dias, Parametros p) =>
        Avaliar(Decendio.De(NoAnoModelo(data)), dias, Anos(dias), p);
}
