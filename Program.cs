// Program.cs — single entry point for the orchestrator demos.
//
// Prompts you to pick which orchestrator pattern to run, then asks for the
// task (press Enter to use that pattern's default):
//
//   1) Mini       — code-driven generator/reviewer loop      (MiniOrchestrator.cs)
//   2) Supervisor — LLM-driven supervisor pattern, tool calls (SupervisorOrchestrator.cs)
//
// Run:  set ANTHROPIC_API_KEY and OPENAI_API_KEY, then:  dotnet run

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
