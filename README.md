# Identity-Service
## Especificação Modular de Requisitos para LLMs & Arquitetura Multi-Tenant

> **Status:** Gate 4 Congelado + Plano de Fases `UT-5-00` Concluído (15 de 15 módulos congelados · 109 CAs com 100% `@borda` · $\Sigma w = 327$ · Lint 0 · Pacotes $\le 59.6\text{ KiB}$)  
> **Metodologia:** Processo de Especificação de Requisitos para LLMs v2.1  
> **Autor / PO:** Matheus Allvz  
> **Orquestrador:** Carcará (Antigravity Multi-Agent Orchestrator)

---

## 📌 Visão Geral

O **Identity-Service** é o microsserviço central de autenticação, emissão de tokens JWT/JWKS, controle de sessão, gestão de tenants, membros e RBAC com isolamento estrito de dados via **PostgreSQL Row Level Security (RLS)**.

Este repositório contém:
1. **A Metodologia Completa:** O processo formal de Engenharia de Requisitos para Modelos de Linguagem (LLMs), os guias de preenchimento, as regras de linter léxico e os modelos estruturais de template.
2. **A Especificação Modular v2:** A especificação técnica completa do microsserviço, dividida em fundação congelada (Gate 1 e Gate 2) e 15 módulos desacoplados concebidos para execução paralela por agentes de IA sem saturação de contexto.
3. **O Guia Mestre de Apresentação:** Documento autossuficiente com fundamentação teórica, anatomia dos templates, governança e roteiro de *Pitch Deck* em 12 slides para liderança e times de engenharia.

---

## 🏛️ Filosofia de Engenharia

O desenvolvimento guiado por LLMs exige uma abordagem radicalmente diferente da especificação de software tradicional:

* **Mundo Fechado (*Closed-World Assumption*):** O que não estiver explicitamente especificado no pacote do módulo não existe nem deve ser implementado. Diante de ambiguidade, o agente deve registrar hipótese ou lacuna (`L-nn`) em vez de inferir ou adivinhar.
* **Princípio *Evidence-or-Zero*:** Toda afirmação sobre o sistema deve ser sustentada por evidência comprovável (`[MEDIDO]`, `[CÓDIGO]`, `[DOC]`, `[HISTÓRICO]`). Todo comportamento mensurável exige script executável na pasta `evidencias/`.
* **Isolamento de Contexto ($\le 60\text{ KiB}$):** Nenhum agente recebe o repositório inteiro. Cada módulo é especificado a partir de um pacote modular isolado e autocontido (`pacote-M-nn.md`), prevenindo degradação de atenção e alucinação silenciosa.
* **Rigor Léxico (Lint 0):** Proibição estrita de termos ambíguos (*"se aplicável"*, *"conforme necessário"*, *"temporariamente"*, *"robusto"*, etc.). Regras de negócio formalizadas em **SE / ENTÃO / SENÃO** e critérios de aceitação em sintaxe **Gherkin** com oráculos determinísticos e tags de severidade (`@critico`, `@borda`, `@MUST`).

---

## 📂 Estrutura do Repositório

