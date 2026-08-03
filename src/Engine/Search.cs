using Zoomies.Core;

namespace Zoomies.Engine;

public struct SearchLimits
{
    public int MaxDepth;
    public long MoveTimeMilliseconds;
    public long SoftTimeMilliseconds;
    public long MaxNodes;
    public bool SearchUntilStopped;

    public static SearchLimits Depth(int depth) => new() { MaxDepth = depth };

    public static SearchLimits Time(long milliseconds) =>
        new() { MoveTimeMilliseconds = milliseconds };

    public static SearchLimits Nodes(long nodeCount) => new() { MaxNodes = nodeCount };
}

public sealed class Search
{
    private const double StabilityPeak = 2.6;
    private const double StabilityFloor = 0.5;
    private const double StabilityDecay = 0.3;
    private static readonly int[] StabilityScale = BuildStabilityScale();

    private static int[] BuildStabilityScale()
    {
        var table = new int[8];
        for (int s = 0; s < table.Length; s++)
            table[s] = (int)Math.Round(
                100 * (StabilityFloor + (StabilityPeak - StabilityFloor) * Math.Pow(StabilityDecay, s)));
        return table;
    }

    private readonly SearchState state = new();

    public bool SuppressOutput { get; set; }
    public long LastNodeCount { get; private set; }
    public long LastQuiescenceNodeCount { get; private set; }
    public long LastEvaluationCount { get; private set; }
    public long LastElapsedMilliseconds { get; private set; }
    public int LastScore { get; private set; }
    public int LastDepth { get; private set; }

    public void Stop() => state.StopRequested = true;

    public void ResetStopRequest() => state.StopRequested = false;

    public void NewGame()
    {
        state.Tt.Clear();
        state.ClearHistory();
    }

    public void ResizeHash(int sizeMb) => state.Tt.Resize(sizeMb);

    public Move FindBestMove(Position position, SearchLimits limits)
    {
        state.Reset(in limits);

        Span<Move> rootMoves = stackalloc Move[256];
        int rootMoveCount = GenerateLegalMoves(position, rootMoves);
        if (rootMoveCount == 0)
        {
            LastScore = 0;
            LastDepth = 0;
            return default;
        }

        state.PrincipalVariationMove = rootMoves[0];
        int maxDepth = limits.MaxDepth > 0 ? limits.MaxDepth : SearchState.MaximumPly - 1;
        int lastScore = 0;
        int completedDepth = 0;
        Move previousBest = default;
        int stability = 0;

        for (int depth = 1; depth <= maxDepth; depth++)
        {
            // aspiration windows: start narrow around the previous score,
            // widen exponentially on fail until the score fits
            int alpha = -SearchState.Infinity;
            int beta = SearchState.Infinity;
            int delta = 25;
            if (depth >= 4)
            {
                alpha = Math.Max(lastScore - delta, -SearchState.Infinity);
                beta = Math.Min(lastScore + delta, SearchState.Infinity);
            }

            int score;
            while (true)
            {
                score = Pruning.AlphaBeta(state, position, depth, alpha, beta, 0);
                if (state.StopRequested) break;

                if (score <= alpha)
                    alpha = Math.Max(score - delta, -SearchState.Infinity);
                else if (score >= beta)
                    beta = Math.Min(score + delta, SearchState.Infinity);
                else
                    break;

                delta *= 2;
            }
            if (state.StopRequested && depth > 1) break;

            lastScore = score;
            completedDepth = depth;

            if (!SuppressOutput)
            {
                long elapsedMilliseconds = state.Clock.ElapsedMilliseconds;
                long nodesPerSecond = elapsedMilliseconds > 0
                    ? state.NodeCount * 1000 / elapsedMilliseconds
                    : state.NodeCount;
                string scoreText = score >= Eval.MateBound
                    ? $"mate {(Eval.MateValue - score + 1) / 2}"
                    : score <= -Eval.MateBound
                        ? $"mate {-((Eval.MateValue + score + 1) / 2)}"
                        : $"cp {score}";
                Console.WriteLine(
                    $"info depth {depth} score {scoreText} nodes {state.NodeCount} " +
                    $"nps {nodesPerSecond} time {elapsedMilliseconds} pv {state.PrincipalVariationMove}");
            }

            if (state.ReachedSearchLimit()) break;

            // don't start another iteration if time is running out;
            // the hard limit still ends the search immediately
            stability = state.PrincipalVariationMove == previousBest
                ? Math.Min(stability + 1, StabilityScale.Length - 1)
                : 0;
            previousBest = state.PrincipalVariationMove;
            long softLimit = state.HasSoftLimit
                ? state.SoftTimeLimitMilliseconds * StabilityScale[stability] / 100
                : state.SoftTimeLimitMilliseconds;
            if (!state.SearchUntilStopped &&
                state.Clock.ElapsedMilliseconds >= softLimit) break;
        }

        state.StopRequested = false;
        LastNodeCount = state.NodeCount;
        LastQuiescenceNodeCount = state.QuiescenceNodeCount;
        LastEvaluationCount = state.EvaluationCount;
        LastElapsedMilliseconds = state.Clock.ElapsedMilliseconds;
        LastScore = lastScore;
        LastDepth = completedDepth;
        return state.PrincipalVariationMove;
    }

    public static int GenerateLegalMoves(Position position, Span<Move> buffer) =>
        position.Turn == Color.White
            ? position.GenerateLegalsInto<White>(buffer)
            : position.GenerateLegalsInto<Black>(buffer);
}
