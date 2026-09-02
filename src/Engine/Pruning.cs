using System.Runtime.CompilerServices;
using Zoomies.Core;

namespace Zoomies.Engine;

internal static class Pruning
{
    private static readonly int[] LmpThreshold = [0, 5, 8, 12, 18, 26, 36];
    private static int LmpMaxDepth;

    private static int LmpBudget(int depth, bool improving) => Math.Max(LmpThreshold[depth] + (improving ? Tune.LmpImp : -Tune.LmpNonImp) - depth, 0);

    private static int SingularMinDepth;
    private static int SingularMargin;
    private static int SingularTtSlack;
    private static int DoubleExtensionMargin;
    private static int DoubleExtensionLimit;
    private static int QuietSeeMaxDepth;
    private static int QuietSeeMargin;
    private static bool DemoteBadCaptures;
    private static bool UseCounterMove;
    private static int CheckExtMaxEvasions;
    private static bool RazorEnabled;
    private static int RazorBase;
    private static int ZoomReduction;
    private static int ZoomMinDepth;
    private static int ZoomCold;
    private static bool ZoomFrom;
    private static int MaoFlat;
    private static int MaoWeight;
    private static int RootNodeOrd;
    private static int OutpostReduction;
    private static int NonPawnCorrWeight;
    private static int LmrGradGood;
    private static int LmrGradBad;
    private static int LmrGradMinDepth;
    private static int LmrGradHistMax;
    private static bool LazyQuietScoring;
    private static int ZoomiesMoveMin;
    private static int ZoomiesHistMax;
    private static int ZoomiesScale;
    private static int ZoomiesNmp;

    private static readonly int[] LmrTable = new int[64 * 64];

    private static readonly ulong[][] OutpostFrontSpan = BuildOutpostSpans();

    private static ulong[][] BuildOutpostSpans()
    {
        var spans = new ulong[2][];
        spans[(int)Color.White] = new ulong[64];
        spans[(int)Color.Black] = new ulong[64];
        for (int sq = 0; sq < 64; sq++)
        {
            int f = sq & 7, r = sq >> 3;
            ulong adjFiles = 0;
            if (f > 0) adjFiles |= Bitboard.FileMask((Core.File)(f - 1));
            if (f < 7) adjFiles |= Bitboard.FileMask((Core.File)(f + 1));
            ulong ahead = 0, behind = 0;
            for (int rr = r + 1; rr <= 7; rr++) ahead |= Bitboard.RankMask((Rank)rr);
            for (int rr = r - 1; rr >= 0; rr--) behind |= Bitboard.RankMask((Rank)rr);
            spans[(int)Color.White][sq] = adjFiles & ahead;
            spans[(int)Color.Black][sq] = adjFiles & behind;
        }
        return spans;
    }

    static Pruning() => Refresh();

    internal static void Refresh()
    {
        LmpMaxDepth = Math.Min(Tune.LmpMaxDepth, 6);
        SingularMinDepth = Tune.SingularMinDepth;
        SingularMargin = Tune.SingularMargin;
        SingularTtSlack = Tune.SingularTtSlack;
        DoubleExtensionMargin = Tune.DoubleExtensionMargin;
        DoubleExtensionLimit = Tune.DoubleExtensionLimit;
        QuietSeeMaxDepth = Tune.QuietSeeMaxDepth;
        QuietSeeMargin = Tune.QuietSeeMargin;
        DemoteBadCaptures = Tune.BadCapDemote != 0;
        UseCounterMove = Tune.CounterMove != 0;
        CheckExtMaxEvasions = Tune.CheckExtMaxEvasions;
        RazorEnabled = Tune.Razor != 0;
        RazorBase = Tune.RazorBase;
        ZoomReduction = Tune.Zoom;
        ZoomMinDepth = Tune.ZoomMinDepth;
        ZoomCold = Tune.ZoomCold;
        ZoomFrom = Tune.ZoomFrom != 0;
        MaoFlat = Tune.MaoFlat;
        MaoWeight = Tune.MaoWeight;
        RootNodeOrd = Tune.RootNodeOrd;
        LazyQuietScoring = Tune.LazyQuietScore != 0;
        ZoomiesMoveMin = Tune.ZoomiesMoves;
        ZoomiesHistMax = Tune.ZoomiesHist;
        ZoomiesScale = Tune.ZoomiesScale;
        ZoomiesNmp = Tune.ZoomiesNmp;
        OutpostReduction = Tune.Outpost;
        NonPawnCorrWeight = Tune.CorrNonPawn;
        LmrGradGood = Tune.LmrGradGood;
        LmrGradBad = Tune.LmrGradBad;
        LmrGradMinDepth = Tune.LmrGradMinDepth;
        LmrGradHistMax = Tune.LmrGradHistMax;

        for (int d = 1; d < 64; d++)
            for (int m = 1; m < 64; m++)
                LmrTable[(d << 6) | m] = (int)(Tune.LmrBase / 100.0 + Math.Log(d) * Math.Log(m) / (Tune.LmrDiv / 100.0));
    }

