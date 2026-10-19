# Validação independente — Gates 1 e 2, ciclo 3 (2026-10-19)

> Validador: subagente Claude novo, sem acesso aos relatos dos autores. Veredito: **Gate 1 e Gate 2 reprovados**. Dead letter → ciclo 4 excecional autorizado pelo PO (D-18). A-64 decidido pelo PO (D-19).
> Referências `nn:linha` = ficheiro `Etapa2/spec-v2/nn-*.md` na versão validada.

## Distribuição do ciclo 4 (D-18)
| UT | Dono | Ficheiros (exclusivos) | Achados |
|---|---|---|---|
| UT-2-91 | Subagente Claude | `10`, `40`, `70` | A-60, A-61, A-64, A-65, A-70, A-71, A-75 (parte `10`), A-76, A-77, A-78, A-79 (parte `40`) |
| UT-2-92 | Ipê | `00`, `evidencias/medir-pacotes.py` (+ `.out.txt`) | A-62, A-63, A-67, A-72 |
| UT-2-93 | Jatobá | `evidencias/env70/`, script novo `evidencias/medir-env70.sh` (+ resultados) | A-66, medição de A-61 através do EF Core |
| UT-2-94 | Sabiá (depois da UT-2-91) | `30`, `20`, `evidencias/medir-postgres.sh`, `evidencias/matriz-isolamento.sql` (+ `.out.txt`) | A-60 (casos da matriz), A-68, A-69, A-75 (parte `30`), A-79 (parte `30`) |
| UT-2-95 | ORQ | `90` | A-73, A-74, A-80 |

## Achados

### Altos
- **A-60 ⚠️** "bypass só nesta operação" de IF-09.7 e IF-12.2 sem mecanismo e em contradição com o núcleo: `40:157`, `40:161` exigem bypass só nessas operações; a IF-11 (`40:123`) não tem operação para isso; a camada 4 só define variáveis na abertura da ligação (`10:247`); a camada 3 rejeita `TenantId ∉ WriteTenants` (`10:246`); o núcleo diz que só `AuthorizeTenantWrite` define o conjunto de escrita e "nenhum outro endpoint o chama" (`10:89`, `10:235`), mas o EP-09 com `TENANT_ADMIN` escreve em vários tenants. A matriz I19 liga `auth_flow=true` para a sessão inteira e não testa o UPDATE cruzado do EP-09 nem a reposição. Risco: a ligação do pedido ficar com `auth_flow='true'` (fuga de isolamento). **Correção:** operação da IF-11 (dono M-13), p.ex. `ExecuteWithBypassAsync`, que trata camadas 2, 3 e 4 e repõe a variável; reconciliar `10` §3.4/§3.9; casos novos na matriz (UPDATE cruzado e reposição).
- **A-61 ⚠️** classificação 503/403/409 não implementável (`40:20`, `70:25` C6, `10:178`): (1) o EF Core embrulha 42501/23505 em `DbUpdateException` — percorrer `InnerException`; (2) com o PostgreSQL parado, a estratégia por omissão do Npgsql EF lança `InvalidOperationException` ("An exception has been raised that is likely due to a transient failure.") → hoje cai em 500; (3) `RedisTimeoutException` deriva de `System.TimeoutException`, não de `RedisException` (verificado por reflexão na 2.13.17); (4) a medição L05 usa Npgsql direto sem EF (`env70/src/IdentityService/Program.cs:16-21`), e não está medido se uma exceção dentro do `IssuerSigningKeyResolver` sai do `ValidateTokenAsync` ou vira 401. **Correção:** classificar pela cadeia de `InnerException` com tipos exatos; ler `ISigningKey` explicitamente antes de validar; repetir a medição através do EF.
- **A-62 ⚠️** regras em falta nos pacotes: os PRS de um só módulo vivem no `90` §2.2, que não entra em nenhum pacote (`00:78`). M-01 sem PRS-30 (composição da palavra-passe; `40:84` só diz 12–128) e sem os 900 s do PRS-31; M-14 sem o lote de 50 do PRS-43; M-04 sem a rejeição de dígitos iguais do PRS-37. **Correção:** pôr as linhas do `90` §2.2 de cada módulo no pacote e no dicionário `P` do script.
- **A-63 ⚠️** o script de fecho não cobre o que o E1 exige: o `IDRE` só cobre ENUM, ENT, IF, RG, ERR e secções do `10` (`medir-pacotes.py:126-128`). Mutação: injetar PRS-999, EP-99, `` `30` §9 `` e `` `70` §9 `` dá OK. Referências por resolver encontradas: `` `40` §1.1 `` (citado no núcleo base, só no pacote do M-00), `` `70` §4 `` (falta em M-00, M-07, M-11, M-14), `` `30` §5 `` (falta em quase todos). **Correção:** alargar a PRS, EP, EV, EXT, L, AUD e às secções de todos os ficheiros, com lista explícita das citações só-etiqueta.
- **A-64 ⚠️** EP-03: `40:86` só lista AUTH-005/006/004 e CTX-002, mas o `90:25` e IF-09.4/9.5 mandam revalidar RG-03, RG-02, RG-11 e RG-13 (geram CTX-001/003/004). **Decidido pelo PO (D-19): revalida, e os erros passam a constar do EP-03.**
- **A-65 ⚠️** tipos partilhados incompletos: IF-08.1 (`40:149`) lista `Jti`, `Iat`, `Roles`, `ActiveScope`, `ActorUserId`… sem tipo C# (o `10` §3.5 só dá tipos JSON); o `IdentityDbContext` expõe `IAuthContext` (`40:178`) mas o pacote do M-11 (`00:95`) não tem a IF-08 e o `00:37` não declara a dependência.

