using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Zoomies.Core;
using Zoomies.Engine;

namespace Zoomies.Solvers.Kbnk;

public static class KbnkLossPropagator
{
    private const ulong NotFileA = 0xfefefefefefefefeUL;
    private const ulong NotFileH = 0x7f7f7f7f7f7f7f7fUL;
    private const ulong NotFilesAB = 0xfcfcfcfcfcfcfcfcUL;
    private const ulong NotFilesGH = 0x3f3f3f3f3f3f3f3fUL;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong KnightOrigins(ulong knights)
    {
        ulong oneFileOut = ((knights << 1) & NotFileA) | ((knights >> 1) & NotFileH);
        ulong twoFilesOut = ((knights << 2) & NotFilesAB) | ((knights >> 2) & NotFilesGH);
        return (oneFileOut << 16) | (oneFileOut >> 16) | (twoFilesOut << 8) | (twoFilesOut >> 8);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ClaimPredecessorWins<TSync>(
        int pairIdx,
        int weakKing,
        ulong losingKnights,
        ulong[] covered,
        ulong[] pendingKings) where TSync : IKbnkSync
    {
        ref ulong covered0 = ref MemoryMarshal.GetArrayDataReference(covered);
        ref ulong pending0 = ref MemoryMarshal.GetArrayDataReference(pendingKings);
        ref ulong between0 = ref MemoryMarshal.GetArrayDataReference(Tables.SquaresBetween);

        int strongKing = pairIdx >> 5;
        int darkIdx = pairIdx & 31;
        int bishopSq = KbnkStateIndex.SquareOfDarkIndex[darkIdx];

        ulong strongKingBB = 1UL << strongKing;
        ulong weakKingBB = 1UL << weakKing;
        ulong bishopBB = 1UL << bishopSq;

        ulong occNoKnight = strongKingBB | weakKingBB | bishopBB;
        ulong knightCheckSquares = Tables.KnightAttacks((Square)weakKing);
        ulong weakKingMoves = Tables.KingAttacks((Square)weakKing);
        ulong strongKingOrigins = Tables.KingAttacks((Square)strongKing) & ~occNoKnight & ~weakKingMoves;
        ulong betweenRay = Unsafe.Add(ref between0, (bishopSq << 6) | weakKing);
        ulong predecessorKings = weakKingMoves & ~Tables.KingAttacks((Square)strongKing) & ~strongKingBB;

        ulong bishopRays = Tables.BishopAttacks((Square)bishopSq, occNoKnight);
        bool knightRayOpen = (bishopRays & weakKingBB) != 0;
        bool kingRayOpen = (Tables.BishopAttacks((Square)bishopSq, weakKingBB | bishopBB) & weakKingBB) != 0;
        ulong weakKingRays = Tables.BishopAttacks((Square)weakKing, strongKingBB);

        ulong knightsNotChecking = losingKnights & ~knightCheckSquares;

        ulong knightOriginWins = KnightOrigins(losingKnights) & ~occNoKnight & ~knightCheckSquares;
        if (knightRayOpen)
        {
            knightOriginWins &= betweenRay;
        }
        if (knightOriginWins != 0)
        {
            ulong newWins = TSync.ClaimBits(ref Unsafe.Add(ref covered0, KbnkStateIndex.BlockIndex(pairIdx, weakKing)), knightOriginWins);
            if (newWins != 0)
            {
                TSync.Or(ref Unsafe.Add(ref pending0, pairIdx), predecessorKings);
            }
        }

        ulong bishopOrigins = knightsNotChecking != 0 ? bishopRays & ~occNoKnight : 0;
        while (bishopOrigins != 0)
        {
            int origin = (int)Bitboard.PopLsb(ref bishopOrigins);
            ulong originBB = 1UL << origin;
            ulong newWins = knightsNotChecking & ~originBB & ~Unsafe.Add(ref between0, (bishopSq << 6) | origin);
            if ((weakKingRays & originBB) != 0)
            {
                newWins &= Unsafe.Add(ref between0, (weakKing << 6) | origin);
            }
            if (newWins == 0)
            {
                continue;
            }

            int destPair = KbnkStateIndex.PairIndex(strongKing, KbnkStateIndex.DarkIndexOfSquare[origin]);
            newWins = TSync.ClaimBits(ref Unsafe.Add(ref covered0, KbnkStateIndex.BlockIndex(destPair, weakKing)), newWins);
            if (newWins != 0)
            {
                TSync.Or(ref Unsafe.Add(ref pending0, destPair), predecessorKings);
            }
        }

        while (strongKingOrigins != 0)
        {
            int kingFrom = (int)Bitboard.PopLsb(ref strongKingOrigins);
            ulong kingFromBB = 1UL << kingFrom;
            ulong newWins = knightsNotChecking & ~kingFromBB;
            if (kingRayOpen && (kingFromBB & betweenRay) == 0)
            {
                newWins &= betweenRay;
            }
            if (newWins == 0)
            {
                continue;
            }

            int destPair = KbnkStateIndex.PairIndex(kingFrom, darkIdx);
            newWins = TSync.ClaimBits(ref Unsafe.Add(ref covered0, KbnkStateIndex.BlockIndex(destPair, weakKing)), newWins);
            if (newWins != 0)
            {
                TSync.Or(ref Unsafe.Add(ref pending0, destPair), weakKingMoves & ~Tables.KingAttacks((Square)kingFrom) & ~kingFromBB);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static bool MarkLostBlocks(
        int pairIdx,
        ulong kingsToCheck,
        byte pliesToMate,
        ulong[] covered,
        ulong[] settled,
        ulong[] nextLossFrontier,
        ulong[] nextFrontierKings,
        byte[] mateDistance)
    {
        ref ulong covered0 = ref MemoryMarshal.GetArrayDataReference(covered);
        ref ulong settled0 = ref MemoryMarshal.GetArrayDataReference(settled);
        ref byte mateDistance0 = ref MemoryMarshal.GetArrayDataReference(mateDistance);

        int strongKing = pairIdx >> 5;
        int darkIdx = pairIdx & 31;
        int blockBase = pairIdx << 6;
        ulong newKings = 0;

        while (kingsToCheck != 0)
        {
            int weakKing = (int)Bitboard.PopLsb(ref kingsToCheck);
            ref ulong settledBlock = ref Unsafe.Add(ref settled0, blockBase | weakKing);
            ulong done = settledBlock;
            if (done == ulong.MaxValue)
            {
                continue;
            }

            ulong lost = ulong.MaxValue;
            ulong targets = Tables.KingAttacks((Square)weakKing);
            while (targets != 0)
            {
                lost &= Unsafe.Add(ref covered0, blockBase | (int)Bitboard.PopLsb(ref targets));
            }

            lost &= ~done;
            if (lost == 0)
            {
                continue;
            }

            settledBlock = done | lost;
            nextLossFrontier[blockBase | weakKing] = lost;
            newKings |= 1UL << weakKing;

            int byteBase = KbnkStateIndex.ComputeIndex(strongKing, weakKing, darkIdx, 0);
            ulong bits = lost;
            while (bits != 0)
            {
                Unsafe.Add(ref mateDistance0, byteBase | (int)Bitboard.PopLsb(ref bits)) = pliesToMate;
            }
        }

        if (newKings == 0)
        {
            return false;
        }

        nextFrontierKings[pairIdx] |= newKings;
        return true;
    }
}
