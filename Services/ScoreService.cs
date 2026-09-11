// Job Classifier — Score de compatibilidade (porta 1:1 do scoreService.ts)
// Backend (.NET/C#) = 40 | Frontend = 20 | DB = 15 | Python = 10
// Remoto = 15 / RS = 10 | Senioridade = 5 | Máximo 100
using System.Text.RegularExpressions;

namespace JobClassifier.Services;

public static partial class ScoreService
{
    [GeneratedRegex(@"\b(c#|\.net|asp\.net|dotnet|web api|entity framework)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BackendRegex();

    [GeneratedRegex(@"\b(angular|typescript|react|vue)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FrontendRegex();

    [GeneratedRegex(@"\b(sql server|oracle|postgresql|mysql|postgres)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DatabaseRegex();

    [GeneratedRegex(@"\bpython|django|flask|fastapi\b", RegexOptions.IgnoreCase)]
    private static partial Regex PythonRegex();

    [GeneratedRegex(@"remoto|remote|home office|teletrabalho", RegexOptions.IgnoreCase)]
    private static partial Regex RemotoRegex();

    [GeneratedRegex(@"\b(gravataí|porto alegre|poa|rs)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RsRegex();

    [GeneratedRegex(@"\b(sênior|senior|lead|pleno)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SeniorRegex();

    public static int CalcularScore(string titulo, string descricao, string empresa)
    {
        var score = 0;
        var texto = $"{titulo} {descricao} {empresa}".ToLowerInvariant();

        // Backend (.NET/C#/ASP.NET) = 40 pts
        if (BackendRegex().IsMatch(texto)) score += 40;

        // Frontend (Angular/TypeScript) = 20 pts
        if (FrontendRegex().IsMatch(texto)) score += 20;

        // Banco de dados = 15 pts
        if (DatabaseRegex().IsMatch(texto)) score += 15;

        // Python = 10 pts
        if (PythonRegex().IsMatch(texto)) score += 10;

        // Remoto ou localização = 15 pts
        if (RemotoRegex().IsMatch(texto)) score += 15;
        else if (RsRegex().IsMatch(texto)) score += 10;

        // Senioridade = 5 pts
        if (SeniorRegex().IsMatch(texto)) score += 5;

        return Math.Min(score, 100);
    }
}