    [SkipLocalsInit]
    public static int AlphaBeta(SearchState state, Position position, int depth,
    int alpha, int beta, int ply,
    bool allowNullMove = true, bool cutNode = false, Move excluded = default,
    int knownInCheck = -1, int knownStaticEval = int.MinValue)
    {
        if (state.StopRequested) return 0;
        if ((state.NodeCount & 8191) == 0 && state.ReachedSearchLimit())
        {
            state.StopRequested = true;
            return 0;
        }

        if (ply > 0 && (position.HasRepeated() || position.IsFiftyMoveRule()))
            return 0;

        // mate distance pruning
        if (ply > 0)
        {
            alpha = Math.Max(alpha, -Eval.MateValue + ply);
            beta = Math.Min(beta, Eval.MateValue - ply - 1);
            if (alpha >= beta) return alpha;
        }

        if (ply >= SearchState.MaximumPly - 1)
            return CorrectEval(state, position, Eval.Evaluate(position));

        bool excludedSearch = excluded.EncodedValue != 0;

        ulong key = position.History[position.Ply].Hash;
        bool ttHit = state.Tt.Probe(key, out TranspositionTable.Entry ttEntry);
        state.Stats.TtProbe(depth, ttHit, ttHit && ttEntry.Move != 0);
        int ttScore = ttHit ? TranspositionTable.ScoreFromTt(ttEntry.Score, ply) : 0;
        if (ttHit && ply > 0 && beta - alpha == 1 && !excludedSearch && ttEntry.Depth >= depth)
        {
            switch (ttEntry.Flag)
            {
                case TtFlag.Exact:
                    state.Stats.TtCutoff();
                    return ttScore;
                case TtFlag.Lower when ttScore >= beta:
                    state.Stats.TtCutoff();
                    return ttScore;
                case TtFlag.Upper when ttScore <= alpha:
                    state.Stats.TtCutoff();
                    return ttScore;
            }
        }

        bool inCheck = knownInCheck >= 0 ? knownInCheck != 0 : position.InCheck(position.Turn);
        bool ttPv = beta - alpha > 1 || (ttHit && ttEntry.WasPv);

        // check extensions
        if (inCheck)
        {
            state.Stats.CheckExtension();
            depth++;
        }

        // internal iterative reduction
        if (ply > 0 && depth >= Tune.IirMinDepth && !inCheck && (!ttHit || ttEntry.Move == 0))
        {
            state.Stats.IirReduction();
            depth--;
            if (cutNode) depth--;
        }

        if (depth <= 0)
            return Quiescence.Search(state, position, alpha, beta, ply, inCheck);

        state.NodeCount++;
        state.Stats.InteriorNode();
        if (ply > state.SelectiveDepth) state.SelectiveDepth = ply;

        int staticEval = inCheck ? 0
            : knownStaticEval != int.MinValue ? knownStaticEval
            : CorrectEval(state, position, Eval.Evaluate(position));
        state.StaticEvalStack[ply] = inCheck ? SearchState.NoStaticEval : staticEval;
        bool improving = !inCheck && ply >= 2 &&
            state.StaticEvalStack[ply - 2] != SearchState.NoStaticEval &&
            staticEval > state.StaticEvalStack[ply - 2];

        // TT score as pruning bound:
        // Prune with the TT score when it provides a tighter bound than the static eval.
        int pruneEval = staticEval;
        if (ttHit &&
            !excludedSearch &&
            !inCheck &&
            Math.Abs(ttScore) < Eval.MateBound &&
            (ttEntry.Flag == TtFlag.Exact ||
             (ttEntry.Flag == TtFlag.Lower && ttScore > staticEval) ||
             (ttEntry.Flag == TtFlag.Upper && ttScore < staticEval)))
            pruneEval = ttScore;

        // reverse futility pruning
        if (ply > 0 &&
            !inCheck &&
            depth <= Tune.RfpMaxDepth &&
            beta < Eval.MateBound &&
            pruneEval - (Tune.RfpBase + (improving ? Tune.RfpImp : -Tune.RfpNonImp)) * depth >= beta)
        {
            state.Stats.RfpCutoff();
            return pruneEval;
        }

        // razoring
        if (RazorEnabled &&
            ply > 0 &&
            !inCheck &&
            depth <= 4 &&
            beta - alpha == 1 &&
            alpha > -Eval.MateBound &&
            pruneEval + RazorBase + 200 * depth <= alpha)
        {
            state.Stats.RazorTry();
            int razorScore = Quiescence.Search(state, position, alpha, beta, ply, inCheck);
            if (razorScore <= alpha)
            {
                state.Stats.RazorCutoff();
                return razorScore;
            }
        }

        // null move pruning
        if (allowNullMove &&
            !excludedSearch &&
            ply > 0 &&
            depth >= Tune.NmpMinDepth &&
            beta < Eval.MateBound &&
            !inCheck &&
            HasNonPawnMaterial(position, position.Turn) &&
            pruneEval >= beta)
        {
            state.Stats.NmpTry();
            int reduction = Tune.NmpBase + depth / Tune.NmpDiv;
            // depth zoomies: cheaper null-move verification at high root depth
            if (state.ZoomiesReduction != 0 && ZoomiesNmp != 0)
                reduction += state.ZoomiesReduction * ZoomiesNmp / 2;
            state.PlayedPieceTo[ply] = -1;
            state.Tt.Prefetch(position.KeyAfterNull());
            position.MakeNullMove();
            int nullScore = -AlphaBeta(state, position, depth - 1 - reduction, -beta, -beta + 1, ply + 1, false, !cutNode);
            position.UnmakeNullMove();

            if (state.StopRequested) return 0;
            if (nullScore >= beta)
            {
                state.Stats.NmpCutoff();
                return nullScore >= Eval.MateBound ? beta : nullScore;
            }
        }

        Span<Move> moves = stackalloc Move[256];
        int moveCount = Engine.Search.GenerateLegalMoves(position, moves);
        if (moveCount == 0)
            return inCheck ? -Eval.MateValue + ply : 0;
        state.Stats.GeneratedMoves(moveCount);

        // check extension demotion
        if (inCheck && depth > 1 && moveCount >= CheckExtMaxEvasions)
        {
            state.Stats.CheckExtDemotion();
            depth--;
        }

        Move hashMove = ttHit ? new Move(ttEntry.Move) : default;
        if (hashMove.EncodedValue == 0 && ply == 0)
            hashMove = state.PrincipalVariationMove;

        bool ttCapture = hashMove.EncodedValue != 0 && hashMove.IsCapture;

        int fixedMoveCount = 0;
        if (hashMove.EncodedValue != 0)
        {
            for (int i = 0; i < moveCount; i++)
            {
                if (moves[i] != hashMove) continue;
                (moves[0], moves[i]) = (moves[i], moves[0]);
                fixedMoveCount = 1;
                break;
            }
        }

        Span<int> badCaptureSee = stackalloc int[256];
        int tacticalMoveEnd = fixedMoveCount +
            Order.TacticalMoves(
                position,
                moves[fixedMoveCount..moveCount],
                state.CaptureHistory,
                DemoteBadCaptures,
                out int badCaptureCount,
                badCaptureSee);

        // SEE-losing captures are ordered after quiets
        int quietEnd = moveCount - badCaptureCount;
        int quietStart = tacticalMoveEnd;
        for (int killerIndex = 0;
            killerIndex < 2 && quietStart < moveCount;
            killerIndex++)
        {
            Move killer = state.KillerMoves[ply * 2 + killerIndex];
            if (killer.EncodedValue == 0) continue;

            for (int i = quietStart; i < quietEnd; i++)
            {
                if (moves[i] != killer) continue;
                (moves[quietStart], moves[i]) = (moves[i], moves[quietStart]);
                quietStart++;
                break;
            }
        }

        Color sideToMove = position.Turn;

        int previous1 = ply >= 1 ? state.PlayedPieceTo[ply - 1] : -1;
        int previous2 = ply >= 2 ? state.PlayedPieceTo[ply - 2] : -1;

        Move counterMove = default;
        if (UseCounterMove && previous1 >= 0)
        {
            Move counter = state.CounterMoves[previous1];
            if (counter.EncodedValue != 0)
            {
                for (int i = quietStart; i < quietEnd; i++)
                {
                    if (moves[i] != counter) continue;
                    (moves[quietStart], moves[i]) = (moves[i], moves[quietStart]);
                    counterMove = counter;
                    quietStart++;
                    break;
                }
            }
        }
        int pawnHistoryBase = PawnHistoryBase(position);

        ulong zoomMask = ((ZoomReduction != 0 || ZoomCold != 0 || MaoFlat != 0 || MaoWeight != 0) && depth >= ZoomMinDepth) ? ZoomMask(position) : 0;
        bool rootNodeOrdering = RootNodeOrd != 0 && ply == 0 && state.RootEffortTotal > 0;

        Span<int> quietScores = stackalloc int[256];
        bool quietsScored = false;
        if (!LazyQuietScoring)
        {
            ScoreQuiets(state, position, sideToMove, moves, quietScores, quietStart, quietEnd, previous1, previous2, pawnHistoryBase, zoomMask, rootNodeOrdering);
            quietsScored = true;
        }
        int alphaOriginal = alpha;
        int bestScore = -SearchState.Infinity;
        Move bestMove = default;
        bool isPv = beta - alpha > 1;

        Span<Move> triedQuiets = stackalloc Move[64];
        int triedQuietCount = 0;
        Span<Move> triedNoisy = stackalloc Move[64];
        int triedNoisyCount = 0;
        int searchedMoves = 0;

        for (int i = 0; i < moveCount; i++)
        {
            if (i >= quietStart && i < quietEnd)
            {
                if (!quietsScored)
                {
                    quietsScored = true;
                    ScoreQuiets(state, position, sideToMove, moves, quietScores, quietStart, quietEnd, previous1, previous2, pawnHistoryBase, zoomMask, rootNodeOrdering);
                }

                int best = i;
                for (int j = i + 1; j < quietEnd; j++)
                    if (quietScores[j] > quietScores[best]) best = j;
                if (best != i)
                {
                    Move bm = moves[best];
                    int bs = quietScores[best];
                    for (int k = best; k > i; k--)
                    {
                        moves[k] = moves[k - 1];
                        quietScores[k] = quietScores[k - 1];
                    }
                    moves[i] = bm;
                    quietScores[i] = bs;
                }
            }

            Move move = moves[i];
            if (excludedSearch && move == excluded) continue;
            bool isQuiet = move.IsQuiet;

            // late move pruning
            if (!isPv &&
                !inCheck &&
                bestScore > -Eval.MateBound &&
                depth <= LmpMaxDepth &&
                i < quietEnd &&
                i >= quietStart + LmpBudget(depth, improving))
            {
                if (quietEnd == moveCount)
                {
                    state.Stats.LmpSkip(moveCount - i);
                    break;
                }
                state.Stats.LmpSkip(quietEnd - i);
                i = quietEnd - 1;
                continue;
            }

            // futility pruning
            if (bestScore > -SearchState.Infinity &&
                !inCheck &&
                depth <= Tune.FutMaxDepth &&
                isQuiet &&
                alpha > -Eval.MateBound &&
                pruneEval + Tune.FutBase + Tune.FutSlope * depth + (improving ? Tune.FutImp : -Tune.FutNonImp) <= alpha)
            {
                state.Stats.FutilityPrune();
                continue;
            }

            // history pruning: skip quiets with very poor history at low depth
            if (bestScore > -SearchState.Infinity &&
                !inCheck &&
                depth <= Tune.HistPruneMaxDepth &&
                isQuiet &&
                i >= quietStart &&
                alpha > -Eval.MateBound &&
                quietScores[i] < -Tune.HistPruneMult * depth)
            {
                state.Stats.HistoryPrune();
                continue;
            }

            // SEE pruning: skip captures losing too much material at low depth      
            if (bestScore > -SearchState.Infinity &&
                depth <= Tune.SeePruneMaxDepth &&
                move.IsCapture &&
                (i >= quietEnd
                    ? badCaptureSee[i - quietEnd] < -Tune.SeePruneMult * depth
                    : !See.Ge(position, move, -Tune.SeePruneMult * depth)))
            {
                state.Stats.SeePrune();
                continue;
            }

            // quiet SEE pruning
            if (bestScore > -Eval.MateBound &&
                ply > 0 &&
                !inCheck &&
                depth <= QuietSeeMaxDepth &&
                isQuiet &&
                !See.Ge(position, move, -QuietSeeMargin * depth))
            {
                state.Stats.QuietSeePrune();
                continue;
            }

            // singular extension: 
            // when only the TT move succeeds extend it by 1 ply
            int extension = 0;
            if (i == 0 &&
                !excludedSearch &&
                ttHit &&
                ply > 0 &&
                move.EncodedValue == ttEntry.Move &&
                depth >= SingularMinDepth &&
                ply < state.RootDepth * 2 &&
                ttEntry.Depth >= depth - SingularTtSlack &&
                ttEntry.Flag != TtFlag.Upper &&
                Math.Abs(ttScore) < Eval.MateBound)
            {
                state.SingularAttempts++;
                int singularBeta = ttScore - SingularMargin * depth;

                // verification search: exclude the TT move and see if 
                // another move can still reach singularBeta
                int singularScore = AlphaBeta(state, position, (depth - 1) / 2, singularBeta - 1, singularBeta, ply, false, cutNode, move, inCheck ? 1 : 0, staticEval);

                if (state.StopRequested) return 0;

                if (singularScore < singularBeta)
                {
                    state.SingularExtensions++;
                    extension = 1;

                    // double extension
                    if (!isPv &&
                        singularScore < singularBeta - DoubleExtensionMargin &&
                        state.DoubleExtensionPath < DoubleExtensionLimit)
                    {
                        state.SingularDoubleExtensions++;
                        extension = 2;
                    }
                }
                else if (singularBeta >= beta)
                {
                    // multicut
                    state.SingularMulticuts++;
                    return singularBeta;
                }
                else if (ttScore >= beta)
                {
                    // negative extension
                    state.SingularNegativeExtensions++;
                    extension = -1;
                }
            }

            if (isQuiet && triedQuietCount < triedQuiets.Length)
                triedQuiets[triedQuietCount++] = move;
            else if (!isQuiet && triedNoisyCount < triedNoisy.Length)
                triedNoisy[triedNoisyCount++] = move;

            bool isQueenTrade = Tune.QkeepLmr > 0 && !isQuiet && Order.IsQueenTrade(position, move);

            searchedMoves++;
            state.Stats.MoveSearched();
            long rootNodesBefore = ply == 0 ? state.NodeCount : 0;
            state.PlayedPieceTo[ply] = ((int)position.At(move.From) << 6) | (int)move.To;
            state.Tt.Prefetch(position.KeyAfter(sideToMove, move));
            position.Play(sideToMove, move);

            // principal variation search
            int score;
            if (i == 0)
            {
                if (extension == 2) state.DoubleExtensionPath++;
                score = -AlphaBeta(state, position, depth - 1 + extension, -beta, -alpha, ply + 1, true, isPv ? false : !cutNode);
                if (extension == 2) state.DoubleExtensionPath--;
            }
            else
            {
                // late move reductions
                int reduction = 0;
                int childInCheck = -1;
                int childStaticEval = int.MinValue;
                bool isBadCapture = i >= quietEnd;
                if (depth >= 3 &&
                    i >= (isPv ? 2 : 1) &&
                    !inCheck &&
                    (isQuiet || (isBadCapture && Tune.LmrBadCap > 0) || isQueenTrade) &&
                    move != state.KillerMoves[ply * 2] &&
                    move != state.KillerMoves[ply * 2 + 1] &&
                    move != counterMove)
                {
                    bool givesCheck = position.InCheck(position.Turn);
                    childInCheck = givesCheck ? 1 : 0;

                    int rr;
                    if (isQuiet)
                    {
                        // adaptive lmr
                        rr = LmrTable[(Math.Min(depth, 63) << 6) | Math.Min(i, 63)];
                        if (ttPv) rr -= Tune.LmrTtPv;
                        rr += improving ? -Tune.LmrImp : Tune.LmrNonImp;
                        if (cutNode) rr += Tune.LmrCutNode;
                        if (ttCapture) rr += Tune.LmrTtCapture;
                        if (givesCheck) rr -= Tune.LmrGivesCheck;
                        rr -= quietScores[i] / Tune.LmrHistDiv;
                        // zoom in to the action
                        if (zoomMask != 0)
                            rr += InZone(zoomMask, move) ? -ZoomReduction : ZoomCold;
                        // reduce a minor piece less when it lands on an outpost
                        if (OutpostReduction != 0 && IsOutpost(position, sideToMove, move.To))
                            rr -= OutpostReduction;
                        // reduce quiet pawn pushes less
                        if (Tune.LmrPawn != 0 && Types.TypeOf(position.At(move.To)) == PieceType.Pawn)
                            rr -= Tune.LmrPawn;
                        // depth zoomies
                        if (state.ZoomiesReduction != 0 && !ttPv &&
                            i >= ZoomiesMoveMin && quietScores[i] < ZoomiesHistMax)
                        {
                            rr += state.ZoomiesReduction;
                            if (ZoomiesScale != 0 && rr > 0)
                                rr += rr * state.ZoomiesReduction * ZoomiesScale / 256;
                        }
                        // use the post move eval gradient to adjust the reduction rather than stale history
                        if ((LmrGradGood | LmrGradBad) != 0 && !givesCheck && depth >= LmrGradMinDepth &&
                            (LmrGradHistMax == 0 || Math.Abs(quietScores[i]) < LmrGradHistMax))
                        {
                            childStaticEval = CorrectEval(state, position, Eval.Evaluate(position));
                            int grad = -childStaticEval - staticEval;
                            if (LmrGradGood != 0 && grad >= LmrGradGood) rr--;
                            if (LmrGradBad != 0 && grad <= -LmrGradBad) rr++;
                        }
                    }
                    else
                    {
                        rr = isBadCapture ? Tune.LmrBadCap : 0;

                        if (isQueenTrade) rr += Tune.QkeepLmr;
                        if (givesCheck) rr -= Tune.LmrGivesCheck;
                    }
                    if (rr < 1) rr = 1;
                    reduction = Math.Min(rr, depth - 2);
                    state.Stats.LmrReduce(reduction);
                }

                score = -AlphaBeta(state, position, depth - 1 - reduction, -alpha - 1, -alpha, ply + 1, true, !cutNode, default, childInCheck, childStaticEval);
                if (score > alpha && (reduction > 0 || score < beta))
                {
                    state.Stats.Research(reduction);
                    int newDepth = depth - 1;
                    if (reduction > 0 && score > bestScore + Tune.DoDeeperMargin) newDepth++;
                    score = -AlphaBeta(state, position, newDepth, -beta, -alpha, ply + 1, true, isPv ? false : !cutNode, default, childInCheck);
                }
            }

            position.Undo(sideToMove, move);

            if (RootNodeOrd != 0 && ply == 0)
                state.AddRootEffort(move, state.NodeCount - rootNodesBefore);

            if (state.StopRequested) return 0;
            if (score > bestScore)
            {
                bestScore = score;
                bestMove = move;
                if (ply == 0)
                {
                    state.PrincipalVariationMove = move;
                    state.BestMoveEffortNodes = state.NodeCount - rootNodesBefore;
                }
            }

            if (score > alpha) alpha = score;
            if (alpha < beta) continue;

            state.Stats.BetaCutoff(searchedMoves, i, fixedMoveCount, tacticalMoveEnd, quietStart, quietEnd);
            if (!excludedSearch)
            {
                UpdateCaptureHistories(
                    state, position, move, isQuiet,
                    triedNoisy[..triedNoisyCount], depth);
                if (isQuiet)
                {
                    UpdateQuietHistories(
                        state, position, sideToMove, move,
                        triedQuiets[..triedQuietCount], depth, previous1, previous2, pawnHistoryBase);
                    if (state.KillerMoves[ply * 2] != move)
                    {
                        state.KillerMoves[ply * 2 + 1] = state.KillerMoves[ply * 2];
                        state.KillerMoves[ply * 2] = move;
                    }
                    if (UseCounterMove && previous1 >= 0)
                        state.CounterMoves[previous1] = move;
                }
                else
                {
                    PenalizeQuiets(
                        state, position, sideToMove, default,
                        triedQuiets[..triedQuietCount], depth, previous1, previous2, pawnHistoryBase);
                }
            }
            break;
        }

        TtFlag storeFlag = bestScore >= beta ? TtFlag.Lower
            : bestScore > alphaOriginal ? TtFlag.Exact
            : TtFlag.Upper;

        // update the correction table with the difference between the search score
        // and static eval when the result is suitable for learning
        if (!inCheck &&
            !excludedSearch &&
            Math.Abs(bestScore) < Eval.MateBound &&
            bestMove.IsQuiet &&
            !(storeFlag == TtFlag.Lower && bestScore <= staticEval) &&
            !(storeFlag == TtFlag.Upper && bestScore >= staticEval))
            UpdateCorrectionHistory(state, position, depth, bestScore - staticEval);

        if (!excludedSearch)
        {
            state.Stats.TtStore(storeFlag);
            state.Tt.Store(
                key,
                bestMove.EncodedValue,
                TranspositionTable.ScoreToTt(bestScore, ply),
                depth,
                storeFlag,
                ttPv);
        }

        return bestScore;
    }

