# financeiro-carrefour

API de movimentações financeiras desenvolvida como projeto de avaliação técnica. Permite cadastrar clientes, abrir contas em reais, registrar créditos e débitos, consultar saldo atual e histórico, listar o extrato e estornar movimentações. A integração com RabbitMQ demonstra processamento assíncrono com consumo idempotente.

O código, as classes e os contratos da API estão em inglês. Este guia está em português.

## Tecnologias e organização

- C# e .NET 10; ASP.NET Core; Entity Framework Core.
- PostgreSQL 18.6 e RabbitMQ 4.4.0 com interface de administração.
- JWT, Swagger/OpenAPI, Docker e Docker Compose.
- xUnit, testes unitários, testes HTTP e integração com PostgreSQL real.
- GitHub Actions para compilação, testes e validação do fluxo com Docker/RabbitMQ.

As versões são fixadas nos arquivos do projeto para tornar a execução reproduzível. O SDK configurado é o 10.0.403.

| Projeto | Responsabilidade |
|---|---|
| `Financial.Domain` | Entidades e invariantes de cliente, conta e movimentação |
| `Financial.Application` | Casos de uso, comandos, consultas e contratos de persistência e eventos |
| `Financial.Infrastructure` | EF Core, PostgreSQL, transações, bloqueios, migrations e persistência de Outbox/recibos |
| `Financial.Api` | HTTP, autenticação, autorização, Swagger, configuração de DI e worker de mensageria |

A solução usa injeção de dependência explícita e CQRS simples: comandos alteram o estado e consultas leem os dados por caminhos separados. Ambos utilizam o mesmo banco de dados. Não é necessário MediatR nem separar a aplicação em microsserviços para demonstrar esses conceitos.

## Organização dos endpoints

A API usa Minimal APIs organizadas por funcionalidade. O `Program.cs` configura os serviços, os middlewares e chama os métodos de mapeamento; os handlers HTTP ficam em arquivos próprios:

| Arquivo em `src/Financial.Api/Endpoints` | Responsabilidade |
|---|---|
| `MovementEndpoints.cs` | Registro de crédito e débito e contrato `MovementRequest` |
| `ReversalEndpoints.cs` | Estorno integral |
| `AccountReadEndpoints.cs` | Saldo atual/histórico e extrato paginado |
| `ProvisioningEndpoints.cs` | Cadastro e consulta de clientes e contas |
| `ReceiptEndpoints.cs` | Consulta dos recibos do consumidor |
| `HealthEndpoints.cs` | Liveness e readiness |

O acesso demonstrativo permanece em `Development/DemoAccess.cs`, habilitado apenas em desenvolvimento. As regras financeiras continuam no domínio e nos handlers da aplicação; os endpoints traduzem HTTP para os casos de uso. Essa organização mantém as camadas e o CQRS existentes, sem adicionar outro framework nem uma migração completa para Vertical Slice Architecture.

## Executar com Docker

Pré-requisito: Docker Desktop com containers Linux e Compose v2, ou Docker Engine com Compose v2. Não é necessário instalar .NET, PostgreSQL ou RabbitMQ localmente. Não é necessário criar `.env`.

```sh
git clone https://github.com/ediorhodendev/financeiro-carrefour.git
cd financeiro-carrefour
docker compose up --build -d
docker compose ps
```

O Compose inicia o banco e o broker, aplica as migrations, cria um cliente/conta de demonstração e inicia a API. O serviço `migrations` terminar com código 0 é o comportamento esperado.

| Recurso | Endereço |
|---|---|
| Swagger | http://localhost:5080/swagger |
| API | http://localhost:5080 |
| RabbitMQ Management | http://localhost:15672 |
| PostgreSQL local | `localhost:5432` |

RabbitMQ local: usuário `financial_local`, senha `rabbit-local-demo-only`. Banco local: database `financial`, usuário `financial_local`, senha `financial-local-demo-only`. São credenciais exclusivas de demonstração. As portas publicadas estão vinculadas ao endereço de loopback.

