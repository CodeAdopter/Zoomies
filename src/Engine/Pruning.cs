using Zoomies.Core;

namespace Zoomies.Engine;

internal static class Pruning
{
    private static readonly int[] LmpThreshold = [0, 5, 8, 12, 18, 26, 36];
    private const int LmpMaxDepth = 4;

    private static int LmpBudget(int depth, bool improving) =>
        Math.Max(LmpThreshold[depth] + (improving ? 2 : -1) - depth, 0);

    private const int SingularMinDepth = 6;
    private const int SingularMargin = 2;
    private const int SingularTtSlack = 3;
    private const int DoubleExtensionMargin = 60;
    private const int DoubleExtensionLimit = 6;

    private static readonly int[] LmrTable = BuildLmrTable();

    private static int[] BuildLmrTable()
    {
        var t = new int[64 * 64];
        for (int d = 1; d < 64; d++)
            for (int m = 1; m < 64; m++)
                t[(d << 6) | m] = (int)(0.75 + Math.Log(d) * Math.Log(m) / 2.25);
        return t;
    }

    public static int AlphaBeta(SearchState state, Position position, int depth, 
    int alpha, int beta, int ply, 
    bool allowNullMove = true, bool cutNode = false, Move excluded = default)
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
            return Eval.Evaluate(position);

        bool excludedSearch = excluded.EncodedValue != 0;

        ulong key = position.History[position.Ply].Hash;
        bool ttHit = state.Tt.Probe(key, out TranspositionTable.Entry ttEntry);
        int ttScore = ttHit ? TranspositionTable.ScoreFromTt(ttEntry.Score, ply) : 0;
        if (ttHit && ply > 0 && !excludedSearch && ttEntry.Depth >= depth)
        {
            switch (ttEntry.Flag)
            {
                case TtFlag.Exact:
                    return ttScore;
                case TtFlag.Lower when ttScore >= beta:
                    return ttScore;
                case TtFlag.Upper when ttScore <= alpha:
                    return ttScore;
            }
        }

        bool inCheck = position.InCheck(position.Turn);

        // check extensions
        if (inCheck) depth++;

        // internal iterative reduction
        if (ply > 0 && depth >= 4 && !inCheck && (!ttHit || ttEntry.Move == 0))
        {
            depth--;
            if (cutNode) depth--;
        }

        if (depth <= 0)
            return Quiescence.Search(state, position, alpha, beta, ply);

        state.NodeCount++;

