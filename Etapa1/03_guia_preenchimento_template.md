# Entregável 3 — Guia de Preenchimento do Conjunto de Modelos de Especificação para LLM

> **Versão:** 2.1 · **Data:** 2026-10-19 · **Aplica-se a:** `template/` v2.1 (Entregável 2) e ao processo v2.1 (Entregável 1)
> **Público:** analistas, arquitetos e orquestradores que especificam para LLMs, humanos ou agentes.
> A secção §G-nn deste guia corresponde ao ficheiro `nn` do conjunto de modelos.

---

## 1. Como usar

1. **Copie a pasta `template/` inteira** para a pasta da especificação do projeto. Copie `50-modulos/M-nn-modelo.md` uma vez por módulo, com o nome `M-<nn>-<nome>.md`.
2. **Preencha pela ordem das fases do processo, não pela ordem dos ficheiros.** O que estiver na mesma linha pode ser feito em paralelo, por agentes diferentes:

   | Fase | Em sequência | Em paralelo |
   |---|---|---|
   | 1 | `10` Núcleo → decomposição em `00` §2 | `20`: uma secção por sistema externo, um agente cada |
   | 2 | `30` Dados → `40` Contratos | `70` Ambiente de teste, junto com `40` |
   | 3 | — | `50-modulos/M-nn`: um agente por módulo |
   | 4 | Consistência entre módulos → ensaio | Uma revisão cega por módulo |
   | 5 | `60` Plano → F-00 → … | Fases sem dependência entre si; suite de aceitação junto com `60` |
   | Sempre | `80` Orquestração (no início) | `90` Registo (em cada transição) |

3. **Nunca deixe uma célula vazia.** Escreva "Nenhum" ou "Não se aplica": isso é uma afirmação. Uma célula vazia é uma lacuna.
4. **Cada ficheiro tem um dono, um estado e uma data** no cabeçalho e no índice. Um ficheiro sem data de atualização ou marcado "Defasado" não entra em nenhum pacote de contexto.
5. **No congelamento**, remova os comentários `<!-- -->` e grave os hashes em `spec.sha256` (fora dos ficheiros).

---

## 2. Regras de escrita anti-alucinação

### 2.1 As doze regras de ouro

| # | Regra | Porquê |
|---|---|---|
| 1 | **Valores concretos em vez de adjetivos.** | A LLM converte "curto" num número qualquer, diferente em cada execução. |
| 2 | **Um termo, um nome**, igual no glossário, na BD, no JSON e no código. | Sinónimos levam a LLM a criar duas entidades. |
| 3 | **Listas fechadas.** Nunca "etc." nem "entre outros". | "Etc." autoriza a LLM a inventar valores. |
| 4 | **Todos os status e todos os erros explícitos**, incluindo os do middleware. | Os status omitidos são preenchidos com "o mais comum", muitas vezes errado. |
| 5 | **Uma consequência por RN.** | Permite medir cada regra com um CA próprio. |
| 6 | **Forma canónica SE/ENTÃO/SENÃO.** | Elimina a ambiguidade de âmbito ("só quando…", "exceto se…"). |
| 7 | **Diga o que NÃO fazer** onde a tentação de inferir é alta. | As LLM tendem a acrescentar comportamento "útil". |
| 8 | **Exemplos JSON coerentes com as tabelas.** | Perante uma contradição, a LLM escolhe ao acaso. |
| 9 | **Ordem dos efeitos e transacionalidade explícitas.** | Persistir antes de validar e publicar fora da transação são erros recorrentes. |
| 10 | **Referências por ID**, nunca por posição ("ver acima"). | Num pacote de contexto, "acima" pode nem existir. |
| 11 | **Etiqueta de evidência em tudo o que descreve um sistema existente.** | Um contrato `[DOC]` errado propaga-se ao código e aos fakes, e os testes passam na mesma. |
| 12 | **Escreva cada módulo como se o leitor só tivesse o seu pacote.** | É exatamente isso que vai acontecer. |

### 2.2 O que fazer *vs.* o que nunca escrever

