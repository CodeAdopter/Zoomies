using System.Runtime.CompilerServices;
using Zoomies.Core;

namespace Zoomies.Engine;

internal static class See
{
    // Static exchange evaluation:
    // Plays out the capture sequence on the target square, both sides always recapturing 
    // with their least valuable attacker, then negamaxes the gain stack backward. Sliders revealed 
    // behind a departing attacker are added incrementally.
    private const int KingValue = 20000;

    public static bool Ge(Position pos, Move m, int threshold = 0)
    {
        if ((m.Flags & MoveFlags.Promotions) != 0 || m.Flags == MoveFlags.EnPassant)
            return 0 >= threshold;
        return Exact(pos, m) >= threshold;
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
                attackers |= Tables.Attacks(PieceType.Bishop, to, occ) & diag;
            if (lva == PieceType.Rook || lva == PieceType.Queen)
                attackers |= Tables.Attacks(PieceType.Rook, to, occ) & ortho;

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
        (Tables.Attacks(PieceType.King, s, occ) &
        (pos.BitboardOf(Color.White, PieceType.King) | pos.BitboardOf(Color.Black, PieceType.King)));
}
