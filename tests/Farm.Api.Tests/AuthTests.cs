using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

[Collection("api")]
public class AuthTests(ApiFabrica api)
{
    const string Senha = "senha-boa-123";
    static string Email() => $"u{Guid.NewGuid():N}@teste.dev";

    async Task<JsonElement> Registrar(string email, string senha = Senha) =>
        await api.Anonimo().PostOk("/auth/registrar", new { email, senha });

    async Task<HttpResponseMessage> Renovar(string refresh) =>
        await api.Anonimo().PostAsJsonAsync("/auth/renovar", new { refreshToken = refresh });

    static string Rt(JsonElement t) => t.GetProperty("refreshToken").GetString()!;
    static string At(JsonElement t) => t.GetProperty("accessToken").GetString()!;

    async Task<HttpStatusCode> Eu(string token)
    {
        var c = ApiFabrica.ComoDono(api.Anonimo(), token);
        return (await c.GetAsync("/auth/eu")).StatusCode;
    }

    // ---------- registrar ----------

    [Fact]
    public async Task Registrar_normaliza_email_e_ja_loga()
    {
        var bruto = $"  Fulano.{Guid.NewGuid():N}@Teste.DEV ";
        var r = await api.Anonimo().PostAsJsonAsync("/auth/registrar", new { email = bruto, senha = Senha, nome = "  Fulano " });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var t = await r.Json();
        Assert.Equal(bruto.Trim().ToLowerInvariant(), t.GetProperty("usuario").GetProperty("email").GetString());
        Assert.Equal("Fulano", t.GetProperty("usuario").GetProperty("nome").GetString());
        Assert.False(string.IsNullOrEmpty(Rt(t)));

        var eu = await ApiFabrica.ComoDono(api.Anonimo(), At(t)).GetOk("/auth/eu");
        Assert.Equal(t.GetProperty("usuario").GetProperty("id").GetString(), eu.GetProperty("id").GetString());
        Assert.False(eu.TryGetProperty("senhaHash", out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("semarroba.com")]
    [InlineData("a@")]  // 2 caracteres
    [InlineData("tem espaco@x.com")]
    public async Task Registrar_email_invalido_da_400(string email)
    {
        var r = await api.Anonimo().PostAsJsonAsync("/auth/registrar", new { email, senha = Senha });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Registrar_email_longo_demais_da_400()
    {
        var r = await api.Anonimo().PostAsJsonAsync("/auth/registrar", new { email = new string('a', 250) + "@x.com", senha = Senha });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Theory]
    [InlineData("1234567")]
    [InlineData("")]
    public async Task Registrar_senha_curta_da_400(string senha)
    {
        var r = await api.Anonimo().PostAsJsonAsync("/auth/registrar", new { email = Email(), senha });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Registrar_senha_de_8_passa()
    {
        var r = await api.Anonimo().PostAsJsonAsync("/auth/registrar", new { email = Email(), senha = "12345678" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Registrar_duplicado_da_409_ignorando_caixa_e_espacos()
    {
        var email = Email();
        await Registrar(email);
        var r = await api.Anonimo().PostAsJsonAsync("/auth/registrar", new { email = "  " + email.ToUpperInvariant(), senha = "outra-senha-1" });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
    }

    // ---------- login ----------

    [Fact]
    public async Task Login_certo_errado_e_inexistente()
    {
        var email = Email();
        var reg = await Registrar(email);

        var ok = await api.Anonimo().PostAsJsonAsync("/auth/login", new { email = email.ToUpperInvariant(), senha = Senha });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(reg.GetProperty("usuario").GetProperty("id").GetString(),
                     (await ok.Json()).GetProperty("usuario").GetProperty("id").GetString());

        var errada = await api.Anonimo().PostAsJsonAsync("/auth/login", new { email, senha = Senha + "x" });
        Assert.Equal(HttpStatusCode.Unauthorized, errada.StatusCode);

        var semConta = await api.Anonimo().PostAsJsonAsync("/auth/login", new { email = Email(), senha = Senha });
        Assert.Equal(HttpStatusCode.Unauthorized, semConta.StatusCode);
        // mesma resposta nos dois casos: não revela quem tem conta
        Assert.Equal(await errada.Content.ReadAsStringAsync(), await semConta.Content.ReadAsStringAsync());
    }

    // ---------- validade dos tokens ----------

    // Pedido do usuário: "20 min". O commit 461cdb9 leu como JWT de acesso = 20 min; o refresh ficou 30 dias.
    [Fact]
    public async Task Validade_jwt_20min_refresh_30dias()
    {
        var antes = DateTimeOffset.UtcNow;
        var t = await Registrar(Email());
        var acesso = t.GetProperty("expiraEm").GetDateTimeOffset() - antes;
        var refresh = t.GetProperty("refreshExpiraEm").GetDateTimeOffset() - antes;
        Assert.InRange(acesso.TotalMinutes, 19.5, 20.5);
        Assert.InRange(refresh.TotalDays, 29.99, 30.01);

        var exp = new JwtSecurityTokenHandler().ReadJwtToken(At(t)).ValidTo;
        Assert.InRange((exp - antes.UtcDateTime).TotalMinutes, 19.5, 20.5);
    }

    // ---------- renovar (rotação) ----------

    [Fact]
    public async Task Renovar_rotaciona_e_reuso_derruba_a_cadeia()
    {
        var t1 = await Registrar(Email());

        var r2 = await Renovar(Rt(t1));
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        var t2 = await r2.Json();
        Assert.NotEqual(Rt(t1), Rt(t2));
        Assert.NotEqual(At(t1), At(t2));
        Assert.Equal(HttpStatusCode.OK, await Eu(At(t2)));

        // refresh antigo: 401, e o reuso revoga o novo também
        Assert.Equal(HttpStatusCode.Unauthorized, (await Renovar(Rt(t1))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Renovar(Rt(t2))).StatusCode);

        // o JWT já emitido segue valendo até expirar (stateless): esperado, mas vale saber
        Assert.Equal(HttpStatusCode.OK, await Eu(At(t2)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("nao-existe")]
    public async Task Renovar_token_desconhecido_da_401(string rt) =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await Renovar(rt)).StatusCode);

    [Fact]
    public async Task Renovar_refresh_expirado_da_401()
    {
        var t = await Registrar(Email());
        var id = t.GetProperty("usuario").GetProperty("id").GetString()!;
        using (var escopo = api.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<FarmDb>();
            var rt = await db.RefreshTokens.SingleAsync(r => r.UsuarioId == id);
            rt.ExpiraEm = DateTimeOffset.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await Renovar(Rt(t))).StatusCode);
    }

    [Fact]
    public async Task Refresh_guardado_so_como_hash()
    {
        var t = await Registrar(Email());
        using var escopo = api.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<FarmDb>();
        Assert.False(await db.RefreshTokens.AnyAsync(r => r.Hash == Rt(t)));
        var u = await db.Usuarios.SingleAsync(x => x.Id == t.GetProperty("usuario").GetProperty("id").GetString());
        Assert.DoesNotContain(Senha, u.SenhaHash);
    }

    // ---------- sair ----------

    [Fact]
    public async Task Sair_revoga_o_refresh()
    {
        var t = await Registrar(Email());
        var r = await api.Anonimo().PostAsJsonAsync("/auth/sair", new { refreshToken = Rt(t) });
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Renovar(Rt(t))).StatusCode);
        // sair com token qualquer não quebra (idempotente)
        Assert.Equal(HttpStatusCode.NoContent, (await api.Anonimo().PostAsJsonAsync("/auth/sair", new { refreshToken = "lixo" })).StatusCode);
    }

    // Efeito colateral: depois de "sair" no aparelho A, um renovar atrasado com o refresh de A (ex.: app que tenta de novo)
    // é tratado como roubo e derruba a sessão do aparelho B. Logout e rotação gravam o mesmo RevogadoEm.
    [Fact]
    public async Task Renovar_depois_de_sair_derruba_as_outras_sessoes()
    {
        var email = Email();
        var a = await Registrar(email);
        var b = await api.Anonimo().PostOk("/auth/login", new { email, senha = Senha });

        await api.Anonimo().PostAsJsonAsync("/auth/sair", new { refreshToken = Rt(a) });
        Assert.Equal(HttpStatusCode.Unauthorized, (await Renovar(Rt(a))).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Renovar(Rt(b))).StatusCode); // sessão B morreu junto
    }

    // ---------- JWT adulterado, expirado, de outra chave ----------

    static string Emitir(string sub, DateTime? expira = null, string emissor = "farm-api", string publico = "farm-app", string chave = ApiFabrica.ChaveJwt)
    {
        var exp = expira ?? DateTime.UtcNow.AddMinutes(10);
        var jwt = new JwtSecurityToken(emissor, publico, [new Claim("sub", sub)],
            notBefore: exp.AddHours(-1), expires: exp,
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(chave)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    [Fact]
    public async Task Token_emitido_com_a_chave_certa_passa()
    {
        var id = (await Registrar(Email())).GetProperty("usuario").GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, await Eu(Emitir(id)));
    }

    [Fact]
    public async Task Tokens_ruins_dao_401()
    {
        var t = await Registrar(Email());
        var id = t.GetProperty("usuario").GetProperty("id").GetString()!;
        var bom = At(t);
        var partes = bom.Split('.');

        // assinatura trocada
        var sig = partes[2].ToCharArray(); sig[5] = sig[5] == 'A' ? 'B' : 'A';
        // payload trocado (sub de outro), assinatura original
        var payload = Base64UrlEncoder.Encode($$"""{"sub":"outro","iss":"farm-api","aud":"farm-app","exp":{{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}}}""");
        // alg none
        var none = Base64UrlEncoder.Encode("""{"alg":"none","typ":"JWT"}""") + "." + partes[1] + ".";

        var ruins = new Dictionary<string, string>
        {
            ["assinatura adulterada"] = $"{partes[0]}.{partes[1]}.{new string(sig)}",
            ["payload adulterado"] = $"{partes[0]}.{payload}.{partes[2]}",
            ["alg none"] = none,
            ["expirado (além da folga de 1 min)"] = Emitir(id, DateTime.UtcNow.AddMinutes(-2)),
            ["outro emissor"] = Emitir(id, emissor: "intruso"),
            ["outro público"] = Emitir(id, publico: "intruso"),
            ["outra chave"] = Emitir(id, chave: "outra-chave-com-mais-de-32-bytes-0123456789"),
            ["lixo"] = "abc.def.ghi",
        };
        foreach (var (nome, token) in ruins)
        {
            var c = ApiFabrica.ComoDono(api.Anonimo(), token);
            Assert.True((await c.GetAsync("/fazendas")).StatusCode == HttpStatusCode.Unauthorized, nome);
        }
    }

    // usuário apagado do banco: /auth/eu dá 401, mas as rotas da fazenda só olham o "sub" e aceitam até o JWT expirar
    [Fact]
    public async Task Jwt_de_usuario_apagado_ainda_abre_fazendas()
    {
        var t = await Registrar(Email());
        var id = t.GetProperty("usuario").GetProperty("id").GetString()!;
        using (var escopo = api.Services.CreateScope())
            await escopo.ServiceProvider.GetRequiredService<FarmDb>().Usuarios.Where(u => u.Id == id).ExecuteDeleteAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, await Eu(At(t)));
        var c = ApiFabrica.ComoDono(api.Anonimo(), At(t));
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/fazendas", new { nome = "fantasma" })).StatusCode);
    }

    // ---------- rate limit ----------

    [Fact]
    public async Task Auth_limita_10_por_minuto_por_ip()
    {
        var ip = ApiFabrica.IpNovo();
        var c = api.Anonimo(ip);
        var codigos = new List<HttpStatusCode>();
        for (int i = 0; i < 11; i++)
            codigos.Add((await c.PostAsJsonAsync("/auth/login", new { email = Email(), senha = Senha })).StatusCode);

        Assert.All(codigos.Take(10), s => Assert.Equal(HttpStatusCode.Unauthorized, s));
        Assert.Equal(HttpStatusCode.TooManyRequests, codigos[10]);
        // o limite vale para todo /auth, não só login
        Assert.Equal(HttpStatusCode.TooManyRequests, (await c.PostAsJsonAsync("/auth/registrar", new { email = Email(), senha = Senha })).StatusCode);
        // outro IP não é afetado
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Anonimo().PostAsJsonAsync("/auth/login", new { email = Email(), senha = Senha })).StatusCode);
        // rotas fora de /auth não têm limite
        for (int i = 0; i < 12; i++) Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/solos")).StatusCode);
    }
}
