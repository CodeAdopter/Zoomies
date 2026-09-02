using Zoomies.Core;
using Zoomies.Engine;
using Zoomies.Solvers.Kbnk;

namespace Zoomies.Solvers;

/// <summary>
/// Exact KBNK (king + bishop + knight vs king) endgame solver.
/// When the position is recognized as KBNK, search uses a mate distance
/// table built on demand instead of running a normal search.
/// </summary>
/// 
/// <remarks>
/// <para>
/// The table stores one byte per position: the number of plies to mate
/// with the defending king to move. A value of 0 means there is no forced
/// mate. Positions are stored without king symmetry amd the bishop is reduced
/// to its 32 dark squares and <see cref="KbnkLookup"/> mirrors positions
/// into that representation before looking them up.
/// </para>
/// 
/// <para>
/// Only defender to move positions are stored. For the winning side,
/// the fastest mate is found by choosing the child with the lowest stored
/// distance. For the defending side, the best defense is found by
/// using lookup two plies ahead. For each defending move, every winning reply
/// is looked up, and the move whose best reply leaves the longest mate
/// distance is chosen.
/// </para>
/// 
/// <para>
/// <see cref="KbnkTableBuilder"/> seeds the checkmates and
/// <see cref="KbnkLossPropagator"/> builds the table backwards in
/// distance order. States are processed in ulong blocks containing all
/// 64 knight squares for a fixed king/bishop placement. A defender block
/// becomes lost when all legal king moves lead to winning positoins.
/// </para>
/// 
/// <para>
/// <see cref="IKbnkSync"/> provides the synchronization used by the
/// single threaded and parallel builders. The parallel build stays correct
/// because ClaimBits returns only newly claimed bits, so each state is
/// propagated exactly once. <see cref="KbnkTableManager"/> builds the
/// table lazily and keeps it cached while KBNK positions are being
/// searched. Search shrinks the transposition table before the build, so
/// the table and its transients live inside the freed hash budget, the
/// allocated total is capped at the minimum TT hash budget of 16mb.
/// </para>
/// </remarks>
public static class KbnkSolver
{
    public static bool TryGetRootMove(
        Position position,
        bool suppressOutput,
        out Move best,
        int threads = 1) => TryGetRootMove(position, suppressOutput, out best, out _, out _, threads);

    public static bool TryGetRootMove(
        Position position,
        bool suppressOutput,
        out Move best,
        out int plies,
        out bool stmIsStrong,
        int threads = 1)
    {
        best = default;
        plies = 0;
        stmIsStrong = false;

        if (!KbnkValidator.TryGetStrongSide(position, out _))
        {
            return false;
        }

        System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

        long builtStates = KbnkTableManager.EnsureBuilt(threads, out byte[] mateDistance) ? KbnkTableManager.GetTableSize() : 0;

        return KbnkLookup.TryFindRootMove(position, mateDistance, suppressOutput, clock, builtStates, out best, out plies, out stmIsStrong);
    }

    public static void Release()
    {
        KbnkTableManager.Release();
    }
}
