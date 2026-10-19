<!-- Copie este ficheiro para M-<nn>-<nome>.md, um por módulo. Cada módulo é escrito por uma UT própria, em paralelo com os outros. -->

# M-<<nn>> · <<Nome do módulo>>

> **Conjunto:** [00 Índice](../00-indice.md) · **Dono:** <<AR>> · **Estado:** <<>> · **Atualizado em:** <<>>
> **Pacote de contexto deste módulo:** este ficheiro + `10` + `30` (só <<ENT-nn, ENT-nn>>) + `40` (§1–§6 e interfaces <<EP-nn>>) + `70` + `20` (só <<EXT-nn>>). Tamanho total: <<KB>> (orçamento: <<60 KB>>).

## 1. Âmbito do módulo
| Dentro | Fora (outro módulo ou fora do sistema) |
|---|---|
| <<>> | <<>> |

**Escreve:** <<ENT-nn>> · **Lê:** <<ENT-nn>> · **Usa interfaces de:** <<M-nn (EP-nn) \| Nenhum>>

## 2. Endpoints e eventos (detalhe)

### EP-<<nn>> `<<MÉTODO>> <<path>>`
| Campo | Valor |
|---|---|
| RF | <<RF-nn>> |
| Autenticação / Permissão | <<igual à matriz de `40` §5>> |
| Idempotência | <<Sim (mecanismo) \| Não>> |
| RN aplicáveis | <<>> |

**Request**
| Campo | Local | Tipo | Obrigatório | Validação (formato, mín/máx, enum) |
|---|---|---|---|---|
| `<<>>` | <<body \| path \| query \| header>> | <<>> | <<>> | <<>> |

```json
<<exemplo válido, coerente com a tabela>>
```

**Ordem de avaliação** (a primeira que falha determina a resposta)
| # | Verificação | Camada (`40` §2) | Se falhar |
|---|---|---|---|
| 1 | <<>> | <<Formato>> | <<400 ERR-...>> |

**Respostas** (incluindo as do middleware)
| Status | Condição | Código |
|---|---|---|
| <<200>> | <<>> | – |

**Corpo de sucesso**
| Campo | Tipo | Sempre presente | Valor / regra de cálculo | Ordenação |
|---|---|---|---|---|
| `<<>>` | <<>> | <<>> | <<>> | <<campo ASC \| Não se aplica>> |

```json
<<exemplo, coerente com a tabela>>
```

**Efeitos colaterais (por ordem):** 1. <<>> 2. <<>>
**Transacionalidade:** <<o que é atómico; o que acontece em falha parcial>>

### EV-<<nn>> `<<tópico>>` (se o módulo publica ou consome)
| Campo | Tipo | Obrigatório | Valor |
|---|---|---|---|
| `<<>>` | <<>> | <<>> | <<>> |

## 3. Requisitos funcionais

### RF-<<nn>> — <<verbo + objeto>>
| Campo | Valor |
|---|---|
| Prioridade | <<MUST \| SHOULD \| COULD>> |
| Ator / Gatilho | <<>> |
| Pré-condições | <<>> |
| RN | <<>> |

**Fluxo principal:** 1. <<>> 2. <<>>

**Fluxos de exceção**
| ID | Passo | Condição | Resposta | Efeitos / estado final |
|---|---|---|---|---|
| EX-<<nn>>.1 | <<>> | <<>> | <<>> | <<o que NÃO é persistido>> |

**Concorrência, idempotência e retomada** (obrigatório se o RF escreve)
| Situação | Comportamento obrigatório | CA |
|---|---|---|
| Dois pedidos iguais ao mesmo tempo | <<>> | <<CA-nn.m>> |
| Processo morto a meio | <<>> | <<>> |
| Resposta perdida de um sistema externo | <<>> | <<>> |
| Registo igual criado por fora | <<>> | <<>> |

**Pós-condições:** <<>>

## 4. Regras de negócio
| ID | Tipo | Regra (SE / ENTÃO / SENÃO) | Parâmetros | Erro | RF | Evidência |
|---|---|---|---|---|---|---|
| RN-<<nn>> | <<Validação \| Autorização \| Cálculo \| Temporal \| Integridade>> | **SE** <<>> **ENTÃO** <<>> **SENÃO** <<>> | <<nome = valor unidade>> | <<ERR-...>> | <<>> | <<[MEDIDO] \| [DOC] \| D-nn \| PRS-nn>> |

## 5. Critérios de aceitação

```gherkin
# language: pt
Funcionalidade: RF-<<nn>> — <<título>>

  Contexto:
    Dado <<dados de partida concretos, que respeitam FK e restrições>>

  @CA-<<nn>>.1 @RF-<<nn>> @MUST
  Cenário: <<caminho feliz>>
    Quando <<método, path e corpo>>
    Então o status da resposta é <<200>>
    E <<campo>> é <<valor literal derivado do Dado e das RN>>

  @CA-<<nn>>.2 @RF-<<nn>> @RN-<<nn>> @MUST @critico
  Esquema do Cenário: <<violação da RN, com bordas>>
    Quando <<>> com <valor>
    Então o status da resposta é <<4xx>>
    E o campo "code" é "<<ERR-...>>"
    E <<nada foi persistido>>

    Exemplos:
      | valor |
      | <<limite inferior - 1>> |
      | <<limite superior + 1>> |
```

## 6. Rastreabilidade do módulo
| RF | Prioridade | RN | EP / EV | EX | CA | Fase |
|---|---|---|---|---|---|---|
| RF-<<nn>> | <<>> | <<>> | <<>> | <<>> | <<>> | <<F-nn>> |

**Totais:** RF = <<>> · RN = <<>> · EP = <<>> · EV = <<>> · CA = <<>> (`@critico` = <<>>) · Σw = <<>>

## 7. Pressupostos do módulo
| ID | Pressuposto | Justificação | Aprovado por (data) |
|---|---|---|---|
| PRS-<<nn>> | <<>> | <<>> | <<PO, data \| Pendente>> |
