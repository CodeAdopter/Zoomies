using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using Zoomies.Core;

namespace Zoomies.Engine;

public static class Nnue
{
    public const int BlockRows = 768;
    private const int QA = 181, QO = 1024, Scale = 600;

    private static short[] ftB = [], ftW = [], outW = [];
    private static int[] outBs = [];
    private static int l1, rows, kb = 1, ob = 1;          

    public static bool Loaded { get; private set; }
    public static int L1 => l1;
    public static string LoadedPath { get; private set; } = "";

    public static void Load(string path)
    {
        var bytes = System.IO.File.ReadAllBytes(path);
        var s = bytes.AsSpan();
        bool a2 = bytes.Length >= 20 && s[..8].SequenceEqual("ZOO768A2"u8);
        bool b1 = bytes.Length >= 20 && s[..8].SequenceEqual("ZOO768B1"u8);
        if (!a2 && !b1)
            throw new InvalidDataException($"{path}: not a ZOO768A2/ZOO768B1 net");
        uint ver = BinaryPrimitives.ReadUInt32LittleEndian(s[8..]);
        int r = (int)BinaryPrimitives.ReadUInt32LittleEndian(s[12..]);
        int n = BinaryPrimitives.ReadUInt16LittleEndian(s[16..]);
        bool screlu = (s[18] & 1) != 0;
        int b = s[19];
        int k = r / BlockRows;
        if (ver != 1 || !screlu || r % BlockRows != 0
            || (a2 && (k != 1 || b != 1))
            || (b1 && ((k != 4 && k != 8 && k != 16 && k != 32) || b < 1 || b > 8)))
            throw new InvalidDataException($"{path}: unsupported {(b1 ? "ZOO768B1" : "ZOO768A2")} (ver {ver} rows {r} screlu {screlu} buckets {b})");
        long need = 20 + 2L * n + 2L * r * n + b * (4 + 4L * n);
        if (bytes.Length != need) throw new InvalidDataException($"{path}: size {bytes.Length} != expected {need} for l1={n}");

        var fb = new short[n];
        var fw = new short[r * n];
        var obs = new int[b];
        var ow = new short[b * 2 * n];
        int off = 20;
        for (int i = 0; i < n; i++, off += 2) fb[i] = BinaryPrimitives.ReadInt16LittleEndian(s[off..]);
        for (int i = 0; i < r * n; i++, off += 2) fw[i] = BinaryPrimitives.ReadInt16LittleEndian(s[off..]);
        for (int bk = 0; bk < b; bk++)
        {
            obs[bk] = BinaryPrimitives.ReadInt32LittleEndian(s[off..]); off += 4;
            for (int i = 0; i < 2 * n; i++, off += 2) ow[bk * 2 * n + i] = BinaryPrimitives.ReadInt16LittleEndian(s[off..]);
        }

        ftB = fb; ftW = fw; outW = ow; outBs = obs; l1 = n; rows = r; kb = k; ob = b;
        loadGen++; 

        int maxW = 0;
        foreach (short x in ow) 
            maxW = Math.Max(maxW, Math.Abs((int)x));
            
        useMadd = Avx2.IsSupported && (n / 16 / 2 + 2) * 2L * QA * QA * maxW <= int.MaxValue;

        Loaded = true;
        LoadedPath = path;
    }

    private static int loadGen;
    private static bool useMadd;

    public static void Unload() { Loaded = false; ftB = []; ftW = []; outW = []; outBs = []; l1 = 0; rows = 0; kb = 1; ob = 1; useMadd = false; LoadedPath = ""; }

    public static void AutoLoad()
    {
        if (Loaded) return;
        string beside = Path.Combine(AppContext.BaseDirectory, "best.nnue");
        string? env = Environment.GetEnvironmentVariable("ZOOMIES_NNUE");
        string? pick = System.IO.File.Exists(beside) ? beside
                     : env != null && System.IO.File.Exists(env) ? env : null;
        if (pick == null) return;
        Load(pick);
        Console.WriteLine($"info string nnue loaded from {pick} (L1={l1})");
    }


