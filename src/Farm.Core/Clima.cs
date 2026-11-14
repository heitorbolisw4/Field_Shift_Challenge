// fórmulas de evapotranspiração da FAO-56
public static class Clima
{
    // Ra: radiação no topo da atmosfera (MJ/m²/dia). FAO-56, equações 21 a 25.
    public static double Ra(DateOnly data, double latGraus)
    {
        double J = data.DayOfYear;
        double phi = latGraus * Math.PI / 180;                         // latitude em radianos
        double dr = 1 + 0.033 * Math.Cos(2 * Math.PI * J / 365);       // distância Terra-Sol
        double delta = 0.409 * Math.Sin(2 * Math.PI * J / 365 - 1.39); // declinação solar
        // ângulo do pôr do sol; perto dos polos o argumento sai de [-1, 1] (dia/noite polar) e o Acos daria NaN
        double ws = Math.Acos(Math.Clamp(-Math.Tan(phi) * Math.Tan(delta), -1, 1));
        return 24 * 60 / Math.PI * 0.0820 * dr *
               (ws * Math.Sin(phi) * Math.Sin(delta) + Math.Cos(phi) * Math.Cos(delta) * Math.Sin(ws));
    }

    // e°(T): pressão de vapor de saturação (kPa). FAO-56, equação 11.
    static double Esat(double t) => 0.6108 * Math.Exp(17.27 * t / (t + 237.3));

    // ET0 por Penman-Monteith (mm/dia). FAO-56, equação 6. É o método padrão da FAO.
    public static double Et0Pm(Dia d, double latGraus)
    {
        double t = (d.TMax + d.TMin) / 2;
        double delta = 4098 * Esat(t) / Math.Pow(t + 237.3, 2);       // inclinação da curva de vapor (eq. 13)
        double gama = 0.000665 * d.Pressao;                           // constante psicrométrica (eq. 8)
        double es = (Esat(d.TMax) + Esat(d.TMin)) / 2;                // vapor de saturação (eq. 12)
        double ea = es * d.UR / 100;                                  // vapor real, pela UR média (eq. 19)

        // saldo de radiação (eq. 37 a 40)
        double z = (293 - 293 * Math.Pow(d.Pressao / 101.3, 1 / 5.26)) / 0.0065; // altitude a partir da pressão
        double rso = (0.75 + 2e-5 * z) * Ra(d.Data, latGraus);       // radiação de céu limpo
        double rsRso = rso > 0 ? Math.Min(d.Rad / rso, 1.0) : 1.0;   // noite polar: rso = 0
        double rns = 0.77 * d.Rad;                                    // onda curta líquida (albedo 0,23)
        double rnl = 4.903e-9 * (Math.Pow(d.TMax + 273.16, 4) + Math.Pow(d.TMin + 273.16, 4)) / 2
                     * (0.34 - 0.14 * Math.Sqrt(ea)) * (1.35 * rsRso - 0.35); // onda longa perdida
        double rn = rns - rnl;                                        // G (fluxo do solo) ~ 0 no diário

        return (0.408 * delta * rn + gama * 900 / (t + 273) * d.Vento * (es - ea))
               / (delta + gama * (1 + 0.34 * d.Vento));
    }

    // ET0 por Hargreaves (mm/dia). FAO-56, equação 52. 0.408 converte MJ/m² em mm.
    public static double Et0(Dia d, double latGraus)
    {
        double tMedia = (d.TMax + d.TMin) / 2;
        return 0.0023 * (tMedia + 17.8) * Math.Sqrt(d.TMax - d.TMin) * 0.408 * Ra(d.Data, latGraus);
    }
}
