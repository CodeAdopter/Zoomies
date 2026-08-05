using System.Runtime.CompilerServices;

namespace Zoomies.Engine;

public enum TtFlag : byte { None = 0, Exact = 1, Lower = 2, Upper = 3 }

public sealed class TranspositionTable
{
    public struct Entry
    {
        public ushort Move;
        public short Score;
        public byte Depth;
        public TtFlag Flag;
    }

    // Slot layout: Data packs move(0-15) | score(16-31) | depth(32-39) | flag(40-41) | gen(42-47).
    // KeyXorData = key ^ Data makes torn writes self-invalidating, so no locks are needed under SMP.
    private struct Slot
    {
        public ulong KeyXorData;
        public ulong Data;
    }

    private Slot[] table = [];
    private ulong mask;
    private byte generation;

    public TranspositionTable(int sizeMb = 16) => Resize(sizeMb);

    public void Resize(int sizeMb)
    {
        int entrySize = Unsafe.SizeOf<Slot>();
        long bytes = (long)sizeMb * 1024 * 1024;
        long want = bytes / entrySize;
        long pow = 1;
        while (pow * 2 <= want) pow *= 2;
        table = new Slot[pow];
        mask = (ulong)(pow - 1);
    }

    public void Clear()
    {
        Array.Clear(table);
        generation = 0;
    }

    public void NewSearch() => generation = (byte)((generation + 1) & 0x3F);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Pack(ushort move, short score, byte depth, TtFlag flag, byte gen) =>
          move
        | ((ulong)(ushort)score << 16)
        | ((ulong)depth << 32)
        | ((ulong)(uint)((int)flag | (gen << 2)) << 40);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Probe(ulong key, out Entry entry)
    {
        ulong baseIdx = (key & mask) & ~1UL;
        for (int w = 0; w < 2; w++)
        {
            ref Slot s = ref table[baseIdx + (ulong)w];
            ulong data = Volatile.Read(ref s.Data);
            ulong keyXorData = Volatile.Read(ref s.KeyXorData);

            if ((keyXorData ^ data) == key)
            {
                var flag = (TtFlag)(byte)((data >> 40) & 3);
                if (flag != TtFlag.None)
                {
                    entry = new Entry
                    {
                        Move = (ushort)data,
                        Score = (short)(data >> 16),
                        Depth = (byte)(data >> 32),
                        Flag = flag,
                    };
                    return true;
                }
            }
        }
        entry = default;
        return false;
    }

    public void Store(ulong key, ushort move, int score, int depth, TtFlag flag)
    {
        ref Slot s = ref table[SelectSlot(key)];
        ulong oldData = Volatile.Read(ref s.Data);
        ulong oldKeyXorData = Volatile.Read(ref s.KeyXorData);

        bool samePosition = (oldKeyXorData ^ oldData) == key;
        var oldFlag = (TtFlag)(byte)((oldData >> 40) & 3);
        int oldDepth = (byte)(oldData >> 32);
        byte oldGen = (byte)((oldData >> 42) & 0x3F);

        int effectiveDepth = depth + (flag == TtFlag.Exact ? 2 : 0);
        if (oldFlag != TtFlag.None && oldGen == generation && oldDepth > effectiveDepth)
            return;

        ushort finalMove = move != 0 ? move : samePosition ? (ushort)oldData : (ushort)0;
        ulong data = Pack(finalMove, (short)score, (byte)depth, flag, generation);
        Volatile.Write(ref s.Data, data);
        Volatile.Write(ref s.KeyXorData, key ^ data);
    }

    private ulong SelectSlot(ulong key)
    {
        ulong baseIdx = (key & mask) & ~1UL;
        ulong bestIdx = baseIdx;
        int bestScore = int.MaxValue;
        for (int w = 0; w < 2; w++)
        {
            ulong idx = baseIdx + (ulong)w;
            ref Slot s = ref table[idx];
            ulong data = Volatile.Read(ref s.Data);
            ulong keyXorData = Volatile.Read(ref s.KeyXorData);
            var flag = (TtFlag)(byte)((data >> 40) & 3);
            if ((keyXorData ^ data) == key || flag == TtFlag.None)
                return idx;

            byte gen = (byte)((data >> 42) & 0x3F);
            int depth = (byte)(data >> 32);
            int score = (gen == generation ? 512 : 0) + depth;
            if (score < bestScore) { bestScore = score; bestIdx = idx; }
        }
        return bestIdx;
    }

    // Mate scores are stored relative to the storing node, not the root, so a "mate in 3
    // from here" entry stays correct when probed at a different ply.
    public static int ScoreToTt(int score, int ply)
    {
        if (score >= Eval.MateBound) return score + ply;
        if (score <= -Eval.MateBound) return score - ply;
        return score;
    }

    public static int ScoreFromTt(int score, int ply)
    {
        if (score >= Eval.MateBound) return score - ply;
        if (score <= -Eval.MateBound) return score + ply;
        return score;
    }
}