| ❌ Nunca escrever | ✅ Escrever |
|---|---|
| "O token deve expirar em pouco tempo." | "O `access_token` **DEVE** ter `exp = iat + 900` segundos." |
| "A senha deve ser segura." | "SE `password` tiver menos de 12 caracteres ENTÃO 422 `ERR-VAL-003`." |
| "Validar os dados de entrada." | Tabela de campos com tipo, obrigatoriedade, formato, limites e erro, e a ordem de avaliação. |
| "Retornar erro se não autorizado." | "SE o JWT estiver ausente ENTÃO 401 `ERR-AUTH-001`. SE válido mas sem `tenant:admin` ENTÃO 403 `ERR-AUTH-010`." |
| "Usar cache para melhorar o desempenho." | "Gravar `denylist:jti:{jti}` = `1` com TTL = `exp − now` segundos." |
| "O sistema trata o refresh token." | Fluxo numerado, com o estado do token em cada passo (`ACTIVE` → `CONSUMED`). |
| "Como no módulo de login." | A especificação completa. O outro módulo não está no pacote. |
| "A API externa devolve 404 se não existir." | "A API externa devolve 404 se não existir. `[MEDIDO]` ambiente de testes, 2026-10-02." Ou, se não foi medido, uma lacuna `L-nn`. |
| "Deve funcionar em concorrência." | Tabela "Concorrência, idempotência e retomada" com o comportamento de cada situação e o CA. |

### 2.3 Termos proibidos (lista oficial; o lint da Fase 4.1 deriva desta lista)

`adequado` · `apropriado` · `rápido` · `eficiente` · `seguro` (sem especificação) · `robusto` · `flexível` · `intuitivo` · `amigável` · `normalmente` · `geralmente` · `quando aplicável` · `se necessário` · `etc.` · `entre outros` · `e/ou` · `...` · `similar a` · `tratar` (sem objeto concreto) · `gerir` (sem operações listadas) · `suportar` (sem limite numérico) · `TBD` · `TODO` · `a definir`

### 2.4 Linguagem normativa

| Palavra | Significado |
|---|---|
| **DEVE** | Obrigatório. A falha conta para a métrica. |
| **NÃO DEVE** | Proibido. |
| **DEVERIA** | Recomendado. Use com parcimónia: gera variação entre execuções. |
| **PODE** | Opcional. A ausência não é falha. |

Evite "deverá", "terá de", "precisa": a LLM não sabe se são obrigatórios.

---

## 3. Como decompor: módulos, pacotes e fases

### 3.1 Cortar em módulos
- **Um módulo é um agregado ou um contexto delimitado**, com dados que só ele escreve. Exemplo num serviço de identidade: *Autenticação* (login, refresh, logout), *Contexto* (troca de tenant e de escopo), *Administração de tenants*, *Impersonation*, *Chaves e eventos*.
- **Os pontos de contacto são só dados partilhados e interfaces.** Se dois módulos precisam de conhecer as regras um do outro, ou estão mal cortados, ou essa regra pertence ao núcleo.
- **Teste do pacote:** núcleo + módulo + tabelas que lê ou escreve + interfaces de que depende ≤ orçamento (60 KB de referência). Se não couber, divida o módulo.
- **Faixas de identificadores antes do paralelismo:** a UT de decomposição (UT-1-90) atribui a cada UT e a cada módulo uma faixa exclusiva de identificadores (`PRS`, `RN`, `CA`, `Q`, `ERR`, `IF`) registada no índice (`00` §4) antes de qualquer unidade paralela iniciar o trabalho. Nenhuma unidade paralela cria identificadores fora da sua faixa atribuída.

### 3.2 Montar um pacote de contexto
- Liste **secções**, não ficheiros inteiros: "`30` só ENT-03 e ENT-04", "`40` §1–§3 e interfaces EP-05, EP-06".
- Das dependências, entra **só a interface** (a linha da tabela de `40` §4), nunca o módulo inteiro.
- **Verificação automática de fecho de referências e orçamento:** utilize obrigatoriamente um script (`evidencias/medir-pacotes.py` ou equivalente) para verificar que: (a) a dimensão total de cada pacote cumpre o orçamento (≤ 60 KB de referência), somando todas as secções declaradas (incluindo `20`, a parte comum de `70` e a reserva de brief); (b) cada identificador ou secção citado no texto do pacote possui definição dentro do próprio pacote. Se alguma referência faltar ou o orçamento for ultrapassado, a verificação falha obrigatoriamente.
- Verifique o pacote com a categoria (e) do interrogatório da Fase 4.3: "informação que exigiria outro documento". Um item nesta categoria significa que falta algo no pacote.

