# 30 · Dados — <<Nome do Sistema>>

> **Conjunto:** [00 Índice](00-indice.md) · **Dono:** <<ARQ>> · **Estado:** <<>> · **Atualizado em:** <<>>
> Contrato partilhado: é congelado no Gate 2. Cada tabela tem **um** módulo dono, que é o único que a escreve.

## 1. Diagrama ER
```mermaid
erDiagram
    <<ENTIDADE_A>> ||--o{ <<ENTIDADE_B>> : "<<verbo>>"
```

## 2. Tipos enumerados

### ENUM-01 `<<NOME>>`
| Valor | Significado |
|---|---|
| `<<>>` | <<>> |

**Transições permitidas** (qualquer outra é proibida):
| De | Para | Gatilho (RF/RN) |
|---|---|---|
| `<<>>` | `<<>>` | <<>> |

## 3. Dicionário de dados

### ENT-01 `<<tabela>>`
**Descrição:** <<>>
**Módulo dono (escreve):** <<M-nn>> · **Lida por:** <<M-nn, M-nn>>
**Invariantes:** <<>>
**Isolamento multi-tenant:** <<Sim, coluna X \| Não, motivo>>

| Coluna | Tipo físico | Nulo | Default | Chave | Restrições | Descrição |
|---|---|---|---|---|---|---|
| `<<id>>` | <<UUID>> | <<NÃO>> | <<>> | <<PK>> | <<>> | <<>> |
| `<<fk_id>>` | <<UUID>> | <<NÃO>> | <<>> | <<FK → tabela.coluna, ON DELETE RESTRICT>> | <<>> | <<>> |

**Índices:**
| Nome | Colunas | Tipo | Único |
|---|---|---|---|
| `<<>>` | <<>> | <<>> | <<>> |

## 4. Cache e armazenamento volátil
| Chave | Tipo | Valor | TTL | Escrita por | Lida por |
|---|---|---|---|---|---|
| `<<prefixo:{param}>>` | <<>> | <<>> | <<>> | <<RF/RN>> | <<RF/RN>> |

## 5. Cobertura de entrada (integrações e importações)
<!-- Cada campo de origem tem destino ou fica NÃO MAPEADO com decisão. Nada é descartado em silêncio. Remova a secção se não houver entrada externa. -->
| Campo de origem | Destino (tabela.coluna \| campo) | Transformação | Se inválido | Decisão |
|---|---|---|---|---|
| <<>> | <<\| NÃO MAPEADO>> | <<>> | <<>> | <<D-nn>> |

## 6. Seed
<<registos obrigatórios na primeira migração, ou "Nenhum">>

## 7. Dados de referência (oráculo)
<!-- Entrada real ou representativa com totais esperados reconciliados. Sem dados pessoais reais: use dados fictícios ou indique onde está o ficheiro protegido e a variável de ambiente que o ativa. -->
| Ficheiro / conjunto | Onde está | Totais esperados | Reconciliação |
|---|---|---|---|
| <<>> | <<>> | <<N linhas → X registos A, Y registos B>> | <<porque os totais diferem entre si>> |