    public sealed class State(int l1)
    {
        public const int Cap = 192;
        public readonly short[] Acc = new short[Cap * 2 * l1];
        public readonly byte[] Valid = new byte[Cap];
        public readonly int L1 = l1;
        public int BasePly = int.MinValue / 4;

        public readonly short[] FinnyAcc = new short[2 * KbMax * l1];
        public readonly ulong[] FinnyBB = new ulong[2 * KbMax * 12];
        public int FinnyGen = -1;

        public void Reset() { BasePly = int.MinValue / 4; Array.Clear(Valid); }
    }

    private const int KbMax = 32;

    private static Span<short> Level(State st, int idx) => st.Acc.AsSpan(idx * 2 * st.L1, 2 * st.L1);

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

    private static int BucketBase(int p, int ksq) => kb > 1 ? BlockRows * KingBucketOf(ksq ^ Orient(p)) : 0;

    private static int KingSqOf(Position pos, int p) =>
        BitOperations.TrailingZeroCount(pos.BitboardOf((Color)p, PieceType.King));

    public static void OnPlay(Position pos, Color us, Move m)
    {
        var st = pos.NnueSt!;
        int cur = pos.Ply - st.BasePly;
        if (cur + 1 >= State.Cap)
            return;
        if (cur < 0)
        {
            if (cur + 1 >= 0) st.Valid[cur + 1] = 0;
            return;
        }
        byte srcValid = st.Valid[cur];
        if (srcValid == 0)
        {
            st.Valid[cur + 1] = 0;
            return;
        }

        int from = (int)m.From, to = (int)m.To;

        int crossed = -1;
        if (kb > 1)
        {
            bool kingMove = m.Flags is MoveFlags.OO or MoveFlags.OOO
                || ((int)pos.At(m.From) & 7) == (int)PieceType.King;
            if (kingMove)
            {
                int usI = (int)us;
                int e = us == Color.White ? (int)Square.e1 : (int)Square.e8;
                int kTo = m.Flags == MoveFlags.OO ? e + 2 : m.Flags == MoveFlags.OOO ? e - 2 : to;
                int kFrom = m.Flags is MoveFlags.OO or MoveFlags.OOO ? e : from;
                if (BucketBase(usI, kFrom) != BucketBase(usI, kTo))
                    crossed = usI;
            }
        }

        var src = Level(st, cur);
        var dst = Level(st, cur + 1);
        int n = st.L1;
        var w = ftW.AsSpan();
        int kW = kb > 1 ? KingSqOf(pos, 0) : 0, kB = kb > 1 ? KingSqOf(pos, 1) : 0;

        byte dstValid = 0;
        for (int p = 0; p < 2; p++)
        {
            if ((srcValid & (1 << p)) == 0 || p == crossed)
                continue;

            int om = Orient(p);
            int bb = BucketBase(p, p == 0 ? kW : kB);
            int Row(int pc, int sq) => (bb + ((pc >> 3) == p ? 0 : 384) + (pc & 7) * 64 + (sq ^ om)) * n;

            var half = dst.Slice(p * n, n);
            var srcHalf = src.Slice(p * n, n);
            switch (m.Flags)
            {
                case MoveFlags.Quiet:
                case MoveFlags.DoublePush:
                {
                    int pc = (int)pos.At(m.From);
                    AddSubFrom(half, srcHalf, w.Slice(Row(pc, to), n), w.Slice(Row(pc, from), n));
                    break;
                }
                case MoveFlags.OO:
                {
                    int k = (int)Types.MakePiece(us, PieceType.King), r = (int)Types.MakePiece(us, PieceType.Rook);
                    int e = us == Color.White ? (int)Square.e1 : (int)Square.e8;
                    AddAddSubSubFrom(half, srcHalf,
                        w.Slice(Row(k, e + 2), n), w.Slice(Row(r, e + 1), n),  // k e->g r h->f
                        w.Slice(Row(k, e), n), w.Slice(Row(r, e + 3), n));
                    break;
                }
                case MoveFlags.OOO:
                {
                    int k = (int)Types.MakePiece(us, PieceType.King), r = (int)Types.MakePiece(us, PieceType.Rook);
                    int e = us == Color.White ? (int)Square.e1 : (int)Square.e8;
                    AddAddSubSubFrom(half, srcHalf,
                        w.Slice(Row(k, e - 2), n), w.Slice(Row(r, e - 1), n),  // k e->c, r a->d
                        w.Slice(Row(k, e), n), w.Slice(Row(r, e - 4), n));
                    break;
                }
                case MoveFlags.EnPassant:
                {
                    int pc = (int)pos.At(m.From);
                    int capSq = to + (int)Types.RelativeDir(us, Direction.South);
                    AddSubSubFrom(half, srcHalf, w.Slice(Row(pc, to), n),
                        w.Slice(Row(pc, from), n),
                        w.Slice(Row((int)Types.MakePiece(us.Flip(), PieceType.Pawn), capSq), n));
                    break;
                }
                case MoveFlags.PrKnight: case MoveFlags.PrBishop: case MoveFlags.PrRook: case MoveFlags.PrQueen:
                {
                    int promo = (int)Types.MakePiece(us, (PieceType)(((int)m.Flags & 3) + 1));
                    AddSubFrom(half, srcHalf, w.Slice(Row(promo, to), n), w.Slice(Row((int)Types.MakePiece(us, PieceType.Pawn), from), n));
                    break;
                }
                case MoveFlags.PcKnight: case MoveFlags.PcBishop: case MoveFlags.PcRook: case MoveFlags.PcQueen:
                {
                    int promo = (int)Types.MakePiece(us, (PieceType)(((int)m.Flags & 3) + 1));
                    AddSubSubFrom(half, srcHalf, w.Slice(Row(promo, to), n),
                        w.Slice(Row((int)Types.MakePiece(us, PieceType.Pawn), from), n),
                        w.Slice(Row((int)pos.At(m.To), to), n));
                    break;
                }
                case MoveFlags.Capture:
                {
                    int pc = (int)pos.At(m.From);
                    AddSubSubFrom(half, srcHalf, w.Slice(Row(pc, to), n),
                        w.Slice(Row(pc, from), n),
                        w.Slice(Row((int)pos.At(m.To), to), n));
                    break;
                }
            }
            dstValid |= (byte)(1 << p);
        }
        st.Valid[cur + 1] = dstValid;
    }

