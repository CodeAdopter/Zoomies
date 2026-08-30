using System.Diagnostics;
using System.Runtime.CompilerServices;
using Zoomies.Core;

namespace Zoomies.Engine;

public static class Perft
{
    [SkipLocalsInit]
    public static long Count(Position pos, int depth)
    {
        Span<Move> moves = stackalloc Move[256];
        int n = Search.GenerateLegalMoves(pos, moves);
        if (depth <= 1) return n;

        long nodes = 0;
        Color us = pos.Turn;
        for (int i = 0; i < n; i++)
        {
            pos.Play(us, moves[i]);
            nodes += Count(pos, depth - 1);
            pos.Undo(us, moves[i]);
        }
        return nodes;
    }

    [SkipLocalsInit]
    public static long CountFast(Position pos, int depth)
    {
        Span<Move> buf = stackalloc Move[256 * depth];
        return pos.Turn == Color.White ? PerftW(pos, depth, buf) : PerftB(pos, depth, buf);
    }

    private static long PerftW(Position p, int depth, Span<Move> buf)
    {
        int n = p.GenerateLegalsInto<White>(buf);
        if (depth <= 1) return n;
        long nodes = 0;
        Span<Move> sub = buf[256..];
        for (int i = 0; i < n; i++)
        {
            p.PlayPerft(Color.White, buf[i]);
            nodes += PerftB(p, depth - 1, sub);
            p.UndoPerft(Color.White, buf[i]);
        }
        return nodes;
    }

    private static long PerftB(Position p, int depth, Span<Move> buf)
    {
        int n = p.GenerateLegalsInto<Black>(buf);
        if (depth <= 1) return n;
        long nodes = 0;
        Span<Move> sub = buf[256..];
        for (int i = 0; i < n; i++)
        {
            p.PlayPerft(Color.Black, buf[i]);
            nodes += PerftW(p, depth - 1, sub);
            p.UndoPerft(Color.Black, buf[i]);
        }
        return nodes;
    }

    [SkipLocalsInit]
    public static long CountBulk(Position pos, int depth)
    {
        Span<Move> buf = stackalloc Move[256 * depth];
        return pos.Turn == Color.White ? BulkW(pos, depth, buf) : BulkB(pos, depth, buf);
    }

    private static long BulkW(Position p, int depth, Span<Move> buf)
    {
        if (depth == 1) return p.CountLegals<White>();
        int n = p.GenerateLegalsInto<White>(buf);
        long nodes = 0;
        Span<Move> sub = buf[256..];
        for (int i = 0; i < n; i++)
        {
            p.PlayPerft(Color.White, buf[i]);
            nodes += BulkB(p, depth - 1, sub);
            p.UndoPerft(Color.White, buf[i]);
        }
        return nodes;
    }

    private static long BulkB(Position p, int depth, Span<Move> buf)
    {
        if (depth == 1) return p.CountLegals<Black>();
        int n = p.GenerateLegalsInto<Black>(buf);
        long nodes = 0;
        Span<Move> sub = buf[256..];
        for (int i = 0; i < n; i++)
        {
            p.PlayPerft(Color.Black, buf[i]);
            nodes += BulkW(p, depth - 1, sub);
            p.UndoPerft(Color.Black, buf[i]);
        }
        return nodes;
    }

    public static long CountParallel(Position root, int depth, int threads, bool bulk = false)
    {
        if (threads <= 1 || depth <= 2) return bulk ? CountBulk(root, depth) : CountFast(root, depth);

        int splitPlies = depth >= 4 ? 2 : 1;
        int jobDepth = depth - splitPlies;
        var jobs = new List<Position>();
        BuildJobs(root, splitPlies, jobs);

        long total = 0;
        object gate = new();
        Parallel.ForEach(
            jobs,
            new ParallelOptions { MaxDegreeOfParallelism = threads },
            () => 0L,
            (job, _, local) => local + (bulk ? CountBulk(job, jobDepth) : CountFast(job, jobDepth)),
            local => { lock (gate) total += local; });
        return total;
    }

    private static void BuildJobs(Position p, int splitPlies, List<Position> jobs)
    {
        if (splitPlies == 0) { jobs.Add(new Position(p)); return; }
        Span<Move> moves = stackalloc Move[256];
        int n = Search.GenerateLegalMoves(p, moves);
        Color us = p.Turn;
        for (int i = 0; i < n; i++)
        {
            p.Play(us, moves[i]);
            BuildJobs(p, splitPlies - 1, jobs);
            p.Undo(us, moves[i]);
        }
    }



    public static void Scale(int maxThreads = 0, bool bulk = false, CancellationToken ct = default)
    {
        const string fen = Fens.Kiwipete;
        const int depth = 6;
        const long expected = 8_031_647_685;

        int cap = maxThreads > 0 ? maxThreads : Environment.ProcessorCount;
        var root = new Position();
        Position.Set(fen, root);

        var ladder = new List<int>();
        foreach (int t in new[] { 1, 2, 4, 6, 8, 12, 16, 24, 32, 48, 64 })
            if (t <= cap) ladder.Add(t);
        if (ladder.Count == 0 || ladder[^1] != cap) ladder.Add(cap);

        CountParallel(root, 4, cap, bulk);

        string label = bulk ? "bulk-count (perft-only)" : "move-gen (emit path, search uses this)";
        Console.WriteLine($"perft scaling [{label}]  {fen}  depth {depth}  ({expected:n0} nodes)  [{Environment.ProcessorCount} logical cores]");
        double baseMnps = 0;
        foreach (int t in ladder)
        {
            if (ct.IsCancellationRequested) { Console.WriteLine("cancelled."); return; }
            long best = long.MaxValue, nodes = 0;
            int reps = 2;
            for (int r = 0; r < reps; r++)
            {
                var sw = Stopwatch.StartNew();
                nodes = CountParallel(root, depth, t, bulk);
                sw.Stop();
                if (sw.ElapsedMilliseconds < best) best = sw.ElapsedMilliseconds;
            }
            double mnps = nodes * 1000.0 / best / 1e6;
            if (t == 1) baseMnps = mnps;
            string ok = nodes == expected ? "ok  " : "FAIL";
            Console.WriteLine($"  {ok} {t,3} thread(s): {best,7} ms  {mnps,8:F1} Mnps  {mnps / baseMnps,5:F1}x");
        }
    }

