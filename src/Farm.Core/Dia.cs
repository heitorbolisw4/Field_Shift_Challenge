// um dia de clima vindo do NASA POWER (já sem os -999)
// Rad: radiação solar (MJ/m²/dia), UR: umidade relativa (%), Vento: m/s a 2 m, Pressao: kPa
public record Dia(DateOnly Data, double TMax, double TMin, double Chuva,
           double Rad, double UR, double Vento, double Pressao);