    public static void OnPlayNull(Position pos)
    {
        var st = pos.NnueSt!;
        int cur = pos.Ply - st.BasePly;
        if (cur + 1 >= State.Cap) return;
        if (cur < 0) { if (cur + 1 >= 0) st.Valid[cur + 1] = 0; return; }
        byte v = st.Valid[cur];
        if (v == 0) { st.Valid[cur + 1] = 0; return; }
        Level(st, cur).CopyTo(Level(st, cur + 1));
        st.Valid[cur + 1] = v;
    }

    public static void OnUndo(Position pos)
    {
        
    }

    public static int Evaluate(Position pos)
    {
        var st = pos.NnueSt;
        if (st == null || st.L1 != l1) pos.NnueSt = st = new State(l1);
        int idx = pos.Ply - st.BasePly;
        if (idx < 0 || idx >= State.Cap)
        {
            st.BasePly = pos.Ply;
            idx = 0;
            Array.Clear(st.Valid);
        }
        byte v = st.Valid[idx];
        if (v != 3)
        {
            var lv = Level(st, idx);
            if ((v & 1) == 0) RefreshHalf(pos, st, 0, lv[..l1]);
            if ((v & 2) == 0) RefreshHalf(pos, st, 1, lv[l1..]);
            st.Valid[idx] = 3;
        }
        ulong occ = pos.AllPieces(Color.White) | pos.AllPieces(Color.Black);
        return Output(Level(st, idx), (int)pos.Turn, BitOperations.PopCount(occ));
    }

    // Stateless scratch eval
    public static int EvaluateScratch(Position pos)
    {
        Span<short> acc = stackalloc short[2 * l1];
        Refresh(pos, acc);
        ulong occ = pos.AllPieces(Color.White) | pos.AllPieces(Color.Black);
        return Output(acc, (int)pos.Turn, BitOperations.PopCount(occ));
    }

