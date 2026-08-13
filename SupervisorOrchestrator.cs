// SupervisorOrchestrator.cs — an LLM-driven orchestrator (supervisor pattern).
//
//   Supervisor: Claude, deciding the next step via tool calls
//   Sub-agents: Claude as code generator, GPT as reviewer — both hidden behind tools
//
// This is a library file, not an entry point — run the demo via `dotnet run`
// in this project, which launches Program.cs and prompts you to pick this
// pattern. See SupervisorOrchestratorApp below for how it's wired up.
//
// Requires .NET 8+. Raw HttpClient + System.Text.Json, no SDK, so the mechanics
// stay visible: the supervisor model only *requests* actions; this runtime loop
// is the only thing that executes anything.
//
// Anthropic API docs: https://docs.claude.com/en/api/overview

// ---------------------------------------------------------------------------
// Entry point for this pattern, called from Program.cs once the user picks
// "Supervisor". Runs the supervisor loop and prints the final code.
// ---------------------------------------------------------------------------

using System.Text;
using System.Text.Json;

static class SupervisorOrchestratorApp
{
    public const string DefaultTask =
        "Write a thread-safe C# in-memory cache with per-entry TTL and a size limit (LRU eviction).";

    public static async Task RunAsync(string task, string anthropicKey, string openaiKey)
    {
        var finalCode = await new Supervisor(anthropicKey, openaiKey).RunAsync(task);

        Console.WriteLine("\n=== final code ===\n");
        Console.WriteLine(finalCode.Length > 0 ? finalCode : "(no code was produced)");
    }
}


// ---------------------------------------------------------------------------
// The supervisor runtime. The LLM decides WHAT happens next; this class is the
// only thing that DOES anything. It also owns the hard guardrails (max turns,
// review budget) and the artifact store (_currentCode never enters the
// supervisor's transcript — tools return short summaries instead).
// ---------------------------------------------------------------------------

class Supervisor(string anthropicKey, string openaiKey)
{
    const int MaxTurns = 12;   // binding cap on supervisor decisions
    const int MaxReviews = 3;  // binding cap, mirrored as advice in the prompt

    static readonly HttpClient Http = new();

    string _currentCode = "";
    int _reviewCount;
    bool _finished;

    const string SystemPrompt =
        "You are the supervisor of a coding workflow. You never write or review code " +
        "yourself — you delegate via tools and decide what happens next based on the results. " +
        "Typical flow: generate_code, then review_code; if the reviewer reports issues, call " +
        "generate_code again with instructions that include every issue to fix, then review again. " +
        "You have a budget of 3 reviews. When the reviewer approves, or the budget is spent, " +
        "call finish. Never call finish before at least one review. Think briefly, then act.";

    // Tool descriptions are the supervisor's only documentation — write them like docs.
    static readonly object[] Tools =
    [
        new
        {
            name = "generate_code",
            description =
                "Delegate to the generator sub-agent (a senior C# engineer). It writes or revises " +
                "the code, which the runtime stores for you. Pass complete, self-contained " +
                "instructions: include the original task and, on revisions, every issue to fix. " +
                "The generator has no memory of previous calls.",
            input_schema = new
            {
                type = "object",
                properties = new
                {
                    instructions = new { type = "string", description = "Full instructions for the generator." }
                },
                required = new[] { "instructions" }
            }
        },
        new
        {
            name = "review_code",
            description =
                "Send the currently stored code to the reviewer sub-agent. Returns a JSON verdict: " +
                "{\"approved\": bool, \"issues\": [string]}. Takes no arguments.",
            input_schema = new { type = "object", properties = new { } }
        },
        new
        {
            name = "finish",
            description =
                "End the workflow. Only call this after review_code has approved the code, " +
                "or after the review budget is exhausted.",
            input_schema = new
            {
                type = "object",
                properties = new
                {
                    summary = new { type = "string", description = "One-sentence outcome summary." }
                },
                required = new[] { "summary" }
            }
        }
    ];

