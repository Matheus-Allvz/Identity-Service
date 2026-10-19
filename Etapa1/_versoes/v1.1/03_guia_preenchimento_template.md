# Entregável 3 — Guia de Preenchimento do Template de Requisitos para LLM

> **Versão:** 1.1 · **Aplica-se a:** `template_requisitos_llm.md` v1.1
> **Público:** Analista de Requisitos, Arquiteto, Programador que especifica para uma LLM.
> A numeração das secções do guia (§G-n) corresponde à secção *n* do template.

---

## 1. Como usar este guia

1. Copie o template para `spec_<componente>.md`.
2. Preencha as secções **pela ordem das fases do processo** (Entregável 1), e não pela ordem do documento:
   - Fase 1: §0, §1, §2, §3, §4
   - Fase 2: §5, §6 (incluindo a ordem de avaliação de cada EP e a matriz de autorização §6.4), §9
   - Fase 3: §7, §8, §10 (incluindo o contrato do ambiente de teste §10.1), §11, §12
   - Fase 4: §13, §14 e os grupos A a D da Definition of Ready (§5 deste guia)
   - Fase 5, antes de gerar: grupo E da Definition of Ready
3. Cada `<<placeholder>>` deve ser substituído ou a linha removida. **Nunca** deixe uma célula vazia: escreva "Nenhum" ou "Não se aplica", que são afirmações. Uma célula vazia é uma lacuna.
4. Antes de enviar o documento à LLM, gere o **payload**: uma cópia da baseline sem comentários `<!-- -->`, sem a §14 e sem o Anexo A. A baseline e o payload têm ambos hash, registado num ficheiro `.sha256` à parte.

---

## 2. Boas práticas de escrita anti-alucinação

### 2.1 As dez regras de ouro

| # | Regra | Porquê |
|---|---|---|
| 1 | **Valores concretos em vez de adjetivos.** | A LLM converte "curto" num número qualquer, possivelmente diferente em cada execução. |
| 2 | **Um termo, um nome**, igual no glossário, na BD, no JSON e no código. | Sinónimos levam a LLM a criar duas entidades ou dois campos. |
| 3 | **Listas fechadas.** Enumere todos os valores; nunca escreva "etc." nem "entre outros". | "Etc." autoriza a LLM a inventar valores. |
| 4 | **Todos os status HTTP e todos os erros explícitos.** | Os status omitidos são preenchidos com "o mais comum", que muitas vezes está errado (por exemplo, 400 em vez de 422). |
| 5 | **Uma regra por ID.** Se contém "e" entre duas condições independentes, divida-a. | Permite medir cada regra e dar-lhe um CA próprio. |
| 6 | **Forma canónica SE/ENTÃO/SENÃO** nas RN. | Elimina a ambiguidade de âmbito ("só quando…", "exceto se…"). |
| 7 | **Diga o que NÃO fazer** quando a tentação de inferir é alta (logs, campos de resposta, efeitos colaterais em falha). | As LLM tendem a "ser úteis" e acrescentam comportamento. |
| 8 | **Exemplos JSON coerentes com as tabelas.** Mesmos nomes, tipos e formatos. | Perante uma contradição, a LLM escolhe ao acaso. |
| 9 | **Ordem dos efeitos colaterais e transacionalidade explícitas.** | Sem isto, persistir antes de validar ou publicar o evento fora da transação são erros recorrentes. |
| 10 | **Referências cruzadas por ID**, nunca por posição ("ver acima"). | A LLM perde a referência posicional em contextos longos. |

### 2.2 O que fazer *vs.* o que nunca escrever

