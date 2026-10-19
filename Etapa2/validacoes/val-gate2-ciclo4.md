# Validação independente — Gates 1 e 2, ciclo 4 (2026-10-19)

> Veredito: **Gate 1 aceite**; **Gate 2 reprovado** (A-100, A-101, A-102). Ciclo 5 restrito a A-100..A-118 autorizado pelo PO (D-20). Decisões do PO: D-21 (A-101 opção b), D-22 (A-107), D-23 (PRS-70..73 e CR-16 aprovados).
> Referências `nn:linha` = `Etapa2/spec-v2/nn-*.md` na versão validada.

## Distribuição do ciclo 5
| UT | Dono | Ficheiros (exclusivos) | Achados |
|---|---|---|---|
| UT-2-91 | Subagente Claude (ARQ) | `10`, `40`, `70`, e no `90` só as linhas de §1 "Entradas para as UT-3", §2.2 e §6 (mapeamento) | A-100 (parte texto), A-101 (D-21), A-103, A-104, A-106 (parte `10`), A-107 (D-22), A-114, A-115, A-116, A-118 |
| UT-2-92 | Ipê | `00`, `evidencias/medir-pacotes.py` (+ `.out.txt`, mutações, `pacotes/`) | A-100 (parte script), A-106 (parte `00`), A-108, A-109, A-110 |
| UT-2-94 | Sabiá | `30`, `20`, `evidencias/medir-postgres.sh`, `matriz-isolamento.sql`, `pg-ddl.sql` (+ resultados) | A-102, A-117 (parte postgres) |
| UT-2-93 | Jatobá | `evidencias/env70/`, `evidencias/medir-env70.sh` (+ resultados) | A-117 (parte env70) |
| UT-2-95 | ORQ | `90` (resto) | A-105, A-111, A-112, A-113 (feitos) |

## Achados

### Altos (bloqueiam)
- **A-100 ⚠️** O fecho de referências é cego a secções de `00`/`60`/`80`/`90` e a `§x` sem prefixo (`medir-pacotes.py:157`, `REF_SECS` só aceita `10|20|30|40|70`). Referência real por resolver em todos os pacotes: `10:10` e `10:111` citam `` `00` §3 `` (precedência), visível em `pacotes/pacote-M-01.md:24` e `:125`. Mutações que passaram: `` `00` §9 ``/`` `90` §12 `` numa linha do `90` §1 (exit 0); `§9.9` sem prefixo em `40` §2 (exit 0). **Correção:** escrever as 6 linhas da precedência em `10` §1 e retirar a citação (sem fazer crescer o núcleo base em termos líquidos — ver A-110); alargar `REF_SECS` a `00`, `60`, `80`, `90`, com lista explícita de exceções; verificar `§x` sem prefixo dentro de `30`, `40`, `70`.
- **A-101 ⚠️** Os pacotes da Onda 3 dependem da v1.0, que não está em nenhum pacote: `90:183-217` ("RF-02, mesmos EX", "RN-11, RN-12 → M-02"), `90:29-34` ("RN-04 e RN-07 da v1.0", "Corrigir o CA-03.1 da v1.0"), `00:101`. Exemplos: **M-02** — na v1.0 o reuso grava `denylist:user` (`spec_identity_service.md:661`, RN-11); no pacote do M-02 só há a linha IF-04.2 sem gatilho (`pacote-M-02.md:391`); a regra está em `30:402` (§4), fora do pacote. **M-01** — formato do e-mail (RFC 5322) e `trim().ToLowerInvariant()` (`spec:729`) fora do pacote. **Decisão D-21 (opção b):** as UT-3 escrevem sem a v1.0; tudo o que dela continua válido passa a estar escrito nos pacotes; retirar "mesmos EX" e "da v1.0" de `90` §1 e §6.
- **A-102 ⚠️** A matriz não cobre `INSERT … RETURNING` em `tenant_member`/`member_role` num tenant só do conjunto de escrita (EP-06 por `TENANT_ADMIN`, D-04/IF-12.1). Mutação P1 (retirar `app.write_tenants` do `USING` de `tenant_member` em `pg-ddl.sql:139`) deu "28 OK, 0 FALHA", exit 0. **Correção:** casos com `tenant_member` e `member_role` em N, com `RETURNING`; atualizar `ESPERADO_OK` e `30` §5.1.

