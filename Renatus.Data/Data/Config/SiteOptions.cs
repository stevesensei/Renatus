namespace Renatus.Host.Data.Config;

public sealed class SiteOptions
{
    public const string SectionName = "Site";

    public string HeroImage { get; set; } = "";
    public string Title { get; set; } = "Renatus";
    public string Description { get; set; } = "Minecraft authentication for your community.";
    public string PublicBaseUrl { get; set; } = "https://localhost";
    public List<SiteButtonOptions> Buttons { get; set; } = [];
}

public sealed class SiteAnnouncementOptions
{
    public string Title { get; set; } = "Welcome to Renatus";
    public string Body { get; set; } = "Sign in to continue.";
    public string Image { get; set; } = "";
}

public sealed class SiteButtonOptions
{
    public string Label { get; set; } = "";
    public string Url { get; set; } = "";
    public string? Style { get; set; }
    public string? Target { get; set; }
}
