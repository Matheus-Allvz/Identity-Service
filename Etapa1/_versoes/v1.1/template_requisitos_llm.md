<!--
  TEMPLATE DE REQUISITOS PARA GERAÇÃO DE CÓDIGO POR LLM — v1.1
  Como preencher: ver 03_guia_preenchimento_template.md (a secção do guia tem o mesmo número que a secção daqui).
  Convenções: <<texto>> = placeholder obrigatório | <!-- --> = instrução para o analista.
  ANTES DE ENVIAR À LLM: gerar o payload (sem comentários <!-- -->, sem §14 e sem Anexo A) e confirmar que não sobra nenhum "<<".
-->

# Especificação de Requisitos — <<Nome do Componente/Serviço>>

## 0. Metadados e Controlo de Versão

| Campo | Valor |
|---|---|
| ID do documento | <<SPEC-XXX>> |
| Versão | <<vX.Y>> |
| Estado | <<Rascunho \| Em validação \| Baseline congelada>> |
| Hash SHA-256 da baseline | Registado fora deste ficheiro, em `<<nome>>.sha256` (baseline + payload), no congelamento (Fase 4.10) |
| Autor (AR) | <<nome>> |
| Aprovadores (PO / ARQ / RI) | <<nomes>> |
| Data | <<AAAA-MM-DD>> |
| Modelo LLM alvo | <<modelo e versão exata>> |

### Histórico
| Versão | Data | Autor | Alteração |
|---|---|---|---|
| <<v0.1>> | <<AAAA-MM-DD>> | <<nome>> | <<descrição>> |

---

## 1. Instruções Obrigatórias para a LLM

> Esta secção destina-se à LLM que vai gerar o código. O texto padrão abaixo **não deve** ser alterado, apenas completado nos campos `<<>>`.

1. Implemente **exclusivamente** o que está especificado neste documento. Qualquer endpoint, campo, tabela, dependência ou comportamento que não esteja descrito aqui **NÃO DEVE** ser criado.
2. As palavras **DEVE**, **NÃO DEVE**, **DEVERIA**, **NÃO DEVERIA** e **PODE** têm o significado definido na RFC 2119/8174.
3. Em caso de conflito entre secções, a precedência é a seguinte: **§8 Regras de Negócio > §10 Critérios de Aceitação > §6 Contratos > §5 Modelo de Dados > §7 Descrições de RF > restantes secções.**
4. Use **apenas** as tecnologias e versões da §3. Não adicione bibliotecas que não estejam na lista de dependências permitidas.
5. Respeite **literalmente** os nomes de entidades, colunas, campos JSON, enums, tópicos, chaves Redis e códigos de erro. Não traduza nem renomeie.
6. Cada Critério de Aceitação (§10) **DEVE** ter um teste automatizado correspondente, com o ID do CA no nome do teste (por exemplo, `CA_03_2_refresh_reutilizado_revoga_familia`).
7. Se uma informação necessária estiver em falta, **NÃO** a invente. Aplique a regra por omissão da §12.2. Se nenhuma regra se aplicar, implemente a alternativa mais restritiva e registe-a na secção "Suposições" do relatório de entrega (§13).
8. Entregue o resultado no formato da §13.

---

## 2. Contexto do Sistema

### 2.1 Propósito
<<Uma ou duas frases: o que o componente faz e para quem.>>

### 2.2 Âmbito
| Dentro do âmbito (implementar) | Fora do âmbito (NÃO implementar) |
|---|---|
| <<capacidade>> | <<capacidade explicitamente excluída>> |

### 2.3 Atores e Sistemas Externos
| Ator / Sistema | Tipo | Interação com o componente |
|---|---|---|
| <<nome>> | <<Humano \| Sistema \| Serviço interno>> | <<o que envia/recebe, por qual interface>> |

### 2.4 Diagrama de Contexto
```mermaid
flowchart LR
    <<ator>> -->|<<protocolo>>| <<componente>>
```

### 2.5 Glossário
<!-- Um termo, um nome canónico. O "Nome no código" é o que a LLM vai usar. -->
| Termo de negócio | Nome no código | Definição | Sinónimos proibidos |
|---|---|---|---|
| <<termo>> | <<NomeCanonico>> | <<definição precisa>> | <<termos que NÃO devem ser usados>> |

---

## 3. Stack Tecnológica

