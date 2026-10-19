# 40 · Contratos partilhados — <<Nome do Sistema>>

> **Conjunto:** [00 Índice](00-indice.md) · **Dono:** <<ARQ>> · **Estado:** <<>> · **Atualizado em:** <<>>
> Contrato partilhado, congelado no Gate 2. O **detalhe** de cada endpoint fica no módulo dono (`50`). Aqui fica o que os outros módulos precisam de saber: a interface.

## 1. Convenções globais da API
| Item | Valor |
|---|---|
| Base path / versão | <<>> |
| Content-Type | <<>> |
| Autenticação | <<>> |
| Correlação | <<header, geração se ausente>> |
| Paginação | <<parâmetros, limites, formato>> |
| Envelope de sucesso | <<ou "sem envelope">> |
| Corpo de erro | RFC 9457, ver abaixo |

```json
{ "type": "<<uri>>", "title": "<<do catálogo>>", "status": 0, "code": "<<ERR-...>>", "detail": "<<>>", "correlation_id": "<<>>" }
```

## 2. Validação de entrada em camadas (ordem fixa)
A primeira camada que falha determina a resposta. As seguintes não são avaliadas.

| # | Camada | Exemplos | Resposta |
|---|---|---|---|
| 1 | Transporte | tamanho máximo, `Content-Type`, codificação | <<413 / 415 / 400 ERR-...>> |
| 2 | Formato | JSON válido, campos obrigatórios, tipos, limites | <<400 ERR-VAL-...>> |
| 3 | Autenticação | token ausente, inválido, revogado | <<401 ERR-AUTH-...>> |
| 4 | Autorização | escopo, papel, permissão | <<403 ERR-...>> |
| 5 | Existência | recurso inexistente | <<404 ERR-...>> |
| 6 | Regras de negócio | por ordem de RN, no módulo | <<409 / 422 ERR-...>> |

**Erro de dado vs. erro HTTP:** <<por exemplo, "uma linha inválida num lote é um resultado de validação da linha, com resposta 200; não é um 400">>

## 3. Checklist global de robustez de entrada
Aplica-se a **todos** os endpoints que aceitam corpo, parâmetros ou ficheiros. Nenhum destes casos pode devolver 5xx.

| # | Entrada | Resposta esperada |
|---|---|---|
| R1 | Corpo vazio | <<>> |
| R2 | JSON malformado | <<>> |
| R3 | `null` dentro de uma lista | <<>> |
| R4 | Carácter NUL (`\u0000`) num texto | <<>> |
| R5 | Texto não UTF-8 | <<>> |
| R6 | Texto acima do comprimento máximo | <<>> |
| R7 | `Content-Type` ausente, errado ou com parâmetros vazios | <<>> |
| R8 | Paginação com valores negativos, zero ou acima do inteiro máximo | <<>> |
| R9 | Multipart truncado (se houver upload) | <<>> |
| R10 | Header com caracteres não ASCII | <<>> |

## 4. Interfaces (todos os endpoints e eventos)

### 4.1 Tipos partilhados
Tabela de tipos partilhados utilizados nas interfaces internas (`IF-nn`), endpoints e eventos. As interfaces **DEVEM** usar exclusivamente tipos desta tabela ou tipos primitivos da stack.

| Nome do tipo | Pasta de destino | Namespace / Módulo | Campos tipados e definição |
|---|---|---|---|
| `<<NomeDoTipo>>` | `<<src/...>>` | `<<Namespace>>` | `<<record / enum / interface: campos e tipos>>` |

### 4.2 Tabela de interfaces (endpoints, eventos e interfaces internas)
| ID | Método e path / Tópico / Operação | Módulo dono | Autenticação / Permissão | Request / Parâmetros | Resposta de sucesso (resumo) | Erros possíveis |
|---|---|---|---|---|---|---|
| EP-01 | <<POST /api/v1/...>> | <<M-01>> | <<>> | <<campos principais>> | <<status + campos principais>> | <<ERR-...>> |
| EV-01 | <<topico>> | <<M-01>> | <<headers obrigatórios>> | Não se aplica | <<payload (resumo)>> | Não se aplica |
| IF-01 | <<IOperacao>> | <<M-01>> | <<chamador interno>> | <<tipos de 4.1>> | <<tipos de 4.1>> | <<exceções / ERR>> |

## 5. Matriz de autorização
| EP | Sem token | <<ESCOPO_1>> | <<ESCOPO_2>> | <<papel>> | Permissão exigida |
|---|---|---|---|---|---|
| EP-01 | <<401 ERR-...>> | <<Permitido>> | <<403 ERR-...>> | <<>> | <<dominio:acao>> |

## 6. Eventos: headers e semântica comuns
| Item | Valor |
|---|---|
| Headers obrigatórios | <<nome (tipo): descrição>> |
| Semântica de entrega | <<at-least-once via outbox>> |
| Idempotência no consumidor | <<chave>> |
