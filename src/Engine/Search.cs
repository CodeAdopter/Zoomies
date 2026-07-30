using Zoomies.Core;

namespace Zoomies.Engine;

public struct SearchLimits
{
    public int MaxDepth;
    public long MoveTimeMilliseconds;
    public long MaxNodes;
    public bool SearchUntilStopped;

    public static SearchLimits Depth(int depth) => new() { MaxDepth = depth };

    public static SearchLimits Time(long milliseconds) =>
        new() { MoveTimeMilliseconds = milliseconds };

    public static SearchLimits Nodes(long nodeCount) => new() { MaxNodes = nodeCount };
}

public sealed class Search
{
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

        for (int depth = 1; depth <= maxDepth; depth++)
        {
            int score = Pruning.AlphaBeta(
                state, position, depth, -SearchState.Infinity, SearchState.Infinity, 0);
            if (state.StopRequested && depth > 1) break;

            lastScore = score;
            completedDepth = depth;

            if (!SuppressOutput)
            {
                long elapsedMilliseconds = state.Clock.ElapsedMilliseconds;
                long nodesPerSecond = elapsedMilliseconds > 0
                    ? state.NodeCount * 1000 / elapsedMilliseconds
                    : state.NodeCount;
                Console.WriteLine(
                    $"info depth {depth} score cp {score} nodes {state.NodeCount} " +
                    $"nps {nodesPerSecond} time {elapsedMilliseconds} pv {state.PrincipalVariationMove}");
            }

            if (Math.Abs(score) >= Eval.MateBound) break;
            if (state.ReachedSearchLimit()) break;
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