### 3.3 Desenhar o plano de fases
- **F-00 é sempre o laço mínimo ponta a ponta**, com todos os transversais. É o que impede cada fase seguinte de reinventar o envelope, os erros ou a autenticação.
- Cada fase tem 5 a 20 CA e cabe num pacote.
- Os ramos paralelos escrevem em **pastas disjuntas**. Se duas fases têm de escrever no mesmo ficheiro (por exemplo, o registo de rotas), uma depende da outra, ou o ficheiro partilhado é preparado na F-00.
- O **"Não tocar ainda"** é tão importante como o "Constrói": é o que o validador compara com o diff.

---

## 4. Preenchimento ficheiro a ficheiro

### §G-00 Índice
- **Mapa de documentos:** a coluna "Leia quando" é o que o orquestrador usa para montar os pacotes. Escreva-a como uma condição ("a UT lê ou escreve dados"), não como uma descrição.
- **Mapa de módulos:** a coluna "Pacote estimado" obriga a fazer o teste do pacote (§3.1) logo na Fase 1.
- **Precedência:** uma única regra. Não crie outra regra noutro ficheiro.

### §G-10 Núcleo
- **Mantenha-o curto.** Entra em todos os pacotes: cada KB aqui multiplica-se pelo número de agentes. O que só um módulo usa vai para o módulo.
- **Instruções para a LLM (§1):** não altere o texto padrão. A regra 6 ("não invente, registe a dúvida") é o mecanismo que transforma ambiguidade em perguntas em vez de código inventado.
- **ADR:** sem "consequência obrigatória no código", um ADR não serve para a LLM.

  | ID | Decisão | Consequência obrigatória no código |
  |---|---|---|
  | ADR-06 | Validação stateless de JWT + revogação via Redis | O serviço **DEVE** expor `GET /.well-known/jwks.json`. Todo o logout **DEVE** gravar `denylist:jti:{jti}` com TTL = tempo restante do token. |

- **RNF:** sempre com número, método e a coluna "Na métrica". Um RNF que não se consiga testar com resultado binário (por exemplo, latência sob carga) fica fora da métrica, e isso fica escrito.
- **Logging:** diga o que **não** se regista (passwords, tokens, hashes, dados pessoais).
- **Migrações:** um único mecanismo. "Migração ou inicializador" é uma ambiguidade.
- **Exceções de infraestrutura vs. erros de negócio:** o catálogo de erros e as regras de omissão **DEVEM** distinguir falhas de comunicação ou indisponibilidade da base de dados (503 `ERR-SYS-002`) de exceções do SGBD com significado de negócio (como violação de unicidade → 409 ou violação de RLS → 403), através de uma tabela explícita de correspondência de `SQLSTATE` (ou código equivalente do driver) para código de erro do catálogo.

### §G-20 Sistema existente
- **Meça sempre que puder.** Um contrato lido na documentação pode estar errado. No caso de referência, uma rota documentada como `DELETE /recurso/{id}` era na realidade `DELETE /recurso?id=`. O fake foi construído a partir da documentação, herdou o erro, e os testes passaram. Só o ensaio no ambiente real o revelou.
- **Toda a medição tem script no repositório da especificação:** a etiqueta `[MEDIDO]` **só é válida** se citar o caminho de um script versionado na pasta `evidencias/` do repositório da especificação e o respetivo ficheiro de saída com o resultado. Medições efetuadas em pastas temporárias ou sem script no repositório não contam como evidência.
- **"Comportamentos que importam"** é a secção mais valiosa: o que o sistema faz de surpreendente. Exemplos: aceita duplicados, não valida um campo, honra o id enviado, devolve 500 sem efeito.
- **Concorrência e falhas do sistema externo** determinam a estratégia de idempotência do módulo que o usa.
- **Lacunas:** o que não foi verificado não é um pressuposto. Fica em `L-nn`, com o risco e quem pode responder.

