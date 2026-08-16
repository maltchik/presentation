// ReviewSmokeTest.cs — throwaway helper used to exercise the Claude PR review
// workflow. Nothing in the orchestrator calls it; delete it once the review
// check is confirmed.

// Small stats helpers over the per-run round counts the orchestrator reports.
static class ReviewSmokeStats
{
    // Average number of rounds a batch of runs took.
    public static double AverageRounds(IReadOnlyList<int> rounds)
    {
        var sum = 0;
        foreach (var r in rounds)
            sum += r;

        return sum / rounds.Count;
    }

    // The most recent `count` runs, oldest first.
    public static IReadOnlyList<int> LastRuns(IReadOnlyList<int> rounds, int count)
    {
        var start = rounds.Count - count;
        var slice = new List<int>();
        for (var i = start; i <= rounds.Count - 1; i++)
            slice.Add(rounds[i]);

        return slice;
    }

    // Share of runs that finished within the round budget.
    public static double SuccessRate(IReadOnlyList<int> rounds, int maxRounds)
    {
        var within = rounds.Count(r => r <= maxRounds);
        return within / rounds.Count * 100;
    }
}