### Médios
- **A-66 ⚠️** `[MEDIDO]` sem ficheiro de resultado (P11): o `env70` não tem `.out`/`.trx`; citações em `70:4`, `70:25`, `70:128`, `70:154-157`, `20:35`, `20:57`, `20:77`, `20:115-118`. Os testes não falham (o `L05_FalhasViaWaf` só faz `WriteLine`; o `TimeoutXunit` falha de propósito). **Correção:** script em `evidencias/` com `dotnet test --logger trx`, resultado versionado e asserções verdadeiras.
- **A-67 ⚠️** sem faixa de Q-nn no `00` (E1:130, :220 pedem PRS, RN, CA, Q, ERR, IF); 15 UT-3 em paralelo vão colidir nos Q-nn.
- **A-68** `medir-postgres.sh:14-17` sai sempre com 0: mutação M1 (política sem `write_tenants`) dá "20 OK, 1 FALHA", exit 0; mutação M2 (erro a meio) dá "16 OK, 0 FALHA", exit 0. **Correção:** exit ≠ 0 se houver FALHA ou se o número de OK ≠ esperado.
- **A-69** ENUM-05 (`30:120-121`) sem a transição `ACTIVE/CONSUMED → REVOKED` pelo RF-01 (D-09, IF-01.4 em `40:131`).
- **A-70** EP-02 sem resposta para família sem linha `ACTIVE` (IF-01.2 pode devolver `false`, `40:129`; `40:85` omisso). Cenário real depois do PRS-68.
- **A-71** ordem entre "chave indisponível" e RG-06 por fixar: `40:46` diz 503 "antes de qualquer outra verificação", mas o `IssuerSigningKeyResolver` só corre com token bem formado. Pedido sem token com a chave indisponível: 503 ou 401?

### Baixos
- **A-72** ciclo M-11 ↔ M-13 contra o "sem ciclos" de `00:42` (IF-11.1 usa o `IdentityDbContext` do M-11; a ordem topológica põe o M-13 antes).
- **A-73** `90:325` "CR-nn: Nenhum" contra CR-01..CR-15. *(resolvido pelo ORQ)*
- **A-74** ciclos incoerentes no registo (`90:4`, `90:13-15`). *(resolvido pelo ORQ)*
- **A-75** PRS-49 desatualizado ("Nenhum EP muda esses estados", `90:71`, `10:293`), contrariado por D-07 (`30:66-67`).
- **A-76** contradições no `70`: `70:151` "fora do repositório" vs `70:4` (env70); `70:159` PRS "Pendente" vs `90` "Aprovado"; `70:5` "nunca num pacote de módulo" vs `70:9` (§2 no pacote do M-11).
- **A-77** "423" residual em `40:50` apesar de D-08.
- **A-78** falta dizer se o login de conta `LOCKED` executa o Argon2id (`40:84` só o fixa para e-mail inexistente); sem isso o tempo de resposta revela o bloqueio (contra D-08).
- **A-79** `30:402` não restringe ao `PLATFORM_ADMIN` a escrita de `denylist:user` pelo M-10 (`40:92` está correto).
- **A-80** CR-15 aplicado antes da aprovação do PO. *(resolvido por D-15)*

## O que está bem (não mexer)
D-04, D-07 e D-08 bem aplicados; posse de tabelas coerente entre `00` §2, `30` §1.1 e `40` §4.3; consumidores das IF iguais em `00` e `40`; cada RF num só módulo; catálogo de erros sem órfãos; tabela SQLSTATE, validação em camadas e checklist R1–R10; ordem Redis/commit; D-01/D-02 `[REMOVIDO]`; CR-01..15 sequenciais; C3 (`TimeProvider`) existe na 8.23.0; concorrência com `Barrier` implementável. Lint 0 nos 7 ficheiros. Pacotes todos ≤ 60 KiB (maior: M-00 59,8).
