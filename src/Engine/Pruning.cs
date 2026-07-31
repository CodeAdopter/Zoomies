using Zoomies.Core;

namespace Zoomies.Engine;

internal static class Pruning
{
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
        if (ttHit && ply > 0 && ttEntry.Depth >= depth)
        {
            int ttScore = TranspositionTable.ScoreFromTt(ttEntry.Score, ply);
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

        // reverse futility pruning
        if (ply > 0 &&
            !inCheck &&
            depth <= 6 &&
            beta < Eval.MateBound &&
            staticEval - 80 * depth >= beta)
            return staticEval;

        // null move pruning
        if (allowNullMove &&
            ply > 0 &&
            depth >= 3 &&
            beta < Eval.MateBound &&
            !inCheck &&
            HasNonPawnMaterial(position, position.Turn) &&
            staticEval >= beta)
        {
            int reduction = 3 + depth / 6;
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

        Span<int> quietScores = stackalloc int[256];
        for (int i = quietStart; i < moveCount; i++)
            quietScores[i] = state.QuietHistory[HistoryIndex(sideToMove, moves[i])];

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

        // futility pruning
        bool futile = !inCheck &&
            depth <= 2 &&
            alpha > -Eval.MateBound &&
            staticEval + 100 + 120 * depth <= alpha;

        for (int i = 0; i < moveCount; i++)
        {
            Move move = moves[i];

            if (futile &&
                bestScore > -SearchState.Infinity &&
                !move.IsCapture &&
                (move.Flags & MoveFlags.Promotions) == 0)
                continue;

            position.Play(sideToMove, move);

            // late move reductions
            int score;
            if (i >= 4 &&
                depth >= 3 &&
                !inCheck &&
                !move.IsCapture &&
                (move.Flags & MoveFlags.Promotions) == 0)
            {
                int reduction = 1 + depth / 8 + i / 16;
                score = -AlphaBeta(state, position, depth - 1 - reduction, -alpha - 1, -alpha, ply + 1);
                if (score > alpha)
                    score = -AlphaBeta(state, position, depth - 1, -beta, -alpha, ply + 1);
            }
            else
            {
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

            if (!move.IsCapture &&
                (move.Flags & MoveFlags.Promotions) == 0)
            {
                state.QuietHistory[HistoryIndex(sideToMove, move)] += depth * depth; // d^2
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

    private static int HistoryIndex(Color side, Move move) =>
        ((int)side << 12) | ((int)move.From << 6) | (int)move.To;

    private static bool HasNonPawnMaterial(Position position, Color side) =>
        (position.BitboardOf(side, PieceType.Knight) |
         position.BitboardOf(side, PieceType.Bishop) |
         position.BitboardOf(side, PieceType.Rook) |
         position.BitboardOf(side, PieceType.Queen)) != 0;
}
