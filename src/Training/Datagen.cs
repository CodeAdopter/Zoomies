using System.Diagnostics;
using System.Numerics;
using Zoomies.Core;
using Zoomies.Engine;

namespace Zoomies.Training;

// Imported from Imp
// Self-play training-data generator
public static class Datagen
{
    private const int MaxGamePlies = 400;
    private const int MaxOpeningScore = 600;
    private const int WinAdjScore = 2000;
    private const int WinAdjPlies = 4;
    private const int DrawAdjScore = 10;
    private const int DrawAdjPlies = 8;
    private const int DrawAdjMinPly = 80;
    private const int MaxRecordScore = 2000;

    private static long totalGames, totalPositions, totalDiscarded;
    private static volatile bool stop;

    // Random book from startpos.
    private static bool StartGame(Position pos, DatagenOptions opt, Random rng, Span<Move> moves)
    {
        Position.Set(Fens.Startpos, pos);
        int jitter = opt.BookPlies + rng.Next(2);
        for (int i = 0; i < jitter; i++)
        {
            int n = Search.GenerateLegalMoves(pos, moves);
            if (n == 0) return false;
            pos.Play(pos.Turn, moves[rng.Next(n)]);
        }
        return Search.GenerateLegalMoves(pos, moves) > 0;
    }

    public static void Run(DatagenOptions opt)
    {
        if (opt.Eval.Length > 0) Nnue.Load(opt.Eval);
        int seed = opt.Seed != 0 ? opt.Seed : Environment.TickCount;
        string playSpec = opt.GenDepth > 0 ? $"depth {opt.GenDepth}" : $"nodes/move {opt.NodesPerMove}";
        string labelSpec = opt.LabelDepth > 0 ? $"depth {opt.LabelDepth}" : "move-search score";
        Console.WriteLine($"datagen: threads {opt.Threads}  play {playSpec}  label {labelSpec}  book plies {opt.BookPlies} seed {seed}");
        Console.WriteLine($"datagen: eval {(Nnue.Loaded ? $"nnue ({Path.GetFileName(Nnue.LoadedPath)}, L1={Nnue.L1})" : "psqt")}  out {opt.OutPath}");
        Console.WriteLine($"datagen: stopping at {(opt.MaxGames > 0 ? $"{opt.MaxGames} games" : opt.MaxPositions > 0 ? $"{opt.MaxPositions} positions" : "Ctrl+C")}");

        totalGames = totalPositions = totalDiscarded = 0;
        stop = false;
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop = true; };

        bool binMode = opt.OutPath.EndsWith(".impb", StringComparison.OrdinalIgnoreCase);
        using var txtOut = binMode ? null : new StreamWriter(opt.OutPath, append: true);
        using var binOut = binMode ? new FileStream(opt.OutPath, FileMode.Append, FileAccess.Write, FileShare.Read, 1 << 20) : null;
        var writerLock = new object();

        var workers = new Thread[opt.Threads];
        for (int i = 0; i < opt.Threads; i++)
        {
            int id = i;
            workers[id] = new Thread(() => Worker(opt, seed + id * 7919, txtOut, binOut, writerLock))
            { IsBackground = true, Name = $"datagen-{id}" };
            workers[id].Start();
        }

        var sw = Stopwatch.StartNew();
        long lastPositions = 0;
        while (workers.Any(w => w.IsAlive))
        {
            Thread.Sleep(5000);
            long p = Interlocked.Read(ref totalPositions), g = Interlocked.Read(ref totalGames);
            long rate = (p - lastPositions) / 5;
            lastPositions = p;
            Console.WriteLine($"datagen: {g:N0} games  {p:N0} positions  {rate:N0} pos/s  {sw.Elapsed:hh\\:mm\\:ss}");
        }
        foreach (var w in workers) w.Join();

