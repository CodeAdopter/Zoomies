using System.Diagnostics;

namespace Zoomies.Engine;
public sealed class SearchStats
{
    // main search
    public long TtProbes, TtHits, TtMoveHits, TtCutoffs;
    public long InteriorNodes, MovesGenerated, MovesSearched;
    public long BetaCutoffs, FirstMoveCutoffs, CutoffOrdinalSum;
    public long CheckExtensions, IirReductions;
    public long RfpCutoffs, RazorTries, RazorCutoffs;
    public long NmpTries, NmpCutoffs;
    public long LmpEvents, LmpSkippedMoves;
    public long FutilityPrunes, HistoryPrunes, SeePrunes, QuietSeePrunes;
    public long LmrApplied, LmrReductionSum, LmrResearches, PvsResearches;
    public long AspFailHighs, AspFailLows;

    // quiescence
    public long QTtProbes, QTtHits, QTtCutoffs;
    public long QStandPatCutoffs, QDeltaPrunes, QSeePrunes;
    public long QMovesGenerated, QMovesSearched;
    public long QBetaCutoffs, QFirstMoveCutoffs, QCutoffOrdinalSum;

    // per-iteration table indexed by root depth
    private readonly long[] iterationNodes = new long[SearchState.MaximumPly];
    private readonly long[] iterationTotalNodes = new long[SearchState.MaximumPly];
    private readonly long[] iterationMilliseconds = new long[SearchState.MaximumPly];
    private readonly int[] iterationScore = new int[SearchState.MaximumPly];
    private readonly int[] iterationSelectiveDepth = new int[SearchState.MaximumPly];
    private readonly int[] iterationFailHighs = new int[SearchState.MaximumPly];
    private readonly int[] iterationFailLows = new int[SearchState.MaximumPly];
    private int lastIterationDepth;

    [Conditional("STATS")]
    public void Reset()
    {
        TtProbes = TtHits = TtMoveHits = TtCutoffs = 0;
        InteriorNodes = MovesGenerated = MovesSearched = 0;
        BetaCutoffs = FirstMoveCutoffs = CutoffOrdinalSum = 0;
        CheckExtensions = IirReductions = 0;
        RfpCutoffs = RazorTries = RazorCutoffs = 0;
        NmpTries = NmpCutoffs = 0;
        LmpEvents = LmpSkippedMoves = 0;
        FutilityPrunes = HistoryPrunes = SeePrunes = QuietSeePrunes = 0;
        LmrApplied = LmrReductionSum = LmrResearches = PvsResearches = 0;
        AspFailHighs = AspFailLows = 0;
        QTtProbes = QTtHits = QTtCutoffs = 0;
        QStandPatCutoffs = QDeltaPrunes = QSeePrunes = 0;
        QMovesGenerated = QMovesSearched = 0;
        QBetaCutoffs = QFirstMoveCutoffs = QCutoffOrdinalSum = 0;
        Array.Clear(iterationNodes);
        Array.Clear(iterationTotalNodes);
        Array.Clear(iterationMilliseconds);
        Array.Clear(iterationScore);
        Array.Clear(iterationSelectiveDepth);
        Array.Clear(iterationFailHighs);
        Array.Clear(iterationFailLows);
        lastIterationDepth = 0;
    }

    // main search probes
    [Conditional("STATS")]
    public void TtProbe(bool hit, bool hasMove)
    {
        TtProbes++;
        if (hit) TtHits++;
        if (hasMove) TtMoveHits++;
    }

    [Conditional("STATS")] public void TtCutoff() => TtCutoffs++;
    [Conditional("STATS")] public void InteriorNode() => InteriorNodes++;
    [Conditional("STATS")] public void GeneratedMoves(int count) => MovesGenerated += count;
    [Conditional("STATS")] public void MoveSearched() => MovesSearched++;
    [Conditional("STATS")] public void CheckExtension() => CheckExtensions++;
    [Conditional("STATS")] public void IirReduction() => IirReductions++;
    [Conditional("STATS")] public void RfpCutoff() => RfpCutoffs++;
    [Conditional("STATS")] public void RazorTry() => RazorTries++;
    [Conditional("STATS")] public void RazorCutoff() => RazorCutoffs++;
    [Conditional("STATS")] public void NmpTry() => NmpTries++;
    [Conditional("STATS")] public void NmpCutoff() => NmpCutoffs++;
    [Conditional("STATS")] public void FutilityPrune() => FutilityPrunes++;
    [Conditional("STATS")] public void HistoryPrune() => HistoryPrunes++;
    [Conditional("STATS")] public void SeePrune() => SeePrunes++;
    [Conditional("STATS")] public void QuietSeePrune() => QuietSeePrunes++;
    [Conditional("STATS")] public void AspFailHigh() => AspFailHighs++;
    [Conditional("STATS")] public void AspFailLow() => AspFailLows++;