### §G-30 Dados
- Tipos físicos do SGBD, `ON DELETE` em toda a FK e um **módulo dono** por tabela.
- Máquinas de estado: liste só as transições **permitidas**.

  | De | Para | Gatilho |
  |---|---|---|
  | `ACTIVE` | `CONSUMED` | RF-04 passo 4 |
  | `ACTIVE` | `REVOKED` | RN-09 (reuso), RF-05 (logout) |

- **Cobertura de entrada:** em importações, cada coluna da origem tem um destino ou fica "NÃO MAPEADO" com uma decisão. *No caso de referência, colunas de telefone foram ignoradas em silêncio até à carga real.*
- **Dados de referência:** totais esperados e a reconciliação. Exemplo: "419 linhas → 362 alunos (57 linhas são responsáveis adicionais do mesmo aluno)". **Nunca** dados pessoais reais no repositório.
- **Matriz de isolamento executada contra o SGBD real:** em sistemas com isolamento por inquilino (*multi-tenant*) via RLS, filtros ou interceptores, elabore uma matriz que exercite cada endpoint de escrita e cada leitura cruzada nos modos operacionais (`SINGLE`, `MACRO_GROUP`, bypass), com e sem cláusula `RETURNING`. A matriz **DEVE** ser executada por script contra o SGBD real, com os resultados registados em `evidencias/` e citados na especificação com `[MEDIDO]`.

### §G-40 Contratos
- **Validação em camadas:** é a tabela que mais divergências evita. Quando um pedido viola duas regras, só a ordem diz qual erro sai.
- **Erro de dado vs. erro HTTP:** decida-o para todo o sistema. Num lote, uma linha inválida é normalmente um resultado de validação, não um 400.
- **Checklist de robustez:** preencha a resposta esperada para cada caso. *No caso de referência, a resposta 500 com entrada malformada repetiu-se em 6 fases.*
- **Interfaces:** é o que os outros módulos recebem. Tem de chegar para chamar o endpoint sem ler o módulo dono.
- **Tipos partilhados:** inclua obrigatoriamente em `40` uma tabela "Tipos partilhados" com nome do tipo, pasta de destino, namespace e campos tipados. As interfaces internas (`IF-nn`), endpoints e eventos **DEVEM** utilizar exclusivamente tipos dessa tabela ou tipos primitivos da linguagem.
- **Matriz de autorização:** uma linha por endpoint. Tem de bater certo com as RN de autorização dos módulos.
- **Validação com a matriz de isolamento:** para endpoints que executam escritas em tabelas com isolamento por inquilino, o comportamento de autorização e de execução **DEVE** estar alinhado com os resultados da matriz de isolamento executada.

### §G-50 Módulos
- **Cabeçalho "Pacote de contexto":** escreva-o antes de escrever o resto. Obriga a saber de que mais o módulo precisa.
- **Endpoints:** request, ordem de avaliação, todas as respostas, corpo de sucesso com ordenação, efeitos por ordem e transacionalidade.

  | # | Verificação | Camada | Se falhar |
  |---|---|---|---|
  | 1 | `refresh_token` presente e com 43 caracteres base64url | Formato | 400 `ERR-VAL-001` |
  | 2 | Hash encontrado | Existência | 401 `ERR-AUTH-005` |
  | 3 | `status` ≠ `CONSUMED` | Regra RN-09 | 401 `ERR-AUTH-006` |
  | 4 | `status` = `ACTIVE` e `expires_at` > agora | Regra RN-10 | 401 `ERR-AUTH-005` |

- **Concorrência, idempotência e retomada:** obrigatório em todo o RF que escreve. *No caso de referência, todas as falhas críticas vieram daqui: pessoa duplicada quando um registo igual aparece por fora, retoma de um passo a meio, decisão perdida com dois pedidos ao mesmo tempo.*

  | Situação | Comportamento obrigatório | CA |
  |---|---|---|
  | Dois `POST /auth/refresh` simultâneos com o mesmo token `ACTIVE` | Exatamente um devolve 200; o outro devolve 401 `ERR-AUTH-006` e revoga a família | CA-04.6 |

