// ============================================================
// Job Classifier — implementação do VagaService em .NET
// Porta 1:1 do job-classifier-rpc/src/services/vagaService.ts
// e routes.ts. Mesma coleção 'vagas' no MongoDB, mesma semântica.
// ============================================================
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Job.V1;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Text.RegularExpressions;

namespace JobClassifier.Services;

public class VagasGrpcService : Job.V1.VagaService.VagaServiceBase
{
    private static readonly Dictionary<string, string> TipoMap = new()
    {
        ["🟢 Remoto"] = "REMOTO",
        ["🟡 Híbrido"] = "HIBRIDO",
        ["🟠 Presencial"] = "PRESENCIAL",
    };

    private static readonly Dictionary<string, string> StatusMap = new()
    {
        ["Pendente"] = "pendente",
        ["Candidatado"] = "candidatado",
        ["Entrevista"] = "entrevista",
        ["Rejeitado"] = "rejeitado",
        ["Contratado"] = "contratado",
    };

    private readonly IMongoDatabase _db;
    private readonly ScrapingGrpcClient _scraping;

    public VagasGrpcService(IMongoDatabase db, ScrapingGrpcClient scraping)
    {
        _db = db;
        _scraping = scraping;
    }

    private IMongoCollection<BsonDocument> Col => _db.GetCollection<BsonDocument>("vagas");

    // ---------- helpers ----------

    private static Vaga DocToVaga(BsonDocument d)
    {
        var vaga = new Vaga
        {
            Id = d.GetValue("id", 0).ToInt64(),
            Titulo = d.GetValue("titulo", "").AsStringOrDefault(),
            Empresa = d.GetValue("empresa", "").AsStringOrDefault(),
            Localizacao = d.GetValue("localizacao", "").AsStringOrDefault(),
            Salario = d.GetValue("salario", "").AsStringOrDefault(),
            Modalidade = d.GetValue("modalidade", "").AsStringOrDefault(),
            Publicado = d.GetValue("publicado", "").AsStringOrDefault(),
            TipoTrabalho = d.GetValue("tipo_trabalho", "").AsStringOrDefault(),
            Descricao = d.GetValue("descricao", "").AsStringOrDefault(),
            Link = d.GetValue("link", "").AsStringOrDefault(),
            JobId = d.GetValue("job_id", "").AsStringOrDefault(),
            Plataforma = d.GetValue("plataforma", "").AsStringOrDefault(),
            StatusUsuario = d.GetValue("status_usuario", "").AsStringOrDefault(),
            Ignorada = d.GetValue("ignorada", false).ToBoolean(),
            PraMim = d.GetValue("pra_mim", false).ToBoolean(),
            ScoreCompatibilidade = d.GetValue("score_compatibilidade", 0).ToInt32(),
            Ativa = d.GetValue("ativa", true).ToBoolean(),
            Notas = d.GetValue("notas", "").AsStringOrDefault(),
        };

        if (d.TryGetValue("data_publicacao", out var dp) && dp.IsBsonDateTime) vaga.DataPublicacao = Timestamp.FromDateTime(dp.ToUniversalTime());
        if (d.TryGetValue("data_coleta", out var dc) && dc.IsBsonDateTime) vaga.DataColeta = Timestamp.FromDateTime(dc.ToUniversalTime());
        if (d.TryGetValue("created_at", out var ca) && ca.IsBsonDateTime) vaga.CreatedAt = Timestamp.FromDateTime(ca.ToUniversalTime());
        if (d.TryGetValue("updated_at", out var ua) && ua.IsBsonDateTime) vaga.UpdatedAt = Timestamp.FromDateTime(ua.ToUniversalTime());
        if (d.TryGetValue("data_verificacao", out var dv) && dv.IsBsonDateTime) vaga.DataVerificacao = Timestamp.FromDateTime(dv.ToUniversalTime());

        return vaga;
    }

