using System.Runtime.CompilerServices;

namespace Zoomies.Solvers.Kbnk;

public static class KbnkStateIndex
{
    public static readonly int[] DarkIndexOfSquare = new int[64];
    public static readonly int[] SquareOfDarkIndex = new int[32];

    static KbnkStateIndex()
    {
        int nextOrdinal = 0;
        for (int s = 0; s < 64; s++)
        {
            bool dark = ((s ^ (s >> 3)) & 1) == 0;
            DarkIndexOfSquare[s] = dark ? nextOrdinal : -1;

            if (dark)
            {
                SquareOfDarkIndex[nextOrdinal++] = s;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ComputeIndex(int strongKing, int weakKing, int darkIdx, int knight) => (strongKing << 17) | (weakKing << 11) | (darkIdx << 6) | knight;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int PairIndex(int strongKing, int darkIdx) => (strongKing << 5) | darkIdx;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int BlockIndex(int pairIdx, int weakKing) => (pairIdx << 6) | weakKing;
}
