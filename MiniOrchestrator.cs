// MiniOrchestrator.cs — a minimal generator <-> reviewer loop between two LLMs.
//   Generator: Claude (Anthropic API)     Reviewer: GPT (OpenAI API)
//
// This is a library file, not an entry point — run the demo via `dotnet run`
// in this project, which launches Program.cs and prompts you to pick this
// pattern. See MiniOrchestratorApp below for how it's wired up.
//
// Requires .NET 8+. No NuGet packages — raw HttpClient + System.Text.Json on purpose,
// so you can see that "two AIs talking" is really just your code copying strings
// between two HTTP endpoints.
//
// Anthropic API docs: https://docs.claude.com/en/api/overview

// ---------------------------------------------------------------------------
// Entry point for this pattern, called from Program.cs once the user picks
// "Mini". Wires the two sub-agents together and prints the final result.
// ---------------------------------------------------------------------------

using System.Text;
using System.Text.Json;

// StringBuilder is in System.Text, which is already imported here.
static class MiniOrchestratorApp
{
    public const string DefaultTask =
        "Write a C# extension method that truncates a string to a max length on a word boundary, handling null/empty input.";

    public static async Task RunAsync(string task, string anthropicKey, string openaiKey)
    {
        var result = await new Orchestrator(
                generator: new ClaudeGenerator(anthropicKey),
                reviewer:  new GptReviewer(openaiKey),
                maxRounds: 4)
            .RunAsync(task);

        Console.WriteLine($"\n=== finished after {result.Rounds} round(s), approved: {result.Approved} ===\n");
        Console.WriteLine(result.Code);
    }
}


// ---------------------------------------------------------------------------
// The orchestrator. Plain code. Owns the state (canonical `code` string),
// the routing (who gets called with what), and the termination conditions.
// The models never see each other — they only see prompts this class builds.
// ---------------------------------------------------------------------------

record Review(bool Approved, List<string> Issues);
record OrchestrationResult(string Code, bool Approved, int Rounds);

class Orchestrator(ClaudeGenerator generator, GptReviewer reviewer, int maxRounds)
{
    public async Task<OrchestrationResult> RunAsync(string task)
    {
        Console.WriteLine($"Task: {task}\n--- round 1: generating ---");
        string code = await generator.GenerateAsync(task, previousCode: null, feedback: null);
        var lastIssues = new List<string>();

        for (int round = 1; round <= maxRounds; round++)
        {
            Console.WriteLine($"--- round {round}: reviewing ---");
            Review review = await reviewer.ReviewAsync(task, code);

            if (review.Approved)
            {
                Console.WriteLine("Reviewer approved.");
                return new(code, Approved: true, round);
            }

            Console.WriteLine($"Reviewer found {review.Issues.Count} issue(s):");
            foreach (var issue in review.Issues) Console.WriteLine($"  - {issue}");

            // No-progress guard: identical issues twice in a row means the loop
            // is stuck (oscillating fixes or an unfixable complaint). Bail out.
            if (review.Issues.SequenceEqual(lastIssues))
            {
                Console.WriteLine("Same issues twice in a row — stopping to avoid a loop.");
                return new(code, Approved: false, round);
            }
            lastIssues = review.Issues;

            if (round == maxRounds) break; // budget exhausted — don't pay for one more generation

            Console.WriteLine($"--- round {round + 1}: revising ---");
            code = await generator.GenerateAsync(task, code, review.Issues);
        }

        return new(code, Approved: false, maxRounds);
    }
}


// ---------------------------------------------------------------------------
// Generator role: Claude. Stateless — every call gets exactly the context the
// orchestrator chooses to give it (task + current code + latest feedback),
// never the whole transcript.
// ---------------------------------------------------------------------------

class ClaudeGenerator(string apiKey)
{
    const string SystemPrompt =
        "You are a senior C# engineer. Reply with ONLY the complete, compilable code. " +
        "No markdown fences, no explanations, no commentary.";

    static readonly HttpClient Http = new();

    public async Task<string> GenerateAsync(string task, string? previousCode, List<string>? feedback)
    {
        var prompt = new StringBuilder($"Task:\n{task}\n");
        if (previousCode is not null && feedback is not null)
        {
            prompt.AppendLine($"\nYour current code:\n{previousCode}\n");
            prompt.AppendLine("A reviewer found these issues. Fix ALL of them and output the complete revised code:");
            foreach (var issue in feedback) prompt.AppendLine($"- {issue}");
        }

        var body = JsonSerializer.Serialize(new
        {
            model = "claude-sonnet-4-6",
            max_tokens = 4000,
            system = SystemPrompt,
            messages = new[] { new { role = "user", content = prompt.ToString() } }
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        using var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var text = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString()!;
        return StripFences(text);
    }

    // Models sometimes wrap output in ```csharp fences despite instructions — strip defensively.
    static string StripFences(string s)
    {
        s = s.Trim();
        if (!s.StartsWith("```")) return s;
        int firstNewline = s.IndexOf('\n');
        int lastFence = s.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewline >= 0 && lastFence > firstNewline
            ? s[(firstNewline + 1)..lastFence].Trim()
            : s;
    }
}


// ---------------------------------------------------------------------------
// Reviewer role: GPT. The contract is a STRICT JSON verdict so the
// orchestrator can branch deterministically instead of parsing prose.
// ---------------------------------------------------------------------------

class GptReviewer(string apiKey)
{
    const string SystemPrompt =
        "You are a strict C# code reviewer. Given a task and code, respond with ONLY a JSON object: " +
        "{\"approved\": bool, \"issues\": [string]}. " +
        "Approve ONLY if the code fully solves the task, compiles, handles edge cases, and follows C# conventions. " +
        "Each issue must be a single concrete, actionable sentence. " +
        "Do NOT invent stylistic nitpicks to seem thorough — if the code is genuinely fine, approve it with an empty issues array.";

    static readonly HttpClient Http = new();

    public async Task<Review> ReviewAsync(string task, string code)
    {
        var body = JsonSerializer.Serialize(new
        {
            model = "gpt-5.6", // swap in whichever OpenAI model you prefer
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = $"Task:\n{task}\n\nCode to review:\n{code}" }
            }
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Authorization", $"Bearer {apiKey}");

        using var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var content = doc.RootElement.GetProperty("choices")[0]
                         .GetProperty("message").GetProperty("content").GetString()!;

        using var verdict = JsonDocument.Parse(content);
        return new Review(
            Approved: verdict.RootElement.GetProperty("approved").GetBoolean(),
            Issues: verdict.RootElement.TryGetProperty("issues", out var issues)
                ? issues.EnumerateArray().Select(i => i.GetString()!).ToList()
                : []);
    }
}
