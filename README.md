# Kafka na vida real (demo) - CK Autoshop 🚚⚙️

Eu queria **brincar com Kafka do jeito que eu usaria no dia a dia** e acabei montando esse mini‑projeto: um **Producer** que publica uma **cotação** e um **Worker** que simula **preços de fornecedores** e devolve os resultados. É simples, direto e _usável de verdade_ nas minhas ideias para CK Autoshop (Vão ouvir muito dela).

> O que faremos? subir Kafka com Docker, rodar **dois consoles .NET 8** e ver as mensagens fluírem.
>
> - `Ck.Cotacao.Producer` → publica `ck.cotacoes.criadas`
> - `Ck.Precos.Worker` → consome a cotação, simula 3 fornecedores e publica `ck.itens.precificados`

---

## 🧰 Stack que usei

- **.NET 8** + [`Confluent.Kafka`](https://www.nuget.org/packages/Confluent.Kafka)
- **Kafka**
- **Docker Desktop** (no Windows usei WSL2; em Linux/macOS é a mesma ideia)

---

## 🧠 O que está acontecendo aqui!!??

1. Eu **publico** uma cotação com alguns itens → `ck.cotacoes.criadas`
2. Um **Worker** pega essa cotação, consulta **3 “fornecedores” (simulados)** por item
3. Para cada fornecedor, ele **publica um evento** com o preço → `ck.itens.precificados`
4. Se der ruim, a ideia é mandar pra uma **DLQ** (`ck.dlq`) e analisar depois

Se você curte desenho, o fluxo é este:

```
flowchart LR
  A[Producer\nCk.Cotacao.Producer] -- ck.cotacoes.criadas --> T1[(Kafka)]
  subgraph Kafka
    T1[Topic: ck.cotacoes.criadas\n(partitions: 3)]
    T2[Topic: ck.itens.precificados\n(partitions: 6)]
    DLQ[Topic: ck.dlq\n(partitions: 1)]
  end
  W[Ck.Precos.Worker] -->|consume| T1
  W -->|publica preços| T2
  W -->|erros| DLQ
```

---

## ▶️ Como rodar (passo a passo)

### 1) Subir o Kafka (Docker)

Subir o docker do Kafta a partir do `docker-compose.yml` na raiz do repo:

```yaml
services:
  kafka:
    image: bitnami/kafka:3.7
    container_name: kafka
    ports:
      - "9094:9094" # porta para acessar do host
    environment:
      - KAFKA_ENABLE_KRAFT=yes
      - KAFKA_CFG_NODE_ID=1
      - KAFKA_CFG_PROCESS_ROLES=broker,controller
      - KAFKA_CFG_LISTENERS=PLAINTEXT://:9092,CONTROLLER://:9093,EXTERNAL://:9094
      - KAFKA_CFG_ADVERTISED_LISTENERS=PLAINTEXT://kafka:9092,EXTERNAL://localhost:9094
      - KAFKA_CFG_LISTENER_SECURITY_PROTOCOL_MAP=CONTROLLER:PLAINTEXT,PLAINTEXT:PLAINTEXT,EXTERNAL:PLAINTEXT
      - KAFKA_CFG_CONTROLLER_LISTENER_NAMES=CONTROLLER
      - KAFKA_CFG_CONTROLLER_QUORUM_VOTERS=1@kafka:9093
      - KAFKA_CFG_AUTO_CREATE_TOPICS_ENABLE=true
    volumes:
      - ./data/kafka:/bitnami/kafka
```

Para subir e acompanhar o boot:

```bash
docker compose up -d
docker compose logs -f kafka
```

> **Conexão:** apps no **host** usam `localhost:9094`.  
> Dentro do **container**, o broker atende em `kafka:9092`.

### 2) (Opcional) Criar tópicos com particionamento

Se você quiser forçar o número de partições (senão o auto‑create resolve na primeira publicação):

```bash
# cotacoes.criadas
docker exec -it kafka /opt/bitnami/kafka/bin/kafka-topics.sh --create \
  --topic ck.cotacoes.criadas --partitions 3 --replication-factor 1 --bootstrap-server kafka:9092

# itens.precificados
docker exec -it kafka /opt/bitnami/kafka/bin/kafka-topics.sh --create \
  --topic ck.itens.precificados --partitions 6 --replication-factor 1 --bootstrap-server kafka:9092

# dlq
docker exec -it kafka /opt/bitnami/kafka/bin/kafka-topics.sh --create \
  --topic ck.dlq --partitions 1 --replication-factor 1 --bootstrap-server kafka:9092
```

### 3) Rodar os apps

Em dois terminais, na raiz do projeto:

```bash
# Terminal A — Worker (consumidor e publicador de preços)
cd Ck.Precos.Worker
dotnet run

# Terminal B — Producer (publica uma cotação com 3 itens)
cd Ck.Cotacao.Producer
dotnet run
```

Ou abra a solução e selecione os dois projetos para iniciarem juntos.

Você deve ver algo assim no Worker:

```
≡ Worker de preços iniciado. Ctrl+C para sair.
→ Cotação recebida: 0f6... | Itens: 3
  ✓ i-001 @ B2B-Pecas = 327,45
  ✓ i-001 @ MercadoLivre = 221,10
  ✓ i-001 @ Fornecedor-X = 413,78
  ...
```

### 4) (Opcional) Espionar mensagens pelo CLI

```bash
docker exec -it kafka /opt/bitnami/kafka/bin/kafka-console-consumer.sh \
  --bootstrap-server kafka:9092 --topic ck.itens.precificados --from-beginning
```

---

## 📁 Estrutura do projeto

```
/
├─ Ck.Cotacao.Producer/
│  ├─ Ck.Cotacao.Producer.csproj
│  └─ Program.cs              # top-level statements; os records ficam no final
└─ Ck.Precos.Worker/
   ├─ Ck.Precos.Worker.csproj
   └─ Program.cs              # top-level statements; os records ficam no final
├─ .gitattributes
├─ .gitignore
├─ Ck.Kafka.sln
├─ docker-compose.yml
├─ LICENSE.txt
└─ README.md
```

> **C# gotcha:** se você usa _top-level statements_, **tipos** (`record`, `class`) têm que vir **depois** do código no mesmo arquivo. Eu já deixei assim para não dar o erro: _“As instruções de nível superior precisam preceder as declarações de namespace e de tipo.”_

---

## 🍕 Partitions, keys e essas coisas

- **Partitions** = “shards” do tópico ⇒ dão **paralelismo**.
- **Ordem** é garantida **dentro da mesma partição**. Por isso a **key** é importante:
  - `ck.cotacoes.criadas` → **key = cotacaoId** (eventos da mesma cotação caem juntos)
  - `ck.itens.precificados` → **key = cotacaoId:itemId** (paralelismo por item)
- **Regra do jogo** no consumo em grupo: até **N consumidores ativos** para um tópico com **N partições**.

---

## ✅ Entrega e confiabilidade

- Worker usa `EnableAutoCommit=false` e só dá `Commit` **depois** de publicar os preços ⇒ **at‑least‑once**.
- Coloquei **headers** nos eventos (ex.: `event-type`, `source`) para ajudar em tracing.
- **DLQ** (`ck.dlq`) fica como rota de escape para payloads problemáticos (padrão de mercado).

---

## 🧯 Se der ruim (troubleshooting rápido)

- **Nada conecta do host** → quase sempre é `ADVERTISED_LISTENERS`. Aqui é **localhost:9094** (host) / **kafka:9092** (container).
- **Consumer “não vê” passado** → use `--from-beginning` na CLI ou `AutoOffsetReset=Earliest` no **primeiro start** do group.
- **Porta 9094 ocupada** → troque no compose **e** nos apps.
- **Quero limpar tudo** → `docker compose down -v` (remove volumes também).

---

## 🧭 Próximos passos que eu pretendo

- Um **Agregador** que lê `ck.itens.precificados`, escolhe **o melhor preço por item** e publica `ck.cotacoes.precificadas` (ou grava direto no meu banco).
- Uma **API .NET** para eu consultar a cotação fechada num dashboard React.

---

## 📜 Licença

MIT — segue o jogo.
