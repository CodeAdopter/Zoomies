using System.Runtime.CompilerServices;

namespace Zoomies.Solvers.Kbnk;

public interface IKbnkSync
{
    static abstract ulong ClaimBits(ref ulong block, ulong bits);
    static abstract void Or(ref ulong block, ulong bits);
}

public struct KbnkSingleSync : IKbnkSync
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ClaimBits(ref ulong block, ulong bits)
    {
        ulong newBits = bits & ~block;
        block |= newBits;
        return newBits;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Or(ref ulong block, ulong bits)
    {
        block |= bits;
    }
}

public struct KbnkParallelSync : IKbnkSync
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ClaimBits(ref ulong block, ulong bits)
    {
        return bits & ~Interlocked.Or(ref block, bits);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Or(ref ulong block, ulong bits)
    {
        Interlocked.Or(ref block, bits);
    }
}