| ❌ Nunca escrever | ✅ Escrever |
|---|---|
| "O token deve expirar em pouco tempo." | "O `access_token` **DEVE** ter `exp = iat + 900` segundos." |
| "A senha deve ser segura." | "SE `password` tiver menos de 12 caracteres OU não contiver pelo menos 1 letra e 1 dígito ENTÃO 422 `ERR-VAL-003`." |
| "Validar os dados de entrada." | Tabela de campos com tipo, obrigatoriedade, formato (regex), mínimo/máximo e o erro de cada um. |
| "Retornar erro se não autorizado." | "SE o JWT estiver ausente ou com assinatura inválida ENTÃO 401 `ERR-AUTH-001`. SE válido mas sem `tenant:admin` ENTÃO 403 `ERR-AUTH-010`." |
| "Usar cache para melhorar o desempenho." | "Gravar `denylist:jti:{jti}` = `1` com TTL = `exp − now` segundos." |
| "Escopos: BUYER, SUPPLIER, etc." | "`ENUM-01 SCOPE` = {`BUYER`, `SUPPLIER`, `CARRIER`, `TENANT_ADMIN`}. Nenhum outro valor é válido." |
| "O sistema trata o refresh token." | Fluxo numerado com cada passo e o estado do token em cada passo (`ACTIVE` → `CONSUMED`). |
| "Se necessário, registar auditoria." | "Toda a resposta 401 de `EP-01` **DEVE** inserir 1 linha em `auth_audit_log` com `event_type = 'LOGIN_FAILED'`." |
| "Seguir boas práticas de segurança." | RNF concreto: algoritmo, parâmetros, cabeçalhos, o que nunca se regista em log. |
| "Similar ao endpoint de login." | Especificação completa do endpoint. Copiar é mais barato do que a LLM inferir. |
| "O usuário/cliente/empresa…" (alternando) | O termo canónico do glossário, sempre o mesmo: `User`, `Tenant`. |

### 2.3 Termos proibidos (lista oficial; o lint da Fase 4.1 deriva desta lista)

`adequado` · `apropriado` · `rápido` · `eficiente` · `seguro` (sem especificação) · `robusto` · `flexível` · `intuitivo` · `amigável` · `normalmente` · `geralmente` · `quando aplicável` · `se necessário` · `etc.` · `entre outros` · `e/ou` · `...` · `similar a` · `tratar` (sem objeto concreto) · `gerir` (sem operações listadas) · `suportar` (sem limite numérico) · `TBD` · `TODO` · `a definir`

### 2.4 Linguagem normativa

| Palavra | Significado | Exemplo |
|---|---|---|
| **DEVE** | Obrigatório. A falha conta para a métrica. | "O endpoint **DEVE** responder 201." |
| **NÃO DEVE** | Proibido. | "A resposta **NÃO DEVE** conter `password_hash`." |
| **DEVERIA** | Recomendado. Use com parcimónia, porque gera variação entre execuções. | — |
| **PODE** | Opcional. A ausência não é falha. | "O cliente **PODE** enviar `X-Correlation-Id`." |

> Evite "deverá", "terá de", "precisa": a LLM não sabe se são obrigatórios. Use apenas as palavras desta tabela.

---

## 3. Preenchimento secção a secção

### §G-0 Metadados
- **Modelo LLM alvo:** versão exata. Muda a reprodutibilidade.
- **Hash:** nunca é escrito dentro do próprio ficheiro, porque isso mudaria o hash. Fica num ficheiro `.sha256` à parte, criado no congelamento (Fase 4.10), com uma linha para a baseline e outra para o payload. Depois do hash, qualquer alteração gera uma nova versão.
- **Aprovadores:** só se regista uma aprovação que de facto aconteceu, com data.

### §G-1 Instruções para a LLM
- **Não altere o texto padrão.** Ele implementa o princípio do mundo fechado e a regra de precedência.
- Ajuste apenas a precedência se o projeto o exigir, e documente essa alteração num `PRS-nn`.

### §G-2 Contexto
- **Âmbito:** a coluna "Fora do âmbito" é tão importante como a coluna "Dentro". Sem ela, a LLM implementa os componentes vizinhos.

  **Exemplo correto:**
  | Dentro | Fora |
  |---|---|
  | Emitir e renovar JWT de acesso | API Gateway e a sua validação na borda |
  | Publicar `usuario_bloqueado` no Kafka | Consumidores desse evento noutros serviços |
  | Gravar entradas na denylist Redis | Interface web de login |

- **Glossário:** a coluna "Sinónimos proibidos" impede que a LLM crie `Company` ao lado de `Tenant`.

  | Termo | Nome no código | Definição | Sinónimos proibidos |
  |---|---|---|---|
  | Tenant | `Tenant` | Pessoa jurídica identificada por um CNPJ único na plataforma | Empresa, Company, Client, Organization |
  | Grupo Económico | `EconomicGroup` | Conjunto de 1..N Tenants sob a mesma holding | Account, Holding |

