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
powershell ..un-dotnet-dev.ps1
```

| Porta | Protocolo | Para que serve | Variável |
|---|---|---|---|
| **8000** | HTTP/1.1 | `/healthz` + **gRPC-Web** (navegador, via proxy Vite `/dotnet`) | `PORT` |
| **8003** | **h2c** (HTTP/2 puro, sem TLS) | **gRPC nativo** — `grpcurl`, clientes backend (Python/Go/etc.) | `H2C_PORT` |

Requer `MONGODB_URI` (env, `.env` ou appsettings.json).

Exemplo de gRPC nativo (validado com cliente **Python** chamando este serviço C#):

```python
ch = grpc.insecure_channel("localhost:8003")  # h2c prior-knowledge
stub = job.v1.VagaServiceStub(ch)
stats = stub.GetStats(job.v1.GetStatsRequest(), timeout=15)  # -> total_vagas: 4109
```

## O que cada arquivo faz

| Arquivo | Papel | Equivalente no Node |
|---|---|---|
| `Program.cs` | Kestrel: 8000 HTTP/1.1 (gRPC-Web) + 8003 h2c (gRPC nativo), `/healthz` | `src/server.ts` |
| `Services/VagasGrpcService.cs` | Os 11 RPCs do contrato | `src/routes.ts` + `vagaService.ts` |
| `Services/ScoreService.cs` | Score de compatibilidade (40/20/15/10) | `src/services/scoreService.ts` |
| `Services/ScrapingGrpcClient.cs` | Cliente gRPC → Python (8002) | `src/services/scrapingClient.ts` |
| `Services/MongoDbFactory.cs` | Conexão Mongo (env ou appsettings) | `src/db/mongodb.ts` |

## Codegen

`Grpc.Tools` gera os stubs C# direto de `../proto` no build (item `<Protobuf>`
com `ProtoRoot="..\proto"` e `GrpcServices="Both"`) — não há código gerado
versionado. Diferença-chave vs Node: stubs **tipados em tempo de compilação**.

## Decisão de protocolos (HTTP/1.1 + h2c — por que duas portas)

> **TLS** (Transport Layer Security) é a camada de **criptografia** do HTTPS — é o "S" do HTTPS.
> Aqui está o porquê de cada escolha de protocolo neste serviço:

**O contexto:** gRPC **exige** HTTP/2. Browsers só negociam HTTP/2 **com TLS**
(via **ALPN** — a extensão TLS que combina "vou falar HTTP/2" no handshake). Sem TLS,
um endpoint misto HTTP/1.1+HTTP/2 do Kestrel cai para HTTP/1.1 e loga o warning
*"HTTP/2 is not enabled... TLS is not enabled"* — inofensivo, mas ruído.

**A decisão:** uma porta por consumo, cada uma com o protocolo certo:

| Porta | Protocolo | Quem consome | Por quê |
|---|---|---|---|
| **8000** | **HTTP/1.1** explícito | **Navegador** (gRPC-Web via proxy Vite) + `/healthz` | O navegador fala **gRPC-Web**, desenhado para HTTP/1.1 (trailers codificados no corpo). Não precisa de HTTP/2 — declarar `Http1` documenta a intenção e elimina o warning |
| **8003** | **h2c** (HTTP/2 prior-knowledge, sem TLS) | **gRPC nativo**: `grpcurl`, clientes backend (Python/Go/etc.) | Endpoint **exclusivo** HTTP/2 é a forma suportada de h2c no Kestrel — gRPC nativo sem certificado local |

**Por que não TLS local?** O dev certificate (`dotnet dev-certs https --trust`) habilitaria
HTTP/2 na mesma porta, mas adiciona atrito de demo (URLs https, confiança do certificado,
ajuste no proxy). Como o projeto é **local/demo**, h2c entrega gRPC nativo sem esse custo.
**Em produção, o TLS termina no reverse proxy** (Cloudflare/nginx/YARP) — o mesmo serviço
passa a expor gRPC nativo e gRPC-Web na porta pública sem mudança de código.

**Por que não só gRPC-Web?** Ferramentas nativas falam gRPC puro por HTTP/2. Com a 8003
é possível demonstrar ao vivo:

```bash
grpcurl -plaintext localhost:8003 job.v1.VagaService/GetStats
```

**Validação feita:** cliente **Python** (grpcio) → serviço **C#** via h2c :8003 —
`GetStats` retornou as 4.109 vagas do Atlas; `ListVagas` retornou títulos reais.
Cross-language de ponta a ponta, sem TLS, sem proxy.

**Frontend (sem mudanças):**
```
Vite proxy:  /dotnet/*  →  http://127.0.0.1:8000/*   (vite.config.ts)
Cliente:     createGrpcWebTransport({ baseUrl: '/dotnet' })  (api.ts)
```

## Status/roadmap

- [x] 11 RPCs implementados e paridade validada contra o Node (mesmos dados)
- [x] gRPC-Web habilitado (testado com cliente Connect-ES)
- [x] **Porta h2c 8003 para gRPC nativo** (validado: cliente Python → serviço C#; warning de ALPN eliminado)
- [x] Healthcheck `/healthz`
- [x] **Promovido a serviço primário na 8000** (Node arquivado)
- [ ] `grpc.health.v1` padrão + deadlines configuráveis