    private static ulong AttackSpan(Position position, Color c, ulong occ)
    {
        Square king = Bitboard.Bsf(position.BitboardOf(c, PieceType.King));
        ulong att = Tables.PawnAttacks(c, position.BitboardOf(c, PieceType.Pawn))
                  | Tables.KingAttacks(king);
        ulong b = position.BitboardOf(c, PieceType.Knight);
        while (b != 0) att |= Tables.KnightAttacks(Bitboard.PopLsb(ref b));
        b = position.DiagonalSliders(c);
        while (b != 0) att |= Tables.BishopAttacks(Bitboard.PopLsb(ref b), occ);
        b = position.OrthogonalSliders(c);
        while (b != 0) att |= Tables.RookAttacks(Bitboard.PopLsb(ref b), occ);
        return att;
    }

    private static bool InZone(ulong zoomMask, Move move) => ((zoomMask >> (int)move.To) & 1) != 0 || (ZoomFrom && ((zoomMask >> (int)move.From) & 1) != 0);

    // conditions for a minor piece outpost  
    // a) within the outpost zone
    // b) defended by a pawn
    // c) cannot be immediately attacked by enemy pawns
    private static bool IsOutpost(Position position, Color mover, Square to)
    {
        PieceType pt = Types.TypeOf(position.At(to));
        if (pt != PieceType.Knight && pt != PieceType.Bishop) return false;

        int rr = (int)Types.RelativeRank(mover, Types.RankOf(to));
        if (rr < (int)Rank.Rank4 || rr > (int)Rank.Rank6) return false;

        Color them = mover == Color.White ? Color.Black : Color.White;

        if ((Tables.PawnAttacks(them, to) & position.BitboardOf(mover, PieceType.Pawn)) == 0) return false;
        if ((OutpostFrontSpan[(int)mover][(int)to] & position.BitboardOf(them, PieceType.Pawn)) != 0) return false;
        return true;
    }

