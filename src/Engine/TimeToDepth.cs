namespace Zoomies.Engine;

public static class TimeToDepth
{
    private static readonly int[] Milestones = [20, 25, 30, 35];

    private static readonly (string Name, string Fen)[] Positions =
    [
        ("startpos", Fens.Startpos),
        ("kiwipete", Fens.Kiwipete),
        ("midgame", Fens.Midgame),
        ("tactical", Fens.Tactical),
        ("endgame", Fens.Endgame),
        ("promotions", Fens.Promotions),
        ("closed-open", Fens.C_O),
        ("closed-mid", Fens.C_M),
        ("closed-end", Fens.C_E),
        ("open-open", Fens.O_O),
        ("open-mid", Fens.O_M),
        ("open-end", Fens.O_E),
        ("dyn-open", Fens.D_O),
        ("dyn-mid", Fens.D_M),
        ("dyn-end", Fens.D_E),
    ];

    public static void Run(long movetimeMilliseconds, int hashMb)
    {
        var search = new Search { SuppressOutput = true };
        search.ResizeHash(hashMb);
        var position = new Core.Position();

        Core.Position.Set(Fens.Startpos, position);
        search.FindBestMove(position, SearchLimits.Depth(6));

        Console.WriteLine($"ttd movetime {movetimeMilliseconds} ms  hash {hashMb} MB  " + $"({Positions.Length} positions)");
        string header = string.Concat(Milestones.Select(m => $"{$"d{m}",9}"));
        Console.WriteLine($"{"position",-12}{header}{"final",9}{"time",9}{"nodes",14}");

        long[] milestoneSum = new long[Milestones.Length];
        int[] milestoneReached = new int[Milestones.Length];
        long depthSum = 0;

        foreach ((string name, string fen) in Positions)
        {
            search.NewGame();
            Core.Position.Set(fen, position);
            search.FindBestMove(position, SearchLimits.Time(movetimeMilliseconds));

            var cells = new System.Text.StringBuilder();
            for (int m = 0; m < Milestones.Length; m++)
            {
                int depth = Milestones[m];
                bool reached = depth <= search.LastDepth &&
                    depth < search.IterationMilliseconds.Length;
                if (reached)
                {
                    long ms = search.IterationMilliseconds[depth];
                    milestoneSum[m] += ms;
                    milestoneReached[m]++;
                    cells.Append($"{ms / 1000.0,8:F2}s");
                }
                else
                {
                    cells.Append($"{"-",9}");
                }
            }

            depthSum += search.LastDepth;
            Console.WriteLine(
                $"{name,-12}{cells}{$"d{search.LastDepth}",9}" +
                $"{search.LastElapsedMilliseconds / 1000.0,8:F2}s" +
                $"{search.LastNodeCount,14:N0}");
        }

        var summary = new System.Text.StringBuilder();
        for (int m = 0; m < Milestones.Length; m++)
            summary.Append(milestoneReached[m] > 0
                ? $"  d{Milestones[m]}: {milestoneSum[m] / 1000.0 / milestoneReached[m]:F2}s avg ({milestoneReached[m]}/{Positions.Length})"
                : $"  d{Milestones[m]}: unreached");
        Console.WriteLine($"avg final depth {(double)depthSum / Positions.Length:F1}{summary}");
    }
}