### §G-3 Stack
- Versão mínima `MAJOR.MINOR`. Para bibliotecas de segurança (JWT, hashing), use a versão exata `x.y.z`.
- A lista de dependências permitidas é **exaustiva**. Se a LLM precisar de algo que não está na lista, isso indica uma lacuna da especificação.

  | Camada | Tecnologia | Versão |
  |---|---|---|
  | Runtime | .NET | 9.0 |
  | ORM | Entity Framework Core + Npgsql | 9.0.x |
  | BD | PostgreSQL | 17 |

### §G-4 Restrições Arquiteturais e RNF
- **ADR sem "consequência no código" não serve para a LLM.** Traduza a decisão numa obrigação verificável.

  | ID | Decisão | Consequência obrigatória no código |
  |---|---|---|
  | ADR-06 | Validação stateless de JWT + revogação via Redis | O serviço **DEVE** expor `GET /.well-known/jwks.json`. Todo o logout **DEVE** gravar `denylist:jti:{jti}` com TTL = tempo restante do token. |

- **RNF:** obrigatoriamente com um número e um método.

  | ID | Requisito | Limiar | Verificação |
  |---|---|---|---|
  | RNF-02 | Hash de senha | Argon2id, m=19456 KiB, t=2, p=1 | Inspeção do código + teste que verifica o prefixo `$argon2id$` |
  | RNF-04 | Latência de `EP-01` | p95 ≤ 300 ms com 50 req/s | Teste de carga k6, 5 min |

- **§4.4 Transversais:** diga o que **NÃO** se regista em log (passwords, tokens, hashes, refresh tokens).

### §G-5 Modelo de Dados
- Use **tipos físicos do SGBD** (`VARCHAR(320)`, `TIMESTAMPTZ`, `UUID`), não tipos conceptuais ("texto", "data").
- Declare `ON DELETE` em toda a FK. A omissão é uma das maiores fontes de divergência entre execuções.
- Indique a coluna de isolamento multi-tenant de cada entidade. É ela que ativa o filtro global e o RLS.
- Máquinas de estado: liste apenas as transições **permitidas**. Tudo o resto é proibido por definição.

  **Exemplo correto (excerto):**
  #### ENT-05 `refresh_token`
  **Invariantes:** em cada `family_id`, no máximo 1 registo com `status = 'ACTIVE'`.

  | Coluna | Tipo | Nulo | Default | Chave | Restrições | Descrição |
  |---|---|---|---|---|---|---|
  | `id` | UUID | NÃO | – | PK | – | Identificador |
  | `family_id` | UUID | NÃO | – | – | – | Família criada no login |
  | `token_hash` | CHAR(64) | NÃO | – | UQ | SHA-256 hex, minúsculas | O token em claro NÃO é persistido |
  | `status` | VARCHAR(16) | NÃO | `'ACTIVE'` | – | CHECK IN (`ACTIVE`,`CONSUMED`,`REVOKED`) | ENUM-04 |
  | `expires_at` | TIMESTAMPTZ | NÃO | – | – | > `created_at` | – |

  | De | Para | Gatilho |
  |---|---|---|
  | `ACTIVE` | `CONSUMED` | RF-04 passo 4 |
  | `ACTIVE` | `REVOKED` | RN-09 (reuso), RF-05 (logout) |

- **§5.4 Cache:** padrão de chave, tipo, valor, TTL e quem lê ou escreve. "Usar Redis" não chega.

  | Chave | Tipo | Valor | TTL | Escrito por | Lido por |
  |---|---|---|---|---|---|
  | `denylist:user:{user_id}` | String | Unix timestamp (s) do bloqueio | Duração máxima do access token (900 s) | RF-07 | RN-11 |

### §G-6 Contratos
- **Todas** as respostas possíveis entram na tabela de status. Na dúvida, consulte as RN e os EX: cada uma produz uma linha.
- Os campos de request têm validação completa: tipo, obrigatoriedade, formato, limites e enum.
- A ordem dos efeitos colaterais é numerada.

  **Exemplo correto (excerto de EP):**
  #### EP-04 `POST /api/v1/auth/refresh`
  | Campo | Tipo | Obrig. | Validação |
  |---|---|---|---|
  | `refresh_token` | string | Sim | 43 caracteres, base64url sem padding |

  | Status | Condição | Código |
  |---|---|---|
  | 200 | Token `ACTIVE` e não expirado | – |
  | 400 | `refresh_token` ausente ou com formato inválido | `ERR-VAL-001` |
  | 401 | Hash não encontrado ou `expires_at` ≤ agora | `ERR-AUTH-005` |
  | 401 | Token com `status = 'CONSUMED'` (reuso) | `ERR-AUTH-006` |

  **Efeitos colaterais (200):** 1) `UPDATE refresh_token SET status='CONSUMED'` no token apresentado; 2) `INSERT` de um novo token `ACTIVE` na mesma `family_id`; 3) os passos 1 e 2 ocorrem na mesma transação.

