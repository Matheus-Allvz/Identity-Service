# Avaliação Independente — Processo, Artefactos e Spec do Identity-Service

> Avaliador: subagente Claude independente (só leitura). Data: 2026-10-18.
> Spec avaliada: `spec_identity_service.md` v1.0-baseline (SHA-256 `4d7adbb7…`).
> Legenda: **[F]** = facto verificado no texto; **[O]** = opinião/juízo técnico. Referências `spec:N` = linha N da spec.

## Notas
| Entregável | Nota | Justificação |
|---|---|---|
| E1 Processo | 7,5 | Fases, gates, RACI e métrica bem montados; validade de construto da métrica, G1 a produzir distribuição bimodal incompatível com t-test, IA final = 0 por definição, referências cruzadas erradas. |
| E2 Template | 7,0 | Muito completo; faltam esquema tabular de respostas, ordem de avaliação, matriz de autorização e contrato da harness; hash circular. |
| E3 Guia + DoR | 7,0 | Bons exemplos; exemplo RN-03 viola as próprias regras; lista de proibidos ≠ lint do E1; DoR exige atividades que o E1 põe na Fase 5. |
| Spec | 6,0 | Detalhe e rastreabilidade formal excelentes; 1 CA objetivamente errado, ambiguidades de ordem/status, fixtures incompletas, áreas sem CA. |
| Registo de ambiguidade | 4,0 | Afirma resoluções ausentes na baseline; RI não independente; sem re-revisão da v1.0; lint ≠ processo; redefine DoR/limiar. |

## Problemas — Críticos

**1. CA `@critico` impossível de passar.** [F] `spec:2151` (CA-03.1) espera `["grupo:relatorio_consolidado","membro:listar","tenant:criar","tenant:editar"]`; o seed de `GROUP_MANAGER` (`spec:674`) é `["tenant:criar","tenant:listar","membro:vincular","membro:listar","usuario:suspender","mapa:visualizar"]`; `grupo:relatorio_consolidado` e `tenant:editar` não existem em mais lado nenhum. Pela RN-09 (`1938`) e RF-03 passo 4 (`1687`), a interseção correta (owner em T20 + GROUP_MANAGER em T21) = as 6 permissões do seed, ordenadas. Impacto: G2 falha em 100 % das execuções. **Correção:** corrigir CA → v1.1-baseline e novo hash; validar todos os "Então" literais contra seed/RN.

**2. Registo declara resolvidas correções inexistentes na baseline.** [F]
- DEF-06 (`registo:126`): `available_contexts` ordenado `group_name ASC, legal_name ASC, tenant_id ASC` e `available_scopes` ASC — a spec só ordena `roles` (`742`, `1618`).
- DEF-05 (`registo:125`): eventos sem JWT usam `acting_scope = "NONE"` — a spec usa `"CONTROL_PLANE"` (`1449–1451`).
- DEF-07 (`registo:127`): propagação `actor_user_id = act.sub` / `ticket_id = act.ticket_id` em toda a auditoria — inexistente.
- DEF-02 (`registo:122`): "Argon2id dummy" para utilizador inexistente e verificação de denylist no login — inexistentes (RF-01 passo 3 só verifica `SUSPENDED`/`LOCKED`).
- DEF-01: "interseção de permissions não vazia" como condição de sucesso — não está na RN-09 (`1938`); só sugerido em PRS-05 (`2555`), sem código de erro.
**Correção:** reconciliar item a item com citação de linha; re-executar 4.2–4.5 às cegas sobre a versão congelada e reportar IA residual.

**3. Gate 4 não cumprido segundo o E1; baseline ≠ payload enviado.** [F] E1 exige DoR completa (`E1:251`); grupo E (`E3:327–331`) exige suite, protocolo e remoção de §14/Anexo A **antes** da geração. O registo reinterpreta E como pós-geração (`registo:136,170–174`) e introduz "C_w ≥ 95 %, C_RF = 100 %, H = 0", contradizendo o limiar de 90 %. A baseline com hash contém §14 e Anexo A (`spec:2596–2626`). **Correção:** congelar dois artefactos com hash (baseline completa + payload LLM); remover critérios inventados.

## Problemas — Altos

**4. Validade de construto.** [F] RNF-01/03 dizem verificar-se "nos testes de aceitação" (`258`, `260`) mas nenhum CA verifica prefixo argon2, `exp−iat` ou `kid`. Sem CA: RLS (e não há DDL `ENABLE/FORCE ROW LEVEL SECURITY`), Outbox → `PUBLISHED` (RNF-05), `X-Correlation-Id`, `metadata` da auditoria, `KafkaConsumerFilter`, payloads EV-01..04 (só "context_jws preenchido", `2348`), reativação (RF-09/10 "suspender ou reativar", nenhum CA com `"status":"ACTIVE"`), RNF-04. **Correção:** acrescentar CA ou restringir a alegação.

**5. Estatística.** (tratado no E1 pelo autor do processo.)

**6. Protocolo de geração indefinido.** [F] Ferramenta "por exemplo, API, Claude Code" (`E1:270`). Migração: `dotnet ef database update` (`spec:2589`) exige dotnet-ef, não listado; §5.5 aceita "inicializador no arranque" (`667`). §13 não pede Dockerfile/compose mas G1 exige "arranca". [O] Saída ~80–150k tokens.

**7. Validação sem independência.** [F] Mesmo subagente fez verificação estrutural, RI e LLM-as-reviewer (`registo:17,82,98`); autor = ARQ (`spec:11–12`); modelo do revisor não registado (E1 4.4 exige o mesmo modelo do gerador).