### 3.1 Tecnologias
| Camada | Tecnologia | Versão exata | Observação |
|---|---|---|---|
| Linguagem / Runtime | <<>> | <<>> | <<>> |
| Framework web | <<>> | <<>> | <<>> |
| ORM / Acesso a dados | <<>> | <<>> | <<>> |
| Base de dados | <<>> | <<>> | <<>> |
| Cache / Armazenamento chave-valor | <<>> | <<>> | <<>> |
| Mensageria | <<>> | <<>> | <<>> |
| Testes | <<>> | <<>> | <<>> |
| Build / Contentores | <<>> | <<>> | <<>> |

### 3.2 Dependências
| Permitidas (pacote@versão) | Finalidade |
|---|---|
| <<pacote@x.y.z>> | <<>> |

**Dependências proibidas:** <<lista, ou "qualquer pacote não listado acima">>

---

## 4. Restrições Arquiteturais e Requisitos Não Funcionais

### 4.1 Decisões Arquiteturais (ADR)
| ID | Decisão | Consequência obrigatória no código |
|---|---|---|
| ADR-01 | <<decisão>> | <<o que o código DEVE / NÃO DEVE fazer por causa dela>> |

### 4.2 Estrutura de Projeto e Camadas
```text
<<árvore de diretórios obrigatória>>
```
**Regras de dependência entre camadas:** <<por exemplo, "Domain NÃO DEVE referenciar Infrastructure">>

### 4.3 Convenções de Código
| Item | Convenção |
|---|---|
| Nomes de classes / ficheiros | <<>> |
| Nomes de tabelas / colunas | <<por exemplo, snake_case, singular>> |
| JSON | <<por exemplo, snake_case>> |
| Datas | <<por exemplo, ISO-8601 em UTC, `2026-01-31T13:45:00Z`>> |
| Identificadores | <<por exemplo, UUID v7>> |

### 4.4 Comportamentos Transversais
| Aspeto | Especificação |
|---|---|
| Autenticação de entrada | <<>> |
| Formato de erro | <<por exemplo, RFC 9457 Problem Details, campos obrigatórios>> |
| Correlação / Rastreio | <<header, propagação>> |
| Logging | <<formato, campos obrigatórios, dados que NÃO DEVEM ser registados>> |
| Auditoria | <<eventos auditados, campos>> |
| Configuração | <<variáveis de ambiente: nome, tipo, default>> |

### 4.5 Requisitos Não Funcionais (mensuráveis)
| ID | Categoria | Requisito | Métrica / Limiar | Método de verificação | Na métrica |
|---|---|---|---|---|---|
| RNF-01 | <<Desempenho \| Segurança \| Disponibilidade \| Observabilidade>> | <<enunciado>> | <<valor numérico>> | <<teste, ferramenta>> | <<CA-nn.m \| Fora da métrica (motivo)>> |

---

## 5. Modelo de Dados

### 5.1 Diagrama Entidade-Relacionamento
```mermaid
erDiagram
    <<ENTIDADE_A>> ||--o{ <<ENTIDADE_B>> : "<<verbo>>"
```

### 5.2 Tipos Enumerados
#### ENUM-01 `<<NOME_ENUM>>`
| Valor | Significado |
|---|---|
| `<<VALOR>>` | <<>> |

**Transições de estado permitidas** (se aplicável; qualquer transição não listada é proibida):
| De | Para | Gatilho (RF/RN) |
|---|---|---|
| `<<>>` | `<<>>` | <<>> |

### 5.3 Dicionário de Dados
<!-- Repetir o bloco por entidade. Tipos físicos exatos do SGBD. -->
#### ENT-01 `<<nome_tabela>>`
**Descrição:** <<o que representa>>
**Restrições gerais / Invariantes:** <<por exemplo, "Um TENANT pertence a exatamente um ECONOMIC_GROUP">>
**Isolamento multi-tenant:** <<Sim, coluna X \| Não, motivo>>

| Coluna | Tipo físico | Nulo | Default | Chave | Restrições (CHECK/UQ/formato) | Descrição |
|---|---|---|---|---|---|---|
| `<<id>>` | <<UUID>> | <<NÃO>> | <<>> | <<PK>> | <<>> | <<>> |
| `<<fk_id>>` | <<UUID>> | <<NÃO>> | <<>> | <<FK → tabela.coluna, ON DELETE RESTRICT>> | <<>> | <<>> |

**Índices:**
| Nome | Colunas | Tipo | Único |
|---|---|---|---|
| `<<ix_...>>` | <<>> | <<B-tree>> | <<Sim/Não>> |

### 5.4 Estruturas em Cache / Armazenamento Volátil
| Padrão da chave | Tipo | Valor | TTL | Escrito por (RF/RN) | Lido por (RF/RN) |
|---|---|---|---|---|---|
| `<<prefixo:{param}>>` | <<String \| Hash \| Set>> | <<>> | <<>> | <<>> | <<>> |