```text
Identity-Service/
├── README.md                                          # Este documento de entrada
│
├── Etapa1/                                            # PROCESSO E TEMPLATES METODOLÓGICOS
│   ├── 01_processo_especificacao_requisitos_llm.md    # Processo formal (5 fases, 4 gates, governança)
│   ├── 03_guia_preenchimento_template.md              # Guia léxico, formato Gherkin e regras de qualidade
│   ├── apresentacao_processo_e_template_ponta_a_ponta.md # GUIA MESTRE DE APRESENTAÇÃO (Ponta a Ponta)
│   └── template/                                      # Conjunto de modelos de especificação
│       ├── 00-indice.md                               # Modelo de índice e mapa de dependências
│       ├── 10-nucleo.md                               # Modelo de núcleo arquitetural e ADRs
│       ├── 20-sistema-existente.md                    # Modelo de levantamento do sistema legado
│       ├── 30-dados.md                                # Modelo físico de dados e RLS
│       ├── 40-contratos.md                            # Modelo de interfaces, RFC 9457 e contratos
│       ├── 50-modulos/M-nn-modelo.md                  # Modelo canónico de módulo funcional
│       ├── 60-plano-de-fases.md                       # Modelo de fatiamento em fases F-nn
│       ├── 70-ambiente-teste.md                       # Modelo do ambiente de testes e containers
│       ├── 80-orquestracao.md                         # Modelo de governança e briefs de agentes
│       └── 90-registo.md                              # Modelo do registo de estado do projeto
│
├── Etapa2/                                            # ESPECIFICAÇÃO v2 DO IDENTITY-SERVICE
│   ├── spec-v2/                                       # Fundação congelada e módulos ativos
│   │   ├── 00-indice.md                               # Índice consolidado e mapa de UTs
│   │   ├── 10-nucleo.md                               # Núcleo, fronteiras, stack pinada e ADRs
│   │   ├── 20-sistema-existente.md                    # Análise do userservice legado e lacunas
│   │   ├── 30-dados.md                                # Modelo PostgreSQL, DDL, ENUMs e RLS
│   │   ├── 40-contratos.md                            # Endpoints, contratos de erro e interfaces
│   │   ├── 60-plano-de-fases.md                       # Plano de 4 fases de implementação (F-00 a F-03)
│   │   ├── 70-ambiente-teste.md                       # Testcontainers, Kestrel e suite de integração
│   │   ├── 80-orquestracao.md                         # Papéis, protocolo e briefs de delegação
│   │   ├── 90-registo.md                              # Fonte única de estado, log D-nn, PRS e CRs
│   │   ├── spec.sha256                                # Hashes SHA-256 de congelamento do Gate 4
│   │   │
│   │   ├── 50-modulos/                                # OS 15 MÓDULOS DE ESPECIFICAÇÃO (Onda 3)
│   │   │   ├── M-00-rotas-bypass.md                   # M-00: Encaminhamento, bypass e tratador global
│   │   │   ├── M-01-login.md                          # M-01: Autenticação por palavra-passe e bloqueio
│   │   │   ├── M-02-sessao-refresh.md                 # M-02: Rotação de refresh token e detecção de reuso
│   │   │   ├── M-03-troca-contexto.md                 # M-03: Troca de contexto ativo (SINGLE / MACRO)
│   │   │   ├── M-04-criar-tenant.md                   # M-04: Provisionamento de novos tenants
│   │   │   ├── M-05-membros.md                        # M-05: Associação de membros e papéis
│   │   │   ├── M-06-impersonation.md                  # M-06: Impersonation por suporte com auditoria
│   │   │   ├── M-07-chaves.md                         # M-07: Rotação de chaves RSA e endpoint JWKS
│   │   │   ├── M-08-acessos.md                        # M-08: Consultas de elegibilidade e RBAC
│   │   │   ├── M-09-logout.md                         # M-09: Revogação de sessão e denylist Redis
│   │   │   ├── M-10-estado-usuario.md                 # M-10: Suspensão e reativação de utilizador
│   │   │   ├── M-11-esquema.md                        # M-11: Migrações EF Core e seed determinístico
│   │   │   ├── M-12-estado-tenant.md                  # M-12: Suspensão e reativação de tenants
│   │   │   ├── M-13-isolamento.md                     # M-13: Isolamento multi-tenant PostgreSQL RLS
│   │   │   └── M-14-eventos.md                        # M-14: Publicação outbox de eventos Kafka
│   │   │
│   │   ├── pacotes/                                   # PACOTES MODULARES PRÉ-COMPILADOS (≤ 60 KiB)
│   │   │   ├── pacote-M-00.md até pacote-M-14.md      # Contextos autocontidos para agentes de IA
│   │   │
│   │   └── evidencias/                                # SCRIPTS DE VALIDAÇÃO E EVIDÊNCIAS
│   │       ├── medir-pacotes.py                       # Verificador de orçamento e fecho de referências
│   │       ├── lint-modulo.py                         # Linter de conformidade léxica e termos proibidos
│   │       ├── matriz-isolamento.sql                  # Matriz RLS multi-tenant (21 casos testados)
│   │       ├── medir-postgres.sh                      # Validação de DDL e RLS no PostgreSQL
│   │       └── medir-env70.sh                         # Validação do ambiente de testes
│   │
│   └── validacoes/                                    # Relatórios de validação formal dos Gates 1 e 2
│
└── Docs/                                              # DOCUMENTAÇÃO DE ORIGEM, DESIGN E ECOSSISTEMA
    ├── Alinhamento-Ecossistema-B2B.md                 # Alinhamento e contratos com Equipe 9, Grupo 2 e Grupo 8
    ├── Apresentacao-Processo-e-Template.md            # Versão inicial da apresentação
    ├── Arquitetura Base do Projeto.pdf                # Arquitetura visual original
    └── Ideia Base.md                                  # Requisitos preliminares de negócio
```

