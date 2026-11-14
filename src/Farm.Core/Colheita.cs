// pedaço da colheita em que a cultura indicada é a mesma; Ha = área colhida nesses dias
public record Trecho(string Cultura, DateOnly De, DateOnly Ate, int Dias, double Ha);

// para um grão: em quantos dias da colheita ele cabe no risco aceito (Pct, %) e o risco médio nesses dias (%)
public record AjusteGrao(string Cultura, int DiasCabe, int Pct, int RiscoMedioPct);

// conta da colheita da soja: quanto tempo leva e quantos ha vão para cada cultura da safrinha
public static class Colheita
{
    // ha colhidos por dia de calendário: ha/dia de todas as colhedoras × fração de dias úteis (chuva, quebra)
    public static double Capacidade(double haDiaTotal, double uteisPct) => haDiaTotal * uteisPct / 100;

    // tetos de entrada: 1000 ha/dia por colhedora já é o dobro da maior do mercado. Com o teto, a tolerância
    // de 1e-9 dia de Dias/Fila descarta no máximo HaDiaMax × ColhedorasMax × 1e-9 = 1e-4 ha (abaixo do 0,01 exibido)
    public const double HaDiaMax = 1000;
    public const int ColhedorasMax = 100;

    // a colheita passa de `dias` dias? Mesma tolerância de Dias, sem calcular Dias (com capacidade minúscula ele estoura int)
    public static bool PassaDe(double area, double capacidade, int dias) => area / capacidade - 1e-9 > dias;

    // mesma tolerância da Fila: 9,9 / 3,3 = 3,0000000000000004 são 3 dias, não 4
    public static int Dias(double area, double capacidade) =>
        capacidade > 0 && area > 0 ? Math.Max(1, (int)Math.Ceiling(area / capacidade - 1e-9)) : 0;

    // último dia de colheita (o início conta como dia 1)
    public static DateOnly Fim(DateOnly inicio, int dias) => inicio.AddDays(Math.Max(dias - 1, 0));

    // percorre a colheita dia a dia, pega a cultura de cada dia e junta dias seguidos iguais.
    // ha por dia = capacidade, sem passar da área total (o último dia pode ser parcial)
    public static List<Trecho> Trechos(DateOnly inicio, DateOnly fim, Func<DateOnly, string> culturaDoDia,
                                       double area, double capacidade)
    {
        var dias = new List<(DateOnly, string, double)>();
        for (var d = inicio; d <= fim; d = d.AddDays(1))
        {
            int k = d.DayNumber - inicio.DayNumber;
            dias.Add((d, culturaDoDia(d), Math.Max(0, Math.Min((k + 1) * capacidade, area) - k * capacidade)));
        }
        return Agrupar(dias);
    }

    // talhão dentro da fila de colheita. HaAntes = área colhida antes dele (outros talhões).
    public record Etapa(DateOnly De, DateOnly Ate, double HaAntes, double Area)
    {
        public int Dias => Ate.DayNumber - De.DayNumber + 1;
    }

    // talhões colhidos um depois do outro com capacidade contínua: se o anterior termina no meio do dia,
    // o próximo começa no mesmo dia com a capacidade que sobrou
    public static List<Etapa> Fila(DateOnly inicio, IReadOnlyList<double> areas, double capacidade)
    {
        const double eps = 1e-9;
        double antes = 0;
        var fila = new List<Etapa>();
        foreach (var a in areas)
        {
            int de = (int)Math.Floor(antes / capacidade + eps);
            int ate = Math.Max(de, (int)Math.Ceiling((antes + a) / capacidade - eps) - 1);
            fila.Add(new(inicio.AddDays(de), inicio.AddDays(ate), antes, a));
            antes += a;
        }
        return fila;
    }

    // trechos de um talhão da fila: em cada dia, a parte da capacidade que cai nele
    public static List<Trecho> Trechos(DateOnly inicio, Etapa e, double capacidade, Func<DateOnly, string> culturaDoDia)
    {
        var dias = new List<(DateOnly, string, double)>();
        for (var d = e.De; d <= e.Ate; d = d.AddDays(1))
        {
            int k = d.DayNumber - inicio.DayNumber;
            var ha = Math.Min(e.HaAntes + e.Area, (k + 1) * capacidade) - Math.Max(e.HaAntes, k * capacidade);
            dias.Add((d, culturaDoDia(d), Math.Max(0, ha)));
        }
        return Agrupar(dias);
    }

    // junta dias seguidos com a mesma cultura. Arredonda a 0,01 ha pelo acumulado, para baixo:
    // a soma dos trechos é exatamente a área (com 2 casas), nunca passa dela (10,125 fica 10,12)
    static List<Trecho> Agrupar(List<(DateOnly Dia, string Cultura, double Ha)> dias)
    {
        var grupos = new List<(string Cultura, DateOnly De, DateOnly Ate, int Dias, double Ha)>();
        foreach (var (d, c, ha) in dias)
            if (grupos.Count > 0 && grupos[^1].Cultura == c)
                grupos[^1] = grupos[^1] with { Ate = d, Dias = grupos[^1].Dias + 1, Ha = grupos[^1].Ha + ha };
            else grupos.Add((c, d, d, 1, ha));

        double acumulado = 0;
        long centAntes = 0;
        var trechos = new List<Trecho>();
        foreach (var g in grupos)
        {
            acumulado += g.Ha;
            var cent = (long)Math.Floor(acumulado * 100 + 1e-6); // 1e-6: 10,37 vira 1036,9999… em double
            trechos.Add(new(g.Cultura, g.De, g.Ate, g.Dias, (cent - centAntes) / 100.0));
            centAntes = cent;
        }
        return trechos;
    }

    // soma de ha sem erro de ponto flutuante (em centésimos)
    public static double Somar(IEnumerable<double> ha) =>
        ha.Sum(x => (long)Math.Round(x * 100, MidpointRounding.AwayFromZero)) / 100.0;

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