### 5.5 Dados Iniciais (Seed)
<<registos obrigatórios na primeira migração, ou "Nenhum">>

---

## 6. Contratos de Interface

### 6.1 Convenções Globais da API
| Item | Valor |
|---|---|
| Base path / versão | <<por exemplo, /api/v1>> |
| Content-Type | <<application/json; charset=utf-8>> |
| Header de autenticação | <<por exemplo, Authorization: Bearer <JWT>>> |
| Header de correlação | <<nome, obrigatoriedade, geração se ausente>> |
| Paginação | <<parâmetros, limites, formato da resposta>> |
| Corpo de erro (modelo) | ver bloco abaixo |

```json
{
  "type": "<<uri>>",
  "title": "<<texto fixo do catálogo §9>>",
  "status": 0,
  "code": "<<ERR-XXX-000>>",
  "detail": "<<>>",
  "correlation_id": "<<>>"
}
```

### 6.2 Endpoints
<!-- Repetir o bloco por endpoint. -->
#### EP-01 `<<MÉTODO>> <<path>>`
| Campo | Valor |
|---|---|
| RF associado(s) | <<RF-nn>> |
| Descrição | <<uma frase>> |
| Autenticação | <<Pública \| JWT obrigatório>> |
| Autorização | <<permissão/escopo exigido, ou "nenhuma">> |
| Idempotência | <<Sim (mecanismo) \| Não>> |
| RN aplicáveis | <<RN-nn, RN-nn>> |

**Headers de request**
| Header | Obrigatório | Formato | Descrição |
|---|---|---|---|
| <<>> | <<Sim/Não>> | <<>> | <<>> |

**Parâmetros de path / query**
| Nome | Local | Tipo | Obrigatório | Validação | Default |
|---|---|---|---|---|---|
| <<>> | <<path \| query>> | <<>> | <<>> | <<>> | <<>> |

**Corpo do request**
| Campo | Tipo JSON | Obrigatório | Validação (formato, mín/máx, enum) | Descrição |
|---|---|---|---|---|
| `<<campo>>` | <<string>> | <<Sim>> | <<>> | <<>> |

```json
<<exemplo de request válido>>
```

**Ordem de avaliação** (a primeira verificação que falha determina a resposta; nenhuma verificação seguinte é executada)
| # | Verificação | Executada por | Se falhar (status + ERR) |
|---|---|---|---|
| 1 | <<formato do corpo e dos parâmetros>> | <<validação>> | <<400 ERR-VAL-...>> |
| 2 | <<autenticação (JWT, denylist)>> | <<middleware>> | <<401/403 ERR-AUTH-...>> |
| 3 | <<autorização (escopo/permissão)>> | <<>> | <<403 ERR-...>> |
| 4 | <<existência do recurso>> | <<>> | <<404 ERR-...>> |
| 5 | <<regras de negócio, pela ordem RN-nn, RN-nn>> | <<>> | <<409/422 ERR-...>> |

<!-- Em listas (por exemplo, vários tenants): dizer se cada verificação corre sobre a lista toda antes da seguinte, ou elemento a elemento. -->

**Respostas** (incluir as rejeições do middleware e de filtros transversais)
| Status | Condição | Corpo | Código de erro |
|---|---|---|---|
| <<200>> | <<sucesso>> | <<esquema abaixo>> | – |
| <<400>> | <<validação de formato falhou>> | Problem Details | <<ERR-...>> |
| <<401>> | <<>> | Problem Details | <<ERR-...>> |

**Corpo da resposta de sucesso**
| Campo | Tipo JSON | Sempre presente | Valor / Regra de cálculo | Ordenação (se lista) |
|---|---|---|---|---|
| `<<campo>>` | <<>> | <<Sim \| Não, quando ...>> | <<>> | <<campo ASC \| Não se aplica>> |

```json
<<exemplo de resposta de sucesso, coerente com a tabela>>
```

**Headers de resposta:** <<por exemplo, Cache-Control: no-store, ou "nenhum adicional">>

**Efeitos colaterais (por ordem de execução):**
1. <<escrita na BD: tabela, operação>>
2. <<escrita em cache: chave, TTL>>
3. <<evento publicado: EV-nn>>

**Transacionalidade:** <<que efeitos ocorrem na mesma transação; o que acontece em falha parcial>>