    public readonly record struct SuiteResult(string Name, string Fen, int Depth, long Nodes, long Ms, bool Ok);

    // counts validated from stockfish 17.1
    private static readonly (string name, string fen, int depth, long expected)[] SuitePositions =
    [
        ("Startpos",   Fens.Startpos,   6, 119_060_324),
        ("Kiwipete",   Fens.Kiwipete,   5, 193_690_690),
        ("Endgame",    Fens.Endgame,    6, 11_030_083),
        ("Tactical",   Fens.Tactical,   5, 15_833_292),
        ("Promotions", Fens.Promotions, 5, 89_941_194),
        ("Midgame",    Fens.Midgame,    5, 164_075_551),
    ];


    public static List<SuiteResult> RunSuite(int runs, bool bulk, CancellationToken ct = default)
    {
        var warm = new Position();
        Position.Set(Fens.Startpos, warm);
        for (int i = 0; i < 3; i++) { if (bulk) CountBulk(warm, 5); else CountFast(warm, 5); }

        var results = new List<SuiteResult>(SuitePositions.Length);
        foreach (var (name, fen, depth, expected) in SuitePositions)
        {
            ct.ThrowIfCancellationRequested();
            var pos = new Position();
            Position.Set(fen, pos);
            long best = long.MaxValue, nodes = 0;
            for (int r = 0; r < runs; r++)
            {
                ct.ThrowIfCancellationRequested();
                var sw = Stopwatch.StartNew();
                nodes = bulk ? CountBulk(pos, depth) : CountFast(pos, depth);
                sw.Stop();
                if (sw.ElapsedMilliseconds < best) best = sw.ElapsedMilliseconds;
            }
            results.Add(new SuiteResult(name, fen, depth, nodes, best, nodes == expected));
        }
        return results;
    }

    public static void Suite(int runs = 3, bool bulk = false, CancellationToken ct = default)
    {
        var results = RunSuite(runs, bulk, ct);

        Console.WriteLine($"perft suite [{(bulk ? "bulk-count, perft-only" : "emit path, search uses this")}]  (best of {runs})");
        long totalNodes = 0, totalMs = 0;
        bool allOk = true;
        foreach (var r in results)
        {
            totalNodes += r.Nodes;
            totalMs += r.Ms;
            allOk &= r.Ok;
            Console.WriteLine($"  {(r.Ok ? "ok  " : "FAIL")} d{r.Depth} {r.Nodes,14:n0}  {r.Ms,6} ms  {Log.Nps(r.Nodes, r.Ms),12}   {r.Fen}");
        }
        Console.WriteLine($"  ---- total {totalNodes,14:n0} nodes  {totalMs,6} ms  {Log.Nps(totalNodes, totalMs),12}   [{(allOk ? "all correct" : "MISMATCH!")}]");
    }

    public static void FrcCheck(CancellationToken ct = default)
    {
        bool previous = Position.Chess960;
        Position.Chess960 = true;
        try
        {
            (string name, string fen, int depth, long expected)[] cases =
            [
                ("FRC_518", Fens.Frc518, 5, 4_865_609),
                ("FRC_518", Fens.Frc518, 6, 119_060_324),
                ("FRC_1", Fens.Frc960a, 5, 4_863_733),
                ("FRC_450", Fens.Frc960b, 5, 5_004_807),
                ("FRC_900", Fens.Frc960c, 5, 3_914_548),
            ];

            var pos = new Position();
            bool allOk = true;
            foreach (var (name, fen, depth, expected) in cases)
            {
                if (ct.IsCancellationRequested) { Console.WriteLine("cancelled."); return; }
                Position.Set(fen, pos);
                long nodes = CountFast(pos, depth);
                bool ok = nodes == expected;
                allOk &= ok;
                Console.WriteLine($"{(ok ? "ok" : "fail")} {name} d{depth}: {nodes:n0} (expected {expected:n0})"); 
            }
            Console.WriteLine(allOk ? "ok" : "fail");
        }
        finally { Position.Chess960 = previous; }
    }

    public static void Bench(string fen, int maxDepth, CancellationToken ct = default)
    {
        var pos = new Position();
        Position.Set(fen, pos);
        Console.WriteLine($"perft  {fen}");
        for (int d = 1; d <= maxDepth; d++)
        {
            if (ct.IsCancellationRequested) { Console.WriteLine("cancelled."); return; }
            var sw = Stopwatch.StartNew();
            long nodes = Count(pos, d);
            sw.Stop();
            Console.WriteLine($"  depth {d,2}: {nodes,14:n0} nodes  {sw.ElapsedMilliseconds,7} ms  {Log.Nps(nodes, sw.ElapsedMilliseconds)}");
        }
    }
}
