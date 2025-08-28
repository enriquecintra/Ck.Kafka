using Confluent.Kafka;
using System.Text.Json;

var consumerConfig = new ConsumerConfig
{
    BootstrapServers = "localhost:9094",
    GroupId = "ck-precos-worker",
    AutoOffsetReset = AutoOffsetReset.Earliest,
    EnableAutoCommit = false
};

var producerConfig = new ProducerConfig
{
    BootstrapServers = "localhost:9094",
    Acks = Acks.All,
    EnableIdempotence = true
};

const string inputTopic = "ck.cotacoes.criadas";
const string outputTopic = "ck.itens.precificados";

string[] fornecedores = new[] { "B2B-Pecas", "MercadoLivre", "Fornecedor-X" };

using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
using var producer = new ProducerBuilder<string, string>(producerConfig).Build();

consumer.Subscribe(inputTopic);
Console.WriteLine("≡ Worker de preços iniciado. Ctrl+C para sair.");

Console.CancelKeyPress += (_, e) => { e.Cancel = true; consumer.Close(); };

while (true)
{
    try
    {
        var cr = consumer.Consume(TimeSpan.FromSeconds(2));
        if (cr is null) continue;

        var evt = JsonSerializer.Deserialize<CotacaoCriadaEvent>(cr.Message.Value)
                  ?? throw new Exception("Payload inválido.");

        Console.WriteLine($"→ Cotação recebida: {evt.CotacaoId} | Itens: {evt.Itens.Length}");

        foreach (var item in evt.Itens)
        {
            foreach (var forn in fornecedores)
            {
                var preco = Math.Round((decimal)(50 + Random.Shared.NextDouble() * 450), 2);
                var prazo = 1 + Random.Shared.Next(0, 7);

                var outEvt = new ItemPrecificadoEvent(
                    CotacaoId: evt.CotacaoId,
                    ItemId: item.ItemId,
                    Fornecedor: forn,
                    Preco: preco,
                    Moeda: "BRL",
                    PrazoDias: prazo,
                    QuotedAtUtc: DateTime.UtcNow
                );

                var value = JsonSerializer.Serialize(outEvt);

                await producer.ProduceAsync(outputTopic, new Message<string, string>
                {
                    Key = $"{evt.CotacaoId}:{item.ItemId}",
                    Value = value,
                    Headers = new Headers
                    {
                        new Header("event-type", System.Text.Encoding.UTF8.GetBytes("item-precificado")),
                        new Header("source", System.Text.Encoding.UTF8.GetBytes("precos-worker"))
                    }
                });

                Console.WriteLine($"  ✓ {item.ItemId} @ {forn} = {preco}");
            }
        }

        consumer.Commit(cr);
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"! Erro: {ex.Message} (enviaria para uma DLQ aqui)");
        Console.ResetColor();
    }
}

// --- tipos no FINAL do arquivo ---
record CotacaoItem(string ItemId, string Descricao, int Qtd, string? Oem);
record CotacaoCriadaEvent(string CotacaoId, string ClienteId, CotacaoItem[] Itens, DateTime CreatedAtUtc);
record ItemPrecificadoEvent(string CotacaoId, string ItemId, string Fornecedor, decimal Preco, string Moeda, int PrazoDias, DateTime QuotedAtUtc);