    [Conditional("STATS")]
    public void LmpSkip(int skippedMoves)
    {
        LmpEvents++;
        LmpSkippedMoves += skippedMoves;
    }

    [Conditional("STATS")]
    public void LmrReduce(int reduction)
    {
        LmrApplied++;
        LmrReductionSum += reduction;
    }

    [Conditional("STATS")]
    public void Research(int reduction)
    {
        if (reduction > 0) LmrResearches++;
        else PvsResearches++;
    }

    [Conditional("STATS")]
    public void BetaCutoff(int ordinal)
    {
        BetaCutoffs++;
        CutoffOrdinalSum += ordinal;
        if (ordinal <= 1) FirstMoveCutoffs++;
    }

    // quiescence probes

    [Conditional("STATS")]
    public void QTtProbe(bool hit)
    {
        QTtProbes++;
        if (hit) QTtHits++;
    }

    [Conditional("STATS")] public void QTtCutoff() => QTtCutoffs++;
    [Conditional("STATS")] public void QStandPatCutoff() => QStandPatCutoffs++;
    [Conditional("STATS")] public void QDeltaPrune() => QDeltaPrunes++;
    [Conditional("STATS")] public void QSeePrune() => QSeePrunes++;
    [Conditional("STATS")] public void QGeneratedMoves(int count) => QMovesGenerated += count;
    [Conditional("STATS")] public void QMoveSearched() => QMovesSearched++;

    [Conditional("STATS")]
    public void QBetaCutoff(int ordinal)
    {
        QBetaCutoffs++;
        QCutoffOrdinalSum += ordinal;
        if (ordinal <= 1) QFirstMoveCutoffs++;
    }

    [Conditional("STATS")]
    public void RecordIteration(
        int depth, long nodes, long totalNodes, long milliseconds,
        int score, int selectiveDepth, int failHighs, int failLows)
    {
        if (depth < 0 || depth >= iterationNodes.Length) return;
        iterationNodes[depth] = nodes;
        iterationTotalNodes[depth] = totalNodes;
        iterationMilliseconds[depth] = milliseconds;
        iterationScore[depth] = score;
        iterationSelectiveDepth[depth] = selectiveDepth;
        iterationFailHighs[depth] = failHighs;
        iterationFailLows[depth] = failLows;
        lastIterationDepth = Math.Max(lastIterationDepth, depth);
    }

    [Conditional("STATS")]
    public void PrintReport(SearchState state, bool suppressOutput)
    {
        if (suppressOutput) return;

        Info("==== search stats (main thread) ====");
        PrintIterationTable();
        PrintTreeSummary(state);
        PrintPruningSummary(state);
        PrintQuiescenceSummary(state);
    }

    [Conditional("STATS")]
    private void PrintIterationTable()
    {
        Info($"{"depth",5} | {"nodes",13} | {"growth",6} | {"total nodes",13} | {"ms",6} | {"score",5} | {"seld",4} | {"aFH",3} | {"aFL",3}");

        long previousNodes = 0;
        for (int depth = 1; depth <= lastIterationDepth; depth++)
        {
            long nodes = iterationNodes[depth];
            if (nodes == 0) continue;

            string growth = previousNodes > 0 ? $"x{(double)nodes / previousNodes:F2}" : "-";
            Info($"{depth,5} | {nodes,13:N0} | {growth,6} | {iterationTotalNodes[depth],13:N0} | " +
                 $"{iterationMilliseconds[depth],6:N0} | {iterationScore[depth],5} | " +
                 $"{iterationSelectiveDepth[depth],4} | {iterationFailHighs[depth],3} | {iterationFailLows[depth],3}");
            previousNodes = nodes;
        }
    }