### Médios
- **A-103** CR-16 regra 1 violada em `40:95` (EP-01): "`email` texto (5 a 320, PRS-11)" não diz qual é a normalização. Escrever na frase: `trim()` e minúsculas invariantes antes da camada 3 (c) e da pesquisa.
- **A-104** D-19: não está dito se as falhas novas do EP-03 (RG-02, RG-11, RG-13 → CTX-003/CTX-004) revogam a família (`90:30`, `30:120`). Escrever a regra (linha UT-3-02 do `90` §1 ou linha EP-03).
- **A-105** PRS-70..73 e CR-16 sem Q-nn. *(Resolvido: Q-15 → D-23.)*
- **A-106** Faixas de PRS incoerentes: PRS-70..73 criados na faixa do ARQ (PRS-61..79) em vez da do UT-2-91 (PRS-80..89) (`00:169`); PRS-73 não aparece em `00:164`, `00:169`, `90:132`; faixa PRS-220..299 partilhada entre UT-4 e UT-5 sem divisão (`00:160-171`). Corrigir contadores e registar o desvio.
- **A-107** *(Decidido D-22)* `TENANT_ADMIN` não suspende vínculos de um dono nem os seus próprios; só `PLATFORM_ADMIN` suspende vínculos de dono. Aplicar em C9 (`40:224`) e IF-12.2.

### Baixos
- **A-108** A-72 por resolver: `00:42` diz "ciclo M-11 ↔ M-13 eliminado", mas `00:73` e `40:173-174` mantêm o M-11 consumidor da IF-11 (IF-11.1/11.2) e `00:37` só declara "M-00 (IF-08)".
- **A-109** "Pacote estimado" em `00:26-40` desatualizado face ao script (M-04 53.9 vs 59.9; M-06 56.1 vs 59.1; M-09 44.7 vs 59.6; M-12 54.4 vs 59.4).
- **A-110** M-13 mede 60.0 KiB, sem margem (+45 B no núcleo base fazem falhar). Reservar margem.
- **A-111** `90:340` "CR-nn: Nenhum". *(Resolvido.)*
- **A-112** Ciclos desatualizados em `90:4`, `90:9-15`, `30:3`, `20:3`. *(Resolvido no `90`; `30`/`20` pela Sabiá.)*
- **A-113** PRS-49 contraditório em `90:76`. *(Resolvido.)*
- **A-114** `70:129` diz "503 em todos os EP", mas EP-01 (palavra-passe errada) e EP-03 (refresh inválido) não assinam e respondem 401; EP-05 contado duas vezes.
- **A-115** `70:77` "`MigrateAsync()` (`10` §3.7)" → §3.11.
- **A-116** IF-11.3 passo (5): com a transação abortada, o `set_config` de reposição falha com 25P02; escrever "relançar a original e descartar a da reposição". O I27 testa reposição depois do commit, mas a ordem real é reposição e depois commit do chamador.
- **A-117** `medir-postgres.sh` só verifica a matriz (as medições M1–M9 do `20` EXT-01 não são verificadas; mutação P3 — remover `ck_user_account_locked_until` — deu exit 0). `medir-env70.sh:62` escreve a tabela de exceções a partir de texto fixo, mesmo com testes a falhar.
- **A-118** Lacunas para os autores: o que é "especial" no PRS-30; que `updated_at` devolve o EP-09 com `scope = GROUP`; que resposta dá a rotação concorrente (23505) no M-02 (o `70` §3 sugere AUTH-006, mas a regra não está escrita).

## O que está bem (não mexer)
Classificação de exceções medida pelo EF; leitura da chave antes da RG-06; IF-11.3 sem aninhamento e com reposição (I26); D-04/D-07/D-08/D-19 coerentes; posse de tabelas e consumidores das IF; tipos partilhados completos; pacotes extraídos iguais ao `00` §2.2; scripts da matriz e do env70 falham com mutações; catálogo sem órfãos; lint 0.
