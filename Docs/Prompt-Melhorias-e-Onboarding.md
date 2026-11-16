# Prompt Refinado: Matriz de Melhorias e Guia de Continuidade (Onboarding de Agente)

> **Arquivo de Destino:** [Prompt-Melhorias-e-Onboarding.md](file:///c:/Users/ACT/Documents/Personal/Identity-Service/Docs/Prompt-Melhorias-e-Onboarding.md)  
> **Finalidade:** Prompt de contextualização para execução por agente autônomo ou engenheiro de software.  
> **Todas as referências mapeadas com caminhos absolutos do ecossistema local.**

---

```markdown
Atue como o Engenheiro Líder de Software e Arquiteto de Sistemas responsável pelo ecossistema do Identity-Service B2B (.NET 10, C# 13, PostgreSQL 17 com RLS, Redis 7.4 com denylist fail-closed, RS256 e RFC 9457).

Execute uma tarefa dividida em duas entregas obrigatórias:

---

### PARTE 1: Matriz de Viabilidade e Priorização de Melhorias Mapeadas

Recupere as melhorias arquiteturais e operacionais documentadas em [Docs/Relatorio-Analise-e-Melhorias-Identity.md](file:///c:/Users/ACT/Documents/Personal/Identity-Service/Docs/Relatorio-Analise-e-Melhorias-Identity.md) (Quick Wins QW-01 a QW-05 e Melhorias Estruturais 4.1 a 4.5), além dos itens derivados de alinhamentos inter-equipes recentes (ex.: Endpoint `/api/v1/auth/introspect` já entregue no PR #12, tolerância de clock skew/leeway e resiliência de cache JWKS).

Para cada item catalogado, apresente uma análise técnica rigorosa contendo:
1. **Identificador e Nome da Melhoria:** (Ex.: `QW-01: Auto-Contexto no Login`, `QW-02: Endpoint /api/v1/auth/me`).
2. **Classificação:** Quick Win (curto prazo) vs. Evolução Estrutural (médio/longo prazo).
3. **Status Atual:** Não iniciado, Em andamento ou Já implementado (referenciando PR/commit se aplicável).
4. **Análise de Viabilidade Técnica:** Esforço estimado (horas/dias), complexidade de implementação e dependências de infraestrutura (banco, Redis, Caddy ou contratos externos).
5. **Impacto no Ecossistema:** Ganhos em DX (Developer Experience para outros grupos), segurança ou conformidade.
6. **Riscos e Efeitos Colaterais:** Impacto em breaking changes nos contratos vigentes, consumo de memória ou latência de requisições.
7. **Recomendação Conclusiva:** Implementar agora, Postergar para fase pós-MVP ou Descartar (com justificativa arquitetural).

Ao final da Parte 1, forneça a matriz de priorização consolidada (Esforço vs. Impacto) indicando a ordem exata de execução sugerida.

---

### PARTE 2: Guia Operacional de Continuidade, Governança e Engenharia (Onboarding de Agente)

Estruture um guia operacional completo para contextualizar um novo agente autônomo (ou engenheiro de software) que assumirá o ciclo de implementação, documentação e manutenção evolutiva do projeto.

O guia deve cobrir obrigatoriamente:

1. **Topologia do Repositório e Mapa de Diretórios:**
   - Detalhar a organização modular em [src/IdentityService/Modules/](file:///c:/Users/ACT/Documents/Personal/Identity-Service/src/IdentityService/Modules/) (do `M00Transversal` ao `M14Eventos`).
   - Mapear a localização de testes unitários ([tests/IdentityService.Tests](file:///c:/Users/ACT/Documents/Personal/Identity-Service/tests/IdentityService.Tests)) e de aceitação com Testcontainers ([tests/IdentityService.Acceptance](file:///c:/Users/ACT/Documents/Personal/Identity-Service/tests/IdentityService.Acceptance)).
   - Apontar diretórios de documentação canônica ([Docs/](file:///c:/Users/ACT/Documents/Personal/Identity-Service/Docs/)), scripts e assets do playground web ([pages/](file:///c:/Users/ACT/Documents/Personal/Identity-Service/pages/)).

2. **Stack Tecnológica e Invariantes Arquiteturais:**
   - .NET 10 / ASP.NET Core Minimal APIs / Entity Framework Core 10.
   - PostgreSQL 17 com isolamento multi-tenant via Row-Level Security (`app.auth_flow`, `app.allowed_tenants`, `app.write_tenants`).
   - Criptografia assimétrica nativa RS256 com chaves RSA-2048 expostas em `GET /.well-known/jwks.json`.
   - Redis 7.4 para denylist instantânea de tokens revogados (`jti`, `user`, `tenant`).
   - Transactional Outbox Pattern com concorrência segura (`SELECT ... FOR UPDATE SKIP LOCKED`) e mensageria Apache Kafka.
   - Respostas de erro estritas em conformidade com RFC 9457 (`application/problem+json`) contendo `code` e `correlationId`.

3. **Fluxo de Trabalho Git, Commits e Pull Requests:**
   - Conta GitHub obrigatória: `Matheus-Allvz` (nunca contas corporativas de terceiros).
   - Política de ramificação: Criação de feature branches (`feat/ma/<nome>` ou `fix/ma/<nome>`).
   - Padrão de commits: Conventional Commits estrito (`feat(...)`, `fix(...)`, `docs(...)`, `test(...)`, `chore(...)`).
   - Fluxo de PR: Criação e merge via GitHub CLI (`gh pr create` e `gh pr merge --admin --merge --delete-branch`), garantindo atualização contínua da branch `main`.

4. **Regras de Ouro e Gates de Qualidade:**
   - **Garantia de Não-Regressão:** Obrigatoriedade de rodar e manter 100% dos testes aprovados (`dotnet test`) antes de qualquer submissão de PR.
   - **Obrigatoriedade de Documentação:** Proibição de código sem atualização documental sincronizada. Toda nova feature ou endpoint deve ser refletida em [Docs/Alinhamento-Ecossistema-B2B.md](file:///c:/Users/ACT/Documents/Personal/Identity-Service/Docs/Alinhamento-Ecossistema-B2B.md), [Docs/Guia-Consumo-Testes-B2B.md](file:///c:/Users/ACT/Documents/Personal/Identity-Service/Docs/Guia-Consumo-Testes-B2B.md) e, quando aplicável, na coleção Postman ([Docs/postman-collection-b2b.json](file:///c:/Users/ACT/Documents/Personal/Identity-Service/Docs/postman-collection-b2b.json)) e no playground web ([pages/](file:///c:/Users/ACT/Documents/Personal/Identity-Service/pages/)).
   - **Isolamento de Ambiente e Privacidade:** Nenhuma menção a ambientes privados/VPS em canais externos. Respostas a outros grupos devem ser estritamente focadas em contratos públicos e padrões da turma.

5. **Ambientes e Infraestrutura de Homologação:**
   - API de Produção/Stage: `https://identity.matheus-alves.dev` (com proxy reverso Caddy e deploy Blue/Green Zero Downtime).
   - Playground Web Interativo: `https://pages-identity.matheus-alves.dev` (ferramenta de teste sem dependências locais).
   - Docker Compose local e estratégia híbrida desacoplada para os demais times.

---

### Formato de Saída Esperado:
- Relatório estruturado em Markdown com tabelas comparativas, diagramas Mermaid quando pertinente e blocos de comandos exatos para terminal PowerShell/Bash.
- Tom técnico, prescritivo e com foco em sustentabilidade de longo prazo do repositório.
```
