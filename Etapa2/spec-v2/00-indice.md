# Identity-Service — Índice da Especificação (v2)

**Leia primeiro.** Este índice é a porta de entrada de qualquer agente, humano ou LLM. Nenhum agente lê a especificação inteira: cada um recebe o **pacote de contexto** da sua unidade de trabalho, montado a partir das colunas abaixo. A v2 substitui a spec monolítica `Etapa2/spec_identity_service.md` (v1.0); a correspondência de IDs está em `90` §6.

## 1. Mapa de documentos

| Doc | Para quê | Leia quando | Dono | Estado | Atualizado em | Hash |
|---|---|---|---|---|---|---|
| [10 · Núcleo](10-nucleo.md) | Contexto, âmbito, glossário, ADR, convenções, transversais, claims, catálogo de RG, stack, erros; extras por módulo | **Sempre** (núcleo base; extras segundo o §2.2) | ARQ (UT-1-01) | Rascunho (ciclo 3) | 2026-10-19 | Não se aplica (Rascunho) |
| [20 · Sistema existente](20-sistema-existente.md) | PostgreSQL, Redis e Kafka medidos; Gateway e consumidores como contratos; lacunas | O pacote inclui um `EXT-nn` (§2.2) | AR (UT-1-02) | Rascunho (ciclo 3) | 2026-10-19 | Não se aplica (Rascunho) |
| [30 · Dados](30-dados.md) | Posse das tabelas, ERD, enums, dicionário físico, Redis, RLS, seed | A UT lê ou escreve dados (secções do §2.2) | ARQ (UT-2-01) | Rascunho (ciclo 3) | 2026-10-19 | Não se aplica (Rascunho) |
| [40 · Contratos](40-contratos.md) | Convenções da API, interfaces EP/EV/IF, matriz de autorização, validação em camadas, entrada malformada | Especificação de módulo e implementação (secções do §2.2) | ARQ (UT-2-02; reconciliado em UT-2-90) | Rascunho (ciclo 3) | 2026-10-19 | Não se aplica (Rascunho) |
| [50 · Módulos](50-modulos/) | Um ficheiro por módulo (§2) | A UT é deste módulo | AR por módulo (UT-3-00 a UT-3-14) | Por escrever | Não se aplica | Não se aplica |
| [60 · Plano de fases](60-plano-de-fases.md) | Fases, grafo, "Não tocar ainda" | Planear, construir ou validar uma fase | ARQ (UT-5-00) | Modelo por preencher | Não se aplica | Não se aplica |
| [70 · Ambiente de teste](70-ambiente-teste.md) | Arranque, dados, tokens, relógio, falhas, nomes de testes | Parte comum em todos os pacotes; restantes secções segundo o cabeçalho de `70` | AUD (UT-2-03; reconciliado em UT-2-90) | Rascunho (ciclo 3) | 2026-10-19 | Não se aplica (Rascunho) |
| [80 · Orquestração](80-orquestracao.md) | Briefs, ciclo, score, entrega | Orquestrar ou receber um brief | ORQ | Modelo por preencher | Não se aplica | Não se aplica |
| [90 · Registo](90-registo.md) | Grafo de UT, PRS, decisões, perguntas, lacunas, alterações, mapeamento v1.0 → v2 | Saber onde está; retomar numa sessão nova | ORQ | Vivo | 2026-10-19 | Não se aplica |
| [evidencias/](evidencias/) | Scripts de medição e resultados (`[MEDIDO]`), esqueleto de pins, medição dos pacotes | Verificar uma evidência ou repetir uma medição | ARQ (UT-1-01) | Rascunho (ciclo 3) | 2026-10-19 | Não se aplica |

## 2. Mapa de módulos

Corte de UT-1-90, revisto em UT-2-90 (CR-12 a CR-15). Cada módulo é dono exclusivo das tabelas, endpoints, eventos e interfaces que lhe estão atribuídos; os outros só os usam pela interface (`40` §4). Quinze módulos (M-00 a M-14): a divisão foi imposta pelo orçamento de 60 KiB (§2.3).