    private static FilterDefinition<BsonDocument> BuildFilter(Filtros? f)
    {
        var q = new List<FilterDefinition<BsonDocument>>();
        var fb = Builders<BsonDocument>.Filter;

        if (f == null) return fb.Empty;

        if (f.Dias > 0)
        {
            q.Add(fb.Gte("data_coleta", DateTime.UtcNow.AddDays(-f.Dias)));
        }

        if (!string.IsNullOrEmpty(f.Plataforma) && f.Plataforma != "Todas")
        {
            q.Add(fb.Eq("plataforma", f.Plataforma));
        }

        if (!string.IsNullOrEmpty(f.Tipo) && f.Tipo != "Todos")
        {
            var tipo = TipoMap.TryGetValue(f.Tipo, out var t) ? t : f.Tipo;
            q.Add(fb.Eq("tipo_trabalho", tipo));
        }

        if (!string.IsNullOrEmpty(f.Status) && f.Status != "Todos")
        {
            var status = StatusMap.TryGetValue(f.Status, out var s) ? s : f.Status;
            q.Add(fb.Eq("status_usuario", status));
        }

        if (!string.IsNullOrEmpty(f.Busca))
        {
            var rx = new BsonRegularExpression(Regex.Escape(f.Busca), "i");
            q.Add(fb.Or(
                fb.Regex("titulo", rx),
                fb.Regex("empresa", rx),
                fb.Regex("localizacao", rx),
                fb.Regex("descricao", rx)));
        }

        if (!f.Ignoradas)
        {
            q.Add(fb.Eq("ignorada", false));
        }

        if (f.ApenasPraMim)
        {
            q.Add(fb.Eq("pra_mim", true));
        }

        return q.Count == 0 ? fb.Empty : fb.And(q);
    }

    // ---------- RPCs: Vagas ----------

    public override async Task<ListVagasResponse> ListVagas(ListVagasRequest request, ServerCallContext context)
    {
        var filter = BuildFilter(request.Filtros);

        var page = Math.Max(1, request.Filtros?.Page ?? 1);
        var perPage = Math.Clamp(request.Filtros?.PerPage ?? 20, 1, 200);

        var total = await Col.CountDocumentsAsync(filter);

        var docs = await Col
            .Find(filter)
            .Sort(Builders<BsonDocument>.Sort.Descending("score_compatibilidade").Descending("data_coleta"))
            .Skip((page - 1) * perPage)
            .Limit(perPage)
            .ToListAsync();

        var response = new ListVagasResponse
        {
            Paginacao = new Paginacao
            {
                Page = page,
                PerPage = perPage,
                Total = total,
                TotalPages = (int)Math.Ceiling((double)total / perPage) switch { 0 => 1, var t => t },
            },
        };
        response.Vagas.AddRange(docs.Select(DocToVaga));
        return response;
    }

    public override async Task<UpdateVagaResponse> UpdateVaga(UpdateVagaRequest request, ServerCallContext context)
    {
        var vaga = await UpdateVagaInternal(request);
        if (vaga == null) throw new RpcException(new Status(StatusCode.NotFound, "not_found"));
        return new UpdateVagaResponse { Vaga = vaga };
    }

    private async Task<Vaga?> UpdateVagaInternal(UpdateVagaRequest request)
    {
        var fb = Builders<BsonDocument>.Filter;
        var ub = Builders<BsonDocument>.Update;
        var set = new List<UpdateDefinition<BsonDocument>> { ub.Set("updated_at", DateTime.UtcNow) };

        if (request.HasStatusUsuario) set.Add(ub.Set("status_usuario", request.StatusUsuario));
        if (request.HasNotas) set.Add(ub.Set("notas", request.Notas));
        if (request.HasIgnorada) set.Add(ub.Set("ignorada", request.Ignorada));
        if (request.HasPraMim) set.Add(ub.Set("pra_mim", request.PraMim));

        var doc = await Col.FindOneAndUpdateAsync(
            fb.Eq("id", request.Id),
            Builders<BsonDocument>.Update.Combine(set),
            new FindOneAndUpdateOptions<BsonDocument, BsonDocument> { ReturnDocument = ReturnDocument.After });

        return doc == null ? null : DocToVaga(doc);
    }

    public override async Task<IgnorarVagaResponse> IgnorarVaga(IgnorarVagaRequest request, ServerCallContext context)
    {
        var vaga = await UpdateVagaInternal(new UpdateVagaRequest { Id = request.Id, Ignorada = true });
        if (vaga == null) throw new RpcException(new Status(StatusCode.NotFound, "not_found"));
        return new IgnorarVagaResponse { Vaga = vaga };
    }

    public override async Task<RestaurarVagaResponse> RestaurarVaga(RestaurarVagaRequest request, ServerCallContext context)
    {
        var vaga = await UpdateVagaInternal(new UpdateVagaRequest { Id = request.Id, Ignorada = false });
        if (vaga == null) throw new RpcException(new Status(StatusCode.NotFound, "not_found"));
        return new RestaurarVagaResponse { Vaga = vaga };
    }

