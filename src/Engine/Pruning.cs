using Zoomies.Core;

namespace Zoomies.Engine;

internal static class Pruning
{
    public static int AlphaBeta(SearchState state, Position position, int depth, int alpha, int beta, int ply)
    {
        if (state.StopRequested) return 0;
        if ((state.NodeCount & 8191) == 0 && state.ReachedSearchLimit())
        {
            state.StopRequested = true;
            return 0;
        }

        if (depth <= 0)
            return Quiescence.Search(state, position, alpha, beta, ply);

        state.NodeCount++;

        Span<Move> moves = stackalloc Move[256];
        int moveCount = Engine.Search.GenerateLegalMoves(position, moves);
        if (moveCount == 0)
            return position.Checkers != 0 ? -Eval.MateValue + ply : 0;

        if (ply == 0 && state.PrincipalVariationMove.EncodedValue != 0)
        {
            for (int i = 1; i < moveCount; i++)
            {
                if (moves[i] != state.PrincipalVariationMove) continue;
                (moves[0], moves[i]) = (moves[i], moves[0]);
                break;
            }
        }

        int fixedMoveCount = ply == 0 ? 1 : 0;
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
        int bestScore = -SearchState.Infinity;

        for (int i = 0; i < moveCount; i++)
        {
            Move move = moves[i];
            position.PlayPerft(sideToMove, move);
            int score = -AlphaBeta(state, position, depth - 1, -beta, -alpha, ply + 1);
            position.UndoPerft(sideToMove, move);

            if (state.StopRequested) return 0;
            if (score > bestScore)
            {
                bestScore = score;
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

        return bestScore;
    }
}