| Módulo | Âmbito (uma frase) | RF | EP / EV / IF de que é dono | Tabelas que escreve | Depende de (interfaces) | Pacote estimado (KiB) |
|---|---|---|---|---|---|---|
| M-00 Pedido HTTP | Middleware de autenticação e revogação, rotas, erros, correlação, auditoria, denylist, chave de assinatura | RF-15, RF-17 | IF-03, IF-04, IF-05, IF-08; respostas comuns e rotas inexistentes | ENT-09, `denylist:*` | Nenhuma | 59.0 |
| M-01 Login | Login com bloqueio e lista de contextos | RF-01 | EP-01; IF-02 | ENT-04 | M-00 (IF-03, IF-04), M-02 (IF-01), M-07 (IF-06), M-14 (IF-07), M-08 (IF-09) | 59.3 |
| M-02 Refresh | Rotação com deteção de reuso e revalidação do contexto | RF-04 | EP-03; IF-01 | ENT-08 | M-00 (IF-03, IF-04, IF-08), M-07 (IF-06), M-14 (IF-07), M-08 (IF-09) | 59.3 |
| M-03 Contexto | Troca de contexto `SINGLE` e `MACRO_GROUP` | RF-02, RF-03 | EP-02 | Nenhuma (ENT-08 por IF-01) | M-00 (IF-03, IF-04, IF-08), M-02 (IF-01), M-07 (IF-06), M-08 (IF-09) | 58.9 |
| M-04 Criar tenant | Criar tenant com escopos; vínculo do criador `TENANT_ADMIN` (D-04) | RF-07 | EP-06; IF-10 | ENT-01 (sem escrita), ENT-02, ENT-03 | M-00 (IF-03, IF-08), M-05 (IF-12), M-14 (IF-07) | 59.5 |
| M-05 Membros | Vincular utilizador a tenant com papéis | RF-08 | EP-07; IF-12 | ENT-05, ENT-07 | M-00 (IF-03, IF-08), M-13 (IF-11) | 59.3 |
| M-06 Impersonation | Token de delegação RFC 8693 | RF-06 | EP-05 | Nenhuma | M-00 (IF-03, IF-08), M-07 (IF-06), M-08 (IF-09) | 58.8 |
| M-07 Chaves | Assinatura de tokens e JWKS | RF-11 | EP-10; IF-06 | Nenhuma | M-00 (IF-05) | 59.0 |
| M-08 Acessos | Implementação única das RG do extra §3.8 do núcleo | RF-18 | IF-09 | Nenhuma (só leitura) | M-00 (IF-04), M-13 (IF-11) | 59.2 |
| M-09 Logout | Revogar `jti` e família | RF-05 | EP-04 | Nenhuma (ENT-08 por IF-01) | M-00 (IF-03, IF-04, IF-08), M-02 (IF-01) | 58.9 |
| M-10 Estado do utilizador | Suspender e reativar: conta (`PLATFORM_ADMIN`) ou vínculos no grupo (`TENANT_ADMIN`, D-07) | RF-10, RF-13 | EP-09 | Nenhuma (ENT-04 por IF-02; ENT-05 por IF-12) | M-00 (IF-03, IF-04, IF-08), M-01 (IF-02), M-02 (IF-01), M-05 (IF-12), M-08 (IF-09), M-14 (IF-07) | 59.3 |
| M-11 Esquema, migração e seed | Entidades EF, `IdentityDbContext`, migração inicial (`30` §3, §5, §6), arranque e contentores | RF-19 | Nenhum | Esquema inteiro; ENT-06 (seed) | M-00 (IF-08), M-13 (IF-11) | 59.7 |
| M-12 Estado do tenant | Suspender e reativar tenant (lê ENT-05 para a condição C8) | RF-09, RF-12 | EP-08 | Nenhuma (`tenant.status` por IF-10) | M-00 (IF-03, IF-04, IF-08), M-02 (IF-01), M-04 (IF-10), M-14 (IF-07) | 59.2 |
| M-13 Isolamento | Camadas 2, 3 e 4 do isolamento | RF-16 | IF-11 (com `ITenantScoped`) | Nenhuma | M-00 (IF-08) | 59.6 |
| M-14 Eventos | Outbox, `context_jws` e publicação Kafka | RF-14 | EV-01 a EV-04; IF-07 | ENT-10 | M-00 (IF-05, IF-08) | 59.0 |