    [Conditional("STATS")]
    private void PrintTreeSummary(SearchState state)
    {
        long totalNodes = state.NodeCount;
        long quiescenceNodes = state.QuiescenceNodeCount;
        double quiescenceShare = Percent(quiescenceNodes, totalNodes);
        Info($"{"nodes:",-13} total={totalNodes:N0} interior={InteriorNodes:N0} " +
             $"qnodes={quiescenceNodes:N0} ({quiescenceShare:F1}%) evals={state.EvaluationCount:N0}");

        double hitRate = Percent(TtHits, TtProbes);
        double moveRate = Percent(TtMoveHits, TtProbes);
        double cutoffRate = Percent(TtCutoffs, TtProbes);
        Info($"{"tt main:",-13} probes={TtProbes:N0} hit={hitRate:F1}% with-move={moveRate:F1}% cutoff={cutoffRate:F1}%");

        double quiescenceHitRate = Percent(QTtHits, QTtProbes);
        double quiescenceCutoffRate = Percent(QTtCutoffs, QTtProbes);
        Info($"{"tt qsearch:",-13} probes={QTtProbes:N0} hit={quiescenceHitRate:F1}% cutoff={quiescenceCutoffRate:F1}%");

        double generatedPerNode = Average(MovesGenerated, InteriorNodes);
        double searchedPerNode = Average(MovesSearched, InteriorNodes);
        Info($"{"moves/node:",-13} generated={generatedPerNode:F2} searched={searchedPerNode:F2}");

        double cutoffNodeShare = Percent(BetaCutoffs, InteriorNodes);
        double firstMoveRate = Percent(FirstMoveCutoffs, BetaCutoffs);
        double averageCutoffMove = Average(CutoffOrdinalSum, BetaCutoffs);
        Info($"{"cutoffs:",-13} {BetaCutoffs:N0} ({cutoffNodeShare:F1}% of interior) " +
             $"first-move={firstMoveRate:F1}% avg-cutoff-move={averageCutoffMove:F2}");
    }

    [Conditional("STATS")]
    private void PrintPruningSummary(SearchState state)
    {
        double reducedShare = Percent(LmrApplied, MovesSearched);
        double averageReduction = Average(LmrReductionSum, LmrApplied);
        double researchRate = Percent(LmrResearches, LmrApplied);
        Info($"{"lmr:",-13} applied={LmrApplied:N0} ({reducedShare:F1}% of searched) " +
             $"avg-reduction={averageReduction:F2} re-search={researchRate:F1}% pvs-re-search={PvsResearches:N0}");

        double nullMoveCutoffRate = Percent(NmpCutoffs, NmpTries);
        Info($"{"nmp:",-13} tries={NmpTries:N0} cutoff={nullMoveCutoffRate:F1}%");

        Info($"{"node prunes:",-13} rfp={RfpCutoffs:N0} razor={RazorCutoffs:N0}/{RazorTries:N0}");

        Info($"{"move prunes:",-13} futility={FutilityPrunes:N0} history={HistoryPrunes:N0} " +
             $"see={SeePrunes:N0} quiet-see={QuietSeePrunes:N0} " +
             $"lmp={LmpSkippedMoves:N0} moves in {LmpEvents:N0} events");

        Info($"{"extensions:",-13} check={CheckExtensions:N0} iir={IirReductions:N0}");

        double extensionRate = Percent(state.SingularExtensions, state.SingularAttempts);
        Info($"{"singular:",-13} tries={state.SingularAttempts:N0} " +
             $"ext={state.SingularExtensions:N0} ({extensionRate:F1}%) " +
             $"multicut={state.SingularMulticuts:N0} negext={state.SingularNegativeExtensions:N0} " +
             $"dext={state.SingularDoubleExtensions:N0}");

        Info($"{"aspiration:",-13} fail-high={AspFailHighs:N0} fail-low={AspFailLows:N0}");
    }

    [Conditional("STATS")]
    private void PrintQuiescenceSummary(SearchState state)
    {
        double firstMoveRate = Percent(QFirstMoveCutoffs, QBetaCutoffs);
        double searchedPerNode = Average(QMovesSearched, state.QuiescenceNodeCount);
        Info($"{"qsearch:",-13} standpat-cut={QStandPatCutoffs:N0} delta={QDeltaPrunes:N0} see={QSeePrunes:N0} " +
             $"beta-cut={QBetaCutoffs:N0} first-move={firstMoveRate:F1}% searched/qnode={searchedPerNode:F2}");
    }

    private static double Percent(long part, long total) => total > 0 ? 100.0 * part / total : 0.0;

    private static double Average(long sum, long count) => count > 0 ? (double)sum / count : 0.0;

    private static void Info(string line) => Console.WriteLine($"info string {line}");
}