- **Ordem de avaliação:** é a tabela que mais divergências evita. Quando um pedido viola duas regras ao mesmo tempo, só a ordem diz qual erro sai. Comece sempre por formato → autenticação → autorização → existência → regras de negócio, e ajuste só com justificação. Em listas (por exemplo, `target_tenants`), diga se cada verificação corre sobre a lista toda antes da seguinte.

  | # | Verificação | Se falhar |
  |---|---|---|
  | 1 | `refresh_token` presente e com 43 caracteres base64url | 400 `ERR-VAL-001` |
  | 2 | Hash encontrado | 401 `ERR-AUTH-005` |
  | 3 | `status` ≠ `CONSUMED` | 401 `ERR-AUTH-006` |
  | 4 | `status` = `ACTIVE` e `expires_at` > agora | 401 `ERR-AUTH-005` |

- **Rejeições transversais:** se o middleware rejeita utilizadores ou tenants bloqueados em todas as rotas autenticadas, o 403 correspondente aparece na tabela de respostas de **cada** endpoint autenticado.
- **Corpo da resposta:** tabela campo a campo, com a regra de cálculo e a **ordenação** de cada lista. Uma lista sem ordenação declarada é uma lista com ordem aleatória, e um CA que compare listas vai falhar.
- **Valores dentro do tipo:** um header declarado UUID nunca leva `""`, e um campo de enum nunca leva um valor que não esteja no enum. Se um evento não tem tenant, declare o campo como opcional ou acrescente o valor ao enum.
- **Matriz de autorização (§6.4):** uma linha por endpoint e uma coluna por escopo ou papel. É o resumo que a LLM usa para gerar as políticas de autorização; tem de bater certo com as RN de autorização.
- **Eventos:** os headers de contexto são obrigatórios e listados um a um (`tenant_id`, `group_id`, `initiator_user_id`, `acting_scope`, `correlation_id`, `context_jws`).

### §G-7 Requisitos Funcionais
- Título no formato **verbo + objeto** ("Renovar sessão por refresh token").
- O fluxo principal é feito de **passos numerados e observáveis**. Cada passo que pode falhar tem uma linha em "Fluxos de exceção".
- A prioridade MoSCoW define o peso na métrica: `MUST` = 3, `SHOULD` = 2, `COULD` = 1. Atribua-a com honestidade, porque inflacionar `MUST` dilui a métrica.

  **Exemplo correto (fluxo de exceção):**
  | ID | Passo | Condição | Resposta | Efeitos |
  |---|---|---|---|---|
  | EX-04.2 | 3 | Token apresentado com `status = 'CONSUMED'` | 401 `ERR-AUTH-006` | Todos os tokens da `family_id` passam a `REVOKED`; grava `denylist:user:{user_id}`; publica `EV-02 sessao_comprometida` |

### §G-8 Regras de Negócio
- Forma canónica obrigatória. Parâmetros com nome, valor e unidade.
- Uma RN = um comportamento. Se há duas consequências independentes, faça duas RN.

  | ID | Tipo | Regra | Parâmetros | Erro |
  |---|---|---|---|---|
  | RN-03 | Temporal | **SE** um `user_id` acumular `MAX_FAILED` falhas de login consecutivas **ENTÃO** `user.locked_until = now + LOCK_DURATION` | `MAX_FAILED = 5`; `LOCK_DURATION = 900 s` | – |
  | RN-04 | Autorização | **SE** `user.locked_until` > agora **ENTÃO** `EP-01` responde 423 sem verificar a palavra-passe **SENÃO** segue para a verificação da palavra-passe | – | `ERR-AUTH-003` |
  | RN-05 | Integridade | **SE** o login for bem-sucedido **ENTÃO** `user.failed_logins = 0` | – | – |
  | RN-12 | Autorização | **SE** `target_tenant_id` ∉ tenants em que o utilizador é `TENANT_MEMBER` **ENTÃO** 403 | – | `ERR-CTX-002` |

  **Errado:** "RN-03: bloquear o utilizador após várias tentativas falhadas por algum tempo." (Quantas? Quanto tempo? Que status? Quando volta a zero?)

  Repare que a regra foi dividida em três: contar e bloquear, recusar enquanto está bloqueado, e repor o contador. Cada uma tem o seu CA.

