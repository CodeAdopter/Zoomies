using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Zoomies.Core;
using Zoomies.Engine;

namespace Zoomies.Solvers.Kbnk;

public static class KbnkTableBuilder
{
    private const int TableSize = 1 << 23;
    private const int PairCount = 64 * 32;
    private const int BlockCount = PairCount << 6;
    private const int PairsPerChunk = 32;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static byte[] Build(int threads)
    {
        byte[] mateDistance = new byte[TableSize];
        ulong[] covered = new ulong[BlockCount];
        ulong[] settled = new ulong[BlockCount];
        ulong[] lossFrontier = new ulong[BlockCount];
        ulong[] nextLossFrontier = new ulong[BlockCount];
        ulong[] frontierKings = new ulong[PairCount];
        ulong[] nextFrontierKings = new ulong[PairCount];
        ulong[] pendingKings = new ulong[PairCount];

        ParallelOptions? parallelOptions = threads > 1
            ? new ParallelOptions { MaxDegreeOfParallelism = threads }
            : null;

        if (parallelOptions == null)
        {
            for (int pairIdx = 0; pairIdx < PairCount; pairIdx++)
            {
                SeedPair(pairIdx, mateDistance, covered, settled, lossFrontier, frontierKings);
            }
        }
        else
        {
            Parallel.ForEach(
                Partitioner.Create(0, PairCount, PairsPerChunk),
                parallelOptions,
                range =>
                {
                    for (int pairIdx = range.Item1; pairIdx < range.Item2; pairIdx++)
                    {
                        SeedPair(pairIdx, mateDistance, covered, settled, lossFrontier, frontierKings);
                    }
                });
        }

        byte level = 1;

        while (true)
        {
            byte pliesToMate = (byte)(level + 2);
            bool grew;

            if (parallelOptions == null)
            {
                for (int pairIdx = 0; pairIdx < PairCount; pairIdx++)
                {
                    ClaimPairWins<KbnkSingleSync>(pairIdx, lossFrontier, frontierKings, covered, pendingKings);
                }

                grew = false;
                for (int pairIdx = 0; pairIdx < PairCount; pairIdx++)
                {
                    ulong kingsToCheck = pendingKings[pairIdx];
                    if (kingsToCheck == 0)
                    {
                        continue;
                    }
                    pendingKings[pairIdx] = 0;
                    grew |= KbnkLossPropagator.MarkLostBlocks(pairIdx, kingsToCheck, pliesToMate, covered, settled, nextLossFrontier, nextFrontierKings, mateDistance);
                }
            }
            else
            {
                ulong[] frontier = lossFrontier;
                ulong[] kings = frontierKings;
                ulong[] next = nextLossFrontier;
                ulong[] nextKings = nextFrontierKings;
                int grewFlag = 0;

                Parallel.ForEach(
                    Partitioner.Create(0, PairCount, PairsPerChunk),
                    parallelOptions,
                    range =>
                    {
                        for (int pairIdx = range.Item1; pairIdx < range.Item2; pairIdx++)
                        {
                            ClaimPairWins<KbnkParallelSync>(pairIdx, frontier, kings, covered, pendingKings);
                        }
                    });

                Parallel.ForEach(
                    Partitioner.Create(0, PairCount, PairsPerChunk),
                    parallelOptions,
                    range =>
                    {
                        bool localGrew = false;
                        for (int pairIdx = range.Item1; pairIdx < range.Item2; pairIdx++)
                        {
                            ulong kingsToCheck = pendingKings[pairIdx];
                            if (kingsToCheck == 0)
                            {
                                continue;
                            }
                            pendingKings[pairIdx] = 0;
                            localGrew |= KbnkLossPropagator.MarkLostBlocks(pairIdx, kingsToCheck, pliesToMate, covered, settled, next, nextKings, mateDistance);
                        }
                        if (localGrew)
                        {
                            Interlocked.Exchange(ref grewFlag, 1);
                        }
                    });
                grew = grewFlag != 0;
            }

            if (!grew)
            {
                break;
            }

            (lossFrontier, nextLossFrontier) = (nextLossFrontier, lossFrontier);
            (frontierKings, nextFrontierKings) = (nextFrontierKings, frontierKings);
            level += 2;
        }

        return mateDistance;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void ClaimPairWins<TSync>(
        int pairIdx,
        ulong[] lossFrontier,
        ulong[] frontierKings,
        ulong[] covered,
        ulong[] pendingKings) where TSync : IKbnkSync
    {
        ulong kings = frontierKings[pairIdx];
        if (kings == 0)
        {
            return;
        }
        frontierKings[pairIdx] = 0;

        while (kings != 0)
        {
            int weakKing = (int)Bitboard.PopLsb(ref kings);
            int blockIdx = KbnkStateIndex.BlockIndex(pairIdx, weakKing);
            ulong losingKnights = lossFrontier[blockIdx];
            lossFrontier[blockIdx] = 0;
            KbnkLossPropagator.ClaimPredecessorWins<TSync>(pairIdx, weakKing, losingKnights, covered, pendingKings);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void SeedPair(
        int pairIdx,
        byte[] mateDistance,
        ulong[] covered,
        ulong[] settled,
        ulong[] lossFrontier,
        ulong[] frontierKings)
    {
        int strongKing = pairIdx >> 5;
        int darkIdx = pairIdx & 31;
        int bishopSq = KbnkStateIndex.SquareOfDarkIndex[darkIdx];
        int blockBase = pairIdx << 6;

        if (bishopSq == strongKing)
        {
            covered.AsSpan(blockBase, 64).Fill(ulong.MaxValue);
            settled.AsSpan(blockBase, 64).Fill(ulong.MaxValue);
            return;
        }

        ulong strongKingBB = 1UL << strongKing;
        ulong bishopBB = 1UL << bishopSq;
        ulong strongKingZone = Tables.KingAttacks((Square)strongKing) | strongKingBB;
        ulong bishopRays = Tables.BishopAttacks((Square)bishopSq, strongKingBB);
        ulong pieceSquares = strongKingBB | bishopBB;
        ref ulong between0 = ref MemoryMarshal.GetArrayDataReference(Tables.SquaresBetween);
        ref byte mateDistance0 = ref MemoryMarshal.GetArrayDataReference(mateDistance);

        Span<ulong> unreachable = stackalloc ulong[64];
        for (int sq = 0; sq < 64; sq++)
        {
            if (((strongKingZone >> sq) & 1) != 0)
            {
                unreachable[sq] = ulong.MaxValue;
                continue;
            }

            ulong mask = Tables.KnightAttacks((Square)sq) | pieceSquares;
            if (((bishopRays >> sq) & 1) != 0)
            {
                mask |= ~Unsafe.Add(ref between0, (bishopSq << 6) | sq);
            }
            unreachable[sq] = mask;
        }

        ulong mateKings = 0;
        for (int weakKing = 0; weakKing < 64; weakKing++)
        {
            int blockIdx = blockBase | weakKing;
            covered[blockIdx] = unreachable[weakKing];

            if (((strongKingZone >> weakKing) & 1) != 0 || weakKing == bishopSq)
            {
                settled[blockIdx] = ulong.MaxValue;
                continue;
            }

            ulong valid = ~(pieceSquares | (1UL << weakKing));
            ulong noEscape = ulong.MaxValue;
            ulong targets = Tables.KingAttacks((Square)weakKing);
            while (targets != 0)
            {
                noEscape &= unreachable[(int)Bitboard.PopLsb(ref targets)];
            }

            ulong inCheck = Tables.KnightAttacks((Square)weakKing);
            if (((bishopRays >> weakKing) & 1) != 0)
            {
                inCheck |= ~Unsafe.Add(ref between0, (bishopSq << 6) | weakKing);
            }

            ulong mate = noEscape & inCheck & valid;
            settled[blockIdx] = ~valid | noEscape;

            if (mate == 0)
            {
                continue;
            }

            lossFrontier[blockIdx] = mate;
            mateKings |= 1UL << weakKing;

            int byteBase = KbnkStateIndex.ComputeIndex(strongKing, weakKing, darkIdx, 0);
            ulong bits = mate;
            while (bits != 0)
            {
                Unsafe.Add(ref mateDistance0, byteBase | (int)Bitboard.PopLsb(ref bits)) = 1;
            }
        }

        frontierKings[pairIdx] = mateKings;
    }
}
