using System.Diagnostics;
using Zoomies.Core;

namespace Zoomies.Engine;

public sealed class SearchState
{
    public const int Infinity = 32000;
    public const int MaximumPly = 128;
    public const int DeltaMargin = 200;

    public long NodeCount;
    public long QuiescenceNodeCount;
    public long EvaluationCount;
    public long TimeLimitMilliseconds;
    public long NodeLimit;
    public bool SearchUntilStopped;
    public volatile bool StopRequested;
    public Move PrincipalVariationMove;

    public readonly Move[,] KillerMoves = new Move[MaximumPly, 2];
    public readonly Stopwatch Clock = new();
    public readonly TranspositionTable Tt = new(16);

    public void Reset(in SearchLimits limits)
    {
        Tt.NewSearch();
        NodeCount = 0;
        QuiescenceNodeCount = 0;
        EvaluationCount = 0;
        SearchUntilStopped = limits.SearchUntilStopped;
        Clock.Restart();
        TimeLimitMilliseconds = limits.MoveTimeMilliseconds > 0
            ? limits.MoveTimeMilliseconds
            : long.MaxValue;
        NodeLimit = limits.MaxNodes;
        PrincipalVariationMove = default;
        Array.Clear(KillerMoves, 0, KillerMoves.Length);
    }

    public bool ReachedSearchLimit()
    {
        if (NodeLimit > 0 && NodeCount >= NodeLimit) return true;
        if (SearchUntilStopped) return false;
        return Clock.ElapsedMilliseconds >= TimeLimitMilliseconds;
    }
}