        lock (writerLock) { txtOut?.Flush(); binOut?.Flush(); }
        long fp = Interlocked.Read(ref totalPositions), fg = Interlocked.Read(ref totalGames), fd = Interlocked.Read(ref totalDiscarded);
        double avgRate = sw.Elapsed.TotalSeconds > 0 ? fp / sw.Elapsed.TotalSeconds : 0;
        Console.WriteLine($"datagen: DONE  {fg:N0} games ({fd:N0} discarded)  {fp:N0} positions  {avgRate:N0} pos/s avg  {sw.Elapsed:hh\\:mm\\:ss}");
    }

    private static void Worker(DatagenOptions opt, int seed, StreamWriter? txtOut, FileStream? binOut, object writerLock)
    {
        var search = new Search { SuppressOutput = true };
        search.ResizeHash(opt.TtMb);
        var rng = new Random(seed);
        var pos = new Position();
        var textRecords = new List<(string fen, int whiteScore)>(256);
        var gameBuf = new byte[512 * PackedRecord.Size];
        int gameCount = 0;
        Action<Position, int> record = binOut != null
            ? (p, ws) =>
            {
                var r = gameBuf.AsSpan(gameCount * PackedRecord.Size, PackedRecord.Size);
                PackedRecord.Encode(p, (short)Math.Clamp(ws, short.MinValue, short.MaxValue), r);
                PackedRecord.StampV2(r, PackedRecord.SourceSelfPlay, opt.NodesPerMove, p.Ply);
                gameCount++;
            }
            : (p, ws) => textRecords.Add((p.Fen(), ws));
        Span<Move> moves = stackalloc Move[218];

        while (!stop)
        {
            textRecords.Clear();
            gameCount = 0;
            if (!StartGame(pos, opt, rng, moves)) continue;

            double result = PlayGame(search, pos, moves, opt, record, opt.RecordEvery > 1 ? rng.Next(opt.RecordEvery) : 0);
            if (result < 0)
            {
                Interlocked.Increment(ref totalDiscarded);
                continue;
            }

            int recorded;
            if (binOut != null)
            {
                byte wr2 = result switch { 1.0 => (byte)2, 0.0 => (byte)0, _ => (byte)1 };
                for (int r = 0; r < gameCount; r++)
                    gameBuf[r * PackedRecord.Size + 26] = wr2;
                lock (writerLock)
                    binOut.Write(gameBuf, 0, gameCount * PackedRecord.Size);
                recorded = gameCount;
            }
            else
            {
                string res = result switch { 1.0 => "1.0", 0.0 => "0.0", _ => "0.5" };
                lock (writerLock)
                    foreach (var (fen, whiteScore) in textRecords)
                        txtOut!.WriteLine($"{fen} 0 1 | {whiteScore} | {res}");
                recorded = textRecords.Count;
            }

            long g = Interlocked.Increment(ref totalGames);
            long p = Interlocked.Add(ref totalPositions, recorded);
            if ((opt.MaxGames > 0 && g >= opt.MaxGames) || (opt.MaxPositions > 0 && p >= opt.MaxPositions))
                stop = true;
        }
    }

    private static double PlayGame(Search search, Position pos, Span<Move> moves, DatagenOptions opt,
                                   Action<Position, int> record, int recPhase, BenchStats? bench = null)
    {
        int consecWin = 0, winSign = 0, consecDraw = 0;
        int prevAbs = 0, recIdx = 0;

        for (int searchedPly = 0; ; searchedPly++)
        {
            if (stop) return -1;

            int n = Search.GenerateLegalMoves(pos, moves);
            if (n == 0)
                return pos.InCheck(pos.Turn) ? (pos.Turn == Color.White ? 0.0 : 1.0) : 0.5;
            if (pos.IsFiftyMoveRule() || pos.IsRepetition() || InsufficientMaterial(pos) || pos.Ply >= MaxGamePlies)
                return 0.5;

            bool depthMode = opt.GenDepth > 0;
            bool reduced = !depthMode && opt.AdaptiveNodes && consecWin >= 1 && prevAbs >= WinAdjScore + 200;
            long budget = reduced ? Math.Max(256, opt.NodesPerMove * 2 / 5) : opt.NodesPerMove;
            Move best = search.FindBestMove(pos, depthMode ? SearchLimits.Depth(opt.GenDepth) : SearchLimits.Nodes(budget));
            if (bench != null)
            {
                bench.Nodes += search.LastNodeCount;
                bench.Moves++;
                bench.DepthSum += search.LastDepth;
            }
            int stmScore = search.LastScore;
            int whiteScore = pos.Turn == Color.White ? stmScore : -stmScore;

            if (searchedPly == 0 && Math.Abs(stmScore) > MaxOpeningScore)
                return -1;

            if (!reduced
                && !pos.InCheck(pos.Turn)
                && !best.IsCapture
                && (best.Flags & MoveFlags.Promotions) == 0
                && Math.Abs(whiteScore) < MaxRecordScore)
            {
                if (recIdx % opt.RecordEvery == recPhase)
                {
                    int labelWhite = whiteScore;
                    if (opt.LabelDepth > 0)
                    {
                        search.FindBestMove(pos, SearchLimits.Depth(opt.LabelDepth));
                        int ls = search.LastScore;
                        labelWhite = pos.Turn == Color.White ? ls : -ls;
                    }
                    record(pos, labelWhite);
                }
                recIdx++;
            }

            prevAbs = Math.Abs(whiteScore);

            if (Math.Abs(whiteScore) >= WinAdjScore)
            {
                int s = Math.Sign(whiteScore);
                consecWin = s == winSign ? consecWin + 1 : 1;
                winSign = s;
                if (consecWin >= WinAdjPlies) return s > 0 ? 1.0 : 0.0;
            }
            else { consecWin = 0; winSign = 0; }

            if (pos.Ply >= DrawAdjMinPly && Math.Abs(whiteScore) <= DrawAdjScore)
            {
                if (++consecDraw >= DrawAdjPlies) return 0.5;
            }
            else consecDraw = 0;

            pos.Play(pos.Turn, best);
        }
    }

    internal static bool InsufficientMaterial(Position pos)
    {
        ulong majorsOrPawns =
            pos.BitboardOf(Piece.WhitePawn) | pos.BitboardOf(Piece.BlackPawn) |
            pos.BitboardOf(Piece.WhiteRook) | pos.BitboardOf(Piece.BlackRook) |
            pos.BitboardOf(Piece.WhiteQueen) | pos.BitboardOf(Piece.BlackQueen);
        if (majorsOrPawns != 0) return false;
        ulong minors =
            pos.BitboardOf(Piece.WhiteKnight) | pos.BitboardOf(Piece.BlackKnight) |
            pos.BitboardOf(Piece.WhiteBishop) | pos.BitboardOf(Piece.BlackBishop);
        return BitOperations.PopCount(minors) <= 1;
    }

    private sealed class BenchStats
    {
        public long Games, Discarded, Positions, Nodes, Moves, DepthSum;
        public ulong Hash;
    }

    public static int Cli(string[] args)
    {
        Run(DatagenOptions.Parse(args));
        return 0;
    }

    public static int BenchCli(string[] args)
    {
        var opt = DatagenOptions.Parse(args);
        if (Array.IndexOf(args, "--threads") < 0) opt.Threads = 1;
        if (Array.IndexOf(args, "--nodes") < 0) opt.NodesPerMove = 2500;
        if (Array.IndexOf(args, "--games") < 0) opt.MaxGames = 40;
        if (Array.IndexOf(args, "--seed") < 0) opt.Seed = 1;

        int seed = opt.Seed != 0 ? opt.Seed : 1;
        long perThread = Math.Max(1, (opt.MaxGames > 0 ? opt.MaxGames : 40) / opt.Threads);
        Console.WriteLine($"dgbench: threads {opt.Threads}  games {perThread * opt.Threads}  nodes/move {opt.NodesPerMove}  seed {seed}  eval psqt");

        stop = false;
        var stats = new BenchStats[opt.Threads];
        var workers = new Thread[opt.Threads];
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < opt.Threads; i++)
        {
            int id = i;
            stats[id] = new BenchStats();
            workers[id] = new Thread(() => BenchWorker(opt, seed + id * 7919, perThread, stats[id]))
            { IsBackground = true, Name = $"dgbench-{id}" };
            workers[id].Start();
        }
        foreach (var w in workers) w.Join();
        sw.Stop();

        long games = 0, discarded = 0, positions = 0, nodes = 0, movesN = 0, depthSum = 0;
        ulong hash = 0;
        foreach (var s in stats)
        {
            games += s.Games; discarded += s.Discarded; positions += s.Positions;
            nodes += s.Nodes; movesN += s.Moves; depthSum += s.DepthSum;
            hash ^= s.Hash;
        }
        double secs = sw.Elapsed.TotalSeconds;
        Console.WriteLine($"dgbench: {games:N0} games ({discarded:N0} discarded)  {positions:N0} positions  {secs:F2} s  {(secs > 0 ? positions / secs : 0):N0} pos/s");
        Console.WriteLine($"dgbench: nodes {nodes:N0}  moves {movesN:N0}  avg nodes/move {(movesN > 0 ? (double)nodes / movesN : 0):F1}  avg depth {(movesN > 0 ? (double)depthSum / movesN : 0):F2}  checksum {hash:x16}");
        return 0;
    }

    private static void BenchWorker(DatagenOptions opt, int seed, long games, BenchStats bs)
    {
        var search = new Search { SuppressOutput = true };
        search.ResizeHash(opt.TtMb);
        var rng = new Random(seed);
        var pos = new Position();
        var gameBuf = new byte[512 * PackedRecord.Size];
        int gameCount = 0;
        Action<Position, int> record = (p, ws) =>
        {
            var r = gameBuf.AsSpan(gameCount * PackedRecord.Size, PackedRecord.Size);
            PackedRecord.Encode(p, (short)Math.Clamp(ws, short.MinValue, short.MaxValue), r);
            PackedRecord.StampV2(r, PackedRecord.SourceSelfPlay, opt.NodesPerMove, p.Ply);
            gameCount++;
        };
        Span<Move> moves = stackalloc Move[218];

        while (bs.Games < games)
        {
            gameCount = 0;
            if (!StartGame(pos, opt, rng, moves)) continue;

            double result = PlayGame(search, pos, moves, opt, record,
                                     opt.RecordEvery > 1 ? rng.Next(opt.RecordEvery) : 0, bs);
            if (result < 0) { bs.Discarded++; continue; }

            byte wr2 = result switch { 1.0 => (byte)2, 0.0 => (byte)0, _ => (byte)1 };
            ulong h = 14695981039346656037UL;
            for (int r = 0; r < gameCount; r++)
            {
                gameBuf[r * PackedRecord.Size + 26] = wr2;
                for (int k = 0; k < PackedRecord.Size; k++)
                { h ^= gameBuf[r * PackedRecord.Size + k]; h *= 0x100000001B3UL; }
            }
            bs.Hash ^= h;
            bs.Positions += gameCount;
            bs.Games++;
        }
    }
}