    public override async Task<ToggleFavoritarResponse> ToggleFavoritar(ToggleFavoritarRequest request, ServerCallContext context)
    {
        var fb = Builders<BsonDocument>.Filter;
        var atual = await Col.Find(fb.Eq("id", request.Id)).FirstOrDefaultAsync();
        if (atual == null) throw new RpcException(new Status(StatusCode.NotFound, "not_found"));

        var vaga = await UpdateVagaInternal(new UpdateVagaRequest { Id = request.Id, PraMim = !atual.GetValue("pra_mim", false).ToBoolean() });
        if (vaga == null) throw new RpcException(new Status(StatusCode.NotFound, "not_found"));
        return new ToggleFavoritarResponse { Vaga = vaga };
    }

    public override async Task<RecalcularScoresResponse> RecalcularScores(RecalcularScoresRequest request, ServerCallContext context)
    {
        var fb = Builders<BsonDocument>.Filter;
        var modelo = Builders<BsonDocument>.Projection
            .Include("id").Include("titulo").Include("descricao").Include("empresa");

        var docs = await Col.Find(fb.Empty).Project<BsonDocument>(modelo).ToListAsync();

        var updates = docs.Select(d =>
            new UpdateOneModel<BsonDocument>(
                fb.Eq("id", d.GetValue("id", 0).ToInt64()),
                Builders<BsonDocument>.Update
                    .Set("score_compatibilidade",
                        ScoreService.CalcularScore(
                            d.GetValue("titulo", "").AsStringOrDefault(),
                            d.GetValue("descricao", "").AsStringOrDefault(),
                            d.GetValue("empresa", "").AsStringOrDefault()))
                    .Set("updated_at", DateTime.UtcNow)));

        var count = updates.Count();
        if (count > 0) await Col.BulkWriteAsync(updates);

        return new RecalcularScoresResponse { VagasAtualizadas = count };
    }

    // ---------- RPCs: Stats / Plataformas ----------

    public override async Task<GetStatsResponse> GetStats(GetStatsRequest request, ServerCallContext context)
    {
        var porPlataforma = await Col.Aggregate()
            .Group(new BsonDocument { { "_id", "$plataforma" }, { "total", new BsonDocument("$sum", 1) } })
            .ToListAsync();
        var porTipo = await Col.Aggregate()
            .Group(new BsonDocument { { "_id", "$tipo_trabalho" }, { "total", new BsonDocument("$sum", 1) } })
            .ToListAsync();
        var porStatus = await Col.Aggregate()
            .Group(new BsonDocument { { "_id", "$status_usuario" }, { "total", new BsonDocument("$sum", 1) } })
            .ToListAsync();
        var total = await Col.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
        var ultima = await Col.Find(FilterDefinition<BsonDocument>.Empty)
            .Sort(Builders<BsonDocument>.Sort.Descending("data_coleta"))
            .Project(Builders<BsonDocument>.Projection.Include("data_coleta"))
            .FirstOrDefaultAsync();

        var stats = new Stats
        {
            TotalVagas = total,
            TotalPlataformas = porPlataforma.Count,
            UltimaColeta = ultima != null && ultima.TryGetValue("data_coleta", out var dc) && dc.IsBsonDateTime
                ? dc.ToUniversalTime().ToString("o")
                : "N/A",
        };
        foreach (var r in porPlataforma) stats.VagasPorPlataforma[r.GetValue("_id", "").AsStringOrDefault()] = r.GetValue("total", 0).ToInt64();
        foreach (var r in porTipo) stats.VagasPorTipo[r.GetValue("_id", "").AsStringOrDefault()] = r.GetValue("total", 0).ToInt64();
        foreach (var r in porStatus) stats.VagasPorStatus[r.GetValue("_id", "").AsStringOrDefault()] = r.GetValue("total", 0).ToInt64();

        return new GetStatsResponse { Stats = stats };
    }

    public override async Task<ListPlataformasResponse> ListPlataformas(ListPlataformasRequest request, ServerCallContext context)
    {
        var rows = await Col.Aggregate()
            .Group(new BsonDocument
            {
                { "_id", "$plataforma" },
                { "total", new BsonDocument("$sum", 1) },
                { "ultima", new BsonDocument("$max", "$data_coleta") },
            })
            .Sort(new BsonDocument("total", -1))
            .ToListAsync();

        var response = new ListPlataformasResponse();
        foreach (var r in rows)
        {
            response.Plataformas.Add(new Plataforma
            {
                Nome = r.GetValue("_id", "").AsStringOrDefault(),
                Total = r.GetValue("total", 0).ToInt64(),
                UltimaColeta = r.Contains("ultima") && r["ultima"].IsBsonDateTime
                    ? r["ultima"].ToUniversalTime().ToString("o")
                    : "N/A",
            });
        }
        return response;
    }

