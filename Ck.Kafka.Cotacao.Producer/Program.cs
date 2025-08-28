using System.Text.Json;
using Confluent.Kafka;

var config = new ProducerConfig
{
    BootstrapServers = "localhost:9094",
    Acks = Acks.All,
    EnableIdempotence = true
};

const string topic = "ck.cotacoes.criadas";
using var producer = new ProducerBuilder<string, string>(config).Build();

// Exemplo de cotação com 3 itens
var evt = new CotacaoCriadaEvent(
    CotacaoId: Guid.NewGuid().ToString("N"),
    ClienteId: "locadora-xyz",
    Itens: new[]
    {
        new CotacaoItem("i-001","Pastilha de Freio Dianteira", 2, "OEM-ABC"),
        new CotacaoItem("i-002","Filtro de Óleo", 1, "OEM-FO123"),
        new CotacaoItem("i-003","Amortecedor Traseiro", 2, "OEM-AMT456"),
    },
    CreatedAtUtc: DateTime.UtcNow
);

var payload = JsonSerializer.Serialize(evt);
var headers = new Headers
{
    new Header("event-type", System.Text.Encoding.UTF8.GetBytes("cotacao-criada")),
    new Header("event-id", System.Text.Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N")))
};

var delivery = await producer.ProduceAsync(topic, new Message<string, string>
{
    Key = evt.CotacaoId,
    Value = payload,
    Headers = headers
});

Console.WriteLine($"✓ Cotação publicada: {delivery.TopicPartitionOffset} | {evt.CotacaoId}");

// --- tipos no FINAL do arquivo ---
record CotacaoItem(string ItemId, string Descricao, int Qtd, string? Oem);
record CotacaoCriadaEvent(string CotacaoId, string ClienteId, CotacaoItem[] Itens, DateTime CreatedAtUtc);
