# tests

`Farm.Core.Tests` (xUnit): testa só o `Farm.Core`, sem rede.

    dotnet test tests/Farm.Core.Tests

- `ClimaTests`: Ra e ET0 contra os exemplos 8 e 18 da FAO-56, e Hargreaves feito à mão.
- `CulturaTests`: fases e curva de Kc.
- `ModeloTests`: balde (nunca < 0, nunca > CAD), Ks, ISNA do ZARC, perda FAO-33, faixas de risco.

Fora do alcance: a janela por decêndio (com virada de ano) está em `Farm.Api/Program.cs`,
e a divisão de hectares está no `web/index.html`. Para testar, mover essa lógica para o `Farm.Core`.