**8. Status em falta (middleware).** [F] ADR-06 (`158`) manda o `AuthContextMiddleware` verificar `denylist:user/tenant` em todas as rotas autenticadas → 403 `ERR-AUTH-004`/`ERR-CTX-002` (RN-04/07); EP-04 (`988–991`, só 204/401) e EP-06..09 não listam esses 403.

**9. Ordem de avaliação.** [F] RN-18 (`1947`) avalia autorização **antes** do 404; RF-10 passo 1 (`1887`) verifica existência **antes**. Em `MACRO_GROUP`, código indefinido quando tenants falham regras diferentes (`1682–1686` vs `1696–1698`). CA-03.3: tenant `…022` sem `TENANT_ADMIN` ativo declarado (`2139`) → pela ordem, CTX-003 e não CTX-004. `target_tenant_id` inexistente em SINGLE (`1648`)/MACRO sem código. **Correção:** tabela "ordem de avaliação" por EP.

## Problemas — Médios

**10. Ambiguidades residuais.** [F] "Minimal APIs ou Controllers" (`111`); "primeira migração (ou inicializador…)" (`667`); `target_group_id` em SINGLE opcional/ignorado (`822`); `target_tenants` vazio/>50: tabela sugere 400, EX/RN dizem 403 (`823` vs `845`, `1696`); `legal_name` com 1 carácter (`1119`, `1141`, `2568`); `scopes`/`role_ids` duplicados ou >10 (`1123`, `1204`, `1221`); `target_scope` fora do enum em EP-02/EP-05; `kid` quando `JWT_RSA_PRIVATE_KEY_PEM` é fornecida (`253`); formato do `jti` (`685`); `reason` do EV-03 no bloqueio por login (`1552`); `user_id`/`actor_user_id` da auditoria no EP-08 (`1309` vs `1374`); `impersonation_context` sem `roles`/`permissions` apesar da §6.1 (`1066`, `685`); interseção vazia em MACRO (`2555`); refresh "permissões atualizadas": recalcula ou copia? (`1722`); falha do Redis após commit (`882`, `1313`); 204 com exemplo `{}` (`993`); EP-09 `ACTIVE` sobre utilizador `LOCKED` e transições idempotentes não constam do ENUM-04 (`338–342` vs `1372`).

**11. Tipos e modelo.** [F] Headers EV-01/02 "UUID" mas admitem `""` (`1463`, `1506` vs `1447–1448`); `acting_scope = "CONTROL_PLANE"` fora do ENUM-03 (ADR-03, `155`); ENT-06 invariante "não vazio + regex" não está no CHECK e default `'[]'` (`537` vs `545`); ENT-09 "append-only" com FK `ON DELETE SET NULL` (`608`, `615–618`); tabela com 8 colunas no cabeçalho e 7 no separador (`1445–1446`); RLS sem `ENABLE/FORCE` nem papel de BD dedicado.

**12. Fixtures/testabilidade.** [F] Contexto RF-04 cria refresh token com `active_tenant_id …020` sem criar tenant/grupo/vínculo (FK `ENT-08:590`; passo 5 revalida tenant) (`2199–2201`); CA-04.4 título "inexistente, REVOKED ou expirado" mas só testa inexistente (`2235`); CA-11.3 exige "provedor de chaves indisponível" (`2518`) sem mecanismo de injeção; Contextos com "JWT com jti fixo" (`2066`) sem contrato da harness para cunhar tokens; CA-06.5 junta 4 EX numa unidade binária (`2318`).

**13. Fidelidade à Ideia Base.** [F] Invenções sem PRS: política de palavra-passe 12–128, 423 com 5 tentativas/900 s, `MAX_MACRO = 50`, refresh de 7 dias, regex de `ticket_id`, `geo_lat_lng POINT` → 2 × NUMERIC, EP-06..09, nomes EV-01..03, TTL `denylist:tenant` = 86400 s (Ideia Base `:180`: "TTL igual ao tempo restante do JWT"). Omissões: criação de `user_account`/`economic_group` não existe nem está em "Fora do âmbito" (`49–58`). [O] TENANT_ADMIN cria tenant mas não fica membro; TENANT_ADMIN pode suspender globalmente utilizador de outro grupo (`1323`); login devolve 403 SUSPENDED antes de validar palavra-passe (`1615`) → enumeração.

**15. IA.** [F/O] IA final = 0 por construção; N cresce com CA; IA inicial calculado com N final (`registo:49`); 33 defeitos atómicos não listados individualmente.

**16. Pins de versão.** [O] `JwtBearer@10.0.0` + `Microsoft.IdentityModel.Tokens@8.3.0` → possível NU1605; confirmar `EFCore.NamingConventions@10.0.0`. **Correção:** `dotnet restore` de um esqueleto com os pins antes do congelamento.

## Problemas — Baixos
**17.** Tamanho declarado no registo (214 679 bytes) ≠ real (214 739).
**18.** RN-05 lista RF-11 e EP-10 lista RN-05, embora o JWKS seja público (`1390`, `1934`); wrappers `context` vs `impersonation_context`.

## Problemas dos entregáveis da Etapa 1 (E1/E2/E3)
Lint do E1 ≠ lista do guia; `E1:274` remete para §7.5 (está em §7.7); hash circular no template; exemplo RN-03 do guia com duas consequências e tipo "Segurança" fora da lista; DoR C2 vs E1 sobre o negativo de autorização; guia atribui §12 à Fase 3 e o E1 não; desenho estatístico (G1 bimodal, Wilcoxon vs média, n por potência, grupo de controlo, kappa). → Tratados pelo autor do processo (Claude Code).
