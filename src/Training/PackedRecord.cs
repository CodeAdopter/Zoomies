using System.Buffers.Binary;
using System.Numerics;
using Zoomies.Core;

namespace Zoomies.Training;
// Imported from Imp
// 32-byte packed training record
//   [0..7]     occupancy bitboard (LE)
//   [8..23]    piece nibbles in occupancy order, low nibble first: type | (black ? 8 : 0)
//   [24..25]   white-POV search score, int16 LE
//   [26]       wr2: game result from white's view * 2 (0 loss / 1 draw / 2 win)
//   [27]       side to move (0 white / 1 black)
//   [28]       v2 marker 0xA2
//   [29]       label source (2 bits) | log2(label nodes) (6 bits)
//   [30..31]   game ply, uint16 LE
public static class PackedRecord
{
    public const int Size = 32;
    public const byte V2Marker = 0xA2;
    public const byte SourceSelfPlay = 0, SourceRelabel = 1, SourceTb = 2;

    public static void StampV2(Span<byte> dst, byte labelSource, long labelNodes, int gamePly)
    {
        dst[28] = V2Marker;
        int effort = 63 - BitOperations.LeadingZeroCount((ulong)Math.Max(1, labelNodes));
        dst[29] = (byte)((labelSource & 3) | (Math.Min(effort, 63) << 2));
        BinaryPrimitives.WriteUInt16LittleEndian(dst[30..], (ushort)Math.Clamp(gamePly, 0, ushort.MaxValue));
    }

    public static bool IsV2(ReadOnlySpan<byte> rec) => rec[28] == V2Marker;
    public static int PieceCount(ReadOnlySpan<byte> rec) => BitOperations.PopCount(BinaryPrimitives.ReadUInt64LittleEndian(rec));

    public static void Encode(Position pos, short whiteScore, Span<byte> dst)
    {
        dst[..Size].Clear();
        ulong occ = pos.AllPieces(Color.White) | pos.AllPieces(Color.Black);
        BinaryPrimitives.WriteUInt64LittleEndian(dst, occ);
        int i = 0;
        for (ulong b = occ; b != 0; b &= b - 1, i++)
        {
            int sq = BitOperations.TrailingZeroCount(b);
            int nib = (int)pos.At((Square)sq);
            dst[8 + (i >> 1)] |= (byte)(nib << ((i & 1) * 4));
        }
        BinaryPrimitives.WriteInt16LittleEndian(dst[24..], whiteScore);
        dst[27] = (byte)pos.Turn;
    }
}