    // Finny table refresh
    private static void RefreshHalf(Position pos, State st, int p, Span<short> dst)
    {
        int n = st.L1;
        if (st.FinnyGen != loadGen)
        {
            for (int e2 = 0; e2 < 2 * KbMax; e2++) ftB.AsSpan().CopyTo(st.FinnyAcc.AsSpan(e2 * n, n));
            Array.Clear(st.FinnyBB);
            st.FinnyGen = loadGen;
        }
        int bkt = kb > 1 ? KingBucketOf(KingSqOf(pos, p) ^ Orient(p)) : 0;
        int e = p * KbMax + bkt;
        var cacc = st.FinnyAcc.AsSpan(e * n, n);
        var cbb = st.FinnyBB.AsSpan(e * 12, 12);
        int om = Orient(p), bb = kb > 1 ? BlockRows * bkt : 0;
        var w = ftW.AsSpan();
        for (int col = 0; col < 2; col++)
            for (int type = 0; type < 6; type++)
            {
                int i12 = col * 6 + type;
                ulong now = pos.BitboardOf((Color)col, (PieceType)type);
                ulong was = cbb[i12];
                if (now == was) continue;
                int rb = bb + (col == p ? 0 : 384) + type * 64;
                for (ulong d = now & ~was; d != 0; d &= d - 1)
                    Add(cacc, w.Slice((rb + (BitOperations.TrailingZeroCount(d) ^ om)) * n, n));
                for (ulong d = was & ~now; d != 0; d &= d - 1)
                    Sub(cacc, w.Slice((rb + (BitOperations.TrailingZeroCount(d) ^ om)) * n, n));
                cbb[i12] = now;
            }
        cacc.CopyTo(dst);
    }

    // Build both perspective halves (white half, black half) from the board.
    private static void Refresh(Position pos, Span<short> acc)
    {
        int n = l1;
        ftB.AsSpan().CopyTo(acc[..n]);
        ftB.AsSpan().CopyTo(acc[n..]);
        ulong occ = pos.AllPieces(Color.White) | pos.AllPieces(Color.Black);
        var w = ftW.AsSpan();
        int kW = kb > 1 ? KingSqOf(pos, 0) : 0, kB = kb > 1 ? KingSqOf(pos, 1) : 0;
        int omW = Orient(0), omB = Orient(1);
        int bbW = BucketBase(0, kW), bbB = BucketBase(1, kB);
        for (ulong b = occ; b != 0; b &= b - 1)
        {
            int sq = BitOperations.TrailingZeroCount(b);
            int pc = (int)pos.At((Square)sq);
            int type = pc & 7, col = pc >> 3;
            Add(acc[..n], w.Slice((bbW + (col == 0 ? 0 : 384) + type * 64 + (sq ^ omW)) * n, n));
            Add(acc[n..], w.Slice((bbB + (col == 1 ? 0 : 384) + type * 64 + (sq ^ omB)) * n, n));
        }
    }

    private static int Output(ReadOnlySpan<short> acc, int stm, int pieceCnt)
    {
        int n = l1;
        int bk = ob > 1 ? Math.Min((pieceCnt - 1) >> 2, ob - 1) : 0;
        int bankOff = bk * 2 * n;
        long sum = outBs[bk]
            + DotSq(acc.Slice(stm * n, n), outW.AsSpan(bankOff, n))
            + DotSq(acc.Slice((stm ^ 1) * n, n), outW.AsSpan(bankOff + n, n));
        int cp = (int)(sum * Scale / ((long)QA * QA * QO));
        return Math.Clamp(cp, -Eval.MateBound + 1, Eval.MateBound - 1);
    }

    private static long DotSq(ReadOnlySpan<short> acc, ReadOnlySpan<short> w) => useMadd ? DotSqMadd(acc, w) : DotSqWiden(acc, w);

