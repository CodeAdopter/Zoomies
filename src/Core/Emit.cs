using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Zoomies.Core;

internal ref struct EmitSink : IMoveSink
{
    private ref Move _list;
    private int _idx;
    public EmitSink(Span<Move> list) { _list = ref MemoryMarshal.GetReference(list); _idx = 0; }
    public readonly int Count => _idx;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Quiets(Square from, ulong to)
    {
        ref Move list = ref _list;
        int idx = _idx;
        int prefix = (int)from << 6;
        while (to != 0) Unsafe.Add(ref list, idx++) = new Move((ushort)(prefix | (int)Bitboard.PopLsb(ref to)));
        _idx = idx;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Captures(Square from, ulong to)
    {
        ref Move list = ref _list;
        int idx = _idx;
        int prefix = ((int)MoveFlags.Capture << 12) | ((int)from << 6);
        while (to != 0) Unsafe.Add(ref list, idx++) = new Move((ushort)(prefix | (int)Bitboard.PopLsb(ref to)));
        _idx = idx;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DoublePushes(Square from, ulong to)
    {
        ref Move list = ref _list;
        int idx = _idx;
        int prefix = ((int)MoveFlags.DoublePush << 12) | ((int)from << 6);
        while (to != 0) Unsafe.Add(ref list, idx++) = new Move((ushort)(prefix | (int)Bitboard.PopLsb(ref to)));
        _idx = idx;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PromoQuiets(Square from, ulong to)
    {
        ref Move list = ref _list;
        int idx = _idx;
        int fromBits = (int)from << 6;
        while (to != 0)
        {
            int sq = (int)Bitboard.PopLsb(ref to);
            Unsafe.Add(ref list, idx++) = new Move((ushort)(((int)MoveFlags.PrKnight << 12) | fromBits | sq));
            Unsafe.Add(ref list, idx++) = new Move((ushort)(((int)MoveFlags.PrBishop << 12) | fromBits | sq));
            Unsafe.Add(ref list, idx++) = new Move((ushort)(((int)MoveFlags.PrRook   << 12) | fromBits | sq));
            Unsafe.Add(ref list, idx++) = new Move((ushort)(((int)MoveFlags.PrQueen  << 12) | fromBits | sq));
        }
        _idx = idx;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PromoCaptures(Square from, ulong to)
    {
        ref Move list = ref _list;
        int idx = _idx;
        int fromBits = (int)from << 6;
        while (to != 0)
        {
            int sq = (int)Bitboard.PopLsb(ref to);
            Unsafe.Add(ref list, idx++) = new Move((ushort)(((int)MoveFlags.PcKnight << 12) | fromBits | sq));
            Unsafe.Add(ref list, idx++) = new Move((ushort)(((int)MoveFlags.PcBishop << 12) | fromBits | sq));
            Unsafe.Add(ref list, idx++) = new Move((ushort)(((int)MoveFlags.PcRook   << 12) | fromBits | sq));
            Unsafe.Add(ref list, idx++) = new Move((ushort)(((int)MoveFlags.PcQueen  << 12) | fromBits | sq));
        }
        _idx = idx;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PawnMoves(ulong to, int dir, MoveFlags flag)
    {
        ref Move list = ref _list;
        int idx = _idx;
        int flagBits = (int)flag << 12;
        while (to != 0)
        {
            int sq = (int)Bitboard.PopLsb(ref to);
            Unsafe.Add(ref list, idx++) = new Move((ushort)(flagBits | ((sq - dir) << 6) | sq));
        }
        _idx = idx;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PawnPromos(ulong to, int dir, bool capture)
    {
        ref Move list = ref _list;
        int idx = _idx;
        int knightFlag = (int)(capture ? MoveFlags.PcKnight : MoveFlags.PrKnight) << 12;
        int bishopFlag = (int)(capture ? MoveFlags.PcBishop : MoveFlags.PrBishop) << 12;
        int rookFlag =   (int)(capture ? MoveFlags.PcRook : MoveFlags.PrRook)     << 12;
        int queenFlag =  (int)(capture ? MoveFlags.PcQueen : MoveFlags.PrQueen)   << 12;
        while (to != 0)
        {
            int sq = (int)Bitboard.PopLsb(ref to);
            int move = ((sq - dir) << 6) | sq;
            Unsafe.Add(ref list, idx++) = new Move((ushort)(knightFlag | move));
            Unsafe.Add(ref list, idx++) = new Move((ushort)(bishopFlag | move));
            Unsafe.Add(ref list, idx++) = new Move((ushort)(rookFlag   | move));
            Unsafe.Add(ref list, idx++) = new Move((ushort)(queenFlag  | move));
        }
        _idx = idx;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void FromMask(ulong from, Square to, MoveFlags flag)
    {
        ref Move list = ref _list;
        int idx = _idx;
        int flagAndTo = ((int)flag << 12) | (int)to;
        while (from != 0) Unsafe.Add(ref list, idx++) = new Move((ushort)(flagAndTo | ((int)Bitboard.PopLsb(ref from) << 6)));
        _idx = idx;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void One(Square from, Square to, MoveFlags flag)
    {
        Unsafe.Add(ref _list, _idx++) =
            new Move((ushort)(((int)flag << 12) | ((int)from << 6) | (int)to));
    }
}
