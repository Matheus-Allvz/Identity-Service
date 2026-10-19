# 80 · Orquestração — <<Nome do Sistema>>

> **Conjunto:** [00 Índice](00-indice.md) · **Dono:** <<ORQ>> · **Estado:** <<>> · **Atualizado em:** <<>>
> Os briefs abaixo são modelos. O orquestrador só preenche os campos `{ }`. **Não** cola histórico de conversa, relatos de outros agentes, segredos nem dados pessoais.

## 1. Papéis
<<Copiar a tabela de papéis do Entregável 1, §1.4, e acrescentar as pessoas e os agentes concretos.>>

## 2. Protocolo pré-registado
| Parâmetro | Valor |
|---|---|
| Modelo e versão | <<>> |
| Ferramenta | <<agente com acesso a ficheiros e ao compilador>> |
| Agentes simultâneos (máximo) | <<>> |
| Orçamento do pacote de contexto | <<60 KB>> |
| Limite de ciclos por fase | 3 |
| Repositório, branches e worktrees | <<`feat/F-nn-<slug>`, um worktree por fase>> |
| Conta git e verificação prévia | <<comando de verificação>> |
| Quem faz merge | PO (humano) |

## 3. Regras
1. O ORQ não escreve especificação nem código.
2. O ORQ **NÃO DEVE** propor nem decidir alterações na especificação nem divisões de módulos. As alterações e divisões são propostas pelo ARQ no âmbito de uma UT técnica e aprovadas pelo PO. Todos os pedidos de alteração usam obrigatoriamente o formato `CR-nn` (nunca outras letras).
3. O ORQ não confia em relatos: corre build e testes e recalcula o score.
4. Uma fase só começa depois do merge das fases de que depende.
5. O registo (`90`) é atualizado em cada transição de UT. Um PR sem a linha correspondente no registo não é aceite.
6. Ações em produção ou fora do repositório exigem uma mensagem literal do PO.
7. **Paragem obrigatória:** se um agente encontrar uma situação que a especificação não prevê e que não pode seguir com uma hipótese segura (por exemplo, perda de dados ou escrita em produção), para e pergunta ao PO no formato: o que fazia · o que observou · o que a especificação previa · opções · recomendação.

## 4. Briefs

### 4.1 Analista de módulo (UT-3-nn)
```text
Papel: Analista de Requisitos do módulo {M-nn}.
Lê só: {lista de ficheiros e secções do pacote}.
Escreve só: 50-modulos/{M-nn}.md.
Não toca: os outros módulos e os ficheiros congelados (10, 30, 40, 70).
Tarefa: preencher o módulo segundo o guia (§G-50) e a Fase 3 do processo.
Se precisar de alterar algo partilhado: NÃO altere; descreva o pedido em "Pedidos de alteração".
Entrega: (1) ficheiro escrito; (2) totais RF/RN/EP/EV/CA/@critico/Σw; (3) pedidos de alteração; (4) dúvidas com a hipótese seguida; (5) PRS novos (pendentes).
```

### 4.2 Revisor cego (UT-4-nn)
```text
Papel: Revisor independente. Não teve contacto com o autor nem com revisões anteriores.
Recebe exatamente o pacote que o construtor da fase vai receber: {pacote}.
NÃO gere código. Liste, numeradas e com o ID afetado: (a) perguntas; (b) suposições; (c) contradições;
(d) comportamentos com mais de uma implementação razoável; (e) informação que exigiria outro documento.
Verifique também: oráculos dos CA (cada valor literal deriva do Dado e das RN), FK dos dados de partida, cobertura mínima de CA.
Entrega: lista de defeitos, um por linha, com severidade (Alta/Média/Baixa). "Nenhum" se vazio.
```

### 4.3 Construtor (F-nn)
```text
Papel: Construtor da fase {F-nn}, no worktree {caminho}, branch {branch}.
Lê só: {pacote da fase}. NÃO recebe a suite de aceitação neste ciclo: escreva os seus próprios testes a partir dos CA do pacote, um por CA, com o ID no nome.
Escreve só: {pastas}. Não tocar ainda: {lista}.
Se algo estiver ambíguo: NÃO invente; registe em "Dúvidas" e siga com a hipótese mais simples, marcada.
Commits com os IDs (RF-nn, CA-nn.m). Não faça merge.
Entrega: commits (SHA + mensagem) · comandos executados com resultado · tabela CA → teste → resultado ·
não feito · decisões fora dos docs · dúvidas (com hipótese) · débitos.
```

### 4.4 Validador (F-nn, ciclo {c})
```text
Papel: Validador da fase {F-nn}, ciclo {c}. É um agente novo e não leu o relato do construtor.
Recebe: {pacote da fase}, o diff da branch {branch} contra {base}, a suite da fase e a das fases anteriores.
Verifique: G1 build/arranque/migração · G2 todos os @critico · G3 C_w da fase · G4 diff contra "Não tocar ainda" ·
G5 regressão das fases aceites · G6 checklist de robustez (40 §3) nos endpoints da fase.
PODE aplicar mutações aos testes da fase para verificar que detetam falhas.
Entrega: tabela G1–G6 (passou/falhou + evidência) · C_w da fase · achados A-nn (ID, gravidade, ⚠️ se crítico, como reproduzir) · veredito.
```

### 4.5 Retrabalho (F-nn, ciclo {c})
```text
Para o Construtor: corrija os achados centrais {A-nn: descrição + como reproduzir}. A partir deste ciclo recebe também a suite de aceitação da fase: {caminho}. Cada correção traz um teste que reproduz o achado.
Para o Revisor de causa raiz: recebe só {A-nn}. Identifique a classe do defeito e proponha (a) o conserto pontual,
(b) a mudança que torna a classe impossível (ponto único de verificação, tipo, ou teste de varredura), (c) débitos adjacentes.
```

## 5. Modelo de PR por fase
```markdown
## F-{nn} · {nome}
**Demo:** {}
**Avaliação (ciclo {c}, validador {id}):** G1 {} · G2 {} · G3 C_w = {} % · G4 {} · G5 {} · G6 {}
**Construído:** {} · **Não tocado (verificado no diff):** {}
**Decisões fora dos docs / dúvidas (hipóteses marcadas):** {}
**Débitos:** {}
**Como reproduzir:** {comandos}
**Registo:** linha {UT} atualizada em 90-registo.md
```
