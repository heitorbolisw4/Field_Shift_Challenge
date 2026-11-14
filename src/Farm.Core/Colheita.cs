// pedaço da colheita em que a cultura indicada é a mesma; Ha = área colhida nesses dias
public record Trecho(string Cultura, DateOnly De, DateOnly Ate, int Dias, double Ha);

// para um grão: em quantos dias da colheita ele cabe no risco aceito (Pct, %) e o risco médio nesses dias (%)
public record AjusteGrao(string Cultura, int DiasCabe, int Pct, int RiscoMedioPct);

// conta da colheita da soja: quanto tempo leva e quantos ha vão para cada cultura da safrinha
public static class Colheita
{
    // ha colhidos por dia de calendário: ha/dia de todas as colhedoras × fração de dias úteis (chuva, quebra)
    public static double Capacidade(double haDiaTotal, double uteisPct) => haDiaTotal * uteisPct / 100;

    public static int Dias(double area, double capacidade) =>
        capacidade > 0 ? (int)Math.Ceiling(area / capacidade) : 0;

    // último dia de colheita (o início conta como dia 1)
    public static DateOnly Fim(DateOnly inicio, int dias) => inicio.AddDays(Math.Max(dias - 1, 0));

    // percorre a colheita dia a dia, pega a cultura de cada dia e junta dias seguidos iguais.
    // ha por trecho = dias × capacidade, sem passar da área total (arredondado a 0,1 ha)
    public static List<Trecho> Trechos(DateOnly inicio, DateOnly fim, Func<DateOnly, string> culturaDoDia,
                                       double area, double capacidade)
    {
        var dias = new List<(string Cultura, DateOnly De, DateOnly Ate, int Dias)>();
        for (var d = inicio; d <= fim; d = d.AddDays(1))
        {
            var c = culturaDoDia(d);
            if (dias.Count > 0 && dias[^1].Cultura == c) dias[^1] = dias[^1] with { Ate = d, Dias = dias[^1].Dias + 1 };
            else dias.Add((c, d, d, 1));
        }

        var resto = area;
        var trechos = new List<Trecho>();
        foreach (var t in dias)
        {
            var ha = Math.Round(Math.Max(0, Math.Min(t.Dias * capacidade, resto)), 1, MidpointRounding.AwayFromZero);
            resto -= ha;
            trechos.Add(new(t.Cultura, t.De, t.Ate, t.Dias, ha));
        }
        return trechos;
    }

    // para cada grão, quantos dias da colheita ficam dentro do risco aceito.
    // riscoDoDia: risco de cada grão no decêndio do dia (null = dia fora do calendário)
    public static List<AjusteGrao> AjusteDosGraos(DateOnly inicio, DateOnly fim,
        Func<DateOnly, IReadOnlyDictionary<string, double>?> riscoDoDia, double riscoMax)
    {
        int n = 0;
        var cabe = new Dictionary<string, int>();
        var soma = new Dictionary<string, double>();
        for (var d = inicio; d <= fim; d = d.AddDays(1))
        {
            if (riscoDoDia(d) is not { } r) continue;
            n++;
            foreach (var (g, v) in r)
            {
                cabe[g] = cabe.GetValueOrDefault(g) + (v <= riscoMax ? 1 : 0);
                soma[g] = soma.GetValueOrDefault(g) + v;
            }
        }
        return cabe.Keys.Select(g => new AjusteGrao(g, cabe[g],
            (int)Math.Round(100.0 * cabe[g] / n, MidpointRounding.AwayFromZero),
            (int)Math.Round(100 * soma[g] / n, MidpointRounding.AwayFromZero))).ToList();
    }
}
