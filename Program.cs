// ============================================================
// Job Classifier — Serviço RPC em .NET (gRPC + gRPC-Web)
// Mesmos contratos proto do serviço Node (job-classifier-rpc),
// mesma base MongoDB — sidecar de comparação na porta 8010.
// ============================================================
using JobClassifier.Services;
using Grpc.AspNetCore.Web;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

// Porta via PORT (padrão 8000 — serviço primário desde a promoção do sidecar)
var port = int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var p) ? p : 8000;

builder.WebHost.ConfigureKestrel(o =>
{
    // HTTP/1.1 (healthz + gRPC-Web) e HTTP/2 (gRPC nativo) na mesma porta
    o.ListenAnyIP(port, listen => listen.Protocols = HttpProtocols.Http1AndHttp2);
});

builder.Services.AddGrpc(o => o.EnableDetailedErrors = true);

builder.Services.AddSingleton<IMongoDatabase>(_ => MongoDbFactory.Create());
builder.Services.AddSingleton<ScrapingGrpcClient>();

var app = builder.Build();

// navegadores falam gRPC-Web — habilitado por padrão para todos os serviços gRPC
app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });
app.MapGrpcService<VagasGrpcService>();

// Healthcheck equivalente ao do serviço Node
app.MapGet("/healthz", () => Results.Json(new { status = "ok", service = "job-classifier-dotnet" }));

app.Run();