### Atualizar preservando os dados

Na pasta do repositório, sem alterações locais pendentes:

```sh
git switch main
git pull --ff-only
docker compose up --build -d
docker compose ps
```

### Zerar e reconstruir o ambiente

**O comando `down -v` apaga os dados dos volumes deste projeto, incluindo clientes, contas, movimentações e dados do RabbitMQ.** A conta de demonstração será recriada; obtenha seu novo ID e novos tokens.

```sh
git switch main
git pull --ff-only
docker compose down -v
docker compose up --build -d
docker compose ps
```

Outros comandos úteis:

```sh
docker compose logs -f api
docker compose logs migrations
docker compose logs -f rabbitmq
docker compose down
```

`down` sem `-v` preserva os volumes. Portas e senhas podem ser sobrescritas pelas variáveis `API_PORT`, `POSTGRES_PORT`, `RABBITMQ_MANAGEMENT_PORT`, `POSTGRES_PASSWORD` e `RABBITMQ_PASSWORD`. Alterar uma variável de senha não altera automaticamente a senha já persistida em um volume existente.

## Autenticação e autorização no Swagger

1. Execute `POST /demo/access`, sem body e sem autenticação.
2. Copie `accountId`, `operatorToken` e `adminToken` da resposta.
3. Clique em **Authorize** e cole somente o JWT, sem escrever `Bearer`.
4. Use `operatorToken` para movimentações e consultas financeiras da conta demonstrativa.
5. Para cadastrar clientes ou abrir contas, troque a autorização por `adminToken`.

Os tokens de demonstração expiram em 3.600 segundos. Gere novos tokens e autorize novamente quando necessário. Gerar um token não adiciona dinheiro à conta nem limpa seus dados.

A identidade do proprietário é obtida do claim `sub` do JWT. O cliente não escolhe o proprietário no payload financeiro. O token administrativo permite cadastro e consulta administrativa; ele não concede acesso irrestrito às operações financeiras de outros clientes. Para um cliente criado com outro `subjectId`, é necessário um token válido com esse `sub` para movimentar a conta; o emissor de demonstração não emite tokens arbitrários para todos os clientes cadastrados.

`/demo/access` e Swagger são recursos de Development. O emissor de demonstração também exige `Demo__Enabled=true`, já configurado no Compose.

## Crédito e débito: contrato e regras

O campo `type` é um código numérico fixo:

| Código | Nome no código | Operação | Efeito no saldo |
|---|---|---|---|
| `1` | `Credit` | Crédito | Soma o valor ao saldo |
| `2` | `Debit` | Débito | Subtrai o valor, se houver saldo suficiente |

**Crédito e débito usam o mesmo endpoint.** A diferença é o `type`. O valor de `amount` é sempre positivo, inclusive no débito.

Crédito de R$ 100,00:

```json
{
  "type": 1,
  "amount": 100
}
```

Débito de R$ 30,00:

```json
{
  "type": 2,
  "amount": 30
}
```

Partindo de saldo zero, as duas operações deixam R$ 70,00. Um débito maior que o saldo retorna `409` e não gera movimentação. Um crédito que exceda o limite do saldo também é rejeitado.

### Valores monetários

- Moeda única: `BRL`. Uma conta por cliente, com saldo inicial zero.
- `amount` deve ser maior que zero e não ultrapassar `9999999999999999.99`.
- São aceitos valores com precisão de centavos, como `10`, `10.1` e `10.12`.
- `10.123` retorna `400`: o sistema não arredonda silenciosamente frações de centavo.
- Em JSON, use ponto decimal: `10.12`.
- Valores negativos e zero são inválidos. O saldo nunca pode ficar negativo nem exceder o limite de armazenamento.
- O código usa `decimal`; o banco usa `numeric(18,2)`.
- `type` igual a `0`, `3`, um código desconhecido, um número fracionário ou uma string como `"Credit"`, `"Debit"` ou `"creditoooo"` retorna `400`.

