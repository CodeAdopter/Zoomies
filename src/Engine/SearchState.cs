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
    public int ZoomiesReduction;
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
    public readonly short[] NonPawnCorrectionHistory = new short[2 * 2 * CorrectionHistorySize];
    public readonly int[] PlayedPieceTo = new int[MaximumPly];
    public readonly int[] StaticEvalStack = new int[MaximumPly];
    public readonly Move[] RootEffortMoves = new Move[256];
    public readonly long[] RootEffortNodes = new long[256];
    public int RootEffortCount;
    public long RootEffortTotal;

    public void AddRootEffort(Move move, long nodes)
    {
        RootEffortTotal += nodes;
        for (int i = 0; i < RootEffortCount; i++)
        {
            if (RootEffortMoves[i] == move) 
            { 
                RootEffortNodes[i] += nodes; 
                return; 
            }
        }

        if (RootEffortCount < RootEffortMoves.Length)
        {
            RootEffortMoves[RootEffortCount] = move;
            RootEffortNodes[RootEffortCount++] = nodes;
        }
    }

    public long RootEffortFor(Move move)
    {
        for (int i = 0; i < RootEffortCount; i++)
            if (RootEffortMoves[i] == move) 
                return RootEffortNodes[i];
        return 0;
    }

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
        ZoomiesReduction = 0;
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
        RootEffortCount = 0;
        RootEffortTotal = 0;
        SelectiveDepth = 0;
        Array.Clear(KillerMoves, 0, KillerMoves.Length);
        Array.Fill(PlayedPieceTo, -1);
    }

    public void ClearHistory()
    {
        Array.Clear(QuietHistory, 0, QuietHistory.Length);
        Array.Clear(CounterMoves, 0, CounterMoves.Length);
        Array.Clear(ContinuationHistory1, 0, ContinuationHistory1.Length);
        Array.Clear(ContinuationHistory2, 0, ContinuationHistory2.Length);
        Array.Clear(CaptureHistory, 0, CaptureHistory.Length);
        Array.Clear(PawnHistory, 0, PawnHistory.Length);
        Array.Clear(PawnCorrectionHistory, 0, PawnCorrectionHistory.Length);
        Array.Clear(NonPawnCorrectionHistory, 0, NonPawnCorrectionHistory.Length);
    }

    public bool ReachedSearchLimit()
    {
        if (NodeLimit > 0 && NodeCount >= NodeLimit) return true;
        if (SearchUntilStopped) return false;
        return Clock.ElapsedMilliseconds >= TimeLimitMilliseconds;
    }
}