---

## 📊 Painel de Métricas da Especificação (Gate 4 Congelado)

| Módulo | Nome | Tamanho (Bytes) | Tamanho (KiB) | Teto (KiB) | Pacote Total (KiB) | Erros Lint | CAs (`@borda`) | $\Sigma w$ | Estado |
|---|---|---|---|---|---|---|---|---|---|
| **M-00** | Rotas, bypass e tratador global | 12 261 | 11.97 | 12.0 | 59.0 | 0 | 5 (100%) | 15 | **Congelado (Gate 4)** |
| **M-01** | Login e bloqueio de conta | 12 165 | 11.88 | 12.0 | 59.2 | 0 | 7 (100%) | 21 | **Congelado (Gate 4)** |
| **M-02** | Sessão, refresh e detecção de reuso | 10 159 | 9.92 | 10.0 | 59.2 | 0 | 8 (100%) | 24 | **Congelado (Gate 4)** |
| **M-03** | Troca de contexto (SINGLE / MACRO) | 14 010 | 13.68 | 14.0 | 58.6 | 0 | 8 (100%) | 24 | **Congelado (Gate 4)** |
| **M-04** | Provisionamento de tenant | 14 280 | 13.95 | 14.0 | 59.4 | 0 | 8 (100%) | 24 | **Congelado (Gate 4)** |
| **M-05** | Membros e atribuição de papéis | 10 233 | 9.99 | 10.0 | 59.3 | 0 | 8 (100%) | 24 | **Congelado (Gate 4)** |
| **M-06** | Impersonation por suporte | 13 564 | 13.25 | 13.5 | 59.1 | 0 | 7 (100%) | 21 | **Congelado (Gate 4)** |
| **M-07** | Gestão de chaves RSA e JWKS | 16 938 | 16.54 | 20.0 | 55.5 | 0 | 8 (100%) | 24 | **Congelado (Gate 4)** |
| **M-08** | Consultas de acessos e elegibilidade | 11 692 | 11.42 | 11.5 | 59.6 | 0 | 5 (100%) | 15 | **Congelado (Gate 4)** |
| **M-09** | Logout e revogação em Redis | 19 720 | 19.26 | 20.0 | 58.1 | 0 | 8 (100%) | 24 | **Congelado (Gate 4)** |
| **M-10** | Estado do utilizador (ativo/suspenso) | 10 225 | 9.99 | 10.0 | 59.3 | 0 | 8 (100%) | 24 | **Congelado (Gate 4)** |
| **M-11** | Esquema, migrações e seed | 6 730 | 6.57 | 7.0 | 59.2 | 0 | 6 (100%) | 18 | **Congelado (Gate 4)** |
| **M-12** | Estado do tenant (ativo/suspenso) | 13 389 | 13.08 | 14.0 | 58.2 | 0 | 7 (100%) | 21 | **Congelado (Gate 4)** |
| **M-13** | Isolamento e interceptores RLS | 16 373 | 15.99 | 16.0 | 57.8 | 0 | 8 (100%) | 24 | **Congelado (Gate 4)** |
| **M-14** | Eventos e outbox Kafka | 17 462 | 17.05 | 17.5 | 58.0 | 0 | 8 (100%) | 24 | **Congelado (Gate 4)** |
| **Total** | **15 módulos homologados** | **199 201** | **194.53 KiB** | — | **$\le 59.6\text{ KiB}$** | **0** | **109 CAs** | **327** | **100% OK** |

---

## 🚀 Como Apresentar o Projeto

Para apresentar a metodologia, a arquitetura do processo e o template modular a equipas técnicas e executivas, utilize o documento mestre:

👉 **[`Etapa1/apresentacao_processo_e_template_ponta_a_ponta.md`](Etapa1/apresentacao_processo_e_template_ponta_a_ponta.md)**

O documento contém um roteiro de 12 slides pronto para conduzir uma apresentação executiva, explicando:
1. Por que o desenvolvimento tradicional com LLMs gera dívida técnica e alucinações.
2. A solução através de pacotes modulares isolados e limites matemáticos de contexto.
3. Como os 4 Gates de qualidade e o princípio *Evidence-or-Zero* asseguram determinismo.
4. Demonstração prática dos 15 módulos do Identity-Service produzidos por agentes autônomos sob orquestração.