        int staticEval = inCheck ? 0 : CorrectEval(state, position, Eval.Evaluate(position));
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
            depth <= 6 &&
            beta < Eval.MateBound &&
            pruneEval - (80 + (improving ? 20 : -10)) * depth >= beta)
            return pruneEval;

        // null move pruning
        if (allowNullMove &&
            !excludedSearch &&
            ply > 0 &&
            depth >= 3 &&
            beta < Eval.MateBound &&
            !inCheck &&
            HasNonPawnMaterial(position, position.Turn) &&
            pruneEval >= beta)
        {
            int reduction = 3 + depth / 6;
            state.PlayedPieceTo[ply] = -1;
            position.MakeNullMove();
            int nullScore = -AlphaBeta(state, position, depth - 1 - reduction, -beta, -beta + 1, ply + 1, false, !cutNode);
            position.UnmakeNullMove();

            if (state.StopRequested) return 0;
            if (nullScore >= beta) return nullScore >= Eval.MateBound ? beta : nullScore;
        }

        Span<Move> moves = stackalloc Move[256];
        int moveCount = Engine.Search.GenerateLegalMoves(position, moves);
        if (moveCount == 0)
            return inCheck ? -Eval.MateValue + ply : 0;

        Move hashMove = ttHit ? new Move(ttEntry.Move) : default;
        if (hashMove.EncodedValue == 0 && ply == 0)
            hashMove = state.PrincipalVariationMove;

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

        int tacticalMoveEnd = fixedMoveCount +
            Order.TacticalMoves(
                position,
                moves[fixedMoveCount..moveCount]);

        int quietStart = tacticalMoveEnd;
        for (int killerIndex = 0;
            killerIndex < 2 && quietStart < moveCount;
            killerIndex++)
        {
            Move killer = state.KillerMoves[ply, killerIndex];
            if (killer.EncodedValue == 0) continue;

            for (int i = quietStart; i < moveCount; i++)
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

        Span<int> quietScores = stackalloc int[256];
        for (int i = quietStart; i < moveCount; i++)
            quietScores[i] = QuietHistoryScore(state, position, sideToMove, moves[i], previous1, previous2);

        for (int i = quietStart + 1; i < moveCount; i++)
        {
            Move move = moves[i];
            int score = quietScores[i];
            int insertionIndex = i - 1;

            // shift moves with a lower score to the right
            while (insertionIndex >= quietStart && quietScores[insertionIndex] < score)
            {
                moves[insertionIndex + 1] = moves[insertionIndex];
                quietScores[insertionIndex + 1] = quietScores[insertionIndex];
                insertionIndex--;
            }

            moves[insertionIndex + 1] = move;
            quietScores[insertionIndex + 1] = score;
        }
        int alphaOriginal = alpha;
        int bestScore = -SearchState.Infinity;
        Move bestMove = default;
        bool isPv = beta - alpha > 1;

        // futility pruning
        bool futile = !inCheck &&
            depth <= 2 &&
            alpha > -Eval.MateBound &&
            pruneEval + 100 + 120 * depth + (improving ? 40 : -30) <= alpha;

        Span<Move> triedQuiets = stackalloc Move[64];
        int triedQuietCount = 0;

        for (int i = 0; i < moveCount; i++)
        {
            Move move = moves[i];
            if (excludedSearch && move == excluded) continue;
            bool isQuiet = !move.IsCapture && (move.Flags & MoveFlags.Promotions) == 0;

            // late move pruning: after enough quiet moves have been searched at low depth
            // skip the rest: tactical moves don't count toward the quiet move limit
            if (!isPv &&
                !inCheck &&
                bestScore > -Eval.MateBound &&
                depth <= LmpMaxDepth &&
                i >= quietStart + LmpBudget(depth, improving))
                break;

            if (futile &&
                bestScore > -SearchState.Infinity &&
                isQuiet)
                continue;

            // history pruning: skip quiets with very poor history at low depth
            if (bestScore > -SearchState.Infinity &&
                !inCheck &&
                depth <= 4 &&
                isQuiet &&
                i >= quietStart &&
                alpha > -Eval.MateBound &&
                quietScores[i] < -2048 * depth)
                continue;

            // SEE pruning: skip captures losing too much material at low depth
            if (bestScore > -SearchState.Infinity &&
                depth <= 8 &&
                move.IsCapture &&
                !See.Ge(position, move, -100 * depth))
                continue;

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
                int singularScore = AlphaBeta(state, position, (depth - 1) / 2, singularBeta - 1, singularBeta, ply, false, cutNode, move);

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

            state.PlayedPieceTo[ply] = ((int)position.At(move.From) << 6) | (int)move.To;
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
                if (depth >= 3 &&
                    i >= (isPv ? 2 : 1) &&
                    !inCheck &&
                    isQuiet &&
                    move != state.KillerMoves[ply, 0] &&
                    move != state.KillerMoves[ply, 1])
                {
                    int rr = LmrTable[(Math.Min(depth, 63) << 6) | Math.Min(i, 63)];
                    if (isPv) rr--;
                    rr += improving ? -1 : 1;
                    if (cutNode) rr++;
                    if (i >= quietStart) rr -= quietScores[i] / 8192;
                    if (rr < 1) rr = 1;
                    reduction = Math.Min(rr, depth - 2);
                }

                score = -AlphaBeta(state, position, depth - 1 - reduction, -alpha - 1, -alpha, ply + 1, true, !cutNode);
                if (score > alpha && (reduction > 0 || score < beta))
                    score = -AlphaBeta(state, position, depth - 1, -beta, -alpha, ply + 1, true, isPv ? false : !cutNode);
            }

            position.Undo(sideToMove, move);

            if (state.StopRequested) return 0;
            if (score > bestScore)
            {
                bestScore = score;
                bestMove = move;
                if (ply == 0) state.PrincipalVariationMove = move;
            }

            if (score > alpha) alpha = score;
            if (alpha < beta) continue;

            if (isQuiet)
            {
                UpdateQuietHistories(
                    state, position, sideToMove, move,
                    triedQuiets[..triedQuietCount], depth, previous1, previous2);
                if (state.KillerMoves[ply, 0] != move)
                {
                    state.KillerMoves[ply, 1] = state.KillerMoves[ply, 0];
                    state.KillerMoves[ply, 0] = move;
                }
            }
            else
            {
                PenalizeQuiets(
                    state, position, sideToMove, default,
                    triedQuiets[..triedQuietCount], depth, previous1, previous2);
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
            !bestMove.IsCapture && (bestMove.Flags & MoveFlags.Promotions) == 0 &&
            !(storeFlag == TtFlag.Lower && bestScore <= staticEval) &&
            !(storeFlag == TtFlag.Upper && bestScore >= staticEval))
            UpdateCorrectionHistory(state, position, depth, bestScore - staticEval);

        if (!excludedSearch)
            state.Tt.Store(
                key,
                bestMove.EncodedValue,
                TranspositionTable.ScoreToTt(bestScore, ply),
                depth,
                storeFlag);

        return bestScore;
    }

    private const int MaxHistory = 8192;

    private static int HistoryIndex(Color side, Move move) =>
        ((int)side << 12) | ((int)move.From << 6) | (int)move.To;

    private static int PieceToIndex(Position position, Move move) =>
        ((int)position.At(move.From) << 6) | (int)move.To;

    private static int QuietHistoryScore(
        SearchState state, Position position, Color side, Move move, int previous1, int previous2)
    {
        int pieceTo = PieceToIndex(position, move);
        int score = state.QuietHistory[HistoryIndex(side, move)];
        if (previous1 >= 0)
            score += state.ContinuationHistory1[previous1 * SearchState.PieceToCount + pieceTo];
        if (previous2 >= 0)
            score += state.ContinuationHistory2[previous2 * SearchState.PieceToCount + pieceTo];
        return score;
    }

    private static void UpdateQuietHistories(
        SearchState state, Position position, Color side, Move cutoffMove,
        ReadOnlySpan<Move> triedQuiets, int depth, int previous1, int previous2)
    {
        int bonus = HistoryBonus(depth);

        UpdateQuietHistory(state, position, side, cutoffMove, bonus, previous1, previous2);
        PenalizeQuiets(state, position, side, cutoffMove, triedQuiets, depth, previous1, previous2);
    }

    // penalize quiet moves searched before the cutoff
    // also do this if the cutoff move is a capture or promotion
    private static void PenalizeQuiets(
        SearchState state, Position position, Color side, Move cutoffMove,
        ReadOnlySpan<Move> triedQuiets, int depth, int previous1, int previous2)
    {
        int malus = -HistoryBonus(depth);
        foreach (Move tried in triedQuiets)
            if (tried != cutoffMove)
                UpdateQuietHistory(state, position, side, tried, malus, previous1, previous2);
    }

    private static int HistoryBonus(int depth) =>
        Math.Min(1200, 16 * depth * depth + 32 * depth + 16);

    private static void UpdateQuietHistory(
        SearchState state, Position position, Color side, Move move, int bonus, int previous1, int previous2)
    {
        int pieceTo = PieceToIndex(position, move);
        Gravity(ref state.QuietHistory[HistoryIndex(side, move)], bonus);
        if (previous1 >= 0)
            Gravity(ref state.ContinuationHistory1[previous1 * SearchState.PieceToCount + pieceTo], bonus);
        if (previous2 >= 0)
            Gravity(ref state.ContinuationHistory2[previous2 * SearchState.PieceToCount + pieceTo], bonus);
    }

    // update the history value, giving more weight to recent results while keeping it bounded
    private static void Gravity(ref int entry, int bonus) => entry += bonus - entry * Math.Abs(bonus) / MaxHistory;

    private const int CorrectionGrain = 256;
    private const int CorrectionWeight = 256;
    private const int CorrectionMax = 32 * CorrectionGrain;

    // adjust the static eval using the average search correction
    // for positions sharing the same pawn structure and non pawn piece placement.
    public static int CorrectEval(SearchState state, Position position, int rawEval)
    {
        int correction = state.PawnCorrectionHistory[CorrectionIndex(position)];
        return Math.Clamp(rawEval + correction / CorrectionGrain, -Eval.MateBound + 1, Eval.MateBound - 1);
    }

    private static void UpdateCorrectionHistory(SearchState state, Position position, int depth, int diff)
    {
        int weight = Math.Min(depth + 1, 16);
        ref int entry = ref state.PawnCorrectionHistory[CorrectionIndex(position)];
        long value = ((long)entry * (CorrectionWeight - weight) + (long)diff * CorrectionGrain * weight) / CorrectionWeight;
        entry = (int)Math.Clamp(value, -CorrectionMax, CorrectionMax);
    }

    private static int CorrectionIndex(Position position) =>
        ((int)position.Turn * SearchState.CorrectionHistorySize) +
        (int)(MixKey(MixKey(position.BitboardOf(Color.White, PieceType.Pawn))
                   ^ position.BitboardOf(Color.Black, PieceType.Pawn))
              & (SearchState.CorrectionHistorySize - 1));

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
