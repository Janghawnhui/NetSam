using System.Text.Json;

namespace NetSamLite.Server.Normalization;

public static class NormalizationRuleLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static NormalizationRuleDocument LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<NormalizationRuleDocument>(json, JsonOptions)
            ?? throw new InvalidOperationException($"{path} 파싱 결과가 비어 있습니다.");
    }
}