Dependências por interface sem ciclos: o M-11 consome as operações IF-11.1 e IF-11.2 do M-13 em runtime (no interceptor/filtro do `IdentityDbContext`), enquanto o M-13 define a interface IF-11 e não consome nenhuma interface de M-11 (o M-11 não exporta interfaces; a dependência é unidirecional M-11 → M-13). Ordem topológica linear para a implementação (as UT-3 correm em paralelo, pois dependem apenas de `40`): M-00; M-13; M-11; M-07, M-08 e M-14; M-02 e M-05; M-01, M-03, M-04, M-06 e M-09; M-12 e M-10.

### 2.1 Endpoints, eventos e interfaces internas
O detalhe de cada EP e EV fica no módulo dono; `40` §4 tem uma linha por EP, EV e IF, uma por operação de IF e uma por tipo partilhado. Os IDs EP e EV mantêm os da v1.0.

| ID | Método e path / Tópico / Interface | Módulo dono | Consumidores |
|---|---|---|---|
| EP-01 | `POST /api/v1/auth/login` | M-01 | Externo |
| EP-02 | `POST /api/v1/auth/switch-context` | M-03 | Externo |
| EP-03 | `POST /api/v1/auth/refresh` | M-02 | Externo |
| EP-04 | `POST /api/v1/auth/logout` | M-09 | Externo |
| EP-05 | `POST /api/v1/auth/impersonate` | M-06 | Externo |
| EP-06 | `POST /api/v1/tenants` | M-04 | Externo |
| EP-07 | `POST /api/v1/tenants/{tenant_id}/members` | M-05 | Externo |
| EP-08 | `PATCH /api/v1/tenants/{tenant_id}/status` | M-12 | Externo |
| EP-09 | `PATCH /api/v1/users/{user_id}/status` | M-10 | Externo |
| EP-10 | `GET /.well-known/jwks.json` | M-07 | EXT-04, EXT-05 |
| EV-01 | `empresa_cadastrada` | M-14 (gatilho: M-04, RF-07) | EXT-05 |
| EV-02 | `tenant_bloqueado` | M-14 (gatilho: M-12, RF-09) | EXT-05 |
| EV-03 | `usuario_bloqueado` | M-14 (gatilhos: M-01, RF-01; M-10, RF-10 com `PLATFORM_ADMIN`) | EXT-05 |
| EV-04 | `sessao_comprometida` | M-14 (gatilho: M-02, RF-04) | EXT-05 |
| IF-01 | `ISessionStore` | M-02 | M-01 (IF-01.1, IF-01.4), M-03 (IF-01.2), M-09 (IF-01.3), M-10 (IF-01.4, IF-01.6), M-12 (IF-01.5) |
| IF-02 | `IUserAccountStatusWriter` | M-01 | M-10 |
| IF-03 | `IAuditLog` | M-00 | M-01 a M-06, M-09, M-10, M-12 |
| IF-04 | `IDenylist` | M-00 | Escrita: M-01, M-02, M-03, M-09, M-10, M-12. Consulta: M-08 |
| IF-05 | `ISigningKey` | M-00 | M-07, M-14 |
| IF-06 | `ITokenIssuer` | M-07 | M-01, M-02, M-03, M-06 |
| IF-07 | `IEventOutbox` | M-14 | M-01, M-02, M-04, M-10, M-12 |
| IF-08 | `IAuthContext` e `AuthorizeTenantWrite` | M-00 | Leitura: M-02, M-03, M-04, M-05, M-06, M-09, M-10, M-11, M-12, M-13, M-14. `AuthorizeTenantWrite`: M-04, M-05 |
| IF-09 | `IAccessQueries` | M-08 | M-01, M-02, M-03, M-06, M-10 (IF-09.7) |
| IF-10 | `ITenantStatusWriter` | M-04 | M-12 |
| IF-11 | `ITenantIsolation` e `ITenantScoped` | M-13 | M-11 (IF-11.1, IF-11.2 em runtime), M-05 (IF-11.3), M-08 (IF-11.3) |
| IF-12 | `IMembershipWriter` | M-05 | M-04 (IF-12.1), M-10 (IF-12.2) |

