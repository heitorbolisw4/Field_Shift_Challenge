# tests

Reservado para `Farm.Core.Tests` (xUnit).

O alvo é o `Farm.Core`: Ra, ET0, curva de Kc e o balanço hídrico.
Tudo ali é cálculo puro, então dá para testar com valores das tabelas da FAO-56, sem rede.

Para criar:
    dotnet new xunit -o tests/Farm.Core.Tests
    dotnet add tests/Farm.Core.Tests reference src/Farm.Core
    dotnet sln add tests/Farm.Core.Tests
