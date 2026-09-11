// Job Classifier — cliente gRPC para o microserviço Python (porta 8002)
// Espelha job-classifier-rpc/src/services/scrapingClient.ts.
// gRPC nativo exige HTTP/2 — Grpc.Net.Client suporta cleartext para localhost
// via AppContext switch (caso exato deste microserviço, sem TLS).
using Grpc.Net.Client;
using Job.V1;

namespace JobClassifier.Services;

public class ScrapingGrpcClient
{
    private readonly Lazy<GrpcChannel> _channel;
    private readonly Lazy<ScrapingService.ScrapingServiceClient> _client;

    public ScrapingGrpcClient()
    {
        // Permite HTTP/2 sem TLS (h2c) — necessário para o Python local na 8002
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

        _channel = new Lazy<GrpcChannel>(() =>
        {
            var url = Environment.GetEnvironmentVariable("PYTHON_GRPC_URL") ?? "http://127.0.0.1:8002";
            return GrpcChannel.ForAddress(url, new GrpcChannelOptions
            {
                MaxReceiveMessageSize = 16 * 1024 * 1024,
            });
        });

        _client = new Lazy<ScrapingService.ScrapingServiceClient>(
            () => new ScrapingService.ScrapingServiceClient(_channel.Value));
    }

    public ScrapingService.ScrapingServiceClient Client => _client.Value;
}
