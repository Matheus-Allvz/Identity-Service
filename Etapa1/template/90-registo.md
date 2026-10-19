# 90 · Registo — <<Nome do Sistema>>

> **Conjunto:** [00 Índice](00-indice.md) · **Dono:** <<ORQ>> · **Estado:** Vivo
> Fonte única do estado do projeto. Uma sessão nova do orquestrador começa por aqui. Atualizado em **cada** transição de UT.

## 1. Unidades de trabalho e fases
| UT / Fase | Papel | Depende de | Estado | Ciclo | Agente(s) | Score / Gate | Achados | Atualizado em |
|---|---|---|---|---|---|---|---|---|
| UT-1-01 | AR + ARQ | — | <<Pendente \| Pronta \| EmCurso \| EmValidação \| Retrabalho \| Aceite \| Bloqueada \| DeadLetter>> | <<>> | <<id>> | <<>> | <<>> | <<>> |

## 2. Decisões do PO
| ID | Data | Decisão | Origem | Itens afetados | Estado |
|---|---|---|---|---|---|
| D-01 | <<>> | <<>> | <<Q-nn \| dúvida de F-nn \| CR-nn>> | <<>> | <<Vigente \| Revista por D-nn \| ~~Revogada~~>> |

## 3. Perguntas abertas
| ID | Pergunta | Quem responde | Bloqueia | Estado | Resposta (D-nn) |
|---|---|---|---|---|---|
| Q-01 | <<>> | <<PO>> | <<UT / F-nn \| Nada>> | <<Aberta \| Respondida>> | <<>> |

## 4. Lacunas
| ID | Lacuna | Origem | Risco | Estado |
|---|---|---|---|---|
| L-01 | <<>> | <<EXT-nn>> | <<>> | <<Aberta \| Verificada (evidência) \| Aceite como risco (D-nn)>> |

## 5. Defeitos de ambiguidade (Fase 4)
| ID | Atividade | Ficheiro:linha | Severidade | Descrição | Resolução (ficheiro:linha) | Estado |
|---|---|---|---|---|---|---|
| DEF-01 | <<4.1–4.7>> | <<>> | <<>> | <<>> | <<>> | <<Aberto \| Resolvido \| Rejeitado (motivo)>> |

**Índice de Ambiguidade:**
| Módulo | N | IA inicial | IA residual |
|---|---|---|---|
| <<M-01>> | <<>> | <<>> | <<0>> |
| **Global** | <<>> | <<>> | <<0>> |

## 6. Pedidos de alteração (propostos pelo ARQ, aprovados pelo PO)
<!-- Todos os pedidos usam obrigatoriamente o formato CR-nn (com dois dígitos, nunca outras letras). Propostos pelo ARQ, aprovados pelo PO. -->
| ID | Data | Proponente (ARQ) | Origem | Alteração | Impacto (RF/CA/módulos/fases) | Decisão PO | UT / sub-fase |
|---|---|---|---|---|---|---|---|
| CR-01 | <<AAAA-MM-DD>> | <<UT-x-xx (ARQ)>> | <<>> | <<>> | <<>> | <<Aceite \| Rejeitado \| Adiado (data)>> | <<>> |

## 7. Congelamentos
| Versão | Data | Ficheiros | Hashes |
|---|---|---|---|
| <<v1.0>> | <<>> | <<10, 30, 40, 70, 50-modulos/*>> | `spec.sha256` |

## 8. Auditoria final (UT-5-90) — por execução do pipeline
**Execução:** <<#n>> · **Especificação:** <<versão>> · **Modelo:** <<>> · **Data:** <<>>

| CA | Fase | Prioridade | w | `@critico` | s no 1.º ciclo | s final | Evidência | Causa se 0 (E/L/T/P) |
|---|---|---|---|---|---|---|---|---|
| <<CA-01.1>> | <<F-00>> | <<MUST>> | <<3>> | <<>> | <<>> | <<>> | <<>> | <<>> |

| Métrica | Valor |
|---|---|
| $C_w^{(1)}$ | <<>> % |
| $C_w^{final}$ | <<>> % |
| $\bar k$ / dead letters | <<>> |
| $C_u$ / $C_{RF}$ | <<>> |
| TA | <<>> |
| Decisões fora dos docs + dúvidas | <<>> |
| **Veredito** | <<Conforme \| Não Conforme>> |
