using System.Buffers.Binary;
using System.Numerics;
using Zoomies.Core;

namespace Zoomies.Engine;

public static class Nnue
{
    public const int Rows = 768;
    private const int QA = 255, QO = 1024, Scale = 600;

    private static short[] ftB = [], ftW = [], outW = [];
    private static int outB, l1;

    public static bool Loaded { get; private set; }
    public static int L1 => l1;
    public static string LoadedPath { get; private set; } = "";

    public static void Load(string path)
    {
        var bytes = System.IO.File.ReadAllBytes(path);
        var s = bytes.AsSpan();
        if (bytes.Length < 20 || !s[..8].SequenceEqual("ZOO768A1"u8))
            throw new InvalidDataException($"{path}: not a ZOO768A1 net");
        uint ver = BinaryPrimitives.ReadUInt32LittleEndian(s[8..]);
        uint rows = BinaryPrimitives.ReadUInt32LittleEndian(s[12..]);
        int n = BinaryPrimitives.ReadUInt16LittleEndian(s[16..]);
        if (ver != 1 || rows != Rows) throw new InvalidDataException($"{path}: unsupported ZOO768A1 ver {ver} rows {rows}");
        long need = 20 + 2L * n + 2L * Rows * n + 4 + 2L * 2 * n;
        if (bytes.Length != need) throw new InvalidDataException($"{path}: size {bytes.Length} != expected {need} for l1={n}");

        var fb = new short[n];
        var fw = new short[Rows * n];
        var ow = new short[2 * n];
        int off = 20;
        for (int i = 0; i < n; i++, off += 2) fb[i] = BinaryPrimitives.ReadInt16LittleEndian(s[off..]);
        for (int i = 0; i < Rows * n; i++, off += 2) fw[i] = BinaryPrimitives.ReadInt16LittleEndian(s[off..]);
        int ob = BinaryPrimitives.ReadInt32LittleEndian(s[off..]); off += 4;
        for (int i = 0; i < 2 * n; i++, off += 2) ow[i] = BinaryPrimitives.ReadInt16LittleEndian(s[off..]);

        ftB = fb; ftW = fw; outW = ow; outB = ob; l1 = n;
        Loaded = true;
        LoadedPath = path;
    }

    public static void Unload() { Loaded = false; ftB = []; ftW = []; outW = []; l1 = 0; LoadedPath = ""; }

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

    // Imported from Imp
    public sealed class State(int l1)
    {
        public const int Cap = 192; 
        public readonly short[] Acc = new short[Cap * 2 * l1];
        public readonly int L1 = l1;
        public int BasePly = int.MinValue / 4;
        public int TopValid = -1;


        public void Reset() { BasePly = int.MinValue / 4; TopValid = -1; }
    }

    private static Span<short> Level(State st, int idx) => st.Acc.AsSpan(idx * 2 * st.L1, 2 * st.L1);

    // feature row base for perspective p (0=white, 1=black): (col==p ? us : them) + type*64 + oriented sq
    private static int RowBase(int p, int pc, int sq)
    {
        int type = pc & 7, col = pc >> 3;
        return ((col == p ? 0 : 384) + type * 64 + (sq ^ (56 * p))) * l1;
    }

    public static void OnPlay(Position pos, Color us, Move m)
    {
        var st = pos.NnueSt!;
        int cur = pos.Ply - st.BasePly;
        if (cur < 0 || st.TopValid != cur || cur + 1 >= State.Cap)
            return;

        var src = Level(st, cur);
        var dst = Level(st, cur + 1);
        src.CopyTo(dst);
        int n = st.L1;
        var w = ftW.AsSpan();
        int from = (int)m.From, to = (int)m.To;

        for (int p = 0; p < 2; p++)
        {
            var half = dst.Slice(p * n, n);
            switch (m.Flags)
            {
                case MoveFlags.Quiet:
                case MoveFlags.DoublePush:
                {
                    int pc = (int)pos.At(m.From);
                    AddSub(half, w.Slice(RowBase(p, pc, to), n), w.Slice(RowBase(p, pc, from), n));
                    break;
                }
                case MoveFlags.OO:
                {
                    int k = (int)Types.MakePiece(us, PieceType.King), r = (int)Types.MakePiece(us, PieceType.Rook);
                    int e = us == Color.White ? (int)Square.e1 : (int)Square.e8;
                    AddSub(half, w.Slice(RowBase(p, k, e + 2), n), w.Slice(RowBase(p, k, e), n));       // e->g
                    AddSub(half, w.Slice(RowBase(p, r, e + 1), n), w.Slice(RowBase(p, r, e + 3), n));   // h->f
                    break;
                }
                case MoveFlags.OOO:
                {
                    int k = (int)Types.MakePiece(us, PieceType.King), r = (int)Types.MakePiece(us, PieceType.Rook);
                    int e = us == Color.White ? (int)Square.e1 : (int)Square.e8;
                    AddSub(half, w.Slice(RowBase(p, k, e - 2), n), w.Slice(RowBase(p, k, e), n));       // e->c
                    AddSub(half, w.Slice(RowBase(p, r, e - 1), n), w.Slice(RowBase(p, r, e - 4), n));   // a->d
                    break;
                }
                case MoveFlags.EnPassant:
                {
                    int pc = (int)pos.At(m.From);
                    int capSq = to + (int)Types.RelativeDir(us, Direction.South);
                    AddSub(half, w.Slice(RowBase(p, pc, to), n), w.Slice(RowBase(p, pc, from), n));
                    Sub(half, w.Slice(RowBase(p, (int)Types.MakePiece(us.Flip(), PieceType.Pawn), capSq), n));
                    break;
                }
                case MoveFlags.PrKnight: case MoveFlags.PrBishop: case MoveFlags.PrRook: case MoveFlags.PrQueen:
                {
                    int promo = (int)Types.MakePiece(us, (PieceType)(((int)m.Flags & 3) + 1));
                    AddSub(half, w.Slice(RowBase(p, promo, to), n), w.Slice(RowBase(p, (int)Types.MakePiece(us, PieceType.Pawn), from), n));
                    break;
                }
                case MoveFlags.PcKnight: case MoveFlags.PcBishop: case MoveFlags.PcRook: case MoveFlags.PcQueen:
                {
                    int promo = (int)Types.MakePiece(us, (PieceType)(((int)m.Flags & 3) + 1));
                    AddSub(half, w.Slice(RowBase(p, promo, to), n), w.Slice(RowBase(p, (int)Types.MakePiece(us, PieceType.Pawn), from), n));
                    Sub(half, w.Slice(RowBase(p, (int)pos.At(m.To), to), n));
                    break;
                }
                case MoveFlags.Capture:
                {
                    int pc = (int)pos.At(m.From);
                    AddSub(half, w.Slice(RowBase(p, pc, to), n), w.Slice(RowBase(p, pc, from), n));
                    Sub(half, w.Slice(RowBase(p, (int)pos.At(m.To), to), n));
                    break;
                }
            }
        }
        st.TopValid = cur + 1;
    }

