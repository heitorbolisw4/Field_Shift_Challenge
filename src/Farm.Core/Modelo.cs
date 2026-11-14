// IsnaI / IsnaIII: água consumida ÷ água desejada nas fases I e III (índice do ZARC)
public record Safra(double Etc, double Chuva, double Deficit, int DiasSede, double IsnaI, double IsnaIII);

public static class Modelo
{
    public const double PerdaMax = 0.20; // CHUTE: aceita perder até 20% da produção...
    public const double AnosOk = 0.80;   // ...em pelo menos 8 de 10 anos (lógica do ZARC)

    // ZARC milho 2ª safra MG 2025/26 (Portaria SPA/MAPA 388/2025): ISNA >= 0,6 na fase I e >= 0,5 na fase III
    public const double IsnaMinI = 0.6;
    public const double IsnaMinIII = 0.5;

    // p: fração da CAD que a planta usa sem sofrer. FAO-56 tabela 22: milho e sorgo ~0,55
    public const double P = 0.55;

    // balanço hídrico dia a dia com balde de solo; null se faltar dado no ciclo
    // aguaIni: fração da CAD cheia no dia do plantio (1 = cheio; o SMAP dá o valor observado)
    public static Safra? Simular(Cultura c, Dictionary<DateOnly, Dia> dias, DateOnly ini, double lat, double cad, double fatorChuva = 1, bool usarPm = false, double aguaIni = 1)
    {
        double etc = 0, chuva = 0, deficit = 0;
        double agua = Math.Clamp(aguaIni, 0, 1) * cad;
        int diasSede = 0;
        var etm = new double[5];   // água desejada por fase (índice 1 a 4)
        var etr = new double[5];   // água consumida por fase

        for (int n = 0; n < c.Ciclo; n++)
        {
            if (!dias.TryGetValue(ini.AddDays(n), out var d)) return null;
            var et0Dia = usarPm ? Clima.Et0Pm(d, lat) : Clima.Et0(d, lat);
            var etcDia = et0Dia * c.Kc(n);             // ETc = ET0 * Kc do dia n do ciclo
            var chuvaDia = d.Chuva * fatorChuva;        // CALIBRAÇÃO: corrige o POWER se ele subestima a chuva
            etc += etcDia;
            chuva += chuvaDia;

            agua += chuvaDia;                          // balde: entra chuva
            if (agua > cad) agua = cad;                // transbordou: excesso escorre

            // Ks (FAO-56, eq. 84): com o balde abaixo de (1-p)*CAD, a planta já não consegue puxar toda a água
            double limite = (1 - P) * cad;
            double ks = agua >= limite ? 1 : agua / limite;
            double etrDia = Math.Min(ks * etcDia, agua); // água que a planta consegue consumir (nunca mais do que há)
            agua -= etrDia;                            // sai o que foi consumido

            double falta = etcDia - etrDia;
            deficit += falta;
            if (ks < 1) diasSede++;                    // dia com estresse hídrico

            int f = c.Fase(n);
            etm[f] += etcDia;
            etr[f] += etcDia - falta;
        }
        return new Safra(etc, chuva, deficit, diasSede, etr[1] / etm[1], etr[3] / etm[3]);
    }

    // perda relativa de produção: Ky * (falta de água / água que a planta queria). FAO-33.
    public static double Perda(Cultura c, Safra s) => c.Ky * s.Deficit / s.Etc;

    // faixas de risco do ZARC: 20%, 30%, 40%; acima disso, não indicado
    public static string Nivel(double risco) =>
        risco <= 0.20 ? "20%" : risco <= 0.30 ? "30%" : risco <= 0.40 ? "40%" : "inapto";

    // a safra "deu certo" nesse ano? criterio = "isna" (ZARC) ou "perda" (FAO-33)
    public static bool Passa(Cultura c, Safra s, string criterio) => criterio == "perda"
        ? Perda(c, s) <= PerdaMax
        : s.IsnaI >= IsnaMinI && s.IsnaIII >= IsnaMinIII;
}