    // mutual attack ordering
    // quiet moves that touch squares attacked by both sides are
    // searched earlier, because they contest the zone of overlapping momentum (ZOOM).
    // They get two stacked bonuses:
    //   1. MaoFlat - for being in the zone (includes queen bonus)
    //   2. MaoWeight - more weight to cheaper pieces (king + queen get zero bonus)
    private static int MaoPieceWeight(Position position, Move move)
    {
        int queenValue = Eval.PieceValue[(int)PieceType.Queen];
        int pawnValue = Eval.PieceValue[(int)PieceType.Pawn];
        PieceType movingPiece = Types.TypeOf(position.At(move.From));
        int movingValue = movingPiece == PieceType.King ? queenValue : Eval.PieceValue[(int)movingPiece];
        int weight = 256 * (queenValue - movingValue) / (queenValue - pawnValue);
        return weight < 0 ? 0 : weight;
    }

    // zoom mask
    private static ulong ZoomMask(Position position)
    {
        ulong occ = position.AllPieces(Color.White) | position.AllPieces(Color.Black);
        return AttackSpan(position, Color.White, occ) & AttackSpan(position, Color.Black, occ);
    }

    private const int MaxHistory = 8192;

    private static int HistoryIndex(Color side, Move move) =>
        ((int)side << 12) | ((int)move.From << 6) | (int)move.To;