### 6.3 Eventos Assíncronos
<!-- Repetir o bloco por evento. -->
#### EV-01 `<<nome_topico>>`
| Campo | Valor |
|---|---|
| Direção | <<Publicado \| Consumido>> |
| Produtor / Consumidor(es) | <<>> |
| Gatilho | <<RF-nn / RN-nn>> |
| Chave de partição | <<campo>> |
| Semântica de entrega | <<at-least-once via outbox, ...>> |
| Tratamento de duplicados | <<chave de idempotência>> |

**Headers obrigatórios:** `<<header>>` (<<tipo>>) — <<descrição>>

**Payload**
| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `<<>>` | <<>> | <<>> | <<>> |

```json
<<exemplo de payload>>
```

### 6.4 Matriz de Autorização
<!-- Uma linha por endpoint. Cada célula: "Permitido" ou o status + ERR devolvido. -->
| EP | Sem token | <<ESCOPO_1>> | <<ESCOPO_2>> | <<papel/plataforma>> | Permissão exigida |
|---|---|---|---|---|---|
| EP-01 | <<Permitido \| 401 ERR-...>> | <<>> | <<>> | <<>> | <<dominio:acao \| nenhuma>> |

---

## 7. Requisitos Funcionais

<!-- Repetir o bloco por RF. -->
### RF-01 — <<Título no formato verbo + objeto>>
| Campo | Valor |
|---|---|
| Prioridade | <<MUST \| SHOULD \| COULD>> |
| Ator | <<>> |
| Gatilho | <<EP-nn \| EV-nn \| agendamento>> |
| Pré-condições | <<estado verificável antes da execução>> |
| RN aplicáveis | <<RN-nn>> |

**Fluxo principal**
1. <<O sistema recebe ...>>
2. <<O sistema valida ... conforme RN-nn.>>
3. <<O sistema persiste ...>>
4. <<O sistema responde 200 com ...>>

**Fluxos de exceção**
| ID | No passo | Condição | Resposta (status + ERR) | Efeitos / Estado final |
|---|---|---|---|---|
| EX-01.1 | <<2>> | <<>> | <<401 ERR-...>> | <<nada persistido; tentativa auditada>> |

**Pós-condições:** <<estado verificável após sucesso>>

---

## 8. Regras de Negócio

<!-- Forma canónica: SE <condição> ENTÃO <efeito> [SENÃO <efeito>]. Parâmetros com valor numérico. -->
| ID | Tipo | Regra (forma canónica) | Parâmetros | Erro se violada | RF afetados |
|---|---|---|---|---|---|
| RN-01 | <<Validação \| Autorização \| Cálculo \| Temporal \| Integridade>> | **SE** <<>> **ENTÃO** <<>> **SENÃO** <<>> | <<nome = valor unidade>> | <<ERR-...>> | <<RF-nn>> |

---

## 9. Catálogo de Erros

| Código | HTTP | `title` (texto fixo) | Condição de disparo | Origem (RN/EX) |
|---|---|---|---|---|
| <<ERR-XXX-001>> | <<>> | <<>> | <<>> | <<>> |

---

## 10. Critérios de Aceitação

### 10.1 Contrato do Ambiente de Teste
<!-- Define como a suite de aceitação (escrita antes da geração) interage com o código gerado. O código gerado DEVE respeitar este contrato. -->
| Item | Especificação |
|---|---|
| Arranque | <<por exemplo, `docker compose up -d --build`; serviço saudável quando `GET /health` = 200>> |
| Porta e base URL | <<>> |
| Variáveis de ambiente usadas pelos testes | <<nome = valor de teste>> |
| Carga dos dados de partida | <<por exemplo, SQL direto na BD antes de cada cenário; ordem de limpeza>> |
| Cunhagem de tokens de teste | <<chave privada de teste, `kid`, claims mínimas>> |
| Relógio | <<como os testes controlam o tempo, ou "relógio real com tolerância de N s">> |
| Injeção de falhas | <<mecanismo exato, ou "Não se aplica: nenhum CA exige falhas de infraestrutura">> |
| Inspeção de efeitos | <<como os testes leem BD, Redis e tópicos>> |

### 10.2 Cenários (Gherkin)

<!--
  Cobertura mínima por RF: 1 caminho feliz + 1 por RN + 1 por EX + 1 negativo de autorização (se houver controlo de acesso).
  Tags obrigatórias: @CA-nn.m @RF-nn @MUST|@SHOULD|@COULD ; opcionais: @RN-nn @EX-nn.m @critico
  Os passos "Então" afirmam valores observáveis: status, campos, linhas na BD, chaves, eventos.
-->