### §G-9 Catálogo de Erros
- O `code` é estável e é o que os testes verificam. O `title` é um texto fixo; o `detail` pode variar.
- Prefixos por domínio: `ERR-VAL-*` (validação), `ERR-AUTH-*` (autenticação), `ERR-CTX-*` (contexto/tenant), `ERR-SYS-*` (sistema).

### §G-10 Critérios de Aceitação
- Cada cenário tem **dados concretos** (UUID fixos, e-mails, datas). "Dado um utilizador válido" não chega.
- Os passos `Então` afirmam **estado observável**: status, campo JSON, linha na BD, chave Redis, evento no tópico.
- Use `Esquema do Cenário` + `Exemplos` para regras de validação com várias entradas.
- Marque `@critico` em tudo o que envolve segurança, isolamento de tenant ou perda de dados. Estes CA são eliminatórios (G2). Não marque mais do que isso: com muitos CA críticos, a probabilidade de passar G2 cai depressa, mesmo com código quase todo certo.
- **Um CA, um comportamento.** Não junte vários EX num só cenário: se um falhar, perde-se a informação de qual.
- **Os dados de partida respeitam o modelo.** Se um `refresh_token` tem FK para `tenant`, o `Contexto` cria primeiro o tenant, o grupo e o vínculo.
- **O oráculo deriva da especificação.** Cada valor literal num `Então` tem de se poder calcular a partir do `Dado` e das RN (por exemplo, a lista de permissões esperada é exatamente a do seed). Verifique-o à mão na Fase 4.7.
- **Cubra mais do que os RF:** payload e headers de cada evento, cada RNF que entra na métrica, cada mecanismo de isolamento (por exemplo, RLS) e cada variante de operação ("suspender" e "reativar").

#### Contrato do ambiente de teste (§10.1)
A suite é escrita **antes** do código existir, por isso tem de assentar num contrato que o código gerado é obrigado a cumprir: como arranca, em que porta, como se carregam dados, como se cunham tokens de teste, como se controla o relógio. Se um CA exige uma falha de infraestrutura (por exemplo, "provedor de chaves indisponível"), o mecanismo de injeção tem de estar no contrato. Caso contrário, o princípio do mundo fechado proíbe a LLM de o criar, e o CA não se consegue testar.

  **Exemplo correto:**
  ```gherkin
  # language: pt
  Funcionalidade: RF-04 — Renovar sessão por refresh token

    Contexto:
      Dado que existe o utilizador "ana@agro.com" com id "0190a6e2-0000-7000-8000-000000000001"
      E que existe um refresh token "RT1" com status "CONSUMED" na família "F1"
      E que existe um refresh token "RT2" com status "ACTIVE" na família "F1"

    @CA-04.3 @RF-04 @RN-09 @EX-04.2 @MUST @critico
    Cenário: Reuso de refresh token consumido revoga a família inteira
      Quando é enviado POST "/api/v1/auth/refresh" com {"refresh_token": "RT1"}
      Então o status da resposta é 401
      E o campo "code" da resposta é "ERR-AUTH-006"
      E todos os registos de "refresh_token" com family_id "F1" têm status "REVOKED"
      E existe a chave Redis "denylist:user:0190a6e2-0000-7000-8000-000000000001"
      E foi publicado 1 evento no tópico "sessao_comprometida" com user_id "0190a6e2-0000-7000-8000-000000000001"

    @CA-04.4 @RF-04 @MUST
    Esquema do Cenário: Formato inválido de refresh token
      Quando é enviado POST "/api/v1/auth/refresh" com {"refresh_token": <valor>}
      Então o status da resposta é 400
      E o campo "code" da resposta é "ERR-VAL-001"

      Exemplos:
        | valor   |
        | ""      |
        | null    |
        | "abc"   |
  ```

  **Errado:**
  ```gherkin
  Cenário: Refresh inválido
    Dado um token inválido
    Quando tento renovar
    Então o sistema deve tratar o erro corretamente
  ```
  (Não diz qual token, qual endpoint nem qual status, e "tratar corretamente" não é verificável.)

