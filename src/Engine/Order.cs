using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Zoomies.Core;

namespace Zoomies.Engine;

internal static class Order
{
    public static int TacticalMoves(Position position, Span<Move> moves, short[]? captureHistory = null) => TacticalMoves(position, moves, captureHistory, false, out _, default);

    public static int TacticalMoves(Position position, Span<Move> moves, short[]? captureHistory, bool demoteLosing, out int losingCount) => TacticalMoves(position, moves, captureHistory, demoteLosing, out losingCount, default);

    private const ushort TacticalThreshold = 0x4000;

    [SkipLocalsInit]
    public static int TacticalMoves(Position position, Span<Move> moves, short[]? captureHistory, bool demoteLosing, out int losingCount, Span<int> losingSeeOut)
    {
        Span<int> scores = stackalloc int[256];
        int tacticalMoveCount = 0;

        ref ushort codes = ref Unsafe.As<Move, ushort>(ref MemoryMarshal.GetReference(moves));
        int scan = 0;
        if (Vector256.IsHardwareAccelerated)
        {
            Vector256<ushort> threshold = Vector256.Create(TacticalThreshold);
            for (; scan + Vector256<ushort>.Count <= moves.Length; scan += Vector256<ushort>.Count)
            {
                uint mask = Vector256.GreaterThanOrEqual(Vector256.LoadUnsafe(ref codes, (nuint)scan), threshold)
                    .ExtractMostSignificantBits();
                while (mask != 0)
                {
                    int index = scan + BitOperations.TrailingZeroCount(mask);
                    mask &= mask - 1;
                    Move move = moves[index];
                    (moves[index], moves[tacticalMoveCount]) = (moves[tacticalMoveCount], move);
                    scores[tacticalMoveCount++] = TacticalScore(position, move, captureHistory);
                }
            }
        }

        for (; scan < moves.Length; scan++)
        {
            Move move = moves[scan];
            if (move.IsQuiet)
                continue;

            (moves[scan], moves[tacticalMoveCount]) =
                (moves[tacticalMoveCount], moves[scan]);
            scores[tacticalMoveCount++] = TacticalScore(position, move, captureHistory);
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

        losingCount = 0;
        if (!demoteLosing || tacticalMoveCount == 0)
            return tacticalMoveCount;

        Span<Move> losing = stackalloc Move[256];
        Span<int> losingSee = stackalloc int[256];
        int goodCount = 0;
        bool demoteQueenTrade = Tune.QkeepDemote != 0;
        for (int i = 0; i < tacticalMoveCount; i++)
        {
            Move move = moves[i];
            if (IsLosingCapture(position, move, out int see) || (demoteQueenTrade && IsQueenTrade(position, move)))
            {
                losingSee[losingCount] = see;
                losing[losingCount++] = move;
            }
            else moves[goodCount++] = move;
        }

        if (losingCount > 0)
        {
            int quietCount = moves.Length - tacticalMoveCount;
            moves.Slice(tacticalMoveCount, quietCount).CopyTo(moves.Slice(goodCount, quietCount));
            losing[..losingCount].CopyTo(moves[(goodCount + quietCount)..]);
            if (!losingSeeOut.IsEmpty) losingSee[..losingCount].CopyTo(losingSeeOut);
        }

        return goodCount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int TacticalScore(Position position, Move move, short[]? captureHistory)
    {
        int victimValue = move.IsCapture ? PieceTypeValue(position.At(move.To)) : 0;
        int score = victimValue * 16 - PieceTypeValue(position.At(move.From))
            + ((move.Flags & MoveFlags.Promotions) != 0
                ? Eval.PieceValue[((int)move.Flags & 0b11) + 1]
                : 0);
        if (captureHistory != null)
            score += captureHistory[CaptureHistoryIndex(position, move)];
        return score;
    }

    private static bool IsLosingCapture(Position position, Move move, out int see)
    {
        see = 0;
        if (!move.IsCapture ||
            (move.Flags &  MoveFlags.Promotions) != 0 ||
             move.Flags == MoveFlags.EnPassant)
            return false;

        int victim = Eval.PieceValue[(int)Types.TypeOf(position.At(move.To))];
        int attacker = Eval.PieceValue[(int)Types.TypeOf(position.At(move.From))];
        if (attacker <= victim) return false;
        see = See.Exact(position, move);
        return see < 0;
    }

    internal static bool IsQueenTrade(Position position, Move move)
    {
        if (!move.IsCapture ||
            (move.Flags &  MoveFlags.Promotions) != 0 ||
             move.Flags == MoveFlags.EnPassant)
            return false;
        if (Types.TypeOf(position.At(move.From)) != PieceType.Queen ||
            Types.TypeOf(position.At(move.To)) != PieceType.Queen)
            return false;
        return !See.Ge(position, move, Tune.QkeepMargin);
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