    // ---------- RPCs: Scraping (repassa ao Python via gRPC) ----------

    public override async Task<StartScrapingResponse> StartScraping(StartScrapingRequest request, ServerCallContext context)
    {
        try
        {
            var resp = await _scraping.Client.StartScrapingAsync(
                new ScrapingServiceStartScrapingRequest { Plataforma = request.Plataforma ?? "" },
                deadline: DateTime.UtcNow.AddSeconds(10));
            var response = new StartScrapingResponse
            {
                Iniciado = resp.Iniciado,
                Mensagem = resp.Mensagem,
            };
            response.Plataformas.AddRange(resp.Plataformas);
            return response;
        }
        catch (RpcException ex)
        {
            throw new RpcException(new Status(StatusCode.Unavailable,
                $"Microserviço de scraping indisponível: {ex.Status.Detail}"));
        }
    }

    public override async Task<GetScrapingStatusResponse> GetScrapingStatus(GetScrapingStatusRequest request, ServerCallContext context)
    {
        try
        {
            var resp = await _scraping.Client.GetStatusAsync(
                new ScrapingServiceGetStatusRequest(),
                deadline: DateTime.UtcNow.AddSeconds(10));
            var response = new GetScrapingStatusResponse();
            response.Plataformas.AddRange(resp.Plataformas);
            return response;
        }
        catch (RpcException ex)
        {
            throw new RpcException(new Status(StatusCode.Unavailable,
                $"Microserviço de scraping indisponível: {ex.Status.Detail}"));
        }
    }

    // ---------- RPC: Importação ----------

    public override async Task<ImportVagasResponse> ImportVagas(ImportVagasRequest request, ServerCallContext context)
    {
        int importadas = 0, atualizadas = 0;
        var fb = Builders<BsonDocument>.Filter;

        var maxDoc = await Col.Find(fb.Empty)
            .Sort(Builders<BsonDocument>.Sort.Descending("id"))
            .Limit(1)
            .Project(Builders<BsonDocument>.Projection.Include("id"))
            .FirstOrDefaultAsync();
        var proximoId = maxDoc != null ? maxDoc.GetValue("id", 0).ToInt64() + 1 : 1;

        foreach (var v in request.Vagas)
        {
            var score = ScoreService.CalcularScore(v.Titulo, v.Descricao ?? "", v.Empresa ?? "");
            var now = DateTime.UtcNow;

            var update = Builders<BsonDocument>.Update
                .Set("titulo", v.Titulo)
                .Set("empresa", v.Empresa ?? "")
                .Set("localizacao", v.Localizacao ?? "")
                .Set("salario", v.Salario ?? "")
                .Set("modalidade", v.Modalidade ?? "")
                .Set("publicado", v.Publicado ?? "")
                .Set("data_publicacao", string.IsNullOrEmpty(v.DataPublicacao) ? (BsonValue?)BsonNull.Value : DateTime.Parse(v.DataPublicacao).ToUniversalTime())
                .Set("tipo_trabalho", string.IsNullOrEmpty(v.TipoTrabalho) ? "NAO IDENTIFICADO" : v.TipoTrabalho)
                .Set("descricao", v.Descricao ?? "")
                .Set("updated_at", now)
                .Set("data_coleta", now)
                .SetOnInsert("id", proximoId++)
                .SetOnInsert("link", v.Link)
                .SetOnInsert("job_id", v.JobId ?? "")
                .SetOnInsert("plataforma", v.Plataforma)
                .SetOnInsert("created_at", now)
                .SetOnInsert("status_usuario", "pendente")
                .SetOnInsert("ignorada", false)
                .SetOnInsert("pra_mim", false)
                .SetOnInsert("score_compatibilidade", score)
                .SetOnInsert("data_verificacao", BsonNull.Value)
                .SetOnInsert("ativa", true)
                .SetOnInsert("notas", "");

            var result = await Col.UpdateOneAsync(fb.Eq("link", v.Link), update, new UpdateOptions { IsUpsert = true });
            if (result.UpsertedId != null) importadas++; else atualizadas++;
        }

        return new ImportVagasResponse { Importadas = importadas, Atualizadas = atualizadas };
    }
}

internal static class BsonExtensions
{
    public static string AsStringOrDefault(this BsonValue v) => v.IsString ? v.AsString : "";
}