Os códigos `1` e `2` são explícitos e estáveis. As respostas também usam esses números. O armazenamento existente mantém os nomes `Credit` e `Debit`; registros anteriores não precisam ser regravados. Veja [compatibilidade dos códigos](docs/operation-codes.md).

## Idempotência

Registrar uma movimentação ou estorno exige o header `Idempotency-Key`, com 1 a 100 caracteres não vazios. A chave é vinculada à conta e deve identificar uma intenção de operação.

| Situação | Resultado |
|---|---|
| Chave nova e operação válida | `201`, `replayed: false` |
| Mesma chave e mesmo conteúdo normalizado | `200`, `replayed: true`, mesmo ID e horário |
| Mesma chave e conteúdo diferente | `409`, nenhuma nova movimentação |
| Nova operação legítima | Usar outra chave |

Exemplo: envie crédito de 100 com `credit-001`. Se houver timeout, repita exatamente a operação com `credit-001`. O saldo será creditado uma única vez. Para outro crédito, use `credit-002`.

As chaves são mantidas com o histórico, sem expiração. Maiúsculas e minúsculas da chave são significativas. Uma operação rejeitada não é registrada como sucesso: um débito recusado por saldo insuficiente pode ser tentado novamente após disponibilizar saldo. Crédito/débito e estorno compartilham o espaço de chaves da conta; use chaves distintas entre operações.

## Referência de endpoints

Nos exemplos abaixo, substitua `{accountId}`, `{customerId}` e `{movementId}` pelos IDs reais. IDs são UUIDs.

### `POST /demo/access` — obter acesso demonstrativo

Sem autenticação e sem body. Retorna o ID da conta demonstrativa, um JWT de operador, um JWT de administrador e `expiresInSeconds`. Disponível somente com a configuração de demonstração em Development. Não é um endpoint de autenticação para produção.

### `POST /api/customers` — cadastrar cliente

Exige `adminToken`. Vincula um nome a uma identidade externa única (`subjectId`). Retorna `201` e os dados do cliente criado.

```json
{
  "name": "Maria Silva",
  "subjectId": "customer-maria"
}
```

O `subjectId` deve corresponder ao `sub` do JWT do cliente. Dados inválidos retornam `400`; identidade já cadastrada retorna `409`.

### `GET /api/customers/{customerId}` — consultar cliente

Exige administrador. Retorna `200` com os dados cadastrais ou `404` se o cliente não existir. Não registra movimentações nem altera saldo.

### `POST /api/customers/{customerId}/accounts` — abrir conta

Exige administrador. Não recebe body. Abre a única conta BRL do cliente com saldo zero e retorna `201`. Cliente inexistente retorna `404`; tentar abrir outra conta para o mesmo cliente retorna `409`.

### `GET /api/accounts/{accountId}` — consultar dados da conta

Exige autenticação. O proprietário ou um administrador pode consultar os dados da conta. Retorna `200`; conta inexistente ou inacessível retorna `404`. Para consultas financeiras e histórico, utilize os endpoints específicos abaixo.

### `POST /api/accounts/{accountId}/movements` — registrar crédito ou débito

Exige papel `operator`, propriedade da conta e header `Idempotency-Key`. Use os payloads numéricos apresentados acima.

Exemplo de header para um crédito:

```text
Idempotency-Key: credit-001
```

Exemplo ilustrativo de resposta de criação:

```json
{
  "id": "8f71e274-aee4-43c4-8dd0-7327b98587f3",
  "accountId": "e986ec4b-06fc-4732-ba38-201fc70a00b1",
  "type": 1,
  "amount": 100,
  "registeredAt": "2026-10-30T16:30:59.80159+00:00",
  "replayed": false
}
```

Retorna `201` na criação e `200` em repetição idempotente. O horário é atribuído no registro, não enviado pelo usuário. Saldo, movimentação e evento Outbox são gravados na mesma transação. Uma falha antes do commit desfaz os três.