    private static int PieceToIndex(Position position, Move move) =>
        ((int)position.At(move.From) << 6) | (int)move.To;

    private static int PawnHistoryBase(Position position)
    {
        ulong pawns = MixKey(MixKey(position.BitboardOf(Color.White, PieceType.Pawn)) ^ position.BitboardOf(Color.Black, PieceType.Pawn));
        return (int)(pawns & (SearchState.PawnHistorySize - 1)) * SearchState.PieceToCount;
    }

    private static void ScoreQuiets(SearchState state, Position position, Color side, Span<Move> moves, Span<int> quietScores, int quietStart, int quietEnd, int previous1, int previous2, int pawnHistoryBase, ulong zoomMask, bool rootNodeOrdering)
    {
        for (int i = quietStart; i < quietEnd; i++)
        {
            quietScores[i] = QuietHistoryScore(state, position, side, moves[i], previous1, previous2, pawnHistoryBase);
            if (zoomMask != 0 && InZone(zoomMask, moves[i]))
                quietScores[i] += MaoFlat + (MaoWeight != 0 ? MaoWeight * MaoPieceWeight(position, moves[i]) / 256 : 0);
            if (rootNodeOrdering)
                quietScores[i] += (int)(state.RootEffortFor(moves[i]) * RootNodeOrd / state.RootEffortTotal);
        }
    }