### 2.2 Pacote de contexto por módulo (UT-3-nn)
**Fonte única:** o dicionário `P` de `evidencias/medir-pacotes.py`; a tabela abaixo é a sua saída (`--tabela`). O script mede o tamanho e verifica o fecho de referências: cada ENUM, ENT, IF, RG, ERR, PRS, EP, EV, EXT, AUD e secção citada no pacote tem de estar definida no mesmo pacote (com exceção de citações que são estritamente etiquetas de evidência ou registo: decisões `D-nn`, `CR-nn`, lacunas `L-nn` e consumidores `EXT-04`, `EXT-05`).
**Entram sempre:** o núcleo base (`10` até à secção final "Extras"); de `30`, o cabeçalho, o §1 sem o §1.1 e as aberturas do §2 e do §3; a parte comum de `70` (cabeçalho e §1); o brief. De `40`: a coluna "`40`" e as linhas indicadas, o texto de abertura do §4 (e do §5 quando há EP) e as linhas da tabela "Tipos partilhados" dos tipos citados no pacote (fecho calculado pelo script). De `90`: a linha correspondente de 'Entradas para as UT-3' (§1) e as linhas da tabela de 'PRS de um só módulo' (§2.2) atribuídas ao módulo (coluna `90 §2.2`).
**Secções:** em `30`, "ENT-nn" e "ENUM-nn" = o título e o texto até ao título seguinte; "ENUM-nn (transições)" = a subsecção `#### ENUM-nn · transições`; "AUD.Mnn" = a subsecção dos eventos de auditoria do módulo. Em `40` §4.3, "IF-nn" = a linha da tabela "Interfaces"; "IF-nn.k" = a linha da operação. Em `20`, a secção `## EXT-nn` inteira. Em `90`, "PRS-nn" = a linha correspondente de pressuposto de um só módulo (§2.2).
**CA obrigatórios de RNF** (vão no brief): M-01 (RNF-01, RNF-03), M-02 (RNF-03), M-07 (RNF-02), M-13 (RNF-06), M-14 (RNF-05).