### `POST /api/accounts/{accountId}/movements/{movementId}/reversal` — estornar movimentação

Exige operador proprietário e uma chave nova, por exemplo `reversal-001`.

```json
{
  "reason": "Movement registered incorrectly"
}
```

O estorno cria uma nova movimentação compensatória vinculada à original. Não apaga nem edita a movimentação anterior.

| Original | Compensação | Exemplo |
|---|---|---|
| Crédito (`1`) | Débito (`2`) | Estornar crédito de 100 retira 100 do saldo |
| Débito (`2`) | Crédito (`1`) | Estornar débito de 30 devolve 30 ao saldo |

Regras:

- Apenas estorno integral; tipo e valor são derivados da operação original.
- Motivo obrigatório, com 1 a 500 caracteres após remover espaços das extremidades.
- Cada original pode ser estornado uma única vez.
- Não é permitido estornar um estorno.
- Estornar crédito exige saldo suficiente; estornar débito respeita o limite máximo do saldo.
- A movimentação deve pertencer à conta acessível pelo operador.
- Não envie `amount`, `type` ou outros campos adicionais: o body de estorno aceita somente `reason`.
- Repetir chave, original e motivo normalizado retorna o mesmo estorno (`200`). Alterar o motivo/original com a mesma chave retorna `409`.
- Nova chave para uma original já estornada retorna `409`.

A criação retorna `201`, incluindo `reversalOfMovementId`, tipo compensatório, valor, motivo, horário e `replayed`. Motivo inválido retorna `400`; original inexistente/inacessível retorna `404`; conflitos de regra retornam `409`.

Exemplo prático: crédito de 100 seguido de débito de 30 gera saldo 70. Estornar o débito devolve 30 e deixa saldo 100. Estornar o crédito enquanto há apenas 70 é recusado por falta de saldo. Veja [regras detalhadas de estorno](docs/reversals.md).

### `GET /api/accounts/{accountId}/balance` — saldo atual ou histórico

Exige proprietário autenticado. Sem o parâmetro `at`, retorna o saldo atual e `at: null`.

```json
{
  "accountId": "e986ec4b-06fc-4732-ba38-201fc70a00b1",
  "currency": "BRL",
  "balance": 70,
  "at": null
}
```

Para consultar saldo histórico, preencha `at` no Swagger com um instante ISO 8601 e fuso, por exemplo `2026-10-30T16:35:00Z`. O cálculo inclui movimentações cujo `registeredAt` seja menor ou igual ao instante informado. Use datas reais da sua conta; instante futuro, anterior à abertura ou sem fuso retorna `400`.

Se o crédito foi registrado antes das 16:35 e o débito depois, o saldo histórico desse instante será 100, mesmo que o saldo atual seja 70. Estornos afetam o histórico a partir de seu próprio horário de registro.

A consulta considera registros já confirmados e visíveis ao banco. Uma transação registrada antes do instante consultado, mas confirmada posteriormente, pode aparecer em uma consulta futura desse mesmo instante. Não é um snapshot permanente da visibilidade do banco no passado.

### `GET /api/accounts/{accountId}/movements` — extrato paginado

Exige proprietário autenticado. Retorna `items` e `nextCursor`, em ordem crescente de registro e ID. Cada item contém ID, tipo numérico, valor e horário; os campos `reversalOfMovementId` e `reversalReason` identificam uma compensação.

| Parâmetro | Uso |
|---|---|
| `limit` | Tamanho da página, de 1 a 100; padrão 50 |
| `cursor` | Valor opaco retornado em `nextCursor` na página anterior |

Para testar, use `limit=1` e deixe `cursor` vazio na primeira chamada. Copie **o valor** de `nextCursor` da resposta para o campo `cursor` na chamada seguinte. Não digite a palavra `nextCursor`. Quando a resposta trouxer `nextCursor: null`, acabou a paginação.