    private static int QuietHistoryScore(
        SearchState state, Position position, Color side, Move move, int previous1, int previous2, int pawnHistoryBase)
    {
        int pieceTo = PieceToIndex(position, move);
        int score = state.QuietHistory[HistoryIndex(side, move)];
        score += state.PawnHistory[pawnHistoryBase + pieceTo];
        if (previous1 >= 0)
            score += state.ContinuationHistory1[previous1 * SearchState.PieceToCount + pieceTo];
        if (previous2 >= 0)
            score += state.ContinuationHistory2[previous2 * SearchState.PieceToCount + pieceTo];
        return score;
    }

    private static void UpdateQuietHistories(
        SearchState state, Position position, Color side, Move cutoffMove,
        ReadOnlySpan<Move> triedQuiets, int depth, int previous1, int previous2, int pawnHistoryBase)
    {
        int bonus = HistoryBonus(depth);

        UpdateQuietHistory(state, position, side, cutoffMove, bonus, previous1, previous2, pawnHistoryBase);
        PenalizeQuiets(state, position, side, cutoffMove, triedQuiets, depth, previous1, previous2, pawnHistoryBase);
    }

    // penalize quiet moves searched before the cutoff
    // also do this if the cutoff move is a capture or promotion
    private static void PenalizeQuiets(
        SearchState state, Position position, Color side, Move cutoffMove,
        ReadOnlySpan<Move> triedQuiets, int depth, int previous1, int previous2, int pawnHistoryBase)
    {
        int malus = -HistoryBonus(depth);
        foreach (Move tried in triedQuiets)
            if (tried != cutoffMove)
                UpdateQuietHistory(state, position, side, tried, malus, previous1, previous2, pawnHistoryBase);
    }

