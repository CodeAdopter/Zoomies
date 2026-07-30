using System.Runtime.CompilerServices;

namespace Zoomies.Core;

public class PRNG(ulong seed)
{
    private ulong s = seed;

    private ulong Rand64()
    {
        s ^= s >> 12;
        s ^= s << 25;
        s ^= s >> 27;
        return s * 2685821657736338717UL;
    }

    public T Rand<T>() where T : struct
    {
        if (typeof(T) == typeof(ulong))
            return (T)(object)Rand64();
        throw new NotSupportedException($"Type {typeof(T)} is not supported");
    }

    public T SparseRand<T>() where T : struct
    {
        if (typeof(T) == typeof(ulong))
            return (T)(object)(Rand64() & Rand64() & Rand64());
        throw new NotSupportedException($"Type {typeof(T)} is not supported");
    }
}

public static class Zobrist
{
    public static readonly ulong[] ZobristTable = new ulong[Types.PieceCount * Types.SquareCount];
    public static readonly ulong[] Castling = new ulong[16];
    public static readonly ulong[] EnPassantFile = new ulong[8];
    #pragma warning disable CA2211
    public static ulong SideToMove;
    #pragma warning restore CA2211

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Piece(Piece pc, Square s) => ZobristTable[((int)pc << 6) | (int)s];

    public static void Initialize()
    {
        PRNG rng = new(70026072);

        SideToMove = rng.Rand<ulong>();

        for (int i = 0; i < Types.PieceCount; i++)
        {
            for (int j = 0; j < Types.SquareCount; j++)
            {
                ZobristTable[(i << 6) | j] = rng.Rand<ulong>();
            }
        }

        for (int i = 0; i < Castling.Length; i++)
            Castling[i] = rng.Rand<ulong>();

        for (int i = 0; i < EnPassantFile.Length; i++)
            EnPassantFile[i] = rng.Rand<ulong>();
    }
}
