using System.Text;
using Newtonsoft.Json.Linq;

namespace Playwright.AiFramework.AI;

/// <summary>
/// Thin HTTP wrapper around the Anthropic /v1/messages endpoint.
/// No SDK dependency — pure HttpClient so the call is easy to inspect
/// during an interview demo (just add a Console.WriteLine on the raw response).
/// </summary>
public class AnthropicClient
{
    private readonly HttpClient _http;

    private const string ApiUrl     = "https://api.anthropic.com/v1/messages";
    private const string Model      = "claude-sonnet-4-6";
    private const string ApiVersion = "2023-06-01";

    public AnthropicClient(string apiKey)
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", ApiVersion);
    }

    /// <summary>
    /// Send a Gherkin scenario to Claude and get back a raw JSON string
    /// containing the list of Playwright actions.
    /// </summary>
    public async Task<string> GenerateActionsAsync(string scenarioText)
    {
        var requestBody = new
        {
            model      = Model,
            max_tokens = 1024,
            system     = SystemPrompts.TestActionGenerator,
            messages   = new[] { new { role = "user", content = scenarioText } }
        };

        var json    = JsonConvert.SerializeObject(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync(ApiUrl, content);
        var body     = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Anthropic API returned {(int)response.StatusCode} {response.StatusCode}:\n{body}");

        // Response shape: { "content": [ { "type": "text", "text": "..." } ] }
        var text = JObject.Parse(body)["content"]?[0]?["text"]?.ToString()
                   ?? throw new InvalidOperationException(
                       $"Unexpected Anthropic API response shape:\n{body}");

        return text;
    }
}
