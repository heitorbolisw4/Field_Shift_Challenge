Projeto: NASA Space Apps 2026 — "Adapting Farms with NASA Data"
Prazo: 2 dias de hackathon.
Usuário: fazendeiro de café no Cerrado Mineiro (Indianópolis-MG).
Problema: decidir quando irrigar o café.

Stack: C# .NET Minimal API. Front mínimo depois.
Dados: NASA POWER API (community=AG, diário, por lat/long).

Contrato:
  entrada: lat, long, data
  saída: "irrigar" ou "esperar" + motivo em 1 frase

Regra v1: balanço hídrico de 7 dias.
  deficit = soma(ETc) - soma(chuva)
  ETc = ET0 * Kc
  ET0 por Hargreaves (T2M_MAX, T2M_MIN)

Regras de trabalho:
- Um passo por vez. Não gerar o app inteiro.
- Código mínimo e explicado.
- Eu testo cada parte antes da próxima.