using Zoomies.Core;
using Zoomies.Engine;

namespace Zoomies.Solvers.Kbnk;

public static class KbnkValidator
{
    public static bool TryGetStrongSide(Position pos, out Color strong)
    {
        strong = Color.White;
        ulong all = pos.AllPieces(Color.White) | pos.AllPieces(Color.Black);
        if (Bitboard.PopCount(all) != 4)
        {
            return false;
        }

        for (int c = 0; c < 2; c++)
        {
            Color side = (Color)c;
            Color other = side.Flip();
            if (Bitboard.PopCount(pos.BitboardOf(side, PieceType.Bishop)) == 1 &&
                Bitboard.PopCount(pos.BitboardOf(side, PieceType.Knight)) == 1 &&
                pos.AllPieces(other) == pos.BitboardOf(other, PieceType.King))
            {
                strong = side;
                return true;
            }
        }
        return false;
    }
}
