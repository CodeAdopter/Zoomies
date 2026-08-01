using System.Diagnostics;
using System.Text;
using Zoomies.Core;
using Zoomies.Engine;

namespace Zoomies;

public static class Uci
{
    private const string EngineName = "Zoomies 1";

    private const int MaxHashMb = 16384;
    private const int DefaultSearchDepth = 8;
    private const int DefaultBenchmarkDepth = 9;

    public static void Run()
    {
        var position = new Position();
        Position.Set(Types.StartingPositionFen, position);

        var search = new Search();
        Thread? activeSearchThread = null;

        void StopActiveSearch()
        {
            if (activeSearchThread is null) return;

            search.Stop();
            activeSearchThread.Join();
            activeSearchThread = null;
        }

        string? input;
        while ((input = Console.ReadLine()) is not null)
        {
            string[] tokens = input.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;

            switch (tokens[0])
            {
                case "uci":
                    WriteEngineIdentity();
                    break;

                case "isready":
                    Console.WriteLine("readyok");
                    break;

                case "setoption":
                    StopActiveSearch();
                    ParseSetOption(search, tokens);
                    break;

                case "ucinewgame":
                    StopActiveSearch();
                    search.NewGame();
                    Position.Set(Types.StartingPositionFen, position);
                    break;

                case "position":
                    StopActiveSearch();
                    ParsePosition(position, tokens);
                    break;

                case "go":
                    StopActiveSearch();
                    SearchLimits? limits = ParseGo(position, tokens);
                    if (limits is null) break;

                    search.ResetStopRequest();
                    SearchLimits searchLimits = limits.Value;
                    activeSearchThread = new Thread(() =>
                    {
                        Move bestMove = search.FindBestMove(position, searchLimits);
                        Console.WriteLine(
                            $"bestmove {(bestMove.EncodedValue == 0 ? "0000" : bestMove)}");
                    })
                    {
                        IsBackground = true,
                        Name = "search"
                    };
                    activeSearchThread.Start();
                    break;

                case "stop":
                    search.Stop();
                    break;

                case "bench":
                    StopActiveSearch();
                    int benchmarkDepth = tokens.Length > 1 &&
                        int.TryParse(tokens[1], out int parsedDepth)
                            ? parsedDepth
                            : DefaultBenchmarkDepth;
                    RunBench(benchmarkDepth);
                    break;

                case "quit":
                    StopActiveSearch();
                    return;
            }
        }

        StopActiveSearch();
    }

    private static void WriteEngineIdentity()
    {
        Console.WriteLine($"id name {EngineName}");
        Console.WriteLine("id author Angelo Wolff");
        Console.WriteLine($"option name Hash type spin default 16 min 1 max {MaxHashMb}");
        Console.WriteLine("uciok");
    }

    private static void ParseSetOption(Search search, string[] tokens)
    {
        int nameIndex = Array.IndexOf(tokens, "name");
        int valueIndex = Array.IndexOf(tokens, "value");
        if (nameIndex < 0 || valueIndex < nameIndex + 2 || valueIndex + 1 >= tokens.Length)
            return;

        string name = string.Join(' ', tokens[(nameIndex + 1)..valueIndex]);
        switch (name.ToLowerInvariant())
        {
            case "hash":
                if (int.TryParse(tokens[valueIndex + 1], out int sizeMb))
                    search.ResizeHash(Math.Clamp(sizeMb, 1, MaxHashMb));
                break;
        }
    }

    private static void ParsePosition(Position position, string[] tokens)
    {
        int nextToken;

        if (tokens.Length > 1 && tokens[1] == "startpos")
        {
            Position.Set(Types.StartingPositionFen, position);
            nextToken = 2;
        }
        else if (tokens.Length > 1 && tokens[1] == "fen")
        {
            var fen = new StringBuilder();
            nextToken = 2;
            while (nextToken < tokens.Length && tokens[nextToken] != "moves")
            {
                if (fen.Length > 0) fen.Append(' ');
                fen.Append(tokens[nextToken]);
                nextToken++;
            }

            Position.Set(fen.ToString(), position);
        }
        else
        {
            return;
        }

        if (nextToken >= tokens.Length || tokens[nextToken] != "moves")
            return;

        for (int i = nextToken + 1; i < tokens.Length; i++)
        {
            Move move = ParseMove(position, tokens[i]);
            if (move.EncodedValue == 0) break;
            position.Play(position.Turn, move);
        }
    }

    private static Move ParseMove(Position position, string uciMove)
    {
        Span<Move> moves = stackalloc Move[256];
        int moveCount = Search.GenerateLegalMoves(position, moves);

        for (int i = 0; i < moveCount; i++)
        {
            if (moves[i].ToString() == uciMove)
                return moves[i];
        }

        return default;
    }

