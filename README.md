# Field_Shift_Challenge

NASA Space Apps 2026: "Field Shift: Adapting Farms with NASA Data".
Depois da soja, o que plantar na safrinha? Calendário de risco por decêndio com 10 anos de NASA POWER e critério do ZARC.

## Estrutura

    src/Farm.Core   domínio: Cultura, Clima (ET0), Modelo (balanço hídrico, ISNA). Sem rede.
    src/Farm.Api    HTTP: endpoints + cliente do NASA POWER. Serve o front.
    web/            front React (CDN, sem build)
    tests/          reservado para Farm.Core.Tests
    docs/           specs (instrucoes*.md)

## Rodar

    dotnet run --project src/Farm.Api
    abrir http://localhost:5196