    public async Task<string> RunAsync(string task)
    {
        Console.WriteLine($"Task: {task}\n");
        var messages = new List<object>
        {
            new { role = "user", content = $"Run the workflow for this task:\n{task}" }
        };

        for (int turn = 1; turn <= MaxTurns && !_finished; turn++)
        {
            JsonElement response = await CallSupervisorModelAsync(messages);
            JsonElement content = response.GetProperty("content");

            // The model's text blocks are its visible reasoning — log them.
            foreach (var block in content.EnumerateArray())
                if (block.GetProperty("type").GetString() == "text")
                    Console.WriteLine($"[supervisor] {block.GetProperty("text").GetString()}");

            if (response.GetProperty("stop_reason").GetString() != "tool_use")
                break; // the model answered in plain text — nothing left to execute

            // Echo the assistant turn (including its tool_use blocks) into the transcript.
            messages.Add(new { role = "assistant", content });

            var results = new List<object>();
            foreach (var block in content.EnumerateArray())
            {
                if (block.GetProperty("type").GetString() != "tool_use") continue;

                string id = block.GetProperty("id").GetString()!;
                string name = block.GetProperty("name").GetString()!;
                JsonElement input = block.GetProperty("input");

                Console.WriteLine($"[runtime]    executing {name}");
                string result = await ExecuteToolAsync(name, input);
                Console.WriteLine($"[runtime]    -> {Truncate(result, 160)}");

                results.Add(new { type = "tool_result", tool_use_id = id, content = result });
            }
            messages.Add(new { role = "user", content = results });
        }

        return _currentCode;
    }

    // Errors are returned AS RESULTS, not thrown — the supervisor reads them and
    // self-corrects ("no code yet? then I should call generate_code first").
    async Task<string> ExecuteToolAsync(string name, JsonElement input)
    {
        try
        {
            switch (name)
            {
                case "generate_code":
                    _currentCode = await GeneratorSubAgentAsync(input.GetProperty("instructions").GetString()!);
                    return $"Code written and stored ({_currentCode.Split('\n').Length} lines). " +
                           "Call review_code to have it checked.";

                case "review_code":
                    if (_currentCode.Length == 0)
                        return "Error: no code exists yet. Call generate_code first.";
                    if (_reviewCount >= MaxReviews)
                        return "Error: review budget exhausted. Call finish with the current code.";
                    _reviewCount++;
                    return await ReviewerSubAgentAsync(_currentCode);

                case "finish":
                    _finished = true;
                    return $"Workflow finished: {input.GetProperty("summary").GetString()}";

                default:
                    return $"Error: unknown tool '{name}'.";
            }
        }
        catch (Exception ex)
        {
            return $"Error while running {name}: {ex.Message}";
        }
    }

    // ---- Supervisor model call (Anthropic Messages API with tools) ----------

    async Task<JsonElement> CallSupervisorModelAsync(List<object> messages)
    {
        var body = JsonSerializer.Serialize(new
        {
            model = "claude-sonnet-4-6",
            max_tokens = 2000,
            system = SystemPrompt,
            tools = Tools,
            messages
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-api-key", anthropicKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        using var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone(); // Clone: the element must outlive the disposed document
    }

    // ---- Sub-agent: generator (Claude, its own prompt, no memory) -----------

    async Task<string> GeneratorSubAgentAsync(string instructions)
    {
        var body = JsonSerializer.Serialize(new
        {
            model = "claude-sonnet-4-6",
            max_tokens = 4000,
            system = "You are a senior C# engineer. Reply with ONLY the complete, compilable code. " +
                     "No markdown fences, no explanations.",
            messages = new[] { new { role = "user", content = instructions } }
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-api-key", anthropicKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        using var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return StripFences(doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString()!);
    }

    // ---- Sub-agent: reviewer (GPT, strict JSON verdict) ---------------------

    async Task<string> ReviewerSubAgentAsync(string code)
    {
        var body = JsonSerializer.Serialize(new
        {
            model = "gpt-4.1", // swap in whichever OpenAI model you prefer
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "You are a strict C# code reviewer. Respond with ONLY a JSON object: " +
                              "{\"approved\": bool, \"issues\": [string]}. Approve only if the code " +
                              "compiles, handles edge cases, and follows C# conventions. Each issue " +
                              "must be one concrete, actionable sentence. Do not invent nitpicks — " +
                              "if the code is genuinely fine, approve with an empty issues array."
                },
                new { role = "user", content = $"Code to review:\n{code}" }
            }
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Authorization", $"Bearer {openaiKey}");

        using var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        // Return the reviewer's raw JSON verdict as the tool result — the supervisor reads it directly.
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()!;
    }

    // ---- Helpers ------------------------------------------------------------

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

    static string Truncate(string s, int max)
    {
        s = s.ReplaceLineEndings(" ");
        return s.Length <= max ? s : s[..max] + "…";
    }
}