- **RN:** uma consequência cada. Errado: "bloquear o utilizador após várias tentativas falhadas por algum tempo". Certo: três RN.

  | ID | Tipo | Regra | Parâmetros | Erro |
  |---|---|---|---|---|
  | RN-03 | Temporal | **SE** um `user_id` acumular `MAX_FAILED` falhas consecutivas **ENTÃO** `user.locked_until = now + LOCK_DURATION` | `MAX_FAILED = 5`; `LOCK_DURATION = 900 s` | – |
  | RN-04 | Autorização | **SE** `user.locked_until` > agora **ENTÃO** `EP-01` responde 423 sem verificar a palavra-passe **SENÃO** segue | – | `ERR-AUTH-003` |
  | RN-05 | Integridade | **SE** o login for bem-sucedido **ENTÃO** `user.failed_logins = 0` | – | – |

- **CA:**
  - dados de partida concretos, que respeitam as FK;
  - um comportamento por CA;
  - cada valor literal do `Então` derivado do `Dado` e das RN;
  - **pelo menos uma borda por CA**;
  - `@critico` só para segurança, isolamento entre tenants, perda ou duplicação de dados e segredos em log. Com demasiados críticos, a probabilidade de passar o veto cai depressa.

  ```gherkin
  @CA-04.3 @RF-04 @RN-09 @MUST @critico
  Cenário: Reuso de refresh token consumido revoga a família inteira
    Dado que existe o tenant "TEN-A" do grupo "GRP-1" e o utilizador "ana@agro.com" é membro de "TEN-A"
    E que existe o refresh token "RT1" com status "CONSUMED" na família "F1"
    E que existe o refresh token "RT2" com status "ACTIVE" na família "F1"
    Quando é enviado POST "/api/v1/auth/refresh" com {"refresh_token": "RT1"}
    Então o status da resposta é 401
    E o campo "code" é "ERR-AUTH-006"
    E todos os registos de "refresh_token" com family_id "F1" têm status "REVOKED"

  @CA-04.4 @RF-04 @MUST @borda
  Esquema do Cenário: Formato inválido de refresh token
    Quando é enviado POST "/api/v1/auth/refresh" com {"refresh_token": <valor>}
    Então o status da resposta é 400
    E o campo "code" é "ERR-VAL-001"
    Exemplos:
      | valor |
      | ""    |
      | null  |
      | "abc" |
  ```

### §G-60 Plano de fases
- Cada CA pertence a uma só fase, e a soma dos CA das fases tem de ser igual ao total da especificação.
- Os construtores paralelos dentro de uma fase escrevem em ficheiros disjuntos. Exemplo do caso de referência: um construtor fazia o código e outro o fake do sistema externo e as fixtures.
- Uma fase de **ensaio no ambiente real** vem antes de qualquer escrita real num sistema externo.

### §G-70 Ambiente de teste
- Escreva-o como um contrato que o código gerado é **obrigado** a cumprir: arranque, portas, dados, tokens, relógio, inspeção.
- **Fakes:** implementam só o comportamento `[MEDIDO]`. Um comportamento `[DOC]` é marcado como não verificado.
- **Injeção de falhas e mapeamento de exceções:** se um CA exige uma falha (sistema externo indisponível, processo morto a meio), o mecanismo tem de estar aqui. Caso contrário, o princípio do mundo fechado proíbe a LLM de o criar. Estabeleça a distinção clara entre indisponibilidade de infraestrutura (queda de rede ou paragem de contentor → 503) e erros do motor de dados com significado de negócio (mapeados via `SQLSTATE`).
- **Esperas** só com espera ativa e teto. *No caso de referência, esperas fixas causaram testes instáveis.*

