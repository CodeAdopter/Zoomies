using System.Diagnostics;
using Zoomies.Core;

namespace Zoomies.Engine;

public sealed class SearchState
{
    public const int Infinity = 32000;
    public const int MaximumPly = 128;

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
    public long BestMoveEffortNodes;
    public int SelectiveDepth;
    public int RootDepth;
    public long SingularAttempts;
    public long SingularExtensions;
    public long SingularMulticuts;
    public long SingularNegativeExtensions;
    public long SingularDoubleExtensions;
    public int DoubleExtensionPath;
    public const int PieceToCount = 14 * 64;
    public const int CorrectionHistorySize = 16384;
    public const int PawnHistorySize = 512;
    public const int NoStaticEval = int.MinValue / 2;

    public readonly Move[] KillerMoves = new Move[MaximumPly * 2];
    public readonly Move[] CounterMoves = new Move[PieceToCount];
    public readonly int[] QuietHistory = new int[2 * 64 * 64];
    public readonly short[] ContinuationHistory1 = new short[PieceToCount * PieceToCount];
    public readonly short[] ContinuationHistory2 = new short[PieceToCount * PieceToCount];
    public readonly short[] CaptureHistory = new short[PieceToCount * 8];
    public readonly short[] PawnHistory = new short[PawnHistorySize * PieceToCount];
    public readonly short[] PawnCorrectionHistory = new short[2 * CorrectionHistorySize];
    public readonly int[] PlayedPieceTo = new int[MaximumPly]; // -1 = none (root/null move)
    public readonly int[] StaticEvalStack = new int[MaximumPly];

    public readonly Stopwatch Clock = new();
    public readonly SearchStats Stats = new();
    public readonly TranspositionTable Tt;
    public SearchState() => Tt = new(16);
    public SearchState(TranspositionTable sharedTt) => Tt = sharedTt;

    public void Reset(in SearchLimits limits, bool bumpTtGeneration = true)
    {
        if (bumpTtGeneration) Tt.NewSearch();
        Stats.Reset();
        NodeCount = 0;
        QuiescenceNodeCount = 0;
        EvaluationCount = 0;
        RootDepth = 0;
        SingularAttempts = 0;
        SingularExtensions = 0;
        SingularMulticuts = 0;
        SingularNegativeExtensions = 0;
        SingularDoubleExtensions = 0;
        DoubleExtensionPath = 0;

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
        BestMoveEffortNodes = 0;
        SelectiveDepth = 0;
        Array.Clear(KillerMoves, 0, KillerMoves.Length);
        Array.Fill(PlayedPieceTo, -1);
    }

    // keep history between searches for faster convergence at short time controls; 
    // stale entries fade over time, and a new game resets everything
    public void ClearHistory()
    {
        Array.Clear(QuietHistory, 0, QuietHistory.Length);
        Array.Clear(CounterMoves, 0, CounterMoves.Length);
        Array.Clear(ContinuationHistory1, 0, ContinuationHistory1.Length);
        Array.Clear(ContinuationHistory2, 0, ContinuationHistory2.Length);
        Array.Clear(CaptureHistory, 0, CaptureHistory.Length);
        Array.Clear(PawnHistory, 0, PawnHistory.Length);
        Array.Clear(PawnCorrectionHistory, 0, PawnCorrectionHistory.Length);
    }

    public bool ReachedSearchLimit()
    {
        if (NodeLimit > 0 && NodeCount >= NodeLimit) return true;
        if (SearchUntilStopped) return false;
        return Clock.ElapsedMilliseconds >= TimeLimitMilliseconds;
    }
}