Cursor inválido, de outra conta ou limite fora do intervalo retorna `400`. A paginação usa keyset e é uma leitura viva: novas movimentações podem entrar durante a navegação.

### `GET /api/accounts/{accountId}/receipts` — verificar consumo de eventos

Exige operador proprietário. Retorna os 100 recibos mais recentes do consumidor, com `eventId`, `movementId`, `accountId` e `processedAt`, do mais recente para o mais antigo.

O recibo indica que o evento foi processado pelo consumidor. Pode demorar alguns instantes após a movimentação; lista vazia não significa, por si só, falha na gravação financeira. Recibos não alteram saldo e não representam envio de notificação externa.

### `GET /health/live` — verificar processo

Sem autenticação. Retorna `200` com `status: alive` enquanto o processo atende. Não verifica todas as dependências.

### `GET /health/ready` — verificar acesso ao banco/esquema

Sem autenticação. Executa uma consulta à tabela de contas e retorna `200` com `status: ready` se conseguir acessá-la. Não é uma verificação de disponibilidade do RabbitMQ. Não retorna dados financeiros.

## Mensageria: quando acontece e para quê

O RabbitMQ demonstra uma integração assíncrona após a gravação financeira. O saldo é atualizado pelo comando HTTP, dentro da transação no PostgreSQL; não depende de o consumidor executar.

1. A aplicação bloqueia a conta para serializar escritas concorrentes nessa conta.
2. Grava saldo, movimentação imutável e evento na Outbox, na mesma transação.
3. Após o commit, o worker publica o evento no RabbitMQ usando confirmação do broker e roteamento obrigatório.
4. O consumidor registra o recibo e confirma a entrega somente depois de persistir.
5. O ID único do evento impede que entregas duplicadas criem recibos duplicados.

O worker é um serviço hospedado na API. A entrega é **pelo menos uma vez**; o efeito do consumidor é idempotente. Não há promessa de transporte exatamente uma vez.

Se o RabbitMQ estiver indisponível, movimentações válidas continuam sendo gravadas. A Outbox mantém eventos pendentes para publicação após a recuperação. Replays HTTP não geram outro evento. Mensagens inválidas são direcionadas para tratamento em fila de mensagens mortas; inspecione `financial.receipts.v1` e `financial.receipts.dead.v1` no painel do broker.

Novos eventos usam contrato `MovementRecorded.v2`; o consumidor também interpreta os eventos legados v1 com o mapeamento antigo dos códigos. O evento de estorno inclui a referência à original, mas não publica o motivo em texto livre. Os nomes duráveis das filas permanecem compatíveis com a instalação anterior.

## Respostas de erro

Os erros tratados usam Problem Details, com campos como `title`, `status` e identificadores de rastreamento. A API também retorna `X-Correlation-ID` para correlacionar logs.

| Status | Significado comum |
|---|---|
| `200` | Consulta bem-sucedida ou repetição idempotente |
| `201` | Recurso ou movimentação criado |
| `400` | Body, valor, código, chave, data ou cursor inválido |
| `401` | Token ausente, inválido ou expirado |
| `403` | Papel exigido não concedido pelo token |
| `404` | Recurso inexistente ou conta/movimentação inacessível |
| `409` | Saldo insuficiente, limite excedido, conflito de idempotência, duplicidade ou estorno proibido |
| `503` | Falha transitória de persistência tratada |

Não troque a chave apenas porque houve timeout ou erro transitório: repita a mesma intenção com a mesma chave para evitar duplicação após um commit cuja resposta não chegou.

## Roteiro de testes pelo Swagger

Em uma conta nova, com saldo zero:

1. Obtenha acesso demonstrativo e autorize o operador.
2. Crédito `type=1`, `amount=100`, chave `credit-001`: espere `201`.
3. Repita exatamente: espere `200`, mesmo ID e `replayed=true`.
4. Altere o valor com a mesma chave: espere `409`.
5. Débito `type=2`, `amount=30`, chave `debit-001`: espere `201`.
6. Consulte saldo atual: espere 70.
7. Consulte saldo no horário do crédito: espere 100, se anterior ao débito.
8. Consulte extrato com `limit=1` e percorra as páginas usando o cursor retornado.
9. Consulte os recibos; aguarde o processamento assíncrono se necessário.
10. Tente débito maior que o saldo com chave nova: espere `409` e saldo inalterado.
11. Tente `amount=10.123` ou `type=3`: espere `400`.
12. Estorne o débito com motivo e chave nova: espere `201` e saldo 100.
13. Repita o mesmo estorno/chave: espere `200` e saldo 100.
14. Tente estornar novamente a original com outra chave: espere `409`.
15. Tente estornar a movimentação compensatória: espere `409`.
16. Remova a autorização e tente uma operação protegida: espere `401`.

## Compilação e testes automatizados

Execute a suíte pelo Docker, sem SDK instalado na máquina:

```sh
docker compose --profile tests run --build --rm tests
```

Esse comando usa um PostgreSQL de testes separado. A porta local 5433 deve estar disponível.

Com o SDK configurado no projeto:

```sh
dotnet restore Financial.slnx
dotnet build Financial.slnx -c Release
dotnet test tests/Financial.Domain.Tests -c Release
dotnet test tests/Financial.Api.Tests -c Release --filter "FullyQualifiedName!~ApiPostgresTests"
```

Para toda a suíte fora do Docker, configure `FINANCIAL_TEST_CONNECTION` apontando para um **banco PostgreSQL exclusivo de testes** e execute:

```sh
dotnet test Financial.slnx -c Release --collect:"XPlat Code Coverage"
```

Os testes de integração manipulam os dados de teste. Nunca utilize uma conexão de produção. A falta de configuração do banco provoca falha dos testes que dependem dele, em vez de ignorá-los silenciosamente.

A validação da versão com estorno e códigos numéricos aprovou **127 testes**, além do smoke test com Docker/RabbitMQ. Os cenários incluem regras monetárias, idempotência, concorrência, propriedade, validação HTTP, estornos, compatibilidade de eventos, duplicação de mensagens, DLQ e recuperação após indisponibilidade do broker. O build Release dessa versão terminou sem erros nem avisos.

O GitHub Actions publica resultados TRX e cobertura Cobertura. Cobertura mede código exercitado; não significa ausência de defeitos nem garantia de todos os cenários possíveis. O smoke test de ponta a ponta executado no CI exige Bash e Python 3 no host; eles não são necessários para subir a aplicação.

## Garantias e limites do projeto

- Escritas de uma conta são serializadas com bloqueio de linha PostgreSQL; contas diferentes podem progredir independentemente.
- Saldo, ledger e Outbox compartilham transação ACID.
- Movimentações confirmadas são imutáveis, com proteção no domínio, no EF e no banco. Correções são compensações explícitas.
- Histórico financeiro e recibos têm propósitos diferentes; a mensageria demonstra integração e não decide saldo.
- Não há transferências entre contas, múltiplas moedas, lançamentos retroativos nem estorno parcial.
- Trata-se de um projeto de avaliação, sem certificação bancária ou promessa de capacidade máxima medida.

Para produção, seriam necessários provedor real de identidade JWT, TLS, gestão de segredos, usuários de banco com privilégios mínimos, backups, testes de recuperação, observabilidade e estratégia de implantação/alta disponibilidade. As credenciais e o emissor locais não devem ser usados em produção.

## Documentação complementar

Os documentos técnicos abaixo mantêm o detalhamento em inglês:

- [Regras de negócio](docs/business-rules.md)
- [Estornos](docs/reversals.md)
- [Códigos e compatibilidade](docs/operation-codes.md)
- [Arquitetura e comportamento em falhas](docs/architecture.md)
- [Mapeamento dos requisitos](docs/requirements.md)
- [Decisões e versões](docs/decisions.md)
- [Validação e limites](docs/validation.md)