### §G-80 Orquestração
- O orquestrador **só preenche os campos `{ }`** dos briefs. Colar histórico de conversa é a forma mais rápida de estourar o contexto e de contaminar a independência.
- **Divisão de responsabilidade nos pedidos de alteração:** o orquestrador não decide alterações na especificação nem divisões de módulos. O arquiteto (ARQ) propõe a alteração no âmbito de uma UT técnica e o PO aprova ou rejeita. Todos os pedidos **DEVEM** utilizar exclusivamente o identificador no formato `CR-nn` (com numeração de dois dígitos, nunca outras letras).
- No **1.º ciclo, o construtor não recebe a suite de aceitação**: só a especificação. É isso que permite que $C_w^{(1)}$ meça a qualidade da spec. A suite só lhe chega no retrabalho.
- O **validador nunca recebe o relato do construtor.** *No caso de referência, um segundo validador independente deu 78 a uma fase que o primeiro tinha avaliado com 95, por uma falha de concorrência.*
- O **retrabalho** tem sempre três partes: conserto pontual, mudança que torna a classe impossível, débitos adjacentes.
- **Paragem obrigatória:** defina-a para tudo o que possa perder dados ou escrever em produção.

### §G-90 Registo
- Atualize-o **em cada transição de UT**. *No caso de referência, o registo deixou de ser atualizado nas fases finais e o processo degradou-se: os PRs perderam a avaliação e as mensagens perderam os IDs.* Por isso, na v2, um PR sem a linha correspondente no registo não é aceite.
- **Decisões:** com data e origem. Uma decisão revista não se apaga: fica marcada "Revista por D-nn".
- **Perguntas:** a coluna "Bloqueia" é o que impede uma fase de começar com uma decisão em aberto. *No caso de referência, decisões que mudaram a meio (máscara de dados, validação de um documento) causaram retrabalho em sequência.*
- **Defeitos:** um ID por defeito, nunca agrupado, com a linha onde a correção ficou.

---

## 5. Erros mais comuns

| Erro | Sintoma no código gerado | Deteção |
|---|---|---|
| Status HTTP omitido | 400 genérico onde se esperava 422 ou 409 | Gate 2/3: cada RN e EX tem uma linha de status |
| Ordem de avaliação ausente | Erro diferente consoante a execução | Gate 3: ordem de avaliação em todo o EP |
| Contrato externo não medido | O fake e o código herdam o mesmo erro; os testes passam | Etiqueta `[DOC]` + lacuna; fase de ensaio real |
| Concorrência não especificada | Duplicação de dados sob pedidos simultâneos ou retoma | Tabela de concorrência obrigatória |
| Entrada malformada sem resposta definida | 500 em vez de 4xx | Checklist de robustez em `40` §3 |
| Pacote incompleto | O construtor "adivinha" o que está noutro módulo | Interrogatório (e) na Fase 4.3 |
| Duas fases paralelas no mesmo ficheiro | Conflitos de merge, regressões | Coluna "Escreve" disjunta no `60` |
| Registo abandonado | Perde-se o estado; a sessão nova repete trabalho | PR só é aceite com a linha do registo |
| Dados pessoais em fixtures | Fuga de dados no histórico do git | Varredura obrigatória (`70` §2) |

---

## 6. Checklists

### 6.1 *Definition of Ready* da especificação (Gate 4)

Aplica-se ao conjunto (grupos A e B) e a **cada módulo** (grupos C e D).

**A. Completude do conjunto**
- [ ] A1. Nenhum `<<`, `TBD`, `TODO` ou `a definir` (lint = 0).
- [ ] A2. Todos os ficheiros com dono, estado "Congelado" e data. Nenhum "Defasado".
- [ ] A3. Âmbito com a coluna "Fora" preenchida.
- [ ] A4. Stack com versões exatas; um esqueleto restaura sem conflitos (Fase 4.7).
- [ ] A5. Nenhuma `Q-nn` aberta que bloqueie uma fase.
- [ ] A6. Todos os PRS aprovados pelo PO, com data.

