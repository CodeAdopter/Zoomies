using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Zoomies.Core;
using Zoomies.Engine;

namespace Zoomies.Training;

// Imported from Imp
public static class NnueCheck
{
    public static int Cli(string[] args)
    {
        string nnue = "", data = "";
        int n = 4096;
        for (int i = 1; i < args.Length - 1; i++)
            switch (args[i])
            {
                case "--nnue": nnue = args[i + 1]; break;
                case "--data": data = args[i + 1]; break;
                case "--n": _ = int.TryParse(args[i + 1], out n); break;
            }
        if (nnue.Length == 0 || data.Length == 0)
        {
            Console.WriteLine("usage: zoomies nnuecheck --nnue NET.nnue --data RELABELED.impb [--n 4096]");
            return 1;
        }

        Nnue.Load(nnue);
        Console.WriteLine($"nnuecheck: {Path.GetFileName(nnue)} (L1={Nnue.L1})  vs  {Path.GetFileName(data)}");

        var pos = new Position();
        var rec = new byte[PackedRecord.Size];
        using var fs = new FileStream(data, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        double sumAbs = 0;
        int worst = 0, worstAt = -1, count = 0;
        for (; count < n; count++)
        {
            int got = fs.Read(rec, 0, rec.Length);
            if (got < rec.Length) break;
            Position.Set(RecordToFen(rec), pos);
            int stmCp = Nnue.Evaluate(pos);
            int white = pos.Turn == Color.White ? stmCp : -stmCp;
            int refWhite = BinaryPrimitives.ReadInt16LittleEndian(rec.AsSpan(24));
            int d = Math.Abs(white - refWhite);
            sumAbs += d;
            if (d > worst) { worst = d; worstAt = count; }
        }
        if (count == 0) { Console.WriteLine("nnuecheck: no records"); return 1; }

        double mae = sumAbs / count;
        bool pass = mae < 8.0 && worst <= 60;
        Console.WriteLine($"nnuecheck: {count} positions  MAE {mae:F2}cp  max {worst}cp{(worstAt >= 0 ? $" @rec {worstAt}" : "")}  {(pass ? "PASS" : "FAIL")}");
        return pass ? 0 : 1;
    }

    private static string RecordToFen(ReadOnlySpan<byte> rec)
    {
        Span<int> board = stackalloc int[64];
        board.Clear();
        ulong occ = BinaryPrimitives.ReadUInt64LittleEndian(rec);
        int k = 0;
        for (ulong b = occ; b != 0; b &= b - 1, k++)
        {
            int sq = BitOperations.TrailingZeroCount(b);
            int nib = (rec[8 + (k >> 1)] >> ((k & 1) * 4)) & 0xF;
            board[sq] = nib + 1;
        }
        const string P = "PNBRQK";
        var sb = new StringBuilder();
        for (int r = 7; r >= 0; r--)
        {
            int empty = 0;
            for (int f = 0; f < 8; f++)
            {
                int code = board[r * 8 + f];
                if (code == 0) { empty++; continue; }
                if (empty > 0) { sb.Append(empty); empty = 0; }
                code -= 1;
                char c = P[code & 7];
                sb.Append((code >> 3) == 0 ? c : char.ToLowerInvariant(c));
            }
            if (empty > 0) sb.Append(empty);
            if (r > 0) sb.Append('/');
        }
        sb.Append(rec[27] == 0 ? " w " : " b ").Append("- - 0 1");
        return sb.ToString();
    }
}
