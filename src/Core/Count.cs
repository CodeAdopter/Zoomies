using System.Numerics;
using System.Runtime.CompilerServices;

namespace Zoomies.Core;

internal struct CountSink : IMoveSink
{
    private int _count;
    public readonly int Count => _count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Quiets(Square from, ulong to) => _count += BitOperations.PopCount(to);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Captures(Square from, ulong to) => _count += BitOperations.PopCount(to);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DoublePushes(Square from, ulong to) => _count += BitOperations.PopCount(to);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PromoQuiets(Square from, ulong to) => _count += 4 * BitOperations.PopCount(to);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PromoCaptures(Square from, ulong to) => _count += 4 * BitOperations.PopCount(to);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PawnMoves(ulong to, int dir, MoveFlags flag) => _count += BitOperations.PopCount(to);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PawnPromos(ulong to, int dir, bool capture) => _count += 4 * BitOperations.PopCount(to);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void FromMask(ulong from, Square to, MoveFlags flag) => _count += BitOperations.PopCount(from);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void One(Square from, Square to, MoveFlags flag) => _count++;
}
