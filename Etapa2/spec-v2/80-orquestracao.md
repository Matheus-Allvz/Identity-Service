# 80 · Orquestração — Identity-Service

> **Conjunto:** [00 Índice](00-indice.md) · **Dono:** ORQ · **Estado:** Vivo · **Atualizado em:** 2026-10-19 (ORQ = Carcará, D-24)
> Os briefs abaixo são modelos. O orquestrador só preenche os campos `{ }`. **Não** cola histórico de conversa, relatos de outros agentes, segredos nem dados pessoais.

## 1. Papéis

| Sigla | Papel | Quem, neste projeto | Pode | Não pode |
|---|---|---|---|---|
| PO | Product Owner (humano) | Matheus | Decidir (`D-nn`), responder (`Q-nn`), aprovar PRS e CR, aceitar fases, fazer merge, autorizar ações fora do repositório | — |
| ORQ | Orquestrador | Carcará (terminal Antigravity; D-24) | Manter o grafo e o `90`, montar pacotes (`medir-pacotes.py --extrair`), lançar agentes, verificar entregas com os scripts, atualizar o `80` e o `90` | Escrever especificação ou código; decidir CR ou divisões de módulos; aceitar uma UT sem validador |
| AR / ARQ | Analista e arquiteto | Subagentes Claude (Opus 5.5) e terminais Antigravity "Sabiá", "Ipê", "Jatobá" e "Antigravity" (Gemini 3.8 Flash, esforço médio) | Escrever os ficheiros de que a UT é dona; propor CR-nn | Escrever fora dos seus ficheiros; usar IDs fora da sua faixa (`00` §4); aprovar CR |
| RI | Revisor independente | Subagente Claude novo por revisão, com o modelo alvo | Ler o pacote da UT revista e apontar defeitos | Ler o registo de defeitos ou o relato do autor |
| VAL | Validador de gate | Subagente Claude novo por ciclo | Correr scripts, fazer mutações em cópias, emitir veredito | Alterar a especificação |
| AUD | Auditor | Terminal Antigravity "Jatobá" (ambiente de teste) e subagente Claude (suite da Fase 5) | Escrever o `70`, o `env70` e a suite de aceitação | Ser autor do módulo que audita |
| CON / REV | Construtor e revisor de causa raiz | Definidos na UT-5-00 | — | — |

**Independência:** os módulos escritos por um terminal Gemini são revistos (UT-4) por um subagente Claude novo; o interrogatório da atividade 4.3 é sempre feito com o modelo alvo `claude-opus-5-5` (D-12).

## 2. Protocolo

### 2.1 Fases 3 e 4 (especificação)
| Parâmetro | Valor |
|---|---|
| Orçamento do pacote | 60 KiB (núcleo, extras, `30`, `40`, `70` comum, `20`, linhas do `90` e brief), medido por `evidencias/medir-pacotes.py`; margem pretendida ≥ 0,3 KiB |
| Montagem do pacote | `python evidencias/medir-pacotes.py --extrair M-nn pacotes/` — o agente recebe o ficheiro `pacotes/pacote-M-nn.md` e mais nada da especificação |
| Ficheiros adicionais permitidos ao AR | `Etapa1/03_guia_preenchimento_template.md` §2 e §G-50; `Etapa1/template/50-modulos/M-nn-modelo.md` |
| Proibido ao AR | A spec v1.0 (`Etapa2/spec_identity_service.md`), outros módulos, o `90` completo (D-21) |
| Agentes simultâneos | Até 4 terminais Antigravity e até 4 subagentes Claude (limite de uso da conta Claude) |
| Limite de ciclos por UT | 3 (desvios só por decisão do PO, como D-18 e D-20) |

### 2.2 Fase 5 (implementação)
Pré-registado na UT-5-00, antes da primeira fase: modelo e versão (`claude-opus-5-5`, D-12), ferramenta, `max_tokens`, comando de G1 (`70` §2), número de execuções, teste estatístico e condições de comparação (E1 §6.7 e §7.6).

## 3. Regras
1. O ORQ não escreve especificação nem código.
2. O ORQ não confia em relatos: corre os scripts (`medir-pacotes.py`, `medir-postgres.sh`, `medir-env70.sh`) e o lint antes de pedir validação.
3. Uma UT só começa com as dependências aceites; uma fase só começa depois do merge das fases de que depende.
4. O `90` é atualizado em cada transição de UT.
5. Ações fora do repositório exigem uma mensagem literal do PO.
6. **Paragem obrigatória:** perante uma situação que a especificação não prevê e que não admite hipótese segura, o agente para e pergunta ao PO: o que fazia · o que observou · o que a especificação previa · opções · recomendação.
7. Pedidos de alteração: o ARQ propõe `CR-nn`; só o PO aprova. Nada é aplicado a ficheiros congelados sem aprovação.

## 4. Briefs

