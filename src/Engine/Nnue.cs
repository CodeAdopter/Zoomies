using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using Zoomies.Core;

namespace Zoomies.Engine;

public static unsafe class Nnue
{
    public const int BlockRows = 768;
    private const int QA = 181, QO = 1024;
    private static int Scale = Tune.NnueScale;

    public static void RefreshTune()
    {
        Scale = Tune.NnueScale;
        BumpScoreGen();
    }

    private static byte scoreGen = 1;

    private static void BumpScoreGen() => scoreGen = (byte)(scoreGen == byte.MaxValue ? 1 : scoreGen + 1);

    private static short[] ftBMemory = [], ftWMemory = [], outWMemory = [];
    private static short* ftB, ftW, outW;
    private static int[] outBs = [];
    private static readonly int[] kingBucket = new int[64];
    private static int l1, rows, kb = 1, ob = 1;
    private static bool pairwise;

    public static bool Loaded { get; private set; }
    public static int L1 => l1;
    public static string LoadedPath { get; private set; } = "";

    public static void Load(string path)
    {
        LoadBytes(System.IO.File.ReadAllBytes(path), path);
    }

    // allocates a pinned short array and returns a 64 byte aligned pointer
    // to the beginning of an aligned region within the array
    private static short* AllocAligned(int count, out short[] memory)
    {
        memory = GC.AllocateArray<short>(count + 32, pinned: true);
        nint p = (nint)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(memory));
        return (short*)((p + 63) & ~(nint)63);
    }

    private static void ReadShorts(ReadOnlySpan<byte> src, short* dst, int count)
    {
        if (BitConverter.IsLittleEndian)
        {
            MemoryMarshal.Cast<byte, short>(src[..(2 * count)]).CopyTo(new Span<short>(dst, count));
            return;
        }
        for (int i = 0; i < count; i++)
            dst[i] = BinaryPrimitives.ReadInt16LittleEndian(src[(2 * i)..]);
    }

    private static void LoadBytes(byte[] bytes, string path)
    {
        var s = bytes.AsSpan();
        bool a2 = bytes.Length >= 20 && s[..8].SequenceEqual("ZOO768A2"u8);
        bool b1 = bytes.Length >= 20 && s[..8].SequenceEqual("ZOO768B1"u8);
        bool p1 = bytes.Length >= 20 && s[..8].SequenceEqual("ZOO768P1"u8);
        if (!a2 && !b1 && !p1)
            throw new InvalidDataException($"{path}: not a ZOO768A2/ZOO768B1/ZOO768P1 net");
        uint ver = BinaryPrimitives.ReadUInt32LittleEndian(s[8..]);
        int r = (int)BinaryPrimitives.ReadUInt32LittleEndian(s[12..]);
        int n = BinaryPrimitives.ReadUInt16LittleEndian(s[16..]);
        bool screlu = (s[18] & 1) != 0;
        int b = s[19];
        int k = r / BlockRows;
        bool okK = k == 1 || k == 4 || k == 8 || k == 16 || k == 32;
        if (ver != 1 || r % BlockRows != 0
            || (a2 && (!screlu || k != 1 || b != 1))
            || (b1 && (!screlu || k == 1 || !okK || b < 1 || b > 8))
            || (p1 && (s[18] != 0 || (n & 1) != 0 || !okK || b < 1 || b > 16)))
            throw new InvalidDataException($"{path}: unsupported {(p1 ? "ZOO768P1" : b1 ? "ZOO768B1" : "ZOO768A2")} (ver {ver} rows {r} flags {s[18]} buckets {b})");
        long need = 20 + 2L * n + 2L * r * n + b * (4 + (p1 ? 2L * n : 4L * n));
        if (bytes.Length != need) throw new InvalidDataException($"{path}: size {bytes.Length} != expected {need} for l1={n}");

        int owPer = p1 ? n : 2 * n;
        short* fb = AllocAligned(n, out short[] fbMemory);
        short* fw = AllocAligned(r * n, out short[] fwMemory);
        short* ow = AllocAligned(b * owPer, out short[] owMemory);
        var obs = new int[b];
        int off = 20;
        ReadShorts(s[off..], fb, n); off += 2 * n;
        ReadShorts(s[off..], fw, r * n); off += 2 * r * n;
        for (int bk = 0; bk < b; bk++)
        {
            obs[bk] = BinaryPrimitives.ReadInt32LittleEndian(s[off..]); off += 4;
            ReadShorts(s[off..], ow + bk * owPer, owPer); off += 2 * owPer;
        }

        ftBMemory = fbMemory; ftWMemory = fwMemory; outWMemory = owMemory;
        ftB = fb; ftW = fw; outW = ow; outBs = obs; l1 = n; rows = r; kb = k; ob = b; pairwise = p1;
        for (int sq = 0; sq < 64; sq++)
            kingBucket[sq] = kb > 1 ? KingBucketOf(sq) : 0;
        loadGen++;
        BumpScoreGen();

        int maxW = 0;
        for (int i = 0; i < b * owPer; i++)
            maxW = Math.Max(maxW, Math.Abs((int)ow[i]));

        useMadd = !p1 && Avx2.IsSupported && (n / 16 / 2 + 2) * 2L * QA * QA * maxW <= int.MaxValue;

        Loaded = true;
        LoadedPath = path;
    }

    private static int loadGen;
    private static bool useMadd;

    public static void Unload()
    {
        Loaded = false;
        ftBMemory = []; ftWMemory = []; outWMemory = [];
        ftB = ftW = outW = null;
        outBs = []; l1 = 0; rows = 0; kb = 1; ob = 1; pairwise = false; useMadd = false; LoadedPath = "";
        Array.Clear(kingBucket);
    }

    public static void AutoLoad()
    {
        if (Loaded) return;
        string beside = Path.Combine(AppContext.BaseDirectory, "best.nnue");
        string? env = Environment.GetEnvironmentVariable("ZOOMIES_NNUE");
        string? pick = System.IO.File.Exists(beside) ? beside
                     : env != null && System.IO.File.Exists(env) ? env : null;
        if (pick != null)
        {
            Load(pick);
            Console.WriteLine($"info string nnue loaded from {pick} (L1={l1})");
            return;
        }

        var asm = typeof(Nnue).Assembly;
        using var stream = asm.GetManifestResourceStream("best.nnue");
        if (stream == null) return;
        var buf = new byte[stream.Length];
        stream.ReadExactly(buf);
        LoadBytes(buf, "embedded:best.nnue");
        Console.WriteLine($"info string nnue loaded from embedded best.nnue (L1={l1})");
    }

    private const byte KindNone = 0, KindMove = 1, KindNull = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct Level
    {
        public ushort Move;     // move from the parent ply to this one (KindMove)
        public byte Piece;      // piece that moved, as it stood on From before the move
        public byte Captured;   // piece that stood on To before the move
        public byte Us;         // side that moved
        public byte Kind;       // KindNone (no parent on the stack), KindMove or KindNull
        public byte Valid;      // bit p: perspective ps half is materialised
        public byte Crossed;    // bit p: the move took ps king into another bucket
        public short Score;     // output for this plys position
        public byte HasScore;   // scoreGen when Score is current otherwise 0
    }

    public sealed class State
    {
        public const int Cap = 192;
        public readonly int L1;
        public readonly Level[] Levels = new Level[Cap];
        public int BasePly = int.MinValue / 4;

        public readonly short* Acc; 
        public readonly short* FinnyAcc;
        public readonly ulong[] FinnyBB = new ulong[2 * KbMax * 12];
        public int FinnyGen = -1;

        private readonly short[] accMemory, finnyMemory;

        public State(int l1)
        {
            L1 = l1;
            Acc = AllocAligned(Cap * 2 * l1, out accMemory);
            FinnyAcc = AllocAligned(2 * KbMax * l1, out finnyMemory);
        }

        public void Reset() { BasePly = int.MinValue / 4; Array.Clear(Levels); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public short* Half(int idx, int p) => Acc + (2 * idx + p) * L1;
    }

    private const int KbMax = 32;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ref Level LevelAt(State st, int idx) => ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(st.Levels), idx);

    private static int Orient(int p) => 56 * p;

    private static int KingBucket4(int ok)
    {
        int r = ok >> 3, f = ok & 7;
        return r >= 4 ? 3 : r >= 2 ? 2 : f >= 4 ? 0 : 1;
    }

    private static int KingBucket8(int ok)
    {
        int r = ok >> 3, f = ok & 7;
        return r >= 6
            ? 7
            : r >= 4
                ? 6
                : r >= 2
                    ? (f >= 4 ? 4 : 5)
                    : 3 - (f >> 1);
    }

    private static int KingBucket16(int ok) => 2 * KingBucket8(ok) + ((ok >> 3) & 1);

    private static int KingBucket32(int ok)
    {
        int r = ok >> 3, f = ok & 7;
        int bit = r >= 4 ? (f >> 2) & 1 : r >= 2 ? (f >> 1) & 1 : f & 1;
        return 2 * KingBucket16(ok) + bit;
    }

    private static int KingBucketOf(int ok) =>
        kb == 32 ? KingBucket32(ok) : kb == 16 ? KingBucket16(ok) : kb == 8 ? KingBucket8(ok) : KingBucket4(ok);

    // first feature row of perspective ps king bucket (0 with a single bucket).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int BucketBase(Position pos, int p) =>
        BlockRows * Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(kingBucket), KingSqOf(pos, p) ^ Orient(p));

    private static int KingSqOf(Position pos, int p) =>
        BitOperations.TrailingZeroCount(pos.BitboardOf((Color)p, PieceType.King));

    // weight row of piece pc on square sq seen by perspective p whose king bucket starts at row bb
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static short* Row(int p, int bb, int pc, int sq) =>
        ftW + (nint)(bb + ((pc >> 3) ^ p) * 384 + (pc & 7) * 64 + (sq ^ Orient(p))) * l1;

    private static void CastleSquares(Position pos, Color us, MoveFlags flags, int from, out int kFrom, out int kTo, out int rFrom, out int rTo)
    {
        bool kingside = flags == MoveFlags.OO;
        if (Position.Chess960)
        {
            pos.CastleSquaresFrc(us, kingside, out kTo, out rFrom, out rTo);
            kFrom = from;
        }
        else
        {
            int e = us == Color.White ? (int)Square.e1 : (int)Square.e8;
            kFrom = e;
            kTo = kingside ? e + 2 : e - 2;
            rFrom = kingside ? e + 3 : e - 4;
            rTo = kingside ? e + 1 : e - 1;
        }
    }

    public static void OnPlay(Position pos, Color us, Move m)
    {
        var st = pos.NnueSt!;
        int nxt = pos.Ply - st.BasePly + 1;
        if ((uint)nxt >= State.Cap) return;

        ref Level lv = ref LevelAt(st, nxt);
        int pc = (int)pos.At(m.From);
        int captured = (int)pos.At(m.To);
        lv.Move = m.EncodedValue;
        lv.Piece = (byte)pc;
        lv.Captured = (byte)captured;
        lv.Us = (byte)us;
        lv.Kind = nxt == 0 ? KindNone : KindMove;
        lv.Valid = 0;
        lv.Crossed = 0;
        lv.HasScore = 0;

        MoveFlags flags = m.Flags;
        bool castle = flags is MoveFlags.OO or MoveFlags.OOO;
        if (kb > 1 && (pc & 7) == (int)PieceType.King)
        {
            int kFrom = (int)m.From, kTo = (int)m.To;
            if (castle)
                CastleSquares(pos, us, flags, kFrom, out kFrom, out kTo, out _, out _);
            int om = Orient((int)us);
            if (kingBucket[kFrom ^ om] != kingBucket[kTo ^ om])
                lv.Crossed = (byte)(1 << (int)us);
        }

        if (!pairwise || !Avx2.IsSupported || castle || lv.Crossed != 0 || nxt == 0) return;
        short* s0 = ParentHalf(st, nxt, 0);
        short* s1 = ParentHalf(st, nxt, 1);
        if (s0 == null || s1 == null) return;

        // No bucket change, so the king squares on the board before the move give the child's buckets.
        int bk = OutputBucketAfter(pos, m, captured);
        long sum = FusedApplyOutput(pos, ref lv, s0, s1, st.Half(nxt, 0), (int)us ^ 1, bk);
        lv.Valid = 3;
        lv.Score = (short)PairwiseCentipawns(sum + outBs[bk]);
        lv.HasScore = scoreGen;
    }

    public static void OnPlayNull(Position pos)
    {
        var st = pos.NnueSt!;
        int nxt = pos.Ply - st.BasePly + 1;
        if ((uint)nxt >= State.Cap) return;

        ref Level lv = ref LevelAt(st, nxt);
        lv.Kind = nxt == 0 ? KindNone : KindNull;
        lv.Valid = 0;
        lv.Crossed = 0;
        lv.HasScore = 0;
    }

    public static void OnUndo(Position pos)
    {

    }

    public static int Evaluate(Position pos)
    {
        var st = pos.NnueSt;
        if (st == null || st.L1 != l1) pos.NnueSt = st = new State(l1);
        int idx = pos.Ply - st.BasePly;
        if ((uint)idx >= State.Cap)
        {
            st.BasePly = pos.Ply;
            idx = 0;
            Array.Clear(st.Levels);
        }

        ref Level lv = ref LevelAt(st, idx);
        if (lv.HasScore == scoreGen) return lv.Score;

        int stm = (int)pos.Turn;
        int bk = OutputBucket(pos);
        short* white = ResolveHalf(pos, st, idx, 0);
        short* black = ResolveHalf(pos, st, idx, 1);
        int score = stm == 0 ? Output(white, black, bk) : Output(black, white, bk);
        lv.Score = (short)score;
        lv.HasScore = scoreGen;
        return score;
    }

    // Any queen (either colour) on the board
    private static int QueenBit(Position pos) =>
        (pos.BitboardOf(Color.White, PieceType.Queen) | pos.BitboardOf(Color.Black, PieceType.Queen)) != 0 ? 1 : 0;

    private static int OutputBucket(Position pos)
    {
        ulong occ = pos.AllPieces(Color.White) | pos.AllPieces(Color.Black);
        return OutputBucket(BitOperations.PopCount(occ), QueenBit(pos));
    }

    private static int OutputBucket(int pieceCnt, int qbit) =>
        ob == 16 ? 2 * Math.Min((pieceCnt - 1) >> 2, 7) + qbit
        : ob > 1 ? Math.Min((pieceCnt - 1) >> 2, ob - 1) : 0;

    private static int OutputBucketAfter(Position pos, Move m, int captured)
    {
        ulong occ = pos.AllPieces(Color.White) | pos.AllPieces(Color.Black);
        ulong queens = pos.BitboardOf(Color.White, PieceType.Queen) | pos.BitboardOf(Color.Black, PieceType.Queen);
        int pieceCnt = BitOperations.PopCount(occ);
        MoveFlags flags = m.Flags;
        ulong toBit = 1UL << (int)m.To;
        if (m.IsCapture)
        {
            pieceCnt--;
            if (flags != MoveFlags.EnPassant && (captured & 7) == (int)PieceType.Queen) queens &= ~toBit;
        }
        if ((flags & MoveFlags.Promotions) != 0 && ((int)flags & 3) == 3) queens |= toBit;
        return OutputBucket(pieceCnt, queens != 0 ? 1 : 0);
    }

    // half p of the nearest ancestor of ply idx reached from it through null moves only
    private static short* ParentHalf(State st, int idx, int p)
    {
        int bit = 1 << p;
        for (int k = idx - 1; k >= 0; k--)
        {
            ref Level lk = ref LevelAt(st, k);
            if ((lk.Valid & bit) != 0) return st.Half(k, p);
            if (lk.Kind != KindNull) return null;
        }
        return null;
    }

    // half p of ply idx, materialising it from the nearest ancestor
    private static short* ResolveHalf(Position pos, State st, int idx, int p)
    {
        int bit = 1 << p;
        int k = idx;
        while (true)
        {
            ref Level lk = ref LevelAt(st, k);
            if ((lk.Valid & bit) != 0) break;
            if (lk.Kind == KindNone || (lk.Crossed & bit) != 0 || k == 0)
            {
                short* half = st.Half(idx, p);
                RefreshHalf(pos, st, p, half);
                LevelAt(st, idx).Valid |= (byte)bit;
                return half;
            }
            k--;
        }

        short* src = st.Half(k, p);
        if (k == idx) return src;

        // No bucket change between k and idx, so ps bucket is the one on the board
        int bb = BucketBase(pos, p);
        for (int j = k + 1; j <= idx; j++)
        {
            ref Level lj = ref LevelAt(st, j);
            if (lj.Kind == KindNull) continue;
            short* dst = st.Half(j, p);
            Apply(pos, ref lj, p, bb, src, dst);
            lj.Valid |= (byte)bit;
            src = dst;
        }
        return src;
    }

    // dst = src updated by the move recorded in lv, for perspective p
    private static void Apply(Position pos, ref Level lv, int p, int bb, short* src, short* dst)
    {
        int n = l1;
        int move = lv.Move;
        int to = move & 0x3F, from = (move >> 6) & 0x3F;
        var flags = (MoveFlags)((move >> 12) & 0xF);
        int us = lv.Us, pc = lv.Piece;
        switch (flags)
        {
            case MoveFlags.Quiet:
            case MoveFlags.DoublePush:
                AddSub(dst, src, Row(p, bb, pc, to), Row(p, bb, pc, from), n);
                break;
            case MoveFlags.Capture:
                AddSubSub(dst, src, Row(p, bb, pc, to), Row(p, bb, pc, from), Row(p, bb, lv.Captured, to), n);
                break;
            case MoveFlags.EnPassant:
                AddSubSub(dst, src, Row(p, bb, pc, to), Row(p, bb, pc, from),
                    Row(p, bb, (us ^ 1) << 3, us == 0 ? to - 8 : to + 8), n);
                break;
            case MoveFlags.PrKnight: case MoveFlags.PrBishop: case MoveFlags.PrRook: case MoveFlags.PrQueen:
                AddSub(dst, src, Row(p, bb, (us << 3) | (((int)flags & 3) + 1), to), Row(p, bb, us << 3, from), n);
                break;
            case MoveFlags.PcKnight: case MoveFlags.PcBishop: case MoveFlags.PcRook: case MoveFlags.PcQueen:
                AddSubSub(dst, src, Row(p, bb, (us << 3) | (((int)flags & 3) + 1), to), Row(p, bb, us << 3, from),
                    Row(p, bb, lv.Captured, to), n);
                break;
            default: // OO / OOO
            {
                int king = (us << 3) | (int)PieceType.King, rook = (us << 3) | (int)PieceType.Rook;
                CastleSquares(pos, (Color)us, flags, from, out int kF, out int kT, out int rF, out int rT);
                AddAddSubSub(dst, src, Row(p, bb, king, kT), Row(p, bb, rook, rT),
                    Row(p, bb, king, kF), Row(p, bb, rook, rF), n);
                break;
            }
        }
    }

    // materialises both halves of a ply reached by a non castling move with no bucket change,
    // from the parent halves s0/s1 into dst, and returns the pairwise output sum (without bias) for side to move stm and output bucket bk.
    private static long FusedApplyOutput(Position pos, ref Level lv, short* s0, short* s1, short* dst, int stm, int bk)
    {
        int bb0 = BucketBase(pos, 0), bb1 = BucketBase(pos, 1);
        int move = lv.Move;
        int to = move & 0x3F, from = (move >> 6) & 0x3F;
        var flags = (MoveFlags)((move >> 12) & 0xF);
        int us = lv.Us, pc = lv.Piece;
        short* w = outW + bk * l1;
        switch (flags)
        {
            case MoveFlags.Quiet:
            case MoveFlags.DoublePush:
                return FusedAddSub(s0, s1, dst,
                    Row(0, bb0, pc, to), Row(0, bb0, pc, from),
                    Row(1, bb1, pc, to), Row(1, bb1, pc, from), w, stm);
            case MoveFlags.Capture:
            {
                int cap = lv.Captured;
                return FusedAddSubSub(s0, s1, dst,
                    Row(0, bb0, pc, to), Row(0, bb0, pc, from), Row(0, bb0, cap, to),
                    Row(1, bb1, pc, to), Row(1, bb1, pc, from), Row(1, bb1, cap, to), w, stm);
            }
            case MoveFlags.EnPassant:
            {
                int cap = (us ^ 1) << 3, capSq = us == 0 ? to - 8 : to + 8;
                return FusedAddSubSub(s0, s1, dst,
                    Row(0, bb0, pc, to), Row(0, bb0, pc, from), Row(0, bb0, cap, capSq),
                    Row(1, bb1, pc, to), Row(1, bb1, pc, from), Row(1, bb1, cap, capSq), w, stm);
            }
            case MoveFlags.PrKnight: case MoveFlags.PrBishop: case MoveFlags.PrRook: case MoveFlags.PrQueen:
            {
                int promo = (us << 3) | (((int)flags & 3) + 1), pawn = us << 3;
                return FusedAddSub(s0, s1, dst,
                    Row(0, bb0, promo, to), Row(0, bb0, pawn, from),
                    Row(1, bb1, promo, to), Row(1, bb1, pawn, from), w, stm);
            }
            default: // promo captures
            {
                int promo = (us << 3) | (((int)flags & 3) + 1), pawn = us << 3, cap = lv.Captured;
                return FusedAddSubSub(s0, s1, dst,
                    Row(0, bb0, promo, to), Row(0, bb0, pawn, from), Row(0, bb0, cap, to),
                    Row(1, bb1, promo, to), Row(1, bb1, pawn, from), Row(1, bb1, cap, to), w, stm);
            }
        }
    }

    // Stateless scratch eval
    public static int EvaluateScratch(Position pos)
    {
        short* acc = stackalloc short[2 * l1];
        Refresh(pos, acc);
        int bk = OutputBucket(pos);
        return pos.Turn == Color.White ? Output(acc, acc + l1, bk) : Output(acc + l1, acc, bk);
    }

    // Finny table refresh
    private static void RefreshHalf(Position pos, State st, int p, short* dst)
    {
        int n = st.L1;
        if (st.FinnyGen != loadGen)
        {
            for (int e2 = 0; e2 < 2 * KbMax; e2++) Copy(st.FinnyAcc + e2 * n, ftB, n);
            Array.Clear(st.FinnyBB);
            st.FinnyGen = loadGen;
        }
        int bkt = kingBucket[KingSqOf(pos, p) ^ Orient(p)];
        int e = p * KbMax + bkt;
        short* cacc = st.FinnyAcc + e * n;
        var cbb = st.FinnyBB.AsSpan(e * 12, 12);
        int om = Orient(p), bb = BlockRows * bkt;
        for (int col = 0; col < 2; col++)
            for (int type = 0; type < 6; type++)
            {
                int i12 = col * 6 + type;
                ulong now = pos.BitboardOf((Color)col, (PieceType)type);
                ulong was = cbb[i12];
                if (now == was) continue;
                int rb = bb + (col == p ? 0 : 384) + type * 64;
                for (ulong d = now & ~was; d != 0; d &= d - 1)
                    Add(cacc, ftW + (nint)(rb + (BitOperations.TrailingZeroCount(d) ^ om)) * n, n);
                for (ulong d = was & ~now; d != 0; d &= d - 1)
                    Sub(cacc, ftW + (nint)(rb + (BitOperations.TrailingZeroCount(d) ^ om)) * n, n);
                cbb[i12] = now;
            }
        Copy(dst, cacc, n);
    }

    // Build both perspective halves (white half, black half) from the board.
    private static void Refresh(Position pos, short* acc)
    {
        int n = l1;
        Copy(acc, ftB, n);
        Copy(acc + n, ftB, n);
        ulong occ = pos.AllPieces(Color.White) | pos.AllPieces(Color.Black);
        int bbW = BucketBase(pos, 0), bbB = BucketBase(pos, 1);
        for (ulong b = occ; b != 0; b &= b - 1)
        {
            int sq = BitOperations.TrailingZeroCount(b);
            int pc = (int)pos.At((Square)sq);
            Add(acc, Row(0, bbW, pc, sq), n);
            Add(acc + n, Row(1, bbB, pc, sq), n);
        }
    }

    private static int Output(short* own, short* opp, int bk)
    {
        int n = l1;
        if (pairwise)
            return PairwiseCentipawns(outBs[bk] + DotPair2(own, opp, outW + bk * n, n >> 1));

        short* w = outW + bk * 2 * n;
        long sum = outBs[bk]
            + DotSq(new ReadOnlySpan<short>(own, n), new ReadOnlySpan<short>(w, n))
            + DotSq(new ReadOnlySpan<short>(opp, n), new ReadOnlySpan<short>(w + n, n));
        int cp = (int)(sum * Scale / ((long)QA * QA * QO));
        return Math.Clamp(cp, -Eval.MateBound + 1, Eval.MateBound - 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int PairwiseCentipawns(long sum)
    {
        int cp = (int)(sum * 256L * Scale / ((long)QA * QA * QO));
        return Math.Clamp(cp, -Eval.MateBound + 1, Eval.MateBound - 1);
    }

    // Pairwise output: act(x) = (clamp(x_lo)*clamp(x_hi) + 128) >> 8, with the bucket's weights
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<short> PairActivation(Vector256<short> lo, Vector256<short> hi, Vector256<short> qa, Vector256<short> rnd)
    {
        var zero = Vector256<short>.Zero;
        return Avx2.ShiftRightLogical(Avx2.AddSaturate(Avx2.MultiplyLow(Avx2.Min(Avx2.Max(lo, zero), qa), Avx2.Min(Avx2.Max(hi, zero), qa)), rnd), 8);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<int> DotAccumulate(Vector256<int> acc, Vector256<short> act, Vector256<short> w) =>
        AvxVnni.IsSupported ? AvxVnni.MultiplyWideningAndAdd(acc, act, w)
                            : Avx2.Add(acc, Avx2.MultiplyAddAdjacent(act, w));

    // sum act(own) * w[0, pairs) + sum act(opp) * w[pairs, 2*pairs)
    private static long DotPair2(short* own, short* opp, short* w, int pairs)
    {
        if (!Avx2.IsSupported)
            return DotPairScalar(own, w, pairs) + DotPairScalar(opp, w + pairs, pairs);

        var qa = Vector256.Create((short)QA);
        var rnd = Vector256.Create((short)128);
        var accOwn = Vector256<int>.Zero;
        var accOpp = Vector256<int>.Zero;
        short* ownHi = own + pairs, oppHi = opp + pairs, wOpp = w + pairs;
        int j = 0;
        for (; j + 16 <= pairs; j += 16)
        {
            accOwn = DotAccumulate(accOwn, PairActivation(Avx.LoadVector256(own + j), Avx.LoadVector256(ownHi + j), qa, rnd), Avx.LoadVector256(w + j));
            accOpp = DotAccumulate(accOpp, PairActivation(Avx.LoadVector256(opp + j), Avx.LoadVector256(oppHi + j), qa, rnd), Avx.LoadVector256(wOpp + j));
        }
        long sum = WideSum(accOwn) + WideSum(accOpp);
        for (; j < pairs; j++)
            sum += PairAct(own[j], ownHi[j]) * w[j] + PairAct(opp[j], oppHi[j]) * wOpp[j];
        return sum;
    }

    // scalar mirror of the AVX2 lane math, including the saturating rounding add
    private static int PairAct(int a, int b)
    {
        int p = Math.Clamp(a, 0, QA) * Math.Clamp(b, 0, QA);
        return Math.Min(p + 128, 32767) >> 8;
    }

    private static long DotPairScalar(short* half, short* w, int pairs)
    {
        long sum = 0;
        for (int j = 0; j < pairs; j++)
            sum += PairAct(half[j], half[j + pairs]) * w[j];
        return sum;
    }

    // 16 pair step of the fused kernels
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<int> FusedStep2(short* s, short* d, short* a, short* b, short* w, int j, int h, Vector256<short> qa, Vector256<short> rnd, Vector256<int> acc)
    {
        var lo = Avx2.Subtract(Avx2.Add(Avx.LoadVector256(s + j), Avx.LoadVector256(a + j)), Avx.LoadVector256(b + j));
        var hi = Avx2.Subtract(Avx2.Add(Avx.LoadVector256(s + h), Avx.LoadVector256(a + h)), Avx.LoadVector256(b + h));
        Avx.Store(d + j, lo);
        Avx.Store(d + h, hi);
        return DotAccumulate(acc, PairActivation(lo, hi, qa, rnd), Avx.LoadVector256(w + j));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<int> FusedStep3(short* s, short* d, short* a, short* b, short* c, short* w, int j, int h,
        Vector256<short> qa, Vector256<short> rnd, Vector256<int> acc)
    {
        var lo = Avx2.Subtract(Avx2.Subtract(Avx2.Add(Avx.LoadVector256(s + j), Avx.LoadVector256(a + j)), Avx.LoadVector256(b + j)), Avx.LoadVector256(c + j));
        var hi = Avx2.Subtract(Avx2.Subtract(Avx2.Add(Avx.LoadVector256(s + h), Avx.LoadVector256(a + h)), Avx.LoadVector256(b + h)), Avx.LoadVector256(c + h));
        Avx.Store(d + j, lo);
        Avx.Store(d + h, hi);
        return DotAccumulate(acc, PairActivation(lo, hi, qa, rnd), Avx.LoadVector256(w + j));
    }

    // dst halves = s0/s1 + a - b per perspective
    private static long FusedAddSub(short* s0, short* s1, short* dst, short* a0, short* b0, short* a1, short* b1, short* w, int stm)
    {
        int n = l1, pairs = n >> 1;
        var qa = Vector256.Create((short)QA);
        var rnd = Vector256.Create((short)128);
        var acc0 = Vector256<int>.Zero;
        var acc1 = Vector256<int>.Zero;
        long tail = 0;
        for (int p = 0; p < 2; p++)
        {
            short* s = p == 0 ? s0 : s1, d = dst + p * n;
            short* a = p == 0 ? a0 : a1, b = p == 0 ? b0 : b1;
            short* wp = w + (p ^ stm) * pairs;
            int j = 0;
            for (; j + 32 <= pairs; j += 32)
            {
                acc0 = FusedStep2(s, d, a, b, wp, j, j + pairs, qa, rnd, acc0);
                acc1 = FusedStep2(s, d, a, b, wp, j + 16, j + 16 + pairs, qa, rnd, acc1);
            }
            for (; j + 16 <= pairs; j += 16)
                acc0 = FusedStep2(s, d, a, b, wp, j, j + pairs, qa, rnd, acc0);
            for (; j < pairs; j++)
            {
                short lo = (short)(s[j] + a[j] - b[j]);
                short hi = (short)(s[j + pairs] + a[j + pairs] - b[j + pairs]);
                d[j] = lo;
                d[j + pairs] = hi;
                tail += PairAct(lo, hi) * wp[j];
            }
        }
        return WideSum(acc0) + WideSum(acc1) + tail;
    }

    // dst halves = s0/s1 + a - b - c per perspective
    private static long FusedAddSubSub(short* s0, short* s1, short* dst, short* a0, short* b0, short* c0, short* a1, short* b1, short* c1, short* w, int stm)
    {
        int n = l1, pairs = n >> 1;
        var qa = Vector256.Create((short)QA);
        var rnd = Vector256.Create((short)128);
        var acc0 = Vector256<int>.Zero;
        var acc1 = Vector256<int>.Zero;
        long tail = 0;
        for (int p = 0; p < 2; p++)
        {
            short* s = p == 0 ? s0 : s1, d = dst + p * n;
            short* a = p == 0 ? a0 : a1, b = p == 0 ? b0 : b1, c = p == 0 ? c0 : c1;
            short* wp = w + (p ^ stm) * pairs;
            int j = 0;
            for (; j + 32 <= pairs; j += 32)
            {
                acc0 = FusedStep3(s, d, a, b, c, wp, j, j + pairs, qa, rnd, acc0);
                acc1 = FusedStep3(s, d, a, b, c, wp, j + 16, j + 16 + pairs, qa, rnd, acc1);
            }
            for (; j + 16 <= pairs; j += 16)
                acc0 = FusedStep3(s, d, a, b, c, wp, j, j + pairs, qa, rnd, acc0);
            for (; j < pairs; j++)
            {
                short lo = (short)(s[j] + a[j] - b[j] - c[j]);
                short hi = (short)(s[j + pairs] + a[j + pairs] - b[j + pairs] - c[j + pairs]);
                d[j] = lo;
                d[j + pairs] = hi;
                tail += PairAct(lo, hi) * wp[j];
            }
        }
        return WideSum(acc0) + WideSum(acc1) + tail;
    }

    private static long DotSq(ReadOnlySpan<short> acc, ReadOnlySpan<short> w) => useMadd ? DotSqMadd(acc, w) : DotSqWiden(acc, w);

    private static long DotSqMadd(ReadOnlySpan<short> acc, ReadOnlySpan<short> w)
    {
        ref short ra = ref MemoryMarshal.GetReference(acc);
        ref short rw = ref MemoryMarshal.GetReference(w);
        var zero = Vector256<short>.Zero;
        var qa = Vector256.Create((short)QA);
        var acc0 = Vector256<int>.Zero;
        var acc1 = Vector256<int>.Zero;
        int len = acc.Length, j = 0;
        for (; j + 32 <= len; j += 32)
        {
            var t0 = Avx2.Min(Avx2.Max(Vector256.LoadUnsafe(ref ra, (nuint)j), zero), qa);
            var t1 = Avx2.Min(Avx2.Max(Vector256.LoadUnsafe(ref ra, (nuint)(j + 16)), zero), qa);
            acc0 = Avx2.Add(acc0, Avx2.MultiplyAddAdjacent(Avx2.MultiplyLow(t0, t0), Vector256.LoadUnsafe(ref rw, (nuint)j)));
            acc1 = Avx2.Add(acc1, Avx2.MultiplyAddAdjacent(Avx2.MultiplyLow(t1, t1), Vector256.LoadUnsafe(ref rw, (nuint)(j + 16))));
        }
        for (; j + 16 <= len; j += 16)
        {
            var t0 = Avx2.Min(Avx2.Max(Vector256.LoadUnsafe(ref ra, (nuint)j), zero), qa);
            acc0 = Avx2.Add(acc0, Avx2.MultiplyAddAdjacent(Avx2.MultiplyLow(t0, t0), Vector256.LoadUnsafe(ref rw, (nuint)j)));
        }
        long sum = WideSum(acc0) + WideSum(acc1);
        for (; j < len; j++) { int c = Math.Clamp((int)acc[j], 0, QA); sum += c * c * w[j]; }
        return sum;
    }

    private static long WideSum(Vector256<int> v)
    {
        var s = Avx2.Add(Avx2.ConvertToVector256Int64(v.GetLower()), Avx2.ConvertToVector256Int64(v.GetUpper()));
        return s.GetElement(0) + s.GetElement(1) + s.GetElement(2) + s.GetElement(3);
    }

    private static long DotSqWiden(ReadOnlySpan<short> acc, ReadOnlySpan<short> w)
    {
        ref short ra = ref MemoryMarshal.GetReference(acc);
        ref short rw = ref MemoryMarshal.GetReference(w);
        var zero = Vector<short>.Zero;
        var qa = new Vector<short>((short)QA);
        long sum = 0;
        var accum = Vector<int>.Zero;
        int v = Vector<short>.Count, len = acc.Length;
        int j = 0, sinceDrain = 0;
        for (; j + v <= len; j += v)
        {
            var a = Vector.Min(Vector.Max(Vector.LoadUnsafe(ref ra, (nuint)j), zero), qa);
            Vector.Widen(a * a, out Vector<int> s0, out Vector<int> s1);
            Vector.Widen(Vector.LoadUnsafe(ref rw, (nuint)j), out Vector<int> w0, out Vector<int> w1);
            accum += s0 * w0 + s1 * w1;
            if (++sinceDrain == 8)
            {
                sum += Vector.Sum(accum);
                accum = Vector<int>.Zero; sinceDrain = 0;
            }
        }
        sum += Vector.Sum(accum);
        for (; j < len; j++) { int c = Math.Clamp((int)acc[j], 0, QA); sum += c * c * w[j]; }
        return sum;
    }

    private static void Copy(short* dst, short* src, int n) =>
        Buffer.MemoryCopy(src, dst, 2L * n, 2L * n);

    private static void Add(short* acc, short* row, int n)
    {
        int v = Vector<short>.Count, j = 0;
        for (; j + v <= n; j += v)
            Vector.Store(Vector.Load(acc + j) + Vector.Load(row + j), acc + j);
        for (; j < n; j++) acc[j] += row[j];
    }

    private static void Sub(short* acc, short* row, int n)
    {
        int v = Vector<short>.Count, j = 0;
        for (; j + v <= n; j += v)
            Vector.Store(Vector.Load(acc + j) - Vector.Load(row + j), acc + j);
        for (; j < n; j++) acc[j] -= row[j];
    }

    private static void AddSub(short* dst, short* src, short* add, short* sub, int n)
    {
        int v = Vector<short>.Count, j = 0;
        for (; j + v <= n; j += v)
            Vector.Store(Vector.Load(src + j) + Vector.Load(add + j) - Vector.Load(sub + j), dst + j);
        for (; j < n; j++) dst[j] = (short)(src[j] + add[j] - sub[j]);
    }

    private static void AddSubSub(short* dst, short* src, short* add, short* sub1, short* sub2, int n)
    {
        int v = Vector<short>.Count, j = 0;
        for (; j + v <= n; j += v)
            Vector.Store(Vector.Load(src + j) + Vector.Load(add + j) - Vector.Load(sub1 + j) - Vector.Load(sub2 + j), dst + j);
        for (; j < n; j++) dst[j] = (short)(src[j] + add[j] - sub1[j] - sub2[j]);
    }

    private static void AddAddSubSub(short* dst, short* src, short* add1, short* add2, short* sub1, short* sub2, int n)
    {
        int v = Vector<short>.Count, j = 0;
        for (; j + v <= n; j += v)
            Vector.Store(Vector.Load(src + j) + Vector.Load(add1 + j) + Vector.Load(add2 + j)
                       - Vector.Load(sub1 + j) - Vector.Load(sub2 + j), dst + j);
        for (; j < n; j++) dst[j] = (short)(src[j] + add1[j] + add2[j] - sub1[j] - sub2[j]);
    }
}