- **Exemplo de CA de isolamento multi-tenant (sempre `@critico`):**
  ```gherkin
  @CA-06.5 @RF-06 @RN-12 @MUST @critico
  Cenário: Troca de contexto para tenant não autorizado é recusada
    Dado que o utilizador "ana@agro.com" é membro apenas do tenant "TEN-A"
    Quando é enviado POST "/api/v1/auth/switch-context" com {"target_tenant_id": "TEN-C", "target_scope": "BUYER", "view_mode": "SINGLE"}
    Então o status da resposta é 403
    E o campo "code" da resposta é "ERR-CTX-002"
    E a resposta não contém o campo "access_token"
  ```

### §G-11 Matriz de Rastreabilidade
- Preencha-a **por último** e use-a como verificação: qualquer célula vazia na coluna CA bloqueia o Gate 3.
- Os totais alimentam o denominador da métrica e do Índice de Ambiguidade.

### §G-12 Pressupostos, Omissões e Exclusões
- Todo o parâmetro numérico ou comportamento que **não** vem da fonte original (por exemplo, "refresh token válido por 7 dias") tem um `PRS-nn` com a aprovação do PO e a data.
- `PRS-nn` regista as decisões tomadas para fechar ambiguidades (por exemplo, "PRS-03: `email` é comparado sem distinção de maiúsculas/minúsculas; é guardado em minúsculas").
- §12.2 *Regras por Omissão* é a rede de segurança: diz à LLM o que fazer nas situações não cobertas. Prefira sempre o comportamento **mais restritivo**.
- §12.3 *Proibido*: o que nenhuma execução pode fazer, mesmo que pareça útil.

### §G-13 Formato de Entrega
- Mantenha o texto padrão. A exigência de "conteúdo integral de cada ficheiro" impede saídas truncadas, que fazem falhar o G1.
- Defina **um único** mecanismo de migração (por exemplo, "migrações aplicadas no arranque") e exija `Dockerfile` e `docker-compose.yml`, porque o G1 exige que a aplicação arranque.
- A lista de "Suposições" que a LLM devolve é um indicador da qualidade da especificação. Se não estiver vazia, há trabalho a fazer na próxima versão.

### §G-14 Questões em Aberto
- Tem de estar vazia no Gate 4. Uma questão em aberto não pode ser "resolvida" pela LLM.

---

## 4. Erros mais comuns (e como detetá-los)

| Erro | Sintoma no código gerado | Deteção pré-LLM |
|---|---|---|
| Status HTTP omitido | 400 genérico onde se esperava 422 ou 409 | Gate 2: cada RN/EX tem uma linha de status |
| `ON DELETE` omitido | `CASCADE` apaga dados de auditoria | Lint: grep `FK` sem `ON DELETE` |
| Sinónimos | Duas entidades para o mesmo conceito | Glossário + revisão RI |
| Exemplo JSON contraditório com a tabela | Campo com o nome ou o tipo do exemplo, não da tabela | Revisão RI: comparar campo a campo |
| Transacionalidade não declarada | Evento publicado e rollback da BD | Bloco "Transacionalidade" em cada EP |
| Ausência de "Fora do âmbito" | A LLM implementa um gateway, uma UI ou consumidores | Gate 1 |
| CA com dados abstratos | Testes gerados que passam com qualquer implementação | Gate 3: os `Então` têm valores literais |

---

## 5. Checklist de Qualidade — *Definition of Ready* (Gate 4)

Os grupos A a D formam o Gate 4 (fim da Fase 4). O grupo E é o gate pré-geração da Fase 5. A especificação **só pode** ser submetida à LLM quando **todos** os itens dos cinco grupos estiverem marcados.

### A. Completude
- [ ] A1. Não existe nenhum `<<`, `TBD`, `TODO` ou `a definir` (lint 4.1 = 0 ocorrências).
- [ ] A2. As §0 a §13 estão preenchidas. As células sem conteúdo dizem "Nenhum" ou "Não se aplica".
- [ ] A3. A §14 *Questões em Aberto* está vazia.
- [ ] A4. Todas as tecnologias e dependências têm versão. A lista de dependências permitidas é exaustiva.
- [ ] A5. O âmbito tem a coluna "Fora do âmbito" preenchida, incluindo as capacidades vizinhas que a fonte menciona mas que não se implementam.
- [ ] A6. Um esqueleto de projeto com as versões exatas da §3 restaura sem conflitos (Fase 4.9).

