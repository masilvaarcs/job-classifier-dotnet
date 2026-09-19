// ============================================================
// Job Classifier — Serviço RPC PRIMÁRIO em .NET (gRPC + gRPC-Web)
// Mesmos contratos proto (job.v1) e mesmo MongoDB dos demais serviços.
// Portas: 8000 = HTTP/1.1 (healthz + gRPC-Web p/ navegador)
//         8003 = h2c/HTTP/2 puro (gRPC nativo — grpcurl, backends)
// Decisões de protocolo detalhadas no README.md deste projeto.
// ============================================================
using JobClassifier.Services;
using Grpc.AspNetCore.Web;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using MongoDB.Driver;
using DotNetEnv;

var builder = WebApplication.CreateBuilder(args);

// Carrega variáveis do arquivo .env.
// Ordem: .env no diretório do serviço (job-classifier-dotnet/.env), depois raiz do workspace (../.env).
// O launcher (run-dotnet-dev.ps1) também pode exportar MONGODB_URI diretamente no ambiente.
var envLocal = Path.Combine(Directory.GetCurrentDirectory(), ".env");
var envRaiz = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", ".env"));
if (File.Exists(envLocal)) Env.Load(envLocal);
else if (File.Exists(envRaiz)) Env.Load(envRaiz);

// Porta via PORT (padrão 8000 — serviço primário desde a promoção do sidecar)
var port = int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var p) ? p : 8000;
// Porta para gRPC NATIVO em claro (h2c, HTTP/2 prior-knowledge) - grpcurl, clientes backend
var h2cPort = int.TryParse(Environment.GetEnvironmentVariable("H2C_PORT"), out var hp) ? hp : 8003;

builder.WebHost.ConfigureKestrel(o =>
{
    // :8000 - HTTP/1.1 apenas: /healthz + gRPC-Web do navegador (nao precisa de HTTP/2).
    // Elimina o warning de ALPN/TLS (inofensivo, mas ruido na demo).
    o.Listen(System.Net.IPAddress.IPv6Any, port, listen => listen.Protocols = HttpProtocols.Http1);
    o.Listen(System.Net.IPAddress.Any, port, listen => listen.Protocols = HttpProtocols.Http1);

    // :8003 - HTTP/2 puro em texto claro (h2c, prior knowledge): gRPC nativo sem TLS.
    // Endpoint EXCLUSIVO HTTP/2 e a forma suportada de h2c no Kestrel.
    o.Listen(System.Net.IPAddress.IPv6Any, h2cPort, listen => listen.Protocols = HttpProtocols.Http2);
    o.Listen(System.Net.IPAddress.Any, h2cPort, listen => listen.Protocols = HttpProtocols.Http2);
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
