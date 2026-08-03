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
    public long SoftTimeLimitMilliseconds;
    public bool HasSoftLimit;
    public long NodeLimit;
    public bool SearchUntilStopped;
    public volatile bool StopRequested;
    public Move PrincipalVariationMove;
    public const int PieceToCount = 14 * 64;

    public readonly Move[,] KillerMoves = new Move[MaximumPly, 2];
    public readonly int[] QuietHistory = new int[2 * 64 * 64];
    public readonly int[] ContinuationHistory1 = new int[PieceToCount * PieceToCount];
    public readonly int[] ContinuationHistory2 = new int[PieceToCount * PieceToCount];
    public readonly int[] PlayedPieceTo = new int[MaximumPly]; // -1 = none (root/null move)

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
        SoftTimeLimitMilliseconds = limits.SoftTimeMilliseconds > 0
            ? limits.SoftTimeMilliseconds
            : TimeLimitMilliseconds;
        HasSoftLimit = limits.SoftTimeMilliseconds > 0;
        NodeLimit = limits.MaxNodes;
        PrincipalVariationMove = default;
        Array.Clear(KillerMoves, 0, KillerMoves.Length);
        Array.Fill(PlayedPieceTo, -1);
    }

    // keep history between searches for faster convergence at short time controls; 
    // stale entries fade over time, and a new game resets everything
    public void ClearHistory()
    {
        Array.Clear(QuietHistory, 0, QuietHistory.Length);
        Array.Clear(ContinuationHistory1, 0, ContinuationHistory1.Length);
        Array.Clear(ContinuationHistory2, 0, ContinuationHistory2.Length);
    }

    public bool ReachedSearchLimit()
    {
        if (NodeLimit > 0 && NodeCount >= NodeLimit) return true;
        if (SearchUntilStopped) return false;
        return Clock.ElapsedMilliseconds >= TimeLimitMilliseconds;
    }
}
