using System.Runtime.CompilerServices;
using Zoomies.Core;

namespace Zoomies.Engine;

internal static class See
{
    private const int KingValue = 20000;

    private static bool EarlyExit = Tune.SeeEarlyExit != 0;
    private static bool Verify = Tune.SeeVerify != 0;

    internal static void Refresh()
    {
        EarlyExit = Tune.SeeEarlyExit != 0;
        Verify = Tune.SeeVerify != 0;
    }

    public static bool Ge(Position pos, Move m, int threshold = 0)
    {
        if ((m.Flags & MoveFlags.Promotions) != 0 || m.Flags == MoveFlags.EnPassant)
            return 0 >= threshold;
        if (EarlyExit)
        {
            bool fast = GeBalance(pos, m, threshold);
            if (Verify && fast != Exact(pos, m) >= threshold)
                throw new InvalidOperationException($"SEE mismatch: {m} threshold {threshold} fast {fast}\n{pos}");
            return fast;
        }
        return Exact(pos, m) >= threshold;
    }

    [SkipLocalsInit]
    private static bool GeBalance(Position pos, Move m, int threshold)
    {
        Square to = m.To;
        Piece victim = pos.At(to);
        int swap = (victim == Piece.NoPiece ? 0 : SeeValue(Types.TypeOf(victim))) - threshold;
        if (swap < 0) return false;

        swap = SeeValue(Types.TypeOf(pos.At(m.From))) - swap;
        if (swap <= 0) return true;

        ulong occ = (pos.AllPieces(Color.White) | pos.AllPieces(Color.Black)) & ~(1UL << (int)m.From);
        ulong diag = pos.DiagonalSliders(Color.White) | pos.DiagonalSliders(Color.Black);
        ulong ortho = pos.OrthogonalSliders(Color.White) | pos.OrthogonalSliders(Color.Black);
        ulong attackers = AttackersTo(pos, to, occ);
        Color side = pos.Turn.Flip();
        int res = 1;

        while (true)
        {
            attackers &= occ;
            ulong sideAtt = attackers & pos.AllPieces(side);
            if (sideAtt == 0) break;
            res ^= 1;

            ulong bb;
            if ((bb = sideAtt & pos.BitboardOf(side, PieceType.Pawn)) != 0)
            {
                if ((swap = SeeValue(PieceType.Pawn) - swap) < res) break;
                occ ^= bb & (0UL - bb);
                attackers |= Tables.BishopAttacks(to, occ) & diag;
            }
            else if ((bb = sideAtt & pos.BitboardOf(side, PieceType.Knight)) != 0)
            {
                if ((swap = SeeValue(PieceType.Knight) - swap) < res) break;
                occ ^= bb & (0UL - bb);
            }
            else if ((bb = sideAtt & pos.BitboardOf(side, PieceType.Bishop)) != 0)
            {
                if ((swap = SeeValue(PieceType.Bishop) - swap) < res) break;
                occ ^= bb & (0UL - bb);
                attackers |= Tables.BishopAttacks(to, occ) & diag;
            }
            else if ((bb = sideAtt & pos.BitboardOf(side, PieceType.Rook)) != 0)
            {
                if ((swap = SeeValue(PieceType.Rook) - swap) < res) break;
                occ ^= bb & (0UL - bb);
                attackers |= Tables.RookAttacks(to, occ) & ortho;
            }
            else if ((bb = sideAtt & pos.BitboardOf(side, PieceType.Queen)) != 0)
            {
                if ((swap = SeeValue(PieceType.Queen) - swap) < res) break;
                occ ^= bb & (0UL - bb);
                attackers |= (Tables.BishopAttacks(to, occ) & diag) | (Tables.RookAttacks(to, occ) & ortho);
            }
            else
            {
                return ((attackers & pos.AllPieces(side.Flip())) != 0 ? res ^ 1 : res) != 0;
            }

            side = side.Flip();
        }
        return res != 0;
    }

    [SkipLocalsInit]
    public static int Exact(Position pos, Move m)
    {
        Square to = m.To;
        ulong occ = (pos.AllPieces(Color.White) | pos.AllPieces(Color.Black)) & ~(1UL << (int)m.From);
        ulong diag = pos.DiagonalSliders(Color.White) | pos.DiagonalSliders(Color.Black);
        ulong ortho = pos.OrthogonalSliders(Color.White) | pos.OrthogonalSliders(Color.Black);

        Span<int> gain = stackalloc int[32];
        int d = 0;
        Piece victim = pos.At(to);
        gain[0] = victim == Piece.NoPiece ? 0 : SeeValue(Types.TypeOf(victim));

        PieceType occupant = Types.TypeOf(pos.At(m.From));
        ulong attackers = AttackersTo(pos, to, occ);
        Color side = pos.Turn.Flip();

        while (true)
        {
            attackers &= occ;
            ulong sideAtt = attackers & pos.AllPieces(side);
            if (sideAtt == 0) break;

            ulong fromBit = 0;
            PieceType lva = PieceType.Pawn;
            for (int pt = 0; pt <= (int)PieceType.King; pt++)
            {
                ulong subset = sideAtt & pos.BitboardOf(side, (PieceType)pt);
                if (subset != 0) { lva = (PieceType)pt; fromBit = subset & (0UL - subset); break; }
            }

            d++;
            gain[d] = SeeValue(occupant) - gain[d - 1];

            occ ^= fromBit;
            if (lva == PieceType.Pawn || lva == PieceType.Bishop || lva == PieceType.Queen)
                attackers |= Tables.BishopAttacks(to, occ) & diag;
            if (lva == PieceType.Rook || lva == PieceType.Queen)
                attackers |= Tables.RookAttacks(to, occ) & ortho;

            occupant = lva;
            side = side.Flip();
            if (d >= 30) break;
        }

        while (d > 0)
        {
            gain[d - 1] = -Math.Max(-gain[d - 1], gain[d]);
            d--;
        }

        return gain[0];
    }

    private static int SeeValue(PieceType pt) =>
        pt == PieceType.King ? KingValue : Eval.PieceValue[(int)pt];

    private static ulong AttackersTo(Position pos, Square s, ulong occ) =>
        pos.AttackersFrom(Color.White, s, occ) |
        pos.AttackersFrom(Color.Black, s, occ) |
        (Tables.KingAttacks(s) &
        (pos.BitboardOf(Color.White, PieceType.King) | pos.BitboardOf(Color.Black, PieceType.King)));
}
