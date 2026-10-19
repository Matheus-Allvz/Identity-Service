# 10 · Núcleo — <<Nome do Sistema>>

> **Conjunto:** [00 Índice](00-indice.md) · **Dono:** <<ARQ>> · **Estado:** <<>> · **Atualizado em:** <<AAAA-MM-DD>>
> Este ficheiro entra em **todos** os pacotes de contexto. Mantenha-o curto: tudo o que só um módulo usa vai para esse módulo.

## 1. Instruções obrigatórias para a LLM

> Texto padrão. Não altere, apenas complete os `<<>>`.

1. Implemente **exclusivamente** o que está no seu pacote de contexto. Qualquer endpoint, campo, tabela, dependência ou comportamento que não esteja descrito **NÃO DEVE** ser criado.
2. **DEVE**, **NÃO DEVE**, **DEVERIA**, **NÃO DEVERIA** e **PODE** têm o significado da RFC 2119/8174.
3. Em caso de conflito, siga a precedência do índice (`00`, §3).
4. Use **apenas** as tecnologias e versões da §4.
5. Respeite **literalmente** os nomes de entidades, colunas, campos JSON, enums, tópicos, chaves de cache e códigos de erro.
6. **Se algo estiver ambíguo ou em falta, NÃO invente.** Registe-o em "Dúvidas" na sua entrega e siga com a hipótese mais simples, marcada como tal. Toda a decisão tomada sem base na especificação vai para "Decisões fora dos docs".
7. Não toque em nada listado em "Não tocar ainda" no seu brief.
8. Cada CA da sua fase tem um teste com o ID no nome (convenção em `70`).
9. Entregue no formato fixo do seu brief (`80`).

## 2. Contexto

### 2.1 Propósito
<<Uma ou duas frases.>>

### 2.2 Âmbito
| Dentro (implementar) | Fora (NÃO implementar) |
|---|---|
| <<>> | <<incluindo o que a fonte menciona mas não se implementa>> |

### 2.3 Atores e sistemas vizinhos
| Ator / Sistema | Tipo | Interação |
|---|---|---|
| <<>> | <<Humano \| Sistema \| Serviço interno>> | <<>> |

### 2.4 Glossário
| Termo | Nome no código | Definição | Sinónimos proibidos |
|---|---|---|---|
| <<>> | <<>> | <<>> | <<>> |

## 3. Restrições arquiteturais

### 3.1 ADR
| ID | Decisão | Consequência obrigatória no código | Origem |
|---|---|---|---|
| ADR-01 | <<>> | <<>> | <<[DOC] fonte \| D-nn>> |

### 3.2 Estrutura do projeto e camadas
```text
<<árvore de diretórios obrigatória>>
```
**Regras de dependência entre camadas:** <<>>

### 3.3 Convenções de código
| Item | Convenção |
|---|---|
| Classes / ficheiros | <<>> |
| Tabelas / colunas | <<>> |
| JSON | <<>> |
| Datas | <<>> |
| Identificadores | <<>> |
| Mensagens de commit | <<por exemplo, `tipo: descrição (RF-nn, CA-nn.m)`>> |

### 3.4 Comportamentos transversais
| Aspeto | Especificação |
|---|---|
| Autenticação de entrada | <<>> |
| Envelope / formato de erro | <<RFC 9457, campos obrigatórios>> |
| Correlação | <<>> |
| Logging | <<formato, campos, e o que **NÃO DEVE** ser registado (segredos, tokens, dados pessoais)>> |
| Auditoria | <<>> |
| Configuração | <<variável = tipo, default>> |
| Migrações | <<mecanismo único, por exemplo "aplicadas no arranque">> |

## 4. Stack
| Camada | Tecnologia | Versão exata |
|---|---|---|
| <<>> | <<>> | <<>> |

**Dependências permitidas (exaustivo):** <<pacote@versão — finalidade>>
**Proibidas:** qualquer pacote não listado.

## 5. Requisitos não funcionais
| ID | Categoria | Requisito | Limiar | Verificação | Na métrica |
|---|---|---|---|---|---|
| RNF-01 | <<>> | <<>> | <<>> | <<>> | <<CA-nn.m \| Fora (motivo)>> |

## 6. Catálogo global de erros
| Código | HTTP | `title` (fixo) | Condição | Origem (RN/EX/camada) |
|---|---|---|---|---|
| <<ERR-VAL-001>> | <<400>> | <<>> | <<>> | <<>> |

### 6.1 Mapeamento de exceções da base de dados (infraestrutura vs. negócio)
Distingue falha de ligação (503) de erros do SGBD com significado de negócio. Falhas de ligação e indisponibilidade geram 503 `ERR-SYS-002`. Erros com semântica de negócio **NÃO DEVEM** ser transformados em 503: devem mapear para os códigos correspondentes do catálogo.

| SQLSTATE / Código do driver | Significado no SGBD | Status HTTP | Código de erro do catálogo |
|---|---|---|---|
| `<<42501>>` | <<Violação de RLS / permissão insuficiente>> | <<403>> | <<ERR-CTX-007>> |
| `<<23505>>` | <<Violação de restrição única (unique_violation)>> | <<409>> | <<ERR-CTX-005>> |
| `<<08* / 57P*>>` | <<Falha de ligação / servidor indisponível>> | <<503>> | <<ERR-SYS-002>> |

## 7. Regras por omissão
| Situação não coberta | Comportamento obrigatório |
|---|---|
| Campo de request não listado | <<>> |
| Erro inesperado | <<500 ERR-SYS-001, sem stack trace no corpo>> |

## 8. Explicitamente proibido
- <<>>

## 9. Pressupostos globais
| ID | Pressuposto / Decisão | Justificação | Itens afetados | Aprovado por (data) |
|---|---|---|---|---|
| PRS-01 | <<>> | <<>> | <<>> | <<PO, AAAA-MM-DD \| Pendente>> |
