# Passagem de orquestração — Identity-Service (2026-10-19)

> Para: o novo Orquestrador (ORQ), terminal Antigravity. De: Claude Code (ORQ anterior).
> Lê este ficheiro inteiro antes de qualquer ação. A seguir lê `Etapa2/spec-v2/90-registo.md` §1 e §3–§4 e `Etapa2/spec-v2/80-orquestracao.md`.

## 1. Objetivo
Atividade académica com 4 entregáveis: **E1 processo**, **E2 template**, **E3 guia** (prontos, v2.1, em `Etapa1/`) e **E4 relatório do experimento** (depois). Agora estamos a **aplicar o processo v2 à Ideia Base** (`Docs/Ideia Base.md`) para produzir a especificação modular do Identity-Service em `Etapa2/spec-v2/`, até ao congelamento (Gate 4). Depois vem a Fase 5 (gerar código), fora deste passo.

Lê o processo em `Etapa1/01_processo_especificacao_requisitos_llm.md` (§0, §1, §4, §5, §8) e o guia `Etapa1/03_guia_preenchimento_template.md` (§2, §3, §6). São a lei.

## 2. O teu papel (ORQ) — regras que não se quebram
1. **Não escreves especificação.** Só o `90-registo.md` (estado, D-nn, Q-nn) e o `80-orquestracao.md`. Tudo o resto é escrito por agentes donos de ficheiros (P9: um dono por ficheiro).
2. **Não confias em relatos.** Depois de cada entrega corres tu: `python Etapa2/spec-v2/evidencias/medir-pacotes.py` (exit 0), `bash Etapa2/spec-v2/evidencias/medir-postgres.sh` (exit 0), `bash Etapa2/spec-v2/evidencias/medir-env70.sh` (exit 0, ~2 min) e o lint (E1 atividade 4.1) nos ficheiros alterados.
3. **Decisões de negócio e aprovações (D-nn, PRS, CR) são do PO (Matheus), por mensagem literal.** Regista-as no `90` §3 com data e a frase literal. Nunca registes uma aprovação que não aconteceu (P12). Pergunta ao PO com recomendação e espera.
4. **Pedidos de alteração (CR-nn):** o ARQ propõe, o PO aprova. Não decides divisões de módulos nem convenções.
5. **Validação independente:** quem valida é sempre um agente NOVO, que não viu os relatos dos autores. Tu não validas o conteúdo — só corres os scripts.
6. **Limite de ciclos:** 3 por UT; acima disso é dead letter e decide o PO (já houve D-18 e D-20).
7. **Atualiza o `90` em cada transição** (tabela §1, colunas Estado e Ciclo).

## 3. A equipa (Maestri)
| Nome | Agente | Uso |
|---|---|---|
| Sabiá | Antigravity, Gemini 3.8 Flash medium | Dona de `30`, `20`, `evidencias/medir-postgres.sh`, `matriz-isolamento.sql`, `pg-ddl.sql`, `pg-rls.sql` |
| Ipê | Antigravity, Gemini 3.8 Flash medium | Dono de `00`, `evidencias/medir-pacotes.py`, `pacotes/` |
| Jatobá | Antigravity, Gemini 3.8 Flash medium | Dono de `evidencias/env70/`, `medir-env70.sh` |
| Antigravity | Antigravity, Gemini 3.8 Flash medium | Livre (fez o E1/E2/E3 v2.1) |
| Claude Code | Claude Opus 5.5 (ORQ anterior) | **Só** para a atividade 4.3 (interrogatório com o modelo alvo `claude-opus-5-5`, D-12) e para pedidos pontuais. Tem limite de uso; não lhe delegues trabalho de rotina |

`10`, `40`, `70` e as linhas do `90` §1/§2.2/§6 foram escritos por um subagente Claude (ARQ "UT-2-91"), que já não está disponível. **Novo dono desses ficheiros:** atribui-os a "Antigravity" (livre) quando for preciso alterá-los.

### Como falar com os agentes (Windows + Git Bash)
- Antes de qualquer comando maestri: `export MSYS_NO_PATHCONV=1` (senão o Git Bash transforma "/model" em caminhos).
- `maestri list` para ver nomes; `maestri check "Nome"` para ver o ecrã; **nunca** envies uma tarefa a um agente que esteja a gerar (o texto cai no input e perde-se). Espera que o ecrã mostre o prompt vazio.
- Tarefas longas: escreve o brief num ficheiro e envia com `maestri ask "Nome" "$(cat ficheiro)"`; termina SEMPRE o brief com: `Ao terminar: maestri ask "<o teu nome>" "<entrega no formato X>"`.
- Vários ao mesmo tempo: `maestri ask --batch '{"A":"...","B":"..."}'`.
- Se o terminal mostrar um ecrã de quotas ou menu, fecha com `maestri ask "Nome" --raw "\e"`.

## 4. Estado atual (2026-10-19)
- **Gate 1:** aceite (validador do ciclo 4).
- **Gate 2:** ciclo 5 (D-20) entregue por todos os autores; corrida final do ORQ verde: pacotes exit 0 (15/15 OK, todos ≤ 59,7 KiB), matriz 33 OK + medições 22 OK (exit 0), env70 11/11, lint 0 nos 8 ficheiros, 15 pacotes regenerados em `pacotes/`.
- **A validação do ciclo 5 ficou por fazer** (o validador Claude parou por limite de uso). **Próximo passo n.º 1:** lançar um validador NOVO (um subagente teu ou um terminal novo, nunca um autor) com o brief da secção 6.
- **Achados a confirmar:** `Etapa2/validacoes/val-gate2-ciclo4.md` (A-100..A-118). Histórico: `val-gate2-ciclo3.md`.
- **Q abertas para o PO:** Q-16 (aprovar PRS-80..84 e 86; recomendação: sim) e Q-17 (repetir o estado reemite o evento EV-03/EV-02; recomendação: sim). Bloqueiam só Gate 4 / UT-3-10 e UT-3-12.
- **Ameaça à validade a registar:** o validador do ciclo 5 passa a ser Gemini em vez de Claude (mudança por limite de uso, autorizada pelo PO ao delegar a orquestração). Regista em `90` como nota da UT-2-9x e menciona no relatório.

