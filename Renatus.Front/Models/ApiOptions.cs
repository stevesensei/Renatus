namespace Renatus.Front.Models;

public sealed class ApiOptions
{
    public const string SectionName = "Api";

    public Uri BaseUrl { get; set; } = new("http://localhost:5294");
}