    private static long DotSqMadd(ReadOnlySpan<short> acc, ReadOnlySpan<short> w)
    {
        ref short ra = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(acc);
        ref short rw = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(w);
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
        ref short ra = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(acc);
        ref short rw = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(w);
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
            if (++sinceDrain == 16) { sum += Vector.Sum(accum); accum = Vector<int>.Zero; sinceDrain = 0; }
        }
        sum += Vector.Sum(accum);
        for (; j < len; j++) { int c = Math.Clamp((int)acc[j], 0, QA); sum += c * c * w[j]; }
        return sum;
    }

    // acc += row (SIMD): add one feature's weight row, i.e. a piece appearing on a square.
    private static void Add(Span<short> acc, ReadOnlySpan<short> row)
    {
        ref short ra = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(acc);
        ref short rr = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(row);
        int v = Vector<short>.Count, len = acc.Length, j = 0;
        for (; j + v <= len; j += v)
            Vector.StoreUnsafe(Vector.LoadUnsafe(ref ra, (nuint)j) + Vector.LoadUnsafe(ref rr, (nuint)j), ref ra, (nuint)j);
        for (; j < len; j++) acc[j] += row[j];
    }

    // acc -= row (SIMD): remove one feature's weight row, i.e. a piece leaving a square.
    private static void Sub(Span<short> acc, ReadOnlySpan<short> row)
    {
        ref short ra = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(acc);
        ref short rr = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(row);
        int v = Vector<short>.Count, len = acc.Length, j = 0;
        for (; j + v <= len; j += v)
            Vector.StoreUnsafe(Vector.LoadUnsafe(ref ra, (nuint)j) - Vector.LoadUnsafe(ref rr, (nuint)j), ref ra, (nuint)j);
        for (; j < len; j++) acc[j] -= row[j];
    }

    private static void AddSubSubFrom(Span<short> dst, ReadOnlySpan<short> src, ReadOnlySpan<short> add, ReadOnlySpan<short> sub1, ReadOnlySpan<short> sub2)
    {
        ref short rd = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(dst);
        ref short rs = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(src);
        ref short r1 = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(add);
        ref short r2 = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(sub1);
        ref short r3 = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(sub2);
        int v = Vector<short>.Count, len = dst.Length, j = 0;
        for (; j + v <= len; j += v)
            Vector.StoreUnsafe(Vector.LoadUnsafe(ref rs, (nuint)j) + Vector.LoadUnsafe(ref r1, (nuint)j)
                             - Vector.LoadUnsafe(ref r2, (nuint)j) - Vector.LoadUnsafe(ref r3, (nuint)j), ref rd, (nuint)j);
        for (; j < len; j++)
            dst[j] = (short)(src[j] + add[j] - sub1[j] - sub2[j]);
    }

    private static void AddAddSubSubFrom(Span<short> dst, ReadOnlySpan<short> src, ReadOnlySpan<short> add1, ReadOnlySpan<short> add2, ReadOnlySpan<short> sub1, ReadOnlySpan<short> sub2)
    {
        ref short rd = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(dst);
        ref short rs = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(src);
        ref short r1 = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(add1);
        ref short r2 = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(add2);
        ref short r3 = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(sub1);
        ref short r4 = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(sub2);
        int v = Vector<short>.Count, len = dst.Length, j = 0;
        for (; j + v <= len; j += v)
            Vector.StoreUnsafe(Vector.LoadUnsafe(ref rs, (nuint)j) + Vector.LoadUnsafe(ref r1, (nuint)j) + Vector.LoadUnsafe(ref r2, (nuint)j)
                             - Vector.LoadUnsafe(ref r3, (nuint)j) - Vector.LoadUnsafe(ref r4, (nuint)j), ref rd, (nuint)j);
        for (; j < len; j++)
            dst[j] = (short)(src[j] + add1[j] + add2[j] - sub1[j] - sub2[j]);
    }

    private static void AddSubFrom(Span<short> dst, ReadOnlySpan<short> src, ReadOnlySpan<short> add, ReadOnlySpan<short> sub)
    {
        ref short rd = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(dst);
        ref short rs = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(src);
        ref short r1 = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(add);
        ref short r2 = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(sub);
        int v = Vector<short>.Count, len = dst.Length, j = 0;
        for (; j + v <= len; j += v)
            Vector.StoreUnsafe(Vector.LoadUnsafe(ref rs, (nuint)j) +
            Vector.LoadUnsafe(ref r1, (nuint)j) -
            Vector.LoadUnsafe(ref r2, (nuint)j), ref rd, (nuint)j);
        for (; j < len; j++)
            dst[j] = (short)(src[j] + add[j] - sub[j]);
    }
}
