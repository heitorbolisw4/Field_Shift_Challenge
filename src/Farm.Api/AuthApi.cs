using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

// configuração do JWT (seção "Jwt" do appsettings / variáveis Jwt__Chave etc.)
public class JwtOpcoes
{
    public string Chave { get; set; } = "";            // segredo HS256, 32+ bytes. Produção: variável de ambiente Jwt__Chave
    public string Emissor { get; set; } = "farm-api";
    public string Publico { get; set; } = "farm-app";
    public int MinutosAcesso { get; set; } = 20;        // vida do JWT
    public int DiasRefresh { get; set; } = 30;          // vida do refresh token

    public SymmetricSecurityKey ChaveAssinatura()
    {
        var bytes = Encoding.UTF8.GetBytes(Chave);
        if (bytes.Length < 32) throw new InvalidOperationException("Jwt:Chave precisa ter ao menos 32 bytes (defina Jwt__Chave)");
        return new SymmetricSecurityKey(bytes);
    }
}

// cadastro, login e troca de tokens
static class AuthApi
{
    public record RegistrarIn(string Email, string Senha, string? Nome = null);
    public record LoginIn(string Email, string Senha);
    public record RefreshIn(string RefreshToken);
    public record TokensOut(string AccessToken, DateTimeOffset ExpiraEm, string RefreshToken, DateTimeOffset RefreshExpiraEm, UsuarioOut Usuario);
    public record UsuarioOut(string Id, string Email, string? Nome);

    public const int SenhaMin = 8;
    static readonly PasswordHasher<Usuario> hasher = new();
    // usuário de mentira: o login sem conta também calcula um hash (sem atalho de tempo)
    static readonly Usuario Falso = Criar();
    static Usuario Criar() { var u = new Usuario { Email = "", SenhaHash = "" }; u.SenhaHash = hasher.HashPassword(u, Guid.NewGuid().ToString()); return u; }

    // e-mail é a chave da conta: minúsculo, sem espaços
    public static string Normalizar(string email) => email.Trim().ToLowerInvariant();

    static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    static UsuarioOut Saida(Usuario u) => new(u.Id, u.Email, u.Nome);

    // JWT curto + refresh token novo (gravado só o hash)
    static async Task<TokensOut> Emitir(Usuario u, FarmDb db, JwtOpcoes o)
    {
        var agora = DateTimeOffset.UtcNow;
        var expira = agora.AddMinutes(o.MinutosAcesso);
        var jwt = new JwtSecurityToken(o.Emissor, o.Publico,
            [new Claim(JwtRegisteredClaimNames.Sub, u.Id), new Claim(JwtRegisteredClaimNames.Email, u.Email),
             new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))],
            notBefore: agora.UtcDateTime, expires: expira.UtcDateTime,
            signingCredentials: new SigningCredentials(o.ChaveAssinatura(), SecurityAlgorithms.HmacSha256));

        var refresh = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var rt = new RefreshToken { UsuarioId = u.Id, Hash = Hash(refresh), ExpiraEm = agora.AddDays(o.DiasRefresh) };
        db.RefreshTokens.Add(rt);
        await db.SaveChangesAsync();
        return new(new JwtSecurityTokenHandler().WriteToken(jwt), expira, refresh, rt.ExpiraEm, Saida(u));
    }

    public static void MapAuth(this WebApplication app)
    {
        var g = app.MapGroup("/auth").WithTags("Autenticação").RequireRateLimiting("auth");

        g.MapPost("/registrar", async Task<Results<Ok<TokensOut>, BadRequest<string>, Conflict<string>>> (RegistrarIn i, FarmDb db, JwtOpcoes o) =>
        {
            var email = Normalizar(i.Email ?? "");
            if (email.Length is < 3 or > 254 || !email.Contains('@') || email.Contains(' ')) return TypedResults.BadRequest("e-mail inválido");
            if ((i.Senha ?? "").Length < SenhaMin) return TypedResults.BadRequest($"senha precisa ter ao menos {SenhaMin} caracteres");
            if (await db.Usuarios.AnyAsync(u => u.Email == email)) return TypedResults.Conflict("e-mail já cadastrado");

            var u = new Usuario { Email = email, SenhaHash = "", Nome = string.IsNullOrWhiteSpace(i.Nome) ? null : i.Nome.Trim() };
            u.SenhaHash = hasher.HashPassword(u, i.Senha!);
            db.Usuarios.Add(u);
            await db.SaveChangesAsync();
            return TypedResults.Ok(await Emitir(u, db, o));
        }).WithSummary("Cria a conta e já devolve os tokens");

        g.MapPost("/login", async Task<Results<Ok<TokensOut>, UnauthorizedHttpResult>> (LoginIn i, FarmDb db, JwtOpcoes o) =>
        {
            var email = Normalizar(i.Email ?? "");
            var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Email == email);
            // mesma resposta (e mesmo tempo) para e-mail inexistente e senha errada: não revela quem tem conta
            var r = hasher.VerifyHashedPassword(u ?? Falso, u?.SenhaHash ?? Falso.SenhaHash, i.Senha ?? "");
            if (u is null || r == PasswordVerificationResult.Failed) return TypedResults.Unauthorized();
            if (r == PasswordVerificationResult.SuccessRehashNeeded) u.SenhaHash = hasher.HashPassword(u, i.Senha!);
            return TypedResults.Ok(await Emitir(u, db, o));
        }).WithSummary("E-mail e senha → JWT (Authorization: Bearer) + refresh token");

        g.MapPost("/renovar", async Task<Results<Ok<TokensOut>, UnauthorizedHttpResult>> (RefreshIn i, FarmDb db, JwtOpcoes o) =>
        {
            var hash = Hash(i.RefreshToken ?? "");
            var rt = await db.RefreshTokens.FirstOrDefaultAsync(r => r.Hash == hash);
            if (rt is null) return TypedResults.Unauthorized();
            var agora = DateTimeOffset.UtcNow;
            if (rt.RevogadoEm is not null)
            {
                // token já usado de novo: provável vazamento, derruba todas as sessões do usuário
                await db.RefreshTokens.Where(r => r.UsuarioId == rt.UsuarioId && r.RevogadoEm == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.RevogadoEm, agora));
                return TypedResults.Unauthorized();
            }
            if (rt.ExpiraEm <= agora) return TypedResults.Unauthorized();
            if (await db.Usuarios.FindAsync(rt.UsuarioId) is not { } u) return TypedResults.Unauthorized();

            rt.RevogadoEm = agora;  // rotação: cada refresh token vale uma vez
            return TypedResults.Ok(await Emitir(u, db, o));
        }).WithSummary("Troca o refresh token por um JWT novo (o refresh antigo deixa de valer)");

        g.MapPost("/sair", async (RefreshIn i, FarmDb db) =>
        {
            var hash = Hash(i.RefreshToken ?? "");
            await db.RefreshTokens.Where(r => r.Hash == hash && r.RevogadoEm == null)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.RevogadoEm, DateTimeOffset.UtcNow));
            return TypedResults.NoContent();
        }).WithSummary("Revoga o refresh token (o JWT atual vale até expirar)");

        g.MapGet("/eu", async Task<Results<Ok<UsuarioOut>, UnauthorizedHttpResult>> (HttpContext ctx, FarmDb db) =>
            await db.Usuarios.FindAsync(FazendaApi.Dono(ctx)) is { } u ? TypedResults.Ok(Saida(u)) : TypedResults.Unauthorized())
         .RequireAuthorization().WithSummary("Dados da conta logada");
    }
}