    private static SearchLimits? ParseGo(Position position, string[] tokens)
    {
        int perftIndex = Array.IndexOf(tokens, "perft");
        if (perftIndex >= 0)
        {
            RunPerft(position, tokens, perftIndex);
            return null;
        }

        var limits = new SearchLimits();
        long whiteTime = 0;
        long blackTime = 0;
        long whiteIncrement = 0;
        long blackIncrement = 0;
        long moveTime = 0;
        long nodeLimit = 0;
        int depth = 0;

        for (int i = 1; i < tokens.Length - 1; i++)
        {
            switch (tokens[i])
            {
                case "depth":
                    int.TryParse(tokens[i + 1], out depth);
                    break;
                case "movetime":
                    long.TryParse(tokens[i + 1], out moveTime);
                    break;
                case "nodes":
                    long.TryParse(tokens[i + 1], out nodeLimit);
                    break;
                case "wtime":
                    long.TryParse(tokens[i + 1], out whiteTime);
                    break;
                case "btime":
                    long.TryParse(tokens[i + 1], out blackTime);
                    break;
                case "winc":
                    long.TryParse(tokens[i + 1], out whiteIncrement);
                    break;
                case "binc":
                    long.TryParse(tokens[i + 1], out blackIncrement);
                    break;
            }
        }

        if (Array.IndexOf(tokens, "infinite") >= 0)
        {
            limits.SearchUntilStopped = true;
        }
        else if (moveTime > 0)
        {
            limits.MoveTimeMilliseconds = moveTime;
        }
        else if (nodeLimit > 0)
        {
            limits.MaxNodes = nodeLimit;
        }
        else if (depth > 0)
        {
            limits.MaxDepth = depth;
        }
        else if (whiteTime > 0 || blackTime > 0)
        {
            long remainingTime = position.Turn == Color.White
                ? whiteTime
                : blackTime;
            long increment = position.Turn == Color.White
                ? whiteIncrement
                : blackIncrement;

            const long communicationOverhead = 30;
            long budget = remainingTime / 20 + increment / 2;
            long safeMaximum = Math.Max(1, remainingTime - communicationOverhead);
            limits.MoveTimeMilliseconds = Math.Clamp(budget, 1, safeMaximum);
        }
        else
        {
            limits.MaxDepth = DefaultSearchDepth;
        }

        return limits;
    }

    private static void RunPerft(
        Position position,
        string[] tokens,
        int perftIndex)
    {
        int depth = perftIndex + 1 < tokens.Length &&
            int.TryParse(tokens[perftIndex + 1], out int parsedDepth)
                ? parsedDepth
                : 1;
        bool useBulkCount = Array.IndexOf(tokens, "bulk") >= 0;
        bool useFastCount = Array.IndexOf(tokens, "fast") >= 0;

        string mode = useBulkCount
            ? "bulk (hash-free, popcount leaves)"
            : useFastCount
                ? "fast (hash-free, emit leaves)"
                : "count (hashed, emit leaves)";

        var stopwatch = Stopwatch.StartNew();
        long nodes = useBulkCount
            ? Perft.CountBulk(position, depth)
            : useFastCount
                ? Perft.CountFast(position, depth)
                : Perft.Count(position, depth);
        stopwatch.Stop();

        double millionNodesPerSecond = stopwatch.ElapsedMilliseconds > 0
            ? nodes / (double)stopwatch.ElapsedMilliseconds / 1000.0
            : 0;
        Console.WriteLine(
            $"Nodes searched: {nodes}  [{mode}]  " +
            $"{stopwatch.ElapsedMilliseconds} ms  {millionNodesPerSecond:F1} Mnps");
    }

    public static void RunBench(int depth)
    {
        string[] positions =
        [
            Fens.Startpos,
            Fens.Kiwipete,
            Fens.Midgame,
            Fens.Tactical,
            Fens.Endgame,
            Fens.Promotions,
        ];
        var search = new Search { SuppressOutput = true };
        var position = new Position();

        Position.Set(Fens.Startpos, position);
        search.FindBestMove(position, SearchLimits.Depth(Math.Min(depth, 6)));

        long totalNodes = 0;
        long totalQuiescenceNodes = 0;
        long totalEvaluations = 0;
        long totalMilliseconds = 0;

        foreach (string fen in positions)
        {
            Position.Set(fen, position);
            Move bestMove = search.FindBestMove(position, SearchLimits.Depth(depth));

            totalNodes += search.LastNodeCount;
            totalQuiescenceNodes += search.LastQuiescenceNodeCount;
            totalEvaluations += search.LastEvaluationCount;
            totalMilliseconds += search.LastElapsedMilliseconds;

            long nodesPerSecond = search.LastElapsedMilliseconds > 0
                ? search.LastNodeCount * 1000 / search.LastElapsedMilliseconds
                : 0;
            string bestMoveText = bestMove.EncodedValue == 0
                ? "----"
                : bestMove.ToString();

            Console.WriteLine(
                $"  d{search.LastDepth,-2} {search.LastNodeCount,12:N0} nodes  " +
                $"{search.LastElapsedMilliseconds,6} ms  " +
                $"{nodesPerSecond / 1_000_000.0,6:F2} Mnps  " +
                $"score {search.LastScore,6}  bm {bestMoveText}");
        }

        long aggregateNodesPerSecond = totalMilliseconds > 0
            ? totalNodes * 1000 / totalMilliseconds
            : 0;
        long evaluationsPerSecond = totalMilliseconds > 0
            ? totalEvaluations * 1000 / totalMilliseconds
            : 0;
        double quiescencePercentage = totalNodes > 0
            ? 100.0 * totalQuiescenceNodes / totalNodes
            : 0;

        Console.WriteLine(
            $"bench depth {depth}: {totalNodes:N0} nodes " +
            $"({quiescencePercentage:F1}% q)  {totalMilliseconds} ms  " +
            $"{aggregateNodesPerSecond / 1_000_000.0:F2} Mnps");
        Console.WriteLine(
            $"  evals: {totalEvaluations:N0}  " +
            $"{evaluationsPerSecond / 1_000_000.0:F2} M evals/sec");
    }
}
