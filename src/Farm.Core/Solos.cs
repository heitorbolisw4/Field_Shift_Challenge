// classes de água disponível do ZARC (AD1 arenoso … AD6 argiloso) → CAD do balde (mm)
// balde ajustado contra o ZARC de Uberaba; AD3 validado também em Indianópolis
public static class Solos
{
    public static readonly IReadOnlyDictionary<string, double> Cad = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
    {
        ["AD1"] = 17, ["AD2"] = 20, ["AD3"] = 30, ["AD4"] = 35, ["AD5"] = 61, ["AD6"] = 80,
    };

    public const string Padrao = "AD3"; // na dúvida, AD3

    public static bool Valida(string? classe) => classe is not null && Cad.ContainsKey(classe);
}
