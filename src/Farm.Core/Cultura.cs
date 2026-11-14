// curva de Kc da FAO-56: 4 fases (inicial, desenvolvimento, média, final), em dias
// Ky: quanto a produção cai por unidade de falta de água (FAO-33). Ky > 1 = sensível.
public record Cultura(string Nome, int Ini, int Des, int Med, int Fim, double KcIni, double KcMed, double KcFim, double Ky = 0)
{
    public int Ciclo => Ini + Des + Med + Fim;

    // fase do dia n: 1 inicial, 2 desenvolvimento, 3 média (floração/enchimento), 4 final
    public int Fase(int n) => n < Ini ? 1 : n < Ini + Des ? 2 : n < Ini + Des + Med ? 3 : 4;

    public double Kc(int n)
    {
        if (n < Ini) return KcIni;                                    // fase inicial: Kc fixo baixo
        n -= Ini;
        if (n < Des) return KcIni + (KcMed - KcIni) * n / Des;        // desenvolvimento: sobe em rampa
        n -= Des;
        if (n < Med) return KcMed;                                    // fase média: Kc máximo
        n -= Med;
        return KcMed + (KcFim - KcMed) * n / Fim;                     // final: desce em rampa
    }
}

public static class Culturas
{
    // CHUTE INICIAL baseado na FAO-56 (tabelas 11 e 12). Conferir e ajustar para safrinha no Cerrado.
    public static readonly Dictionary<string, Cultura> Todas = new(StringComparer.OrdinalIgnoreCase)
    {
        ["milho"]      = new("milho",      20, 35, 40, 30, 0.30, 1.20, 0.35, Ky: 1.25),
        ["sorgo"]      = new("sorgo",      15, 30, 40, 25, 0.30, 1.00, 0.55, Ky: 0.90),
        ["braquiaria"] = new("braquiária", 10, 20, 60, 30, 0.40, 0.85, 0.85),
    };

    // culturas de grão, da mais exigente para a menos; braquiária é o plano B
    public static readonly Cultura[] Graos = [Todas["milho"], Todas["sorgo"]];
}