```gherkin
# language: pt
Funcionalidade: RF-01 — <<Título>>

  Contexto:
    Dado <<estado inicial comum, com dados concretos>>

  @CA-01.1 @RF-01 @MUST
  Cenário: <<caminho feliz>>
    Dado <<pré-condição com valores concretos>>
    Quando <<ação: método, path e corpo>>
    Então o status da resposta é <<200>>
    E <<campo>> da resposta é <<valor>>
    E <<estado persistido verificável>>

  @CA-01.2 @RF-01 @RN-01 @MUST @critico
  Cenário: <<violação da RN-01>>
    Dado <<>>
    Quando <<>>
    Então o status da resposta é <<4xx>>
    E o campo "code" da resposta é "<<ERR-...>>"
    E <<nada foi persistido / evento não publicado>>
```

---

## 11. Matriz de Rastreabilidade

| RF | Prioridade | RN | EP / EV | ENT | EX | CA | Teste automatizado |
|---|---|---|---|---|---|---|---|
| RF-01 | <<MUST>> | <<RN-01>> | <<EP-01>> | <<ENT-01>> | <<EX-01.1>> | <<CA-01.1, CA-01.2>> | <<nome do teste>> |

**Totais:** RF = <<n>> · RN = <<n>> · EP = <<n>> · EV = <<n>> · ENT = <<n>> · CA = <<n>> (das quais `@critico` = <<n>>)

---

## 12. Pressupostos, Regras por Omissão e Exclusões

### 12.1 Pressupostos e Decisões de Fecho
<!-- Todo o parâmetro numérico ou comportamento que não vem da fonte original tem de ter um PRS. -->
| ID | Pressuposto / Decisão | Justificação | Itens afetados | Aprovado por (data) |
|---|---|---|---|---|
| PRS-01 | <<>> | <<>> | <<RN-nn, EP-nn>> | <<PO, AAAA-MM-DD>> |

### 12.2 Regras por Omissão (aplicam-se quando a especificação é omissa)
| Situação | Comportamento obrigatório |
|---|---|
| Campo de request não listado | <<por exemplo, rejeitar com 400 ERR-VAL-002>> |
| Erro inesperado | <<por exemplo, 500 ERR-SYS-001, sem stack trace no corpo>> |
| <<>> | <<>> |

### 12.3 Explicitamente Proibido
- <<por exemplo, NÃO DEVE registar passwords, tokens ou hashes em logs.>>

---

## 13. Formato de Entrega Esperado da LLM

1. **Árvore de ficheiros** completa e, a seguir, o conteúdo integral de cada ficheiro (sem omissões do tipo "// resto igual").
2. **Migrações** de BD que criam exatamente o modelo da §5, aplicadas pelo mecanismo único definido em <<§ / PRS>>.
3. **Contentores:** `Dockerfile` e `docker-compose.yml` que cumprem o contrato da §10.1.
4. **Testes**: um por CA, com o ID no nome.
5. **README** com os comandos para compilar, migrar, executar e testar.
6. **Relatório de entrega** com:
   - tabela `CA → teste → ficheiro`;
   - **Suposições:** lista de decisões tomadas por falta de especificação (o valor esperado é "nenhuma").

---

## 14. Questões em Aberto

<!-- Tem de estar VAZIA (apenas a linha "Nenhuma.") para passar o Gate 4. Esta secção pode ser removida da versão enviada à LLM. -->
| ID | Questão | Item afetado | Responsável | Prazo |
|---|---|---|---|---|
| QA-01 | <<>> | <<>> | <<>> | <<>> |

---

## Anexo A — Folha de Auditoria de Conformidade

<!-- Preenchida pelo AUD na Fase 5. NÃO é enviada à LLM. -->
**Execução:** <<#n>> · **Baseline:** <<versão + hash>> · **Modelo:** <<>> · **Data:** <<>> · **Medição:** <<M0 \| M1>>

| Gate | Resultado (Passou/Falhou) | Evidência |
|---|---|---|
| G1 — Compila, arranca, migra | <<>> | <<>> |
| G2 — Todos os `@critico` passam | <<>> | <<>> |

| CA | RF | Prioridade | Peso $w$ | `@critico` | Resultado $s$ (0/1) | Evidência | Causa se 0 (E/L/T) |
|---|---|---|---|---|---|---|---|
| CA-01.1 | RF-01 | <<MUST>> | <<3>> | <<Não>> | <<>> | <<>> | <<>> |

| Métrica | Valor |
|---|---|
| $\sum w \cdot s$ / $\sum w$ | <<>> / <<>> |
| $C_w$ | <<>> % |
| $C_u$ | <<>> % |
| $C_{RF}$ | <<>> % |
| Elementos alucinados $H$ / TA | <<>> / <<>> |
| **Veredito** | <<Conforme \| Não Conforme>> |
