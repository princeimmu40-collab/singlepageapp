using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        string? githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        string? repo = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
        string? prNumber = Environment.GetEnvironmentVariable("PR_NUMBER");
        string? geminiApiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");

        if (string.IsNullOrEmpty(prNumber)) return;

        using HttpClient client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DotNet-PR-Bot");

        // 1. Get PR Diff
        string diffUrl = $"https://api.github.com/repos/{repo}/pulls/{prNumber}";
        var diffRequest = new HttpRequestMessage(HttpMethod.Get, diffUrl);
        diffRequest.Headers.Authorization = new AuthenticationHeaderValue("token", githubToken);
        diffRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3.diff"));

        var diffResponse = await client.SendAsync(diffRequest);
        string diffData = await diffResponse.Content.ReadAsStringAsync();

        if (string.IsNullOrWhiteSpace(diffData)) return;

        // 2. Call Gemini
        string aiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={geminiApiKey}";
        string prompt = $"Review this code diff. Identify bugs and suggest improvements in clean bullet points:\n\n{diffData}";

        var aiPayload = new { contents = new[] { new { parts = new[] { new { text = prompt } } } } };
        var aiResponse = await client.PostAsJsonAsync(aiUrl, aiPayload);
        var aiResultJson = await aiResponse.Content.ReadAsStringAsync();
        
        string reviewText = "AI Review failed.";
        try
        {
            using JsonDocument doc = JsonDocument.Parse(aiResultJson);
            reviewText = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? reviewText;
        }
        catch {}

        // 3. Post Comment
        string commentUrl = $"https://api.github.com/repos/{repo}/issues/{prNumber}/comments";
        var commentPayload = new { body = $"### 🤖 AI Bot Code Review (.NET)\n\n{reviewText}" };

        var commentRequest = new HttpRequestMessage(HttpMethod.Post, commentUrl);
        commentRequest.Headers.Authorization = new AuthenticationHeaderValue("token", githubToken);
        commentRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
        commentRequest.Content = new StringContent(JsonSerializer.Serialize(commentPayload), Encoding.UTF8, "application/json");

        await client.SendAsync(commentRequest);
    }
}
