Projeto: NASA Space Apps 2026 — "Field Shift: Adapting Farms with NASA Data"
Prazo: 2 dias de hackathon.
Usuário: produtor de soja no Cerrado Mineiro (Indianópolis-MG).
Problema: depois de colher a soja, qual cultura plantar na safrinha?
  Opções v1: milho, sorgo, braquiária (cobertura).

Stack: C# .NET Minimal API. Front mínimo depois.
Dados v1: NASA POWER (community=AG, diário). v2: GLDAS/SMAP (umidade do solo inicial).

Contrato:
  entrada: lat, long, colheitaInicio, colheitaFim
    (a colheita é uma janela de dias/semanas, não uma data)
  saída: calendário de plantio dentro da janela, ex.:
    "até 20/02: milho | 21/02 a 05/03: sorgo | depois: braquiária"
    + motivo em 1 frase por trecho

Regra v1 (a validar):
  Para cada dia D da janela de colheita e cada cultura:
    janela de cultivo = D até D + ciclo.
  Usar climatologia: mesma janela nos últimos 10 anos do POWER.
  deficit = soma(ETc) - soma(chuva) na janela, média dos anos.
  ETc = ET0 * Kc; ET0 por Hargreaves (T2M_MAX, T2M_MIN, Ra).
  Em cada D, recomendar a cultura mais exigente cujo deficit cabe no limite.
  Agrupar dias seguidos com a mesma cultura -> trechos do calendário.
  Lógica real: colheu tarde -> chuva acaba antes -> milho vira risco -> sorgo/braquiária.

A definir (pesquisar):
  - ciclo e Kc de cada cultura (FAO-56 tabela 12 e 11)
  - limite de deficit aceitável por cultura
  - ZARC (MAPA) da safrinha em Indianópolis, para validar a regra

Regras de trabalho:
- Um passo por vez. Não gerar o app inteiro.
- Código mínimo e explicado.
- Eu testo cada parte antes da próxima.