| Módulo | Extras de `10` | Secções de `30` | Linhas de `40` §4 (itens e operações) | `40` | `70` §2 | `20` | `90` §2.2 | Teto do ficheiro (KiB) |
|---|---|---|---|---|---|---|---|---|
| M-00 | §3.7, §3.9 | ENT-09, ENUM-08, §4 | IF-03, IF-03.1, IF-04, IF-04.1, IF-04.2, IF-04.3, IF-04.4, IF-04.5, IF-04.6, IF-05, IF-05.1, IF-08, IF-08.1, IF-08.2 | §1, §2, §3, §1.1 | Não | EXT-02 | Nenhum | 12 |
| M-01 | Nenhum | ENT-04, ENUM-04, ENUM-04 (transições), AUD.M01 | EP-01, EV-03, IF-01.1, IF-01.4, IF-02, IF-02.1, IF-03.1, IF-04.2, IF-06.1, IF-07.3, IF-09.1, IF-09.6 | §1, §2, §3 | Não | Nenhuma | PRS-30, PRS-31, PRS-32 | 12 |
| M-02 | Nenhum | ENT-08, ENUM-05, ENUM-05 (transições), AUD.M02 | EP-03, EV-04, IF-01, IF-01.1, IF-01.2, IF-01.3, IF-01.4, IF-01.5, IF-01.6, IF-03.1, IF-04.2, IF-06.1, IF-07.5, IF-08.1, IF-09.1, IF-09.2, IF-09.3, IF-09.4, IF-09.5 | §1, §2, §3 | Não | Nenhuma | PRS-33, PRS-34, PRS-45 | 10 |
| M-03 | Nenhum | ENUM-03, ENUM-06, ENUM-06 (transições), AUD.M03 | EP-02, IF-01.2, IF-03.1, IF-04.1, IF-06.1, IF-08.1, IF-09.1, IF-09.2, IF-09.3, IF-09.4, IF-09.5, IF-09.8 | §1, §2, §3 | Não | Nenhuma | PRS-05 | 15 |
| M-04 | Nenhum | ENT-01, ENT-02, ENT-03, ENUM-01, ENUM-02, ENUM-03, AUD.M04 | EP-06, EV-01, IF-03.1, IF-07.1, IF-08.1, IF-08.2, IF-10, IF-10.1, IF-12.1 | §1, §2, §3 | Não | Nenhuma | PRS-36, PRS-37 | 14 |
| M-05 | Nenhum | ENT-02, ENT-03, ENT-04, ENT-05, ENT-06, ENT-07, ENUM-01, AUD.M05 | EP-07, IF-03.1, IF-08.1, IF-08.2, IF-11.3, IF-12, IF-12.1, IF-12.2 | §1, §2, §3 | Não | Nenhuma | PRS-38 | 10 |
| M-06 | Nenhum | ENT-04, ENUM-03, ENUM-04, ENUM-07, AUD.M06 | EP-05, IF-03.1, IF-06.1, IF-08.1, IF-09.1, IF-09.2, IF-09.3, IF-09.4, IF-09.8 | §1, §2, §3 | Não | Nenhuma | PRS-08, PRS-40, PRS-41 | 15 |
| M-07 | §3.7 | Nenhuma | EP-10, IF-05.1, IF-06, IF-06.1 | §1, §2, §3 | Não | Nenhuma | PRS-44 | 20 |
| M-08 | §3.8 | ENT-01, ENT-02, ENT-03, ENT-04, ENT-05, ENT-06, ENT-07, ENUM-01, ENUM-03, ENUM-04, §4, §6 | IF-04.5, IF-04.6, IF-09, IF-09.1, IF-09.2, IF-09.3, IF-09.4, IF-09.5, IF-09.6, IF-09.7, IF-09.8, IF-11.3 | §1 | Não | EXT-02 | PRS-05 | 11 |
| M-09 | Nenhum | ENUM-06, AUD.M09 | EP-04, IF-01.3, IF-03.1, IF-04.1, IF-08.1 | §1, §2, §3 | Não | Nenhuma | Nenhum | 21 |
| M-10 | Nenhum | ENT-02, ENT-04, ENT-05, ENUM-04, ENUM-04 (transições), AUD.M10 | EP-09, EV-03, IF-01.4, IF-01.6, IF-02.1, IF-03.1, IF-04.2, IF-07.4, IF-08.1, IF-09.7, IF-12.2 | §1, §2, §3 | Não | Nenhuma | PRS-37, PRS-39 | 10 |
| M-11 | §3.7, §3.11 | ENT-01, ENT-02, ENT-03, ENT-04, ENT-05, ENT-06, ENT-07, ENT-08, ENT-09, ENT-10, §5, §6 | IF-08.1, IF-11.1, IF-11.2 | Nenhuma | Sim | EXT-01 | Nenhum | 7 |
| M-12 | Nenhum | ENT-02, ENT-05, ENUM-01, ENUM-01 (transições), AUD.M12 | EP-08, EV-02, IF-01.5, IF-03.1, IF-04.3, IF-04.4, IF-07.2, IF-08.1, IF-10.1 | §1, §2, §3 | Não | Nenhuma | PRS-36, PRS-37 | 14 |
| M-13 | §3.10, §5 | ENT-03, ENT-05, ENT-07, §5, §5.1 | IF-08.1, IF-11, IF-11.1, IF-11.2, IF-11.3 | §1 | Não | EXT-01 | Nenhum | 18 |
| M-14 | §3.7, §5 | ENT-10, ENUM-03, ENUM-06, ENUM-09, ENUM-09 (transições), ENUM-10 | EV-01, EV-02, EV-03, EV-04, IF-05.1, IF-07, IF-07.1, IF-07.2, IF-07.3, IF-07.4, IF-07.5, IF-08.1 | §1, §6 | Não | EXT-03 | PRS-09, PRS-42, PRS-43 | 19 |

### 2.3 Cálculo do orçamento
Medição reprodutível: `python evidencias/medir-pacotes.py` (sai com código 1 se um pacote passar de 60 KiB ou se o fecho de referências falhar); resultado em `evidencias/medir-pacotes.out.txt`. Pacote = partes acima + brief (reserva de 2 KiB) + ficheiro do módulo estimado (a partir do material da v1.0, descontado o que `40` já descreve). O teto não sobe: um ficheiro de módulo que o passe é dividido ou cortado.

