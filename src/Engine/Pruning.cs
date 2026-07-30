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

        if (depth <= 0)
            return Quiescence.Search(state, position, alpha, beta, ply);

        state.NodeCount++;
        
        // null move pruning
        if (allowNullMove &&
            ply > 0 &&
            depth >= 3 &&
            beta < Eval.MateBound &&
            !position.InCheck(position.Turn) &&
            HasNonPawnMaterial(position, position.Turn) &&
            Eval.Evaluate(position) >= beta)
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
            return position.InCheck(position.Turn) ? -Eval.MateValue + ply : 0;

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

        for (int killerIndex = 0, insertAt = tacticalMoveEnd;
            killerIndex < 2 && insertAt < moveCount;
            killerIndex++)
        {
            Move killer = state.KillerMoves[ply, killerIndex];
            if (killer.EncodedValue == 0) continue;

            for (int i = insertAt; i < moveCount; i++)
            {
                if (moves[i] != killer) continue;
                (moves[insertAt], moves[i]) = (moves[i], moves[insertAt]);
                insertAt++;
                break;
            }
        }

        Color sideToMove = position.Turn;
        int alphaOriginal = alpha;
        int bestScore = -SearchState.Infinity;
        Move bestMove = default;

        for (int i = 0; i < moveCount; i++)
        {
            Move move = moves[i];
            position.Play(sideToMove, move);
            int score = -AlphaBeta(state, position, depth - 1, -beta, -alpha, ply + 1);
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
                (move.Flags & MoveFlags.Promotions) == 0 &&
                state.KillerMoves[ply, 0] != move)
            {
                state.KillerMoves[ply, 1] = state.KillerMoves[ply, 0];
                state.KillerMoves[ply, 0] = move;
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

    private static bool HasNonPawnMaterial(Position position, Color side) =>
        (position.BitboardOf(side, PieceType.Knight) |
         position.BitboardOf(side, PieceType.Bishop) |
         position.BitboardOf(side, PieceType.Rook) |
         position.BitboardOf(side, PieceType.Queen)) != 0;
}
