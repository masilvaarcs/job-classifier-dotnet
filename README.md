# job-classifier-dotnet — Serviço gRPC primário em ASP.NET Core

Implementação do **`job.v1.VagaService`** em .NET, consumindo **os mesmos
contratos proto** (`proto/job/v1/*.proto`) e o **mesmo MongoDB** da arquitetura.
Criado como sidecar de comparação com o serviço Node e **promovido a serviço
primário (porta 8000)** após a paridade ser validada — o Node foi arquivado em
`_archive/job-classifier-rpc`.

## Executar

```powershell
dotnet run
# ou, resolvendo o MongoDB automaticamente (raiz do projeto):
powershell ..\run-dotnet-dev.ps1
```

Porta **8000** (variável `PORT`). Requer `MONGODB_URI` (env, `.env` ou appsettings.json).

## O que cada arquivo faz

| Arquivo | Papel | Equivalente no Node |
|---|---|---|
| `Program.cs` | Kestrel HTTP/1.1+HTTP/2, gRPC-Web, `/healthz` | `src/server.ts` |
| `Services/VagasGrpcService.cs` | Os 11 RPCs do contrato | `src/routes.ts` + `vagaService.ts` |
| `Services/ScoreService.cs` | Score de compatibilidade (40/20/15/10) | `src/services/scoreService.ts` |
| `Services/ScrapingGrpcClient.cs` | Cliente gRPC → Python (8002) | `src/services/scrapingClient.ts` |
| `Services/MongoDbFactory.cs` | Conexão Mongo (env ou appsettings) | `src/db/mongodb.ts` |

## Codegen

`Grpc.Tools` gera os stubs C# direto de `../proto` no build (item `<Protobuf>`
com `ProtoRoot="..\proto"` e `GrpcServices="Both"`) — não há código gerado
versionado. Diferença-chave vs Node: stubs **tipados em tempo de compilação**.

## gRPC-Web (navegador)

O frontend chama este serviço **por padrão** (sem Envoy) via:

```
Vite proxy:  /dotnet/*  →  http://127.0.0.1:8000/*   (vite.config.ts)
Cliente:     createGrpcWebTransport({ baseUrl: '/dotnet' })  (api.ts)
```

## Status/roadmap

- [x] 11 RPCs implementados e paridade validada contra o Node (mesmos dados)
- [x] gRPC-Web habilitado (testado com cliente Connect-ES)
- [x] Healthcheck `/healthz`
- [x] **Promovido a serviço primário na 8000** (Node arquivado)
- [ ] `grpc.health.v1` padrão + deadlines configuráveis
