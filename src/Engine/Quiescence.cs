using System.Runtime.CompilerServices;
using Zoomies.Core;

namespace Zoomies.Engine;

internal static class Quiescence
{
    [SkipLocalsInit]
    public static int Search(SearchState state, Position position, int alpha, int beta, int ply)
    {
        if (state.StopRequested) return 0;
        if ((state.NodeCount & 8191) == 0 && state.ReachedSearchLimit())
        {
            state.StopRequested = true;
            return 0;
        }

        state.NodeCount++;
        state.QuiescenceNodeCount++;
        if (ply > state.SelectiveDepth) state.SelectiveDepth = ply;

        ulong key = position.History[position.Ply].Hash;
        bool ttHit = state.Tt.Probe(key, out TranspositionTable.Entry ttEntry);
        int ttScore = ttHit ? TranspositionTable.ScoreFromTt(ttEntry.Score, ply) : 0;
        if (ttHit && beta - alpha <= 1)
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
        Move ttMove = ttHit ? new Move(ttEntry.Move) : default;

        bool inCheck = position.InCheck(position.Turn);
        int bestScore = -SearchState.Infinity;
        Move bestMove = default;
        int standingPatScore = 0;

        if (!inCheck)
        {
            state.EvaluationCount++;
            standingPatScore = Pruning.CorrectEval(state, position, Eval.Evaluate(position));

            if (ttHit &&
                Math.Abs(ttScore) < Eval.MateBound &&
                (ttEntry.Flag == TtFlag.Exact ||
                (ttEntry.Flag == TtFlag.Lower && ttScore > standingPatScore) ||
                (ttEntry.Flag == TtFlag.Upper && ttScore < standingPatScore)))
                    standingPatScore = ttScore;

            bestScore = standingPatScore;
            if (standingPatScore >= beta)
            {
                state.Tt.Store(key, 0, TranspositionTable.ScoreToTt(standingPatScore, ply), 0, TtFlag.Lower);
                return standingPatScore;
            }
            if (standingPatScore > alpha) alpha = standingPatScore;
            if (ply >= SearchState.MaximumPly - 1) return standingPatScore;

            if (alpha > -Eval.MateBound)
            {
                Color opponent = position.Turn.Flip();
                int maxGain =
                      position.BitboardOf(opponent, PieceType.Queen)  != 0 ? Eval.PieceValue[(int)PieceType.Queen]
                    : position.BitboardOf(opponent, PieceType.Rook)   != 0 ? Eval.PieceValue[(int)PieceType.Rook]
                    : position.BitboardOf(opponent, PieceType.Bishop) != 0 ? Eval.PieceValue[(int)PieceType.Bishop]
                    : position.BitboardOf(opponent, PieceType.Knight) != 0 ? Eval.PieceValue[(int)PieceType.Knight]
                    : Eval.PieceValue[(int)PieceType.Pawn];
                ulong promotionRank = position.Turn == Color.White
                    ? 0x00FF_0000_0000_0000UL
                    : 0x0000_0000_0000_FF00UL;

                if ((position.BitboardOf(position.Turn, PieceType.Pawn) & promotionRank) != 0)
                {
                    maxGain += Eval.PieceValue[(int)PieceType.Queen] -
                        Eval.PieceValue[(int)PieceType.Pawn];
                }

                if (standingPatScore + maxGain + Tune.DeltaMargin <= alpha)
                    return standingPatScore;
            }
        }
        else if (ply >= SearchState.MaximumPly - 1)
        {
            return Pruning.CorrectEval(state, position, Eval.Evaluate(position));
        }

        // in check we need to generate every legal evasion 
        Span<Move> moves = stackalloc Move[256];
        int moveCount = inCheck
            ? Engine.Search.GenerateLegalMoves(position, moves)
            : position.Turn == Color.White
                ? position.GenerateCapturesFast<White>(moves)
                : position.GenerateCapturesFast<Black>(moves);

        if (moveCount == 0)
            return inCheck ? -Eval.MateValue + ply : bestScore;

        Order.TacticalMoves(position, moves[..moveCount]);

        // search the TT move first when it is present in the list
        if (ttMove.EncodedValue != 0)
        {
            for (int i = 0; i < moveCount; i++)
            {
                if (moves[i] != ttMove) continue;
                (moves[0], moves[i]) = (moves[i], moves[0]);
                break;
            }
        }

        Color sideToMove = position.Turn;

        for (int i = 0; i < moveCount; i++)
        {
            Move move = moves[i];

            if (!inCheck)
            {
                if (alpha > -Eval.MateBound)
                {
                    int gain = 0;
                    if (move.IsCapture)
                    {
                        gain += move.Flags == MoveFlags.EnPassant
                            ? Eval.PieceValue[(int)PieceType.Pawn]
                            : Eval.PieceValue[(int)Types.TypeOf(position.At(move.To))];
                    }

                    if ((move.Flags & MoveFlags.Promotions) != 0)
                    {
                        gain += Eval.PieceValue[((int)move.Flags & 0b11) + 1] -
                            Eval.PieceValue[(int)PieceType.Pawn];
                    }

                    if (standingPatScore + gain + Tune.DeltaMargin <= alpha)
                        continue;
                }

                // prune losing captures
                if (!See.Ge(position, move))
                    continue;
            }
            else if (bestScore > -Eval.MateBound)
            {
                // skip quiet evasions
                if (!move.IsCapture && (move.Flags & MoveFlags.Promotions) == 0)
                    continue;

                // prune capture evasions that lose material
                if (move.IsCapture && !See.Ge(position, move))
                    continue;
            }

            position.Play(sideToMove, move);
            
            if (!inCheck && IsIllegal(position, sideToMove))
            {
                position.Undo(sideToMove, move);
                continue;
            }

            int score = -Search(state, position, -beta, -alpha, ply + 1);
            position.Undo(sideToMove, move);

            if (state.StopRequested) return 0;
            if (score > bestScore)
            {
                bestScore = score;
                bestMove = move;
            }
            if (score >= beta)
            {
                state.Tt.Store(key, move.EncodedValue, TranspositionTable.ScoreToTt(score, ply), 0, TtFlag.Lower);
                return score;
            }
            if (score > alpha) alpha = score;
        }

        state.Tt.Store(
            key,
            bestMove.EncodedValue,
            TranspositionTable.ScoreToTt(bestScore, ply),
            0,
            TtFlag.Upper);

        return bestScore;
    }

    private static bool IsIllegal(Position position, Color mover)
    {
        if (position.InCheck(mover)) return true;
        Square ourKing = Bitboard.Bsf(position.BitboardOf(mover, PieceType.King));
        return (Tables.KingAttacks(ourKing) & position.BitboardOf(mover.Flip(), PieceType.King)) != 0;
    }
}