**B. Contratos partilhados**
- [ ] B1. Todo o atributo com tipo físico, nulidade, restrição e `ON DELETE`; toda a tabela com um módulo dono.
- [ ] B2. A tabela de interfaces cobre todos os EP e EV.
- [ ] B3. A matriz de autorização cobre todos os endpoints.
- [ ] B4. Validação em camadas e checklist de robustez com resposta definida.
- [ ] B5. Catálogo de erros sem órfãos.
- [ ] B6. Nenhum valor usado fora do tipo ou do enum declarado.
- [ ] B7. Ambiente de teste completo: arranque, dados, tokens, relógio, fakes (só do medido), injeção de falhas.
- [ ] B8. Toda a afirmação sobre sistemas externos com etiqueta de evidência; toda a etiqueta `[MEDIDO]` cita script versionado em `evidencias/` e ficheiro de resultado (pastas temporárias não contam); o não verificado está em `L-nn`.
- [ ] B9. Tabela de tipos partilhados completa em `40` e adotada por todas as interfaces.
- [ ] B10. Matriz de isolamento executada por script contra o SGBD real e citada com `[MEDIDO]` (quando existir isolamento multi-tenant).
- [ ] B11. Distinção entre falhas de infraestrutura e erros de negócio estabelecida via tabela SQLSTATE no núcleo e no ambiente de teste.
- [ ] B12. Fecho de referências e conformidade com o orçamento verificados por script automático nos pacotes das UT.

**C. Cada módulo**
- [ ] C1. Pacote de contexto declarado, com tamanho ≤ orçamento contando todas as secções declaradas (`20`, `70`, brief) e com fecho de referências verificado por script automático.
- [ ] C2. Todo o EP com ordem de avaliação, todas as respostas (incluindo as do middleware) e corpo de sucesso com ordenação.
- [ ] C3. Todas as RN em forma canónica, uma consequência cada, com erro e evidência.
- [ ] C4. Tabela de concorrência, idempotência e retomada em todo o RF que escreve.
- [ ] C5. Cobertura mínima de CA: caminho feliz, 1 por RN, 1 por EX, 1 por variante, 1 por transição, 1 negativo de autorização, 1 por cenário de concorrência; **uma borda em cada CA**.
- [ ] C6. Cada CA com um só comportamento, dados de partida que respeitam o modelo e oráculo validado.
- [ ] C7. `@critico` só nos tipos permitidos.
- [ ] C8. Identificadores novos contidos na faixa atribuída em `00` §4; só referencia dados e interfaces congelados; qualquer necessidade nova está registada em `CR-nn`.

**D. Validação independente**
- [ ] D1. Revisão cega por módulo feita por um agente novo, com o mesmo pacote do construtor.
- [ ] D2. Interrogatório com o modelo alvo, com "nenhum" nas cinco categorias ou todos os itens resolvidos.
- [ ] D3. Consistência entre módulos verificada.
- [ ] D4. Ensaio por um agente sem contexto feito e corrigido.
- [ ] D5. Um ID por defeito, com resolução e linha; IA inicial registado; IA residual = 0 em todos os módulos.
- [ ] D6. Hashes em `spec.sha256` e registados em `90` §7.

### 6.2 *Definition of Ready* de uma fase (antes de lançar o construtor)
- [ ] R1. As fases de que depende estão aceites e mergeadas.
- [ ] R2. O pacote da fase está montado, dentro do orçamento e só com ficheiros congelados.
- [ ] R3. A suite da fase está escrita e validada (contra uma referência ou oráculo a oráculo).
- [ ] R3b. A suite está fora do alcance do construtor no 1.º ciclo (fora do worktree; verificado e registado em `90`).
- [ ] R4. "Não tocar ainda" e "Escreve" estão preenchidos e disjuntos das fases paralelas.
- [ ] R5. Nenhuma `Q-nn` bloqueia esta fase.
- [ ] R6. O protocolo está pré-registado em `80` §2 (só antes da primeira fase).

### 6.3 *Definition of Done* de uma fase (antes do merge)
- [ ] O1. G1 a G6 passam (Entregável 1, §7.2), segundo um validador novo.
- [ ] O2. Cada achado corrigido tem um teste que o reproduz.
- [ ] O3. As decisões fora dos docs e as dúvidas foram triadas em `D-nn`, `CR-nn` ou rejeitadas com motivo.
- [ ] O4. O PR segue o modelo de `80` §5.
- [ ] O5. A linha da fase no registo (`90`) está atualizada.
- [ ] O6. Os ficheiros de instrução do repositório (por exemplo, `CLAUDE.md`) refletem o estado atual.
- [ ] O7. A varredura de dados pessoais nas fixtures passou.
