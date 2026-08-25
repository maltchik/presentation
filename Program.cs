// Program.cs — single entry point for the orchestrator demos.
//
// Prompts you to pick which orchestrator pattern to run, then asks for the
// task (press Enter to use that pattern's default):
//
//   1) Mini       — code-driven generator/reviewer loop      (MiniOrchestrator.cs)
//   2) Supervisor — LLM-driven supervisor pattern, tool calls (SupervisorOrchestrator.cs)
//
// Run:  set ANTHROPIC_API_KEY and OPENAI_API_KEY, then:  dotnet run

LoadDotEnv();

var anthropicKey = Env("ANTHROPIC_API_KEY");
var openaiKey = Env("OPENAI_API_KEY");

Console.WriteLine("Which orchestrator do you want to run?");
Console.WriteLine("  1) Mini       — code-driven generator/reviewer loop");
Console.WriteLine("  2) Supervisor — LLM-driven supervisor pattern (tool calls)");

bool runMini;
while (true)
{
    Console.Write("> ");
    var choice = Console.ReadLine()?.Trim().ToLowerInvariant();
    if (choice is "1" or "mini") { runMini = true; break; }
    if (choice is "2" or "supervisor") { runMini = false; break; }
    Console.WriteLine("Please enter 1 (mini) or 2 (supervisor).");
}

var defaultTask = runMini ? MiniOrchestratorApp.DefaultTask : SupervisorOrchestratorApp.DefaultTask;

Console.WriteLine($"\nTask (press Enter to use the default):\n  {defaultTask}");
Console.Write("> ");
var taskInput = Console.ReadLine();
var task = string.IsNullOrWhiteSpace(taskInput) ? defaultTask : taskInput.Trim();

if (runMini)
    await MiniOrchestratorApp.RunAsync(task, anthropicKey, openaiKey);
else
    await SupervisorOrchestratorApp.RunAsync(task, anthropicKey, openaiKey);

static string Env(string name) =>
    Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"Set the {name} environment variable.");

static void LoadDotEnv()
{
    var path = Path.Combine(Environment.CurrentDirectory, ".env");
    if (!File.Exists(path))
        return;

    foreach (var line in File.ReadLines(path))
    {
        var trimmedLine = line.Trim();
        if (trimmedLine.Length == 0 || trimmedLine.StartsWith('#'))
            continue;

        var separator = trimmedLine.IndexOf('=');
        if (separator <= 0)
            continue;

        var key = trimmedLine[..separator].Trim();
        if (key is not ("ANTHROPIC_API_KEY" or "OPENAI_API_KEY") || Environment.GetEnvironmentVariable(key) is not null)
            continue;

        var value = trimmedLine[(separator + 1)..].Trim();
        if (value.Length >= 2 && (value[0] == '"' || value[0] == '\''))
        {
            var closingQuote = value.IndexOf(value[0], 1);
            var trailingText = closingQuote >= 0 ? value[(closingQuote + 1)..].Trim() : string.Empty;
            if (closingQuote > 0 && (trailingText.Length == 0 || trailingText.StartsWith('#')))
                value = value[1..closingQuote];
        }
        else
        {
            var comment = value.IndexOf(" #", StringComparison.Ordinal);
            if (comment >= 0)
                value = value[..comment].TrimEnd();
        }

        Environment.SetEnvironmentVariable(key, value);
    }
}
