using System.Runtime.CompilerServices;
using Zoomies.Core;

namespace Zoomies.Engine;

internal static class Order
{
    [SkipLocalsInit]
    public static int TacticalMoves(Position position, Span<Move> moves, short[]? captureHistory = null)
    {
        Span<int> scores = stackalloc int[256];
        int tacticalMoveCount = 0;

        for (int i = 0; i < moves.Length; i++)
        {
            Move move = moves[i];
            if (!move.IsCapture && (move.Flags & MoveFlags.Promotions) == 0)
                continue;

            int victimValue = move.IsCapture ? PieceTypeValue(position.At(move.To)) : 0;
            int score = victimValue * 16 - PieceTypeValue(position.At(move.From))
                + ((move.Flags & MoveFlags.Promotions) != 0
                    ? Eval.PieceValue[(int)PieceType.Queen]
                    : 0);
            if (captureHistory != null)
                score += captureHistory[CaptureHistoryIndex(position, move)];

            (moves[i], moves[tacticalMoveCount]) =
                (moves[tacticalMoveCount], moves[i]);
            scores[tacticalMoveCount++] = score;
        }

        for (int i = 1; i < tacticalMoveCount; i++)
        {
            Move move = moves[i];
            int score = scores[i];
            int insertionIndex = i - 1;

            while (insertionIndex >= 0 && scores[insertionIndex] < score)
            {
                moves[insertionIndex + 1] = moves[insertionIndex];
                scores[insertionIndex + 1] = scores[insertionIndex];
                insertionIndex--;
            }

            moves[insertionIndex + 1] = move;
            scores[insertionIndex + 1] = score;
        }

        return tacticalMoveCount;
    }

    private static int PieceTypeValue(Piece piece) =>
        piece == Piece.NoPiece
            ? Eval.PieceValue[(int)PieceType.Pawn]
            : Eval.PieceValue[(int)Types.TypeOf(piece)];

    internal static int CaptureHistoryIndex(Position position, Move move)
    {
        int bucket = move.Flags == MoveFlags.EnPassant 
            ? (int)PieceType.Pawn
            : move.IsCapture ? (int)Types.TypeOf(position.At(move.To))
            : 6;
        return ((((int)position.At(move.From)) << 6) | (int)move.To) * 8 + bucket;
    }
}