## 3. Precedência (regra única)

Em caso de conflito entre documentos, vale, pela ordem:

1. **Regras de negócio**: RN do módulo dono (`50`) e regras globais RG (`10` §3.6, com o texto canónico em `10` §3.8, §3.9 e §3.10)
2. **Critérios de aceitação** do módulo dono (`50`, secção CA)
3. **Contratos** (`40`, e o detalhe do endpoint no módulo dono)
4. **Dados** (`30`)
5. **Restante núcleo** (`10`)
6. **Plano de fases** (`60`) e restantes

Uma RN de módulo que contradiga uma RG é um defeito. Um conflito encontrado regista-se em `90` como `DEF-nn` ou `CR-nn`; **não** se resolve por interpretação.

## 4. Convenções de identificadores

| Prefixo | Item | Onde vive |
|---|---|---|
| `ADR-nn` | Decisão arquitetural | `10` §3.1 |
| `RNF-nn` | Requisito não funcional | `10` §5 |
| `ERR-<área>-nnn` | Código de erro (áreas `VAL`, `REQ`, `AUTH`, `CTX`, `SYS`) | `10` §6 |
| `RG-nn` | Regra global usada por mais de um módulo | `10` §3.6, §3.8, §3.9 |
| `EXT-nn`, `L-nn` | Sistema externo; lacuna | `20`; `90` §5 |
| `ENT-nn`, `ENUM-nn` | Entidade; tipo enumerado | `30` |
| `EP-nn`, `EV-nn`, `IF-nn` | Endpoint; evento; interface interna entre módulos | Linha em `40` §4; detalhe no módulo dono (§2.1) |
| `M-nn` | Módulo | `50-modulos/M-nn-<nome>.md` |
| `RF-nn`, `RN-nn`, `EX-nn.m`, `CA-nn.m` | Requisito, regra, exceção, critério | Módulo dono |
| `F-nn` | Fase de implementação | `60` |
| `UT-<fase>-nn`, `D-nn`, `Q-nn`, `DEF-nn`, `A-nn`, `CR-nn` | Unidade de trabalho; decisão; pergunta; defeito; achado; alteração | `90` |
| `PRS-nn` | Pressuposto de fecho | `10` §9 (globais) ou módulo; estado em `90` §2 |
| `M1`, `R1`, `K1` (sem hífen) | Identificador de uma medição | `evidencias/*.sh`, `*.sql` e `*.out.txt` |

Os IDs são **imutáveis**; um item removido fica `[REMOVIDO]` e o ID não se reutiliza. RF, EP, EV, ENT e EX mantêm os IDs da v1.0 quando o item continua a existir (`90` §6).

**Faixas para IDs novos criados em paralelo na Fase 3 (UT-3).** `EX-nn.m` e `CA-nn.m` derivam do RF exclusivo de cada módulo (atividade 3.6). Cada UT-3 possui faixa própria e exclusiva de `Q-nn` para esclarecimentos ao PO (sem colidir com `Q-01` a `Q-14` já utilizados em `90` §4):