    private static int HistoryBonus(int depth) =>
        Math.Min(Tune.HistBonusCap, Tune.HistBonusQuad * depth * depth + Tune.HistBonusLin * depth + 16);

    // capture history
    private static void UpdateCaptureHistories(
        SearchState state, Position position, Move cutoffMove, bool cutoffQuiet,
        ReadOnlySpan<Move> triedNoisy, int depth)
    {
        int bonus = HistoryBonus(depth);
        if (!cutoffQuiet)
            Gravity(ref state.CaptureHistory[Order.CaptureHistoryIndex(position, cutoffMove)], bonus);
        foreach (Move tried in triedNoisy)
            if (tried != cutoffMove)
                Gravity(ref state.CaptureHistory[Order.CaptureHistoryIndex(position, tried)], -bonus);
    }

    private static void UpdateQuietHistory(
        SearchState state, Position position, Color side, Move move, int bonus, int previous1, int previous2, int pawnHistoryBase)
    {
        int pieceTo = PieceToIndex(position, move);
        Gravity(ref state.QuietHistory[HistoryIndex(side, move)], bonus);
        Gravity(ref state.PawnHistory[pawnHistoryBase + pieceTo], bonus);
        if (previous1 >= 0)
            Gravity(ref state.ContinuationHistory1[previous1 * SearchState.PieceToCount + pieceTo], bonus);
        if (previous2 >= 0)
            Gravity(ref state.ContinuationHistory2[previous2 * SearchState.PieceToCount + pieceTo], bonus);
    }

