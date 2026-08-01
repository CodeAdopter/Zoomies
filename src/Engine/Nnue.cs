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

    public static int Evaluate(Position pos)
    {
        int n = l1;
        Span<short> acc = stackalloc short[2 * n];
        ftB.AsSpan().CopyTo(acc[..n]);
        ftB.AsSpan().CopyTo(acc[n..]);

        int stm = (int)pos.Turn;
        int omS = 56 * stm, omN = 56 ^ omS;
        ulong occ = pos.AllPieces(Color.White) | pos.AllPieces(Color.Black);
        var w = ftW.AsSpan();
        for (ulong b = occ; b != 0; b &= b - 1)
        {
            int sq = BitOperations.TrailingZeroCount(b);
            int pc = (int)pos.At((Square)sq);
            int type = pc & 7, col = pc >> 3;
            int idxS = (col == stm ? 0 : 384) + type * 64 + (sq ^ omS);
            int idxN = (col != stm ? 0 : 384) + type * 64 + (sq ^ omN);
            AddRow(acc[..n], w.Slice(idxS * n, n));
            AddRow(acc[n..], w.Slice(idxN * n, n));
        }

        long sum = outB;
        for (int j = 0; j < 2 * n; j++)
        {
            int a = acc[j];
            if (a < 0) a = 0; else if (a > QA) a = QA;
            sum += (long)outW[j] * a;
        }
        int cp = (int)(sum * Scale / (QA * (long)QO));
        return Math.Clamp(cp, -Eval.MateBound + 1, Eval.MateBound - 1);
    }

    private static void AddRow(Span<short> acc, ReadOnlySpan<short> row)
    {
        int v = Vector<short>.Count, len = acc.Length, j = 0;
        for (; j + v <= len; j += v)
            (new Vector<short>(acc[j..]) + new Vector<short>(row[j..])).CopyTo(acc[j..]);
        for (; j < len; j++) acc[j] += row[j];
    }
}