## 5. Próximos passos, por ordem
1. **Validar o Gate 2 (ciclo 5)** com validador novo (secção 6). Se **aceite**: passo 2. Se **reprovado**: é o 6.º ciclo da fundação → dead letter → leva os achados ao PO com opções e recomendação (não corrijas sem D-nn).
2. **Congelar a fundação:** `sha256sum Etapa2/spec-v2/{00-indice,10-nucleo,20-sistema-existente,30-dados,40-contratos,70-ambiente-teste}.md > Etapa2/spec-v2/spec-fundacao.sha256` e regista em `90` §9 (Congelamentos). A partir daí, alterações só por CR-nn aprovado.
3. **Onda 3 (UT-3-00 a UT-3-14):** um agente por módulo, brief do `80` §4.1. Cada um recebe SÓ `pacotes/pacote-M-nn.md` + guia §2/§G-50 + modelo `Etapa1/template/50-modulos/M-nn-modelo.md`. Teto do ficheiro do módulo: coluna "ficheiro estimado" de `python evidencias/medir-pacotes.py`. Faixas de IDs: `00` §4. Distribuição sugerida (até 4 em simultâneo; repete quando terminarem):
   - Lote 1: Sabiá M-11, Ipê M-04, Jatobá M-07, Antigravity M-09
   - Lote 2: Sabiá M-12, Ipê M-05, Jatobá M-14, Antigravity M-13
   - Lote 3: M-00, M-01, M-02, M-03 (os mais críticos)
   - Lote 4: M-06, M-08, M-10
   Um agente NÃO escreve no módulo de outro. Ao receber cada módulo: verifica lint, tamanho (≤ teto) e corre o script de pacotes.
4. **Onda 4 (UT-4):** por módulo, revisor cego NOVO (brief `80` §4.2) — se o autor foi Gemini, o revisor deve ser Claude quando disponível (pede ao "Claude Code"); a atividade 4.3 (interrogatório) usa sempre o modelo alvo Claude. Depois: consistência entre módulos (UT-4-90) e ensaio (UT-4-91). Defeitos voltam ao autor do módulo; re-revisão cega até IA residual = 0. Regista cada defeito no `90` §9 com ID único.
5. **Gate 4:** DoR do guia §6.1 completa, todas as Q respondidas, todos os PRS aprovados; hash de todos os ficheiros em `spec.sha256`. Informa o PO.

## 6. Brief do validador do Gate 2 (ciclo 5) — usa tal como está
```text
Papel: Validador independente (VAL) do Gate 2 do processo v2, ciclo 5 (D-20). O Gate 1 já foi aceite no ciclo 4; confirma só que nada o reabriu. És um agente novo: não leste relatos dos autores nem validações anteriores, EXCETO Etapa2/validacoes/val-gate2-ciclo4.md (para confirmar resolução). NÃO alteres ficheiros da especificação; mutações só em cópias numa pasta temporária tua.
Base: C:/Users/ACT/Documents/Personal/Identity-Service
Critérios: Etapa1/01_processo_especificacao_requisitos_llm.md v2.1 (§0, §1.2–§1.3, Gate 2 em §3, atividade 2.11) e Etapa1/03_guia_preenchimento_template.md v2.1 (§2, §3, §G-00..§G-40, §G-70, §G-80, §G-90, DoR §6.1 A e B).
Ficheiros: Etapa2/spec-v2/00,10,20,30,40,70,80,90 + evidencias/ + pacotes/.
Verifica com ficheiro:linha: (1) lint 0; (2) corre e lê os 3 scripts (medir-pacotes.py, medir-postgres.sh, medir-env70.sh) e faz pelo menos uma mutação NOVA em cada numa cópia; (3) D-21: para M-01, M-02, M-03, M-05, M-10, M-14 lê SÓ pacotes/pacote-M-nn.md e compara com a v1.0 (Etapa2/spec_identity_service.md) e com 30/40 completos — falta alguma regra necessária? CR-16 regra 1 cumprida?; (4) A-100..A-118 resolvidos?; (5) D-22, D-19+PRS-84, IF-11.3 passo (5), classificação de exceções; (6) briefs do 80 §4.1/§4.2 suficientes; (7) registo coerente.
Entrega: veredito Gate 2 (Aceite/Reprovado, separando defeitos da spec de pendências do PO); achados a partir de A-130 com severidade, ficheiro:linha, evidência, correção, ⚠️ se bloqueia; saída dos scripts e mutações; tabela de estado A-100..A-118; o que está bem.
Grava o relatório em Etapa2/validacoes/val-gate2-ciclo5.md.
```

## 7. Armadilhas conhecidas
- Limites de uso (Claude e Gemini) param agentes a meio: quando voltarem, retoma o mesmo agente ("continua de onde paraste"), não lances outro.
- Agentes às vezes dizem "feito" sem ter feito: confirma sempre em disco (datas dos ficheiros, scripts).
- Um agente que edita fora dos seus ficheiros quebra a regra P9: devolve a tarefa.
- Citações cruzadas em texto partilhado: o script de fecho apanha-as; resolve escrevendo o conteúdo na frase (CR-16), nunca alargando o orçamento de 60 KiB.
