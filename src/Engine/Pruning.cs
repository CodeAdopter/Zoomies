using Zoomies.Core;

namespace Zoomies.Engine;

internal static class Pruning
{
    private static readonly int[] LmrTable = BuildLmrTable();

    private static int[] BuildLmrTable()
    {
        var t = new int[64 * 64];
        for (int d = 1; d < 64; d++)
            for (int m = 1; m < 64; m++)
                t[(d << 6) | m] = (int)(0.75 + Math.Log(d) * Math.Log(m) / 2.25);
        return t;
    }

    public static int AlphaBeta(SearchState state, Position position, int depth, int alpha, int beta, int ply, bool allowNullMove = true)
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

        ulong key = position.History[position.Ply].Hash;
        bool ttHit = state.Tt.Probe(key, out TranspositionTable.Entry ttEntry);
        int ttScore = ttHit ? TranspositionTable.ScoreFromTt(ttEntry.Score, ply) : 0;
        if (ttHit && ply > 0 && ttEntry.Depth >= depth)
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

        if (depth <= 0)
            return Quiescence.Search(state, position, alpha, beta, ply);

        state.NodeCount++;

        int staticEval = inCheck ? 0 : Eval.Evaluate(position);

        // TT score as pruning bound:
        // Prune with the TT score when it provides a tighter bound than the static eval.
        int pruneEval = staticEval;
        if (ttHit &&
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
            pruneEval - 80 * depth >= beta)
            return pruneEval;

        // null move pruning
        if (allowNullMove &&
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
            int nullScore = -AlphaBeta(state, position, depth - 1 - reduction, -beta, -beta + 1, ply + 1, false);
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
            pruneEval + 100 + 120 * depth <= alpha;

        Span<Move> triedQuiets = stackalloc Move[64];
        int triedQuietCount = 0;

        for (int i = 0; i < moveCount; i++)
        {
            Move move = moves[i];
            bool isQuiet = !move.IsCapture && (move.Flags & MoveFlags.Promotions) == 0;

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

            if (isQuiet && triedQuietCount < triedQuiets.Length)
                triedQuiets[triedQuietCount++] = move;

            state.PlayedPieceTo[ply] = ((int)position.At(move.From) << 6) | (int)move.To;
            position.Play(sideToMove, move);

            // principal variation search
            int score;
            if (i == 0)
            {
                score = -AlphaBeta(state, position, depth - 1, -beta, -alpha, ply + 1);
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
                    if (i >= quietStart) rr -= quietScores[i] / 8192;
                    if (rr < 1) rr = 1;
                    reduction = Math.Min(rr, depth - 2);
                }

                score = -AlphaBeta(state, position, depth - 1 - reduction, -alpha - 1, -alpha, ply + 1);
                if (score > alpha && (reduction > 0 || score < beta))
                    score = -AlphaBeta(state, position, depth - 1, -beta, -alpha, ply + 1);
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
            break;
        }

        TtFlag storeFlag = bestScore >= beta ? TtFlag.Lower
            : bestScore > alphaOriginal ? TtFlag.Exact
            : TtFlag.Upper;
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
        int bonus = Math.Min(1200, 16 * depth * depth + 32 * depth + 16);

        UpdateQuietHistory(state, position, side, cutoffMove, bonus, previous1, previous2);
        foreach (Move tried in triedQuiets)
            if (tried != cutoffMove)
                UpdateQuietHistory(state, position, side, tried, -bonus, previous1, previous2);
    }

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

    private static bool HasNonPawnMaterial(Position position, Color side) =>
        (position.BitboardOf(side, PieceType.Knight) |
         position.BitboardOf(side, PieceType.Bishop) |
         position.BitboardOf(side, PieceType.Rook) |
         position.BitboardOf(side, PieceType.Queen)) != 0;
}
