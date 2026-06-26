using Microsoft.Extensions.Configuration;

namespace Playwright.AiFramework;

/// <summary>
/// Static config accessor. Reads appsettings.json from the project root,
/// then overlays any environment variables (using __ as the : separator,
/// e.g. ANTHROPIC__APIKEY overrides Anthropic:ApiKey).
/// </summary>
public static class AppConfig
{
    private static readonly IConfiguration _config;

    static AppConfig()
    {
        var root = FindProjectRoot();
        DotNetEnv.Env.Load(Path.Combine(root, "env/.env"));
        _config = new ConfigurationBuilder()
            .SetBasePath(root)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();
    }

    public static string? Get(string key) => _config[key];

    /// <summary>
    /// Walk up from the test output directory until we find the .csproj file.
    /// This ensures appsettings.json is always read from the project source,
    /// not from the bin/Debug/net8.0 copy.
    /// </summary>
    private static string FindProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.GetFiles("*.csproj").Any())
            dir = dir.Parent;

        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
