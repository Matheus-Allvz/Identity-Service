<!--
  CONJUNTO DE MODELOS DE ESPECIFICAÇÃO PARA LLM — v2.1 (Entregável 2)
  Copie a pasta template/ inteira para a pasta da especificação do projeto.
  Convenções: <<texto>> = placeholder obrigatório | <!-- --> = instrução para o analista (remover no congelamento).
  Guia de preenchimento: 03_guia_preenchimento_template.md (secção §G-nn = ficheiro nn).
-->

# <<Nome do Sistema>> — Índice da Especificação

**Leia primeiro.** Este índice é a porta de entrada de qualquer agente, humano ou LLM. Nenhum agente lê a especificação inteira: cada um recebe o **pacote de contexto** da sua unidade de trabalho, montado a partir das colunas abaixo.

## 1. Mapa de documentos

| Doc | Para quê | Leia quando | Dono | Estado | Atualizado em | Hash |
|---|---|---|---|---|---|---|
| [10 · Núcleo](10-nucleo.md) | Contexto, âmbito, glossário, stack, ADR, RNF, convenções, transversais, erros globais, instruções para a LLM | **Sempre** (entra em todos os pacotes) | <<ARQ>> | <<Rascunho \| Congelado \| Defasado>> | <<AAAA-MM-DD>> | <<ver spec.sha256>> |
| [20 · Sistema existente](20-sistema-existente.md) | Sistemas externos com contrato, comportamento medido, evidência e lacunas | O pacote inclui um `EXT-nn` | <<AR>> | <<>> | <<>> | <<>> |
| [30 · Dados](30-dados.md) | ERD, enums, dicionário físico, cache, seed, dados de referência | A UT lê ou escreve dados (só as tabelas listadas no pacote) | <<ARQ>> | <<>> | <<>> | <<>> |
| [40 · Contratos](40-contratos.md) | Convenções da API, interfaces de todos os EP/EV, matriz de autorização, validação em camadas, robustez | **Sempre** em implementação; secções indicadas no pacote | <<ARQ>> | <<>> | <<>> | <<>> |
| [50 · Módulos](50-modulos/) | Um ficheiro por módulo: endpoints em detalhe, RF, RN, EX, concorrência, CA | A UT é deste módulo | <<AR por módulo>> | <<>> | <<>> | <<>> |
| [60 · Plano de fases](60-plano-de-fases.md) | Fases de implementação, grafo, "Não tocar ainda", pacote de cada fase | Planear, construir ou validar uma fase | <<ARQ>> | <<>> | <<>> | <<>> |
| [70 · Ambiente de teste](70-ambiente-teste.md) | Contrato com a suite: arranque, dados, tokens, relógio, fakes, falhas, nomes de testes | Construir, validar ou auditar | <<AUD>> | <<>> | <<>> | <<>> |
| [80 · Orquestração](80-orquestracao.md) | Papéis, ciclo, score, retrabalho, briefs, formato de entrega, git, segurança | Orquestrar ou receber um brief | <<ORQ>> | <<>> | <<>> | <<>> |
| [90 · Registo](90-registo.md) | Grafo de UT, ciclos e scores, decisões D, perguntas Q, lacunas L, defeitos DEF, alterações CR, auditoria | Saber onde está; retomar numa sessão nova | <<ORQ>> | Vivo | <<>> | Não se aplica |

## 2. Mapa de módulos

| Módulo | Âmbito (uma frase) | RF | EP / EV | Tabelas que escreve | Depende de (interfaces) | Pacote estimado (KB) |
|---|---|---|---|---|---|---|
| [M-01](50-modulos/M-01-<<nome>>.md) | <<>> | <<RF-01..RF-04>> | <<EP-01, EP-02, EV-01>> | <<ENT-01, ENT-02>> | <<Nenhum \| M-02 (EP-05)>> | <<≤ 60>> |

## 3. Precedência (regra única)

Em caso de conflito entre documentos, vale, pela ordem:

1. **Regras de negócio** do módulo dono (`50`, secção RN)
2. **Critérios de aceitação** do módulo dono (`50`, secção CA)
3. **Contratos** (`40`, e o detalhe do endpoint no módulo dono)
4. **Dados** (`30`)
5. **Núcleo** (`10`)
6. **Plano de fases** (`60`) e restantes

Um conflito encontrado é um defeito: regista-se em `90` como `DEF-nn` ou `CR-nn`. **Não** se resolve por interpretação.

## 4. Convenções de identificadores

<<Copiar a tabela de identificadores do Entregável 1, §0.2, e acrescentar os prefixos específicos do projeto, se houver.>>

### 4.1 Faixas de identificadores para trabalho em paralelo

A UT de decomposição (UT-1-90) atribui a cada módulo e a cada UT uma faixa exclusiva de identificadores ANTES de qualquer unidade paralela começar. Nenhuma UT cria identificadores fora da sua faixa.

| Módulo | Faixa de RN novas | Faixa de PRS novos | Faixa de CA novos | Faixa de ERR novos | Faixa de IF novas |
|---|---|---|---|---|---|
| M-01 | <<RN-10 a RN-19>> | <<PRS-100 a PRS-109>> | <<CA-01.1 a CA-01.99>> | <<ERR-MOD1-001 a ERR-MOD1-099>> | <<IF-01 a IF-09>> |
| M-02 | <<RN-20 a RN-29>> | <<PRS-110 a PRS-119>> | <<CA-02.1 a CA-02.99>> | <<ERR-MOD2-001 a ERR-MOD2-099>> | <<IF-10 a IF-19>> |

| UT fora da Fase 3 | Faixa de PRS novos | Faixa de Q novas |
|---|---|---|
| UT-1-01, UT-1-90 (ARQ) | <<PRS-10 a PRS-29>> | <<Q-01 a Q-20>> |
| UT-2-01 (Dados) | <<PRS-30 a PRS-49>> | <<Q-21 a Q-30>> |
| UT-2-02 (Contratos) | <<PRS-50 a PRS-69>> | <<Q-31 a Q-40>> |
| UT-2-03 (Ambiente de teste) | <<PRS-70 a PRS-89>> | <<Q-41 a Q-50>> |
| UT-4 e UT-5 (Correções por CR-nn) | <<PRS-200 a PRS-299>> | <<Q-51 a Q-99>> |

## 5. Etiquetas de evidência

| Etiqueta | Significado |
|---|---|
| `[MEDIDO]` | Exercitado num ambiente real; só é válido se citar um script versionado em `evidencias/` e o respetivo ficheiro de resultado (pastas temporárias não contam) |
| `[CÓDIGO]` | Lido no código-fonte (`ficheiro:linha`) |
| `[DOC]` | Lido em documentação (indicar a fonte) |
| `[DECISÃO D-nn]` | Decidido pelo PO; não verificável no sistema existente |
