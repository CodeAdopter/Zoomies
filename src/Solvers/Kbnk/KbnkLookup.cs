using System.Diagnostics;
using Zoomies.Core;
using Zoomies.Engine;

namespace Zoomies.Solvers.Kbnk;

public static class KbnkLookup
{
    public static bool TryFindRootMove(
        Position position,
        byte[] mateDistance,
        bool suppressOutput,
        Stopwatch clock,
        long builtStates,
        out Move best,
        out int plies,
        out bool stmIsStrong)
    {
        best = default;
        plies = 0;
        stmIsStrong = false;

        if (!KbnkValidator.TryGetStrongSide(position, out Color strong)) return false;

        long nodes = builtStates;
        Color weak = strong.Flip();
        stmIsStrong = position.Turn == strong;

        int flipMask = strong == Color.Black ? 56 : 0;
        int bishopSq = (int)Bitboard.Bsf(position.BitboardOf(strong, PieceType.Bishop)) ^ flipMask;

        if (((bishopSq ^ (bishopSq >> 3)) & 1) != 0)
        {
            flipMask ^= 7;
            bishopSq ^= 7;
        }

        int strongKing = (int)Bitboard.Bsf(position.BitboardOf(strong, PieceType.King)) ^ flipMask;
        int weakKing = (int)Bitboard.Bsf(position.BitboardOf(weak, PieceType.King)) ^ flipMask;
        int knightSq = (int)Bitboard.Bsf(position.BitboardOf(strong, PieceType.Knight)) ^ flipMask;

        Span<Move> moves = stackalloc Move[64];
        int moveCount = Search.GenerateLegalMoves(position, moves);

        if (moveCount == 0)
        {
            return false;
        }

        if (stmIsStrong)
        {
            return FindStrongSideMove(
                position,
                moves,
                moveCount,
                mateDistance,
                flipMask,
                strongKing,
                weakKing,
                bishopSq,
                knightSq,
                clock,
                ref nodes,
                suppressOutput,
                out best,
                out plies);
        }

        return FindWeakSideMove(
            position,
            weak,
            moves,
            moveCount,
            mateDistance,
            flipMask,
            strongKing,
            weakKing,
            bishopSq,
            knightSq,
            clock,
            ref nodes,
            suppressOutput,
            out best,
            out plies);
    }

    private static bool FindStrongSideMove(
        Position position,
        Span<Move> moves,
        int moveCount,
        byte[] mateDistance,
        int flipMask,
        int strongKing,
        int weakKing,
        int bishopSq,
        int knightSq,
        Stopwatch clock,
        ref long nodes,
        bool suppressOutput,
        out Move best,
        out int plies)
    {
        best = default;
        plies = 0;
        int bestPlies = int.MaxValue;
        nodes += moveCount;

        for (int i = 0; i < moveCount; i++)
        {
            int from = (int)moves[i].From ^ flipMask;
            int to = (int)moves[i].To ^ flipMask;
            int strongKingAfter = strongKing, bishopAfter = bishopSq, knightAfter = knightSq;

            if (from == strongKing)
            {
                strongKingAfter = to;
            }
            else if (from == bishopSq)
            {
                bishopAfter = to;
            }
            else
            {
                knightAfter = to;
            }

            int childPlies = mateDistance[KbnkStateIndex.ComputeIndex(strongKingAfter, weakKing, KbnkStateIndex.DarkIndexOfSquare[bishopAfter], knightAfter)];

            if (childPlies != 0 && childPlies < bestPlies)
            {
                bestPlies = childPlies;
                best = moves[i];
            }
        }

        if (bestPlies == int.MaxValue) return false;

        plies = bestPlies;

        if (!suppressOutput)
        {
            long ms = clock.ElapsedMilliseconds;
            Console.WriteLine($"info depth {plies} score mate {(plies + 1) / 2} nodes {nodes} nps {nodes * 1000 / Math.Max(1, ms)} time {ms} pv {position.FormatUci(best)}");
        }

        return true;
    }

    private static bool FindWeakSideMove(
        Position position,
        Color weak,
        Span<Move> moves,
        int moveCount,
        byte[] mateDistance,
        int flipMask,
        int strongKing,
        int weakKing,
        int bishopSq,
        int knightSq,
        Stopwatch clock,
        ref long nodes,
        bool suppressOutput,
        out Move best,
        out int plies)
    {
        Position probe = new(position);

        best = default;
        plies = 0;

        Span<Move> replies = stackalloc Move[64];
        int longestDefense = -1;
        bool isDraw = false;

        for (int i = 0; i < moveCount; i++)
        {
            Move m = moves[i];
            if (m.IsCapture)
            {
                best = m;
                isDraw = true;
                break;
            }

            int weakKingAfter = (int)m.To ^ flipMask;
            probe.Play(weak, m);
            int replyCount = Search.GenerateLegalMoves(probe, replies);
            nodes += replyCount;
            int bestStrongReply = int.MaxValue;

            for (int r = 0; r < replyCount; r++)
            {
                int from = (int)replies[r].From ^ flipMask;
                int to = (int)replies[r].To ^ flipMask;
                int strongKingAfter = strongKing, bishopAfter = bishopSq, knightAfter = knightSq;

                if (from == strongKing)
                {
                    strongKingAfter = to;
                }
                else if (from == bishopSq)
                {
                    bishopAfter = to;
                }
                else
                {
                    knightAfter = to;
                }

                int childPlies = mateDistance[KbnkStateIndex.ComputeIndex(strongKingAfter, weakKingAfter, KbnkStateIndex.DarkIndexOfSquare[bishopAfter], knightAfter)];

                if (childPlies != 0 && childPlies < bestStrongReply)
                {
                    bestStrongReply = childPlies;
                }
            }
            probe.Undo(weak, m);
            if (bestStrongReply == int.MaxValue)
            {
                best = m;
                isDraw = true;
                break;
            }
            if (bestStrongReply + 1 > longestDefense)
            {
                longestDefense = bestStrongReply + 1;
                best = m;
            }
        }

        plies = isDraw ? 0 : longestDefense;
        if (!suppressOutput)
        {
            long ms = clock.ElapsedMilliseconds;
            long nps = nodes * 1000 / Math.Max(1, ms);
            Console.WriteLine(isDraw
                ? $"info depth 1 score cp 0 nodes {nodes} nps {nps} time {ms} pv {position.FormatUci(best)}"
                : $"info depth {plies} score mate {-(plies / 2)} nodes {nodes} nps {nps} time {ms} pv {position.FormatUci(best)}");
        }
        return true;
    }
}