    // update the history value, giving more weight to recent results while keeping it bounded
    private static void Gravity(ref int entry, int bonus) => entry += bonus - entry * Math.Abs(bonus) / MaxHistory;

    private static void Gravity(ref short entry, int bonus)
    {
        int v = entry + bonus - entry * Math.Abs(bonus) / MaxHistory;
        entry = (short)v;
    }

    private const int CorrectionGrain = 256;
    private const int CorrectionWeight = 256;
    private const int CorrectionMax = 32 * CorrectionGrain;

    // adjust the static eval using the average search correction
    // for positions sharing the same pawn structure and non pawn piece placement.
    public static int CorrectEval(SearchState state, Position position, int rawEval)
    {
        int correction = state.PawnCorrectionHistory[CorrectionIndex(position)];
        // non pawn correction
        if (NonPawnCorrWeight != 0)
        {
            int nonPawn = state.NonPawnCorrectionHistory[NonPawnCorrectionIndex(position, Color.White)]
                        + state.NonPawnCorrectionHistory[NonPawnCorrectionIndex(position, Color.Black)];
            correction += nonPawn * NonPawnCorrWeight / 256;
        }
        return Math.Clamp(rawEval + correction / CorrectionGrain, -Eval.MateBound + 1, Eval.MateBound - 1);
    }

    private static void UpdateCorrectionHistory(SearchState state, Position position, int depth, int diff)
    {
        int weight = Math.Min(depth + 1, Tune.CorrWeightCap);
        BlendCorrection(ref state.PawnCorrectionHistory[CorrectionIndex(position)], diff, weight);
        BlendCorrection(ref state.NonPawnCorrectionHistory[NonPawnCorrectionIndex(position, Color.White)], diff, weight);
        BlendCorrection(ref state.NonPawnCorrectionHistory[NonPawnCorrectionIndex(position, Color.Black)], diff, weight);
    }

    private static void BlendCorrection(ref short entry, int diff, int weight)
    {
        long value = ((long)entry * (CorrectionWeight - weight) + (long)diff * CorrectionGrain * weight) / CorrectionWeight;
        entry = (short)Math.Clamp(value, -CorrectionMax, CorrectionMax);
    }

    private static int CorrectionIndex(Position position) =>
        ((int)position.Turn * SearchState.CorrectionHistorySize) +
        (int)(MixKey(MixKey(position.BitboardOf(Color.White, PieceType.Pawn))
                   ^ position.BitboardOf(Color.Black, PieceType.Pawn))
              & (SearchState.CorrectionHistorySize - 1));

    private static int NonPawnCorrectionIndex(Position position, Color pieceColor)
    {
        ulong h = MixKey(position.BitboardOf(pieceColor, PieceType.Knight) + 0x9E3779B97F4A7C15UL);
        h = MixKey(h ^ position.BitboardOf(pieceColor, PieceType.Bishop));
        h = MixKey(h ^ position.BitboardOf(pieceColor, PieceType.Rook));
        h = MixKey(h ^ position.BitboardOf(pieceColor, PieceType.Queen));
        h = MixKey(h ^ position.BitboardOf(pieceColor, PieceType.King));
        return (((int)pieceColor * 2 + (int)position.Turn) * SearchState.CorrectionHistorySize) + (int)(h & (SearchState.CorrectionHistorySize - 1));
    }

    private static ulong MixKey(ulong x)
    {
        x ^= x >> 33; x *= 0xFF51AFD7ED558CCDUL;
        x ^= x >> 33; x *= 0xC4CEB9FE1A85EC53UL;
        x ^= x >> 33;
        return x;
    }

    private static bool HasNonPawnMaterial(Position position, Color side) =>
        (position.BitboardOf(side, PieceType.Knight) |
         position.BitboardOf(side, PieceType.Bishop) |
         position.BitboardOf(side, PieceType.Rook) |
         position.BitboardOf(side, PieceType.Queen)) != 0;
}
