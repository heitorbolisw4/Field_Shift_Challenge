using System.Globalization;
using System.Reflection;

// testes unitários do leitor de números da query (src/Farm.Api/Numeros.cs, commit 1cce046).
// Numeros é internal na API: chega por reflexão (sem InternalsVisibleTo, sem mexer em src).
public class NumerosLeitorTests
{
    static readonly Type Tipo = typeof(FarmDb).Assembly.GetType("Numeros", throwOnError: true)!;
    static readonly MethodInfo ValidarM = Tipo.GetMethod("Validar", BindingFlags.Public | BindingFlags.Static)!;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static string? Validar(string nome, string? texto) => (string?)ValidarM.Invoke(null, [nome, texto]);
    static void Ok(string nome, string texto) => Assert.True(Validar(nome, texto) is null, $"{nome}={texto} -> {Validar(nome, texto)}");
    static void Ruim(string nome, string texto) => Assert.True(Validar(nome, texto) is not null, $"{nome}={texto} deveria ser recusado");

    // o que o binding do minimal API faz com um double da query: Float | AllowThousands, cultura invariante
    static bool Binding(string s, out double v) =>
        double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, Inv, out v);

    public static readonly string[] Nomes = ["lat", "lon", "cad", "riscoMax", "fatorChuva", "area", "haDia", "colhedoras", "uteis"];

    // ---------- a tabela cobre todo número de query das rotas ----------

    [Fact]
    public void Tabela_tem_todos_os_numeros_das_rotas()
    {
        var faixas = (System.Collections.IDictionary)Tipo.GetField("Faixas")!.GetValue(null)!;
        foreach (var n in Nomes) Assert.True(faixas.Contains(n), $"{n} fora de Numeros.Faixas");
        Assert.Equal(Nomes.Length, faixas.Count);
    }

    // ---------- nome fora da tabela e valor ausente não são mexidos ----------

    [Theory]
    [InlineData("ordem", "3,1,2")]
    [InlineData("inicio", "2026-01-05")]
    [InlineData("fonteChuva", "NaN")]
    [InlineData("latitude", "1,5")]   // parecido com lat, mas outro nome
    [InlineData("lat ", "1,5")]       // espaço no nome: o binding também não acha "lat"
    [InlineData("", "abc")]
    public void Nome_fora_da_tabela_passa(string nome, string texto) => Assert.Null(Validar(nome, texto));

    [Fact]
    public void Texto_nulo_passa() => Assert.Null(Validar("cad", null));

    // ---------- vírgula em qualquer posição ----------

    [Theory]
    [InlineData("cad", "30,5")]
    [InlineData("cad", "1,000")]
    [InlineData("cad", ",5")]
    [InlineData("cad", "5,")]
    [InlineData("cad", ",")]
    [InlineData("riscoMax", "0,2")]
    [InlineData("lat", "-15,55")]
    [InlineData("area", "1.000,5")]
    [InlineData("area", "1,000.5")]   // milhar en-US: também recusado
    [InlineData("colhedoras", "1,0")]
    public void Virgula_e_recusada(string nome, string texto)
    {
        var erro = Validar(nome, texto);
        Assert.NotNull(erro);
        Assert.Contains(nome, erro);
        Assert.Contains("vírgula", erro);
    }

    // ---------- truques de cultura/formato que o double.Parse invariante não aceita ----------

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData(".")]
    [InlineData("-")]
    [InlineData("+")]
    [InlineData("e5")]
    [InlineData("1e")]
    [InlineData("1e+")]
    [InlineData("1e1.5")]
    [InlineData("1.2.3")]
    [InlineData("1 0")]          // espaço no meio
    [InlineData("1 0")]     // espaço inseparável
    [InlineData("1_0")]
    [InlineData("0x10")]
    [InlineData("10d")]
    [InlineData("10f")]
    [InlineData("10m")]
    [InlineData("$10")]
    [InlineData("10%")]
    [InlineData("(10)")]         // negativo contábil
    [InlineData("10-")]          // sinal no fim
    [InlineData("--10")]
    [InlineData("+-10")]
    [InlineData("++10")]
    [InlineData("−10")]     // sinal de menos Unicode
    [InlineData("１０")] // dígitos de largura total
    [InlineData("١٠")] // dígitos arábico-índicos
    [InlineData("１0")]
    [InlineData("10'000")]
    [InlineData("1e5e5")]
    [InlineData("∞")]
    [InlineData("abc")]
    [InlineData("true")]
    [InlineData("null")]
    public void Texto_que_nao_e_numero_e_recusado(string texto)
    {
        foreach (var n in Nomes) Ruim(n, texto);
    }

    // ---------- não finitos, em todas as grafias ----------

    [Theory]
    [InlineData("NaN")]
    [InlineData("nan")]
    [InlineData("-NaN")]
    [InlineData("+NaN")]
    [InlineData("Infinity")]
    [InlineData("infinity")]
    [InlineData("INFINITY")]
    [InlineData("-Infinity")]
    [InlineData("+Infinity")]
    [InlineData(" Infinity ")]
    [InlineData("1e309")]
    [InlineData("-1e309")]
    [InlineData("1.8e308")]
    [InlineData("1e99999")]
    public void Nao_finito_e_recusado(string texto)
    {
        foreach (var n in Nomes)
        {
            var erro = Validar(n, texto);
            Assert.NotNull(erro);
            Assert.Contains("finito", erro);
        }
    }

    // ---------- sinais, espaços em volta, expoentes: o que o double.Parse invariante aceita ----------

    [Theory]
    [InlineData("cad", "+30")]
    [InlineData("cad", " 30")]
    [InlineData("cad", "30 ")]
    [InlineData("cad", "\t30\n")]
    [InlineData("cad", "\r\n30\r\n")]
    [InlineData("cad", "3e1")]
    [InlineData("cad", "3E1")]
    [InlineData("cad", "3e+1")]
    [InlineData("cad", "300e-1")]
    [InlineData("cad", "30.")]
    [InlineData("cad", ".5")]
    [InlineData("cad", "0030")]
    [InlineData("cad", "5e-324")]                 // menor double positivo: > 0
    [InlineData("riscoMax", "-0")]                // -0 == 0, mínimo fechado
    [InlineData("riscoMax", "0e5")]
    [InlineData("riscoMax", "1.0000000000000000001")] // vira 1.0 exato
    [InlineData("riscoMax", "1e0")]
    [InlineData("riscoMax", "100e-2")]
    [InlineData("lat", "-90")]
    [InlineData("lat", "+90")]
    [InlineData("lat", "-9e1")]
    [InlineData("lon", "-180")]
    [InlineData("lon", "180.0")]
    [InlineData("fatorChuva", "5")]
    [InlineData("fatorChuva", "0")]
    [InlineData("area", "1e6")]
    [InlineData("area", "1000000")]
    [InlineData("haDia", "1e3")]
    [InlineData("colhedoras", "1")]
    [InlineData("colhedoras", "100")]
    [InlineData("colhedoras", "1e2")]             // inteiro pelo valor (o binding de int recusa depois)
    [InlineData("colhedoras", "2.0")]
    [InlineData("colhedoras", "+3")]
    [InlineData("uteis", "100")]
    [InlineData("uteis", "1e-300")]
    public void Escritas_validas_passam(string nome, string texto) => Ok(nome, texto);

    // ---------- bordas de cada faixa, de um lado e do outro ----------

    [Theory]
    [InlineData("lat", "90.000000000001")]
    [InlineData("lat", "-90.000000000001")]
    [InlineData("lon", "180.00000000001")]
    [InlineData("lon", "-180.00000000001")]
    [InlineData("cad", "0")]
    [InlineData("cad", "-0")]
    [InlineData("cad", "0e10")]
    [InlineData("cad", "-5e-324")]
    [InlineData("cad", "500.00000000001")]
    [InlineData("cad", "5e3")]
    [InlineData("riscoMax", "-1e-300")]
    [InlineData("riscoMax", "1.000000000000001")]
    [InlineData("fatorChuva", "-0.000001")]
    [InlineData("fatorChuva", "5.000000000001")]
    [InlineData("area", "0")]
    [InlineData("area", "-1")]
    [InlineData("area", "1000000.0000001")]
    [InlineData("haDia", "0")]
    [InlineData("haDia", "-0")]
    [InlineData("haDia", "1000.0000000001")]
    [InlineData("colhedoras", "0")]
    [InlineData("colhedoras", "101")]
    [InlineData("colhedoras", "1.5")]
    [InlineData("colhedoras", "1.0000000001")]
    [InlineData("colhedoras", "-1")]
    [InlineData("uteis", "0")]
    [InlineData("uteis", "100.0000000001")]
    public void Fora_da_faixa_e_recusado(string nome, string texto)
    {
        var erro = Validar(nome, texto);
        Assert.NotNull(erro);
        Assert.Contains("precisa ser", erro);
    }

    // o nome não diferencia maiúsculas, como o binding
    [Theory]
    [InlineData("RISCOMAX")]
    [InlineData("riscomax")]
    [InlineData("RiscoMax")]
    public void Nome_sem_diferenca_de_maiusculas(string nome) => Ruim(nome, "2");

    // a mensagem mostra a faixa em formato invariante (com ponto), mesmo com o PC em pt-BR
    [Fact]
    public void Mensagem_da_faixa_com_ponto_em_pt_BR()
    {
        var antes = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = Inv;
            var inv = Nomes.ToDictionary(n => n, n => Validar(n, "-1e7"));
            CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
            foreach (var n in Nomes) Assert.Equal(inv[n], Validar(n, "-1e7"));
            Assert.Contains("maior que 0 e até 500", Validar("cad", "-1"));
        }
        finally { CultureInfo.CurrentCulture = antes; }
    }

    // ---------- propriedade: o que o leitor aceita, o binding lê com o mesmo valor ----------
    // Se divergirem, a rota roda com um número que o leitor nunca conferiu.

    [Fact]
    public void Aceito_pelo_leitor_e_lido_igual_pelo_binding()
    {
        var rnd = new Random(20261114);
        const string alfabeto = "0123456789.,eE+- \t";
        string[] pedacos = ["NaN", "Infinity", "1e308", "5e-324", "0", "00", ".", "-", "+", "e"];
        var aceitos = 0;
        for (var k = 0; k < 200_000; k++)
        {
            var sb = new System.Text.StringBuilder();
            var tam = rnd.Next(1, 9);
            for (var i = 0; i < tam; i++)
                sb.Append(rnd.Next(8) == 0 ? pedacos[rnd.Next(pedacos.Length)] : alfabeto[rnd.Next(alfabeto.Length)].ToString());
            var s = sb.ToString();
            var nome = Nomes[rnd.Next(Nomes.Length)];
            if (Validar(nome, s) is not null) continue;
            aceitos++;
            Assert.True(double.TryParse(s, NumberStyles.Float, Inv, out var lido), $"{nome}=\"{s}\" aceito mas não é número");
            Assert.True(Binding(s, out var b), $"{nome}=\"{s}\" aceito pelo leitor e recusado pelo binding");
            Assert.True(lido.Equals(b), $"{nome}=\"{s}\": leitor {lido}, binding {b}");
            Assert.True(double.IsFinite(b));
        }
        Assert.True(aceitos > 1000, $"só {aceitos} aceitos: o gerador não está exercitando o caminho bom");
    }

    // ---------- propriedade: dentro/fora da faixa bate com a definição ----------

    [Fact]
    public void Faixa_bate_com_a_definicao_em_valores_aleatorios()
    {
        var rnd = new Random(42);
        (string Nome, double Min, double Max, bool Aberto, bool Inteiro)[] f =
        [
            ("lat", -90, 90, false, false), ("lon", -180, 180, false, false), ("cad", 0, 500, true, false),
            ("riscoMax", 0, 1, false, false), ("fatorChuva", 0, 5, false, false), ("area", 0, 1_000_000, true, false),
            ("haDia", 0, 1000, true, false), ("colhedoras", 1, 100, false, true), ("uteis", 0, 100, true, false),
        ];
        foreach (var (nome, min, max, aberto, inteiro) in f)
            for (var k = 0; k < 5000; k++)
            {
                // perto das bordas na maioria das vezes, às vezes longe
                var v = rnd.Next(4) switch
                {
                    0 => min + (rnd.NextDouble() - 0.5) * 1e-6,
                    1 => max + (rnd.NextDouble() - 0.5) * 1e-6,
                    2 => Math.Round(min - 2 + rnd.NextDouble() * (max - min + 4)),
                    _ => (rnd.NextDouble() - 0.5) * 4 * Math.Max(Math.Abs(min), Math.Abs(max)),
                };
                var s = v.ToString("R", Inv);
                var esperado = (aberto ? v > min : v >= min) && v <= max && (!inteiro || v == Math.Floor(v));
                Assert.True(esperado == (Validar(nome, s) is null), $"{nome}={s}: esperado {(esperado ? "aceitar" : "recusar")}");
            }
    }
}
