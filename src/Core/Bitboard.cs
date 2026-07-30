using System.Numerics;
using System.Runtime.CompilerServices;

namespace Zoomies.Core;

public static class Bitboard
{
    private const ulong FileABits = 0x0101010101010101UL;
    private const ulong FileHBits = 0x8080808080808080UL;
    private const ulong Rank1Bits = 0xffUL;
    private const ulong MainDiagonalBits = 0x8040201008040201UL;
    private const ulong MainAntiDiagonalBits = 0x0102040810204080UL;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong SquareMask(Square square) => 1UL << (int)square;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong FileMask(File file) => FileABits << (int)file;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong RankMask(Rank rank) => Rank1Bits << (8 * (int)rank);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong DiagonalMask(int diagonal) =>
        diagonal < 7
            ? MainDiagonalBits >> (8 * (7 - diagonal))
            : MainDiagonalBits << (8 * (diagonal - 7));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong AntiDiagonalMask(int antiDiagonal) =>
        antiDiagonal < 7
            ? MainAntiDiagonalBits >> (8 * (7 - antiDiagonal))
            : MainAntiDiagonalBits << (8 * (antiDiagonal - 7));

    public static int PopCount(ulong x)
    {
        const ulong k1 = 0x5555555555555555UL;
        const ulong k2 = 0x3333333333333333UL;
        const ulong k4 = 0x0f0f0f0f0f0f0f0fUL;
        const ulong kf = 0x0101010101010101UL;

        x -= (x >> 1) & k1;
        x = (x & k2) + ((x >> 2) & k2);
        x = (x + (x >> 4)) & k4;
        x = (x * kf) >> 56;
        return (int)x;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SparsePopCount(ulong x)
    {
        int count = 0;
        while (x != 0)
        {
            count++;
            x &= x - 1;
        }
        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Square PopLsb(ref ulong b)
    {
        Square lsb = Bsf(b);
        b &= b - 1;
        return lsb;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Square Bsf(ulong b)
    {
        return (Square)BitOperations.TrailingZeroCount(b);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Shift(Direction d, ulong b) => d switch
    {
        Direction.North => b << 8,
        Direction.South => b >> 8,
        Direction.NorthNorth => b << 16,
        Direction.SouthSouth => b >> 16,
        Direction.East => (b & ~FileHBits) << 1,
        Direction.West => (b & ~FileABits) >> 1,
        Direction.NorthEast => (b & ~FileHBits) << 9,
        Direction.NorthWest => (b & ~FileABits) << 7,
        Direction.SouthEast => (b & ~FileHBits) >> 7,
        Direction.SouthWest => (b & ~FileABits) >> 9,
        _ => 0,
    };
}