### B. Determinismo
- [ ] B1. Lint de termos proibidos (§2.3) = 0 ocorrências.
- [ ] B2. Todas as RN estão na forma SE/ENTÃO/SENÃO, com parâmetros numéricos e unidades.
- [ ] B3. Todos os enums estão fechados (sem "etc.") e as máquinas de estado listam as transições permitidas.
- [ ] B4. Todos os atributos têm tipo físico, nulidade, default e restrições. Todas as FK têm `ON DELETE`.
- [ ] B5. Todos os endpoints listam todos os status possíveis com o respetivo `ERR-*`.
- [ ] B6. Os efeitos colaterais estão ordenados e a transacionalidade está declarada em cada EP de escrita.
- [ ] B7. Os exemplos JSON são consistentes com as tabelas de campos (nomes, tipos, formatos).
- [ ] B8. Cada termo de domínio tem um único nome em todo o documento.
- [ ] B9. Cada endpoint tem ordem de avaliação, e a tabela de respostas inclui as rejeições do middleware.
- [ ] B10. A matriz de autorização (§6.4) cobre todos os endpoints e bate certo com as RN de autorização.
- [ ] B11. Nenhum valor usado em headers, claims ou eventos está fora do tipo ou do enum declarado.
- [ ] B12. Cada lista devolvida tem ordenação declarada.
- [ ] B13. Não há alternativas em aberto no texto ("X ou Y") sobre decisões de implementação.

### C. Verificabilidade e rastreabilidade
- [ ] C1. Cada RF tem prioridade MoSCoW.
- [ ] C2. Cada RF cumpre a cobertura mínima de CA: caminho feliz + 1 por RN + 1 por EX + 1 por variante de operação + 1 por transição de estado + 1 negativo de autorização (se o RF tiver controlo de acesso).
- [ ] C3. Cada RN e cada EX é coberta por pelo menos um CA.
- [ ] C4. Cada CA tem as tags `@CA`, `@RF` e de prioridade, dados concretos e passos `Então` observáveis.
- [ ] C5. Os CA de segurança e de isolamento multi-tenant estão marcados `@critico`.
- [ ] C6. A matriz de rastreabilidade (§11) não tem células vazias e os totais estão corretos.
- [ ] C7. O catálogo de erros não tem códigos órfãos em nenhum dos sentidos.
- [ ] C8. Cada evento, cada RNF dentro da métrica e cada mecanismo de isolamento tem pelo menos um CA.
- [ ] C9. Cada CA verifica um só comportamento, os seus dados de partida respeitam as FK e restrições, e o oráculo foi validado contra o seed e as RN (Fase 4.7).
- [ ] C10. O contrato do ambiente de teste (§10.1) está completo, incluindo o mecanismo de injeção de falhas quando algum CA o exige.

### D. Validação independente
- [ ] D1. A revisão RI foi feita por alguém (ou uma sessão) diferente da que fez o interrogatório por LLM, e todos os defeitos estão resolvidos.
- [ ] D2. O interrogatório por LLM (Fase 4.4) foi feito com o modelo alvo, registado, e devolveu "nenhum" nas quatro categorias, ou todos os itens foram resolvidos ou rejeitados com justificação.
- [ ] D3. Cada defeito tem um ID próprio e uma resolução com a linha da especificação onde a correção está.
- [ ] D4. A re-revisão cega da versão candidata (Fase 4.8) não encontrou defeitos: IA residual = 0. O IA inicial foi registado.
- [ ] D5. O PO aprovou as RN, os CA e todos os PRS (com data). O ARQ aprovou a stack, os ADR, os RNF e os contratos.

### E. Preparação da medição (gate pré-geração, Fase 5.2a)
- [ ] E1. A suite de testes de aceitação (um teste por CA) está escrita, validada contra uma implementação de referência ou oráculo a oráculo, e versionada com hash **antes** da geração.
- [ ] E2. O protocolo de geração está pré-registado: modelo, condição (G-API ou G-AGT), `max_tokens`, prompt, comando de G1, *n* por análise de potência (mínimo 15), teste estatístico principal e condição de controlo.
- [ ] E3. A baseline e o payload estão congelados, com os dois hashes num ficheiro `.sha256` à parte.
- [ ] E4. O payload não tem comentários `<!-- -->`, nem a §14, nem o Anexo A.