    public static void OnPlayNull(Position pos)
    {
        var st = pos.NnueSt!;
        int cur = pos.Ply - st.BasePly;
        if (cur < 0 || st.TopValid != cur || cur + 1 >= State.Cap) return;
        Level(st, cur).CopyTo(Level(st, cur + 1));
        st.TopValid = cur + 1;
    }

    public static void OnUndo(Position pos)
    {
        var st = pos.NnueSt!;
        int idx = pos.Ply - st.BasePly;
        if (st.TopValid > idx) st.TopValid = idx;
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
            st.TopValid = -1;
        }
        if (st.TopValid != idx)
        {
            Refresh(pos, Level(st, idx));
            st.TopValid = idx;
        }
        return Output(Level(st, idx), (int)pos.Turn);
    }

    // Stateless scratch eval
    public static int EvaluateScratch(Position pos)
    {
        Span<short> acc = stackalloc short[2 * l1];
        Refresh(pos, acc);
        return Output(acc, (int)pos.Turn);
    }

    // Build both perspective halves (white half, black half) from the board.
    private static void Refresh(Position pos, Span<short> acc)
    {
        int n = l1;
        ftB.AsSpan().CopyTo(acc[..n]);
        ftB.AsSpan().CopyTo(acc[n..]);
        ulong occ = pos.AllPieces(Color.White) | pos.AllPieces(Color.Black);
        var w = ftW.AsSpan();
        for (ulong b = occ; b != 0; b &= b - 1)
        {
            int sq = BitOperations.TrailingZeroCount(b);
            int pc = (int)pos.At((Square)sq);
            Add(acc[..n], w.Slice(RowBase(0, pc, sq), n));
            Add(acc[n..], w.Slice(RowBase(1, pc, sq), n));
        }
    }

    // Output layer: clipped-ReLU both perspective halves (side to move first), dot with the output weights, scale to centipawns.
    private static int Output(ReadOnlySpan<short> acc, int stm)
    {
        int n = l1;
        long sum = outB
            + Dot(acc.Slice(stm * n, n), outW.AsSpan(0, n))
            + Dot(acc.Slice((stm ^ 1) * n, n), outW.AsSpan(n, n));
        int cp = (int)(sum * Scale / (QA * (long)QO));
        return Math.Clamp(cp, -Eval.MateBound + 1, Eval.MateBound - 1);
    }

    // SIMD dot product of one accumulator half (clamped to 0..QA on the fly) with its output-weight half.
    private static long Dot(ReadOnlySpan<short> acc, ReadOnlySpan<short> w)
    {
        ref short ra = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(acc);
        ref short rw = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(w);
        var zero = Vector<short>.Zero;
        var qa = new Vector<short>((short)QA);
        var accum = Vector<int>.Zero;
        int v = Vector<short>.Count, len = acc.Length;
        int j = 0;
        for (; j + v <= len; j += v)
        {
            var a = Vector.Min(Vector.Max(Vector.LoadUnsafe(ref ra, (nuint)j), zero), qa);
            Vector.Widen(a, out Vector<int> a0, out Vector<int> a1);
            Vector.Widen(Vector.LoadUnsafe(ref rw, (nuint)j), out Vector<int> w0, out Vector<int> w1);
            accum += a0 * w0 + a1 * w1;
        }
        long sum = Vector.Sum(accum);
        for (; j < len; j++) sum += Math.Clamp((int)acc[j], 0, QA) * w[j];
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

    // acc += add - sub in one SIMD pass: a piece moving from one square to another.
    private static void AddSub(Span<short> acc, ReadOnlySpan<short> add, ReadOnlySpan<short> sub)
    {
        ref short ra = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(acc);
        ref short r1 = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(add);
        ref short r2 = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(sub);
        int v = Vector<short>.Count, len = acc.Length, j = 0;
        for (; j + v <= len; j += v)
            Vector.StoreUnsafe(Vector.LoadUnsafe(ref ra, (nuint)j) + Vector.LoadUnsafe(ref r1, (nuint)j) - Vector.LoadUnsafe(ref r2, (nuint)j), ref ra, (nuint)j);
        for (; j < len; j++) acc[j] += (short)(add[j] - sub[j]);
    }
}