| Módulo | UT-3 | RN novas | PRS novos | Q novos | CA (pelo RF dono) |
|---|---|---|---|---|---|
| M-00 | UT-3-00 | RN-20 a RN-29 | PRS-100 a PRS-109 | Q-20 a Q-29 | CA-15.m, CA-17.m |
| M-01 | UT-3-01 | RN-30 a RN-39 | PRS-110 a PRS-119 | Q-30 a Q-39 | CA-01.m |
| M-02 | UT-3-02 | RN-40 a RN-49 | PRS-120 a PRS-129 | Q-40 a Q-49 | CA-04.m |
| M-03 | UT-3-03 | RN-50 a RN-59 | PRS-130 a PRS-139 | Q-50 a Q-59 | CA-02.m, CA-03.m |
| M-04 | UT-3-04 | RN-60 a RN-69 | PRS-140 a PRS-149 | Q-60 a Q-69 | CA-07.m |
| M-05 | UT-3-05 | RN-70 a RN-79 | PRS-150 a PRS-159 | Q-70 a Q-79 | CA-08.m |
| M-06 | UT-3-06 | RN-80 a RN-89 | PRS-160 a PRS-169 | Q-80 a Q-89 | CA-06.m |
| M-07 | UT-3-07 | RN-90 a RN-99 | PRS-170 a PRS-179 | Q-90 a Q-99 | CA-11.m |
| M-08 | UT-3-08 | RN-100 a RN-109 | PRS-180 a PRS-189 | Q-100 a Q-109 | CA-18.m |
| M-09 | UT-3-09 | RN-110 a RN-119 | PRS-190 a PRS-199 | Q-110 a Q-119 | CA-05.m |
| M-10 | UT-3-10 | RN-120 a RN-129 | PRS-200 a PRS-209 | Q-120 a Q-129 | CA-10.m, CA-13.m |
| M-11 | UT-3-11 | RN-130 a RN-139 | PRS-210 a PRS-219 | Q-130 a Q-139 | CA-19.m |
| M-12 | UT-3-12 | RN-140 a RN-149 | PRS-300 a PRS-309 | Q-140 a Q-149 | CA-09.m, CA-12.m |
| M-13 | UT-3-13 | RN-150 a RN-159 | PRS-310 a PRS-319 | Q-150 a Q-159 | CA-16.m |
| M-14 | UT-3-14 | RN-160 a RN-169 | PRS-320 a PRS-329 | Q-160 a Q-169 | CA-14.m |

**Faixas para IDs novos na Fase 4 (UT-4):**

| UT | Âmbito | Q novos | PRS novos / correções | CA novos |
|---|---|---|---|---|
| UT-4-00 a UT-4-14 | Revisão cega por módulo (M-00 a M-14) | Q-200 a Q-349 (faixa de 10 por UT: `Q-(200 + 10*n)` a `Q-(209 + 10*n)`) | PRS-220 a PRS-249 | Seguem o RF do módulo |
| UT-4-90 | Consistência entre módulos | Q-350 a Q-359 | PRS-250 a PRS-254 | Não se aplica |
| UT-4-91 | Ensaio | Q-360 a Q-369 | PRS-255 a PRS-259 | CA-91.m |

**Faixas de PRS por UT fora da Fase 3** (a partir de UT-2-90; usados até agora: PRS-01 a PRS-60, PRS-61 a PRS-73, PRS-80 a PRS-84, PRS-86, PRS-90 a PRS-97):

| UT | PRS novos |
|---|---|
| UT-1-01, UT-1-90, UT-2-01, UT-2-90, UT-2-92 (ARQ: `10`, `30`, `00`) | PRS-61 a PRS-79 (usados: PRS-61 a PRS-69; desvio: PRS-70 a PRS-73 criados pela UT-2-91 na faixa do ARQ, aprovados por D-23) |
| UT-2-02, UT-2-91 (`40`, `10`) | PRS-80 a PRS-89 (usados: PRS-80 a PRS-84, PRS-86; livres: PRS-85, PRS-87 a PRS-89; desvio anterior: PRS-70 a PRS-73 na faixa do ARQ, aprovados por D-23) |
| UT-2-03, UT-2-93 (`70`) | PRS-97 a PRS-99 (usado: PRS-97) |
| UT-4 (revisão cega e consistência, correções por `CR-nn`) | PRS-220 a PRS-259 |
| UT-5 (integração e aceitação, correções por `CR-nn`) | PRS-260 a PRS-299 |

Os RN da v1.0 que ficam num módulo mantêm o ID (`90` §6). Códigos `ERR`, EP, EV, ENT e IF novos só por `CR-nn`.

## 5. Etiquetas de evidência

| Etiqueta | Significado |
|---|---|
| `[MEDIDO]` | Exercitado num ambiente real; cita a medição (`M1`, `R1`, `K1`) e o script de `evidencias/` |
| `[CÓDIGO]` | Lido no código-fonte (`ficheiro:linha`) |
| `[DOC]` | Lido em documentação externa a esta especificação (indicar a fonte) |
| `[DECISÃO D-nn]` | Decidido pelo PO; não verificável no sistema existente |