### 4.1 Analista de módulo (UT-3-nn)
```text
Papel: Analista de Requisitos do módulo {M-nn} ({nome}), UT-3-{nn}.
Lê SÓ: Etapa2/spec-v2/pacotes/pacote-{M-nn}.md (o teu pacote de contexto; o que não está lá não existe),
Etapa1/03_guia_preenchimento_template.md §2 e §G-50, e o modelo Etapa1/template/50-modulos/M-nn-modelo.md.
NÃO lês a spec v1.0, outros módulos nem o 90 completo.
Escreves SÓ: Etapa2/spec-v2/50-modulos/{M-nn}-{nome}.md.
IDs: só da tua faixa (cabeçalho do pacote / 00 §4): RN {faixa RN}, PRS {faixa PRS}, Q {faixa Q}; CA-{rf}.m dos teus RF.
Tarefa: especificar o módulo segundo a Fase 3 do processo (E1 §4): endpoints em detalhe com ordem de avaliação e todas as respostas,
RF, RN em SE/ENTÃO/SENÃO, EX, tabela de concorrência/idempotência/retoma, CA em Gherkin com borda em cada CA e cobertura mínima,
rastreabilidade e PRS. Cumpre as linhas "Entradas para a UT-3-{nn}" e os PRS de módulo que estão no fim do teu pacote.
Teto do ficheiro do módulo: {teto} KiB.
Se precisares de algo partilhado que não está no pacote: NÃO inventes nem alteres; regista em "Pedidos de alteração".
Ambiguidade: regista em "Dúvidas" com a hipótese mais simples marcada.
Lint 0 (termos proibidos do guia §2.3; nenhum placeholder por preencher).
Entrega: (1) ficheiro e tamanho; (2) totais RF/RN/EP/EV/CA/@critico/Σw; (3) pedidos de alteração; (4) dúvidas com hipótese; (5) PRS e Q novos.
```

### 4.2 Revisor cego (UT-4-nn)
```text
Papel: Revisor independente do módulo {M-nn}. Não tiveste contacto com o autor nem com revisões anteriores.
Recebes: Etapa2/spec-v2/pacotes/pacote-{M-nn}.md e Etapa2/spec-v2/50-modulos/{M-nn}-{nome}.md. Nada mais.
NÃO geres código. Lista, numeradas e com o ID afetado: (a) perguntas; (b) suposições; (c) contradições;
(d) comportamentos com mais de uma implementação razoável; (e) informação que exigiria outro documento.
Verifica também: oráculos dos CA (cada literal deriva do Dado, do seed e das RN), dados de partida coerentes com FK,
cobertura mínima de CA (E1 §4 atividade 3.6), uma borda por CA, `@critico` só nos tipos permitidos.
Entrega: um defeito por linha, com severidade (Alta/Média/Baixa) e ficheiro:linha. "Nenhum" se vazio.
```

### 4.3 Construtor (F-nn) — usado na Fase 5
```text
Papel: Construtor da fase {F-nn}, no worktree {caminho}, branch {branch}.
Lê só: {pacote da fase}. Ciclo 1: NÃO recebes a suite de aceitação; escreves os teus testes a partir dos CA, um por CA, com o ID no nome.
Escreves só: {pastas}. Não tocar ainda: {lista}.
Ambiguidade: não inventes; regista em "Dúvidas" e segue com a hipótese mais simples, marcada.
Commits com os IDs (RF-nn, CA-nn.m). Não fazes merge.
Antes de entregar, corre a varredura de dados pessoais (`70` §5); saída 0 = limpo.
Entrega: commits (SHA + mensagem) · comandos com resultado · tabela CA → teste → resultado · não feito ·
decisões fora dos docs · dúvidas (com hipótese) · débitos.
```

### 4.4 Validador de fase (F-nn, ciclo {c}) — usado na Fase 5
```text
Papel: Validador da fase {F-nn}, ciclo {c}. Agente novo; não leste o relato do construtor.
Recebes: {pacote da fase}, o diff de {branch} contra {base}, a suite da fase e a das fases anteriores.
Verifica G1 a G6 (E1 §7.2). PODES aplicar mutações aos testes da fase.
Entrega: tabela G1–G6 com evidência · C_w da fase · achados A-nn (gravidade, ⚠️ se crítico, como reproduzir) · veredito.
```

### 4.5 Retrabalho (F-nn, ciclo {c}) — usado na Fase 5
```text
Construtor: corrige {A-nn: descrição + como reproduzir}. A partir deste ciclo recebes a suite de aceitação: {caminho}.
Cada correção traz um teste que reproduz o achado.
Revisor de causa raiz: recebes só {A-nn}. Identifica a classe do defeito e propõe (a) conserto pontual,
(b) a mudança que torna a classe impossível, (c) débitos adjacentes.
```

## 5. Modelo de PR por fase
```markdown
## F-{nn} · {nome}
**Demo:** {}
**Avaliação (ciclo {c}, validador {id}):** G1 {} · G2 {} · G3 C_w = {} % · G4 {} · G5 {} · G6 {}
**Construído:** {} · **Não tocado (verificado no diff):** {}
**Decisões fora dos docs / dúvidas (hipóteses marcadas):** {}
**Débitos:** {}
**Varredura de dados pessoais:** {saída}
**Como reproduzir:** {comandos}
**Registo:** linha {UT} atualizada em 90-registo.md
```
