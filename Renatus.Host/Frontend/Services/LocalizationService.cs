using System.Globalization;
using System.Text.Json;

namespace Renatus.Host.Frontend.Services;

public sealed class LocalizationService
{
    private readonly IReadOnlyDictionary<string, string> _chinese;
    private readonly IReadOnlyDictionary<string, string> _english;

    public LocalizationService(IWebHostEnvironment environment)
    {
        _chinese = Load(environment.WebRootPath, "zh-CN");
        _english = Load(environment.WebRootPath, "en-US");
    }

    public string this[string key] => Get(key);

    public string Get(string key)
    {
        var preferred = CultureInfo.CurrentUICulture.Name.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            ? _english
            : _chinese;
        var fallback = ReferenceEquals(preferred, _chinese) ? _english : _chinese;
        return preferred.GetValueOrDefault(key) ?? fallback.GetValueOrDefault(key) ?? key;
    }

    private static IReadOnlyDictionary<string, string> Load(string webRootPath, string culture)
    {
        var path = Path.Combine(webRootPath, "locales", $"{culture}.json");
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidDataException($"Localization resource '{path}' is empty.");
    }
}
