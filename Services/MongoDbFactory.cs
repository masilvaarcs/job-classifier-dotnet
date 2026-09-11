// Job Classifier — fábrica do cliente MongoDB (espelha job-classifier-rpc/src/db/mongodb.ts)
// Ordem de precedência: variável de ambiente > appsettings.json.
using MongoDB.Driver;

namespace JobClassifier.Services;

public static class MongoDbFactory
{
    public static IMongoDatabase Create()
    {
        var uri = Environment.GetEnvironmentVariable("MONGODB_URI");

        if (string.IsNullOrWhiteSpace(uri))
        {
            throw new InvalidOperationException(
                "MONGODB_URI não configurada (env MONGODB_URI ou appsettings.json).");
        }

        var dbName = Environment.GetEnvironmentVariable("MONGODB_DB") ?? "job_tracker";

        // FromConnectionString aceita mongodb:// e mongodb+srv://
        var settings = MongoClientSettings.FromConnectionString(uri);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(8);
        settings.ApplicationName = "job-classifier-dotnet";

        var client = new MongoClient(settings);
        return client.GetDatabase(dbName);
    }
}
