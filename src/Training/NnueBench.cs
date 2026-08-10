using System.Diagnostics;
using Zoomies.Core;
using Zoomies.Engine;

namespace Zoomies.Training;
public static class NnueBench
{
    public static int Cli(string[] args)
    {
        string nnue = "";
        int games = 4000, seed = 7, passes = 3;
        double evalRate = 1.0;
        for (int i = 1; i < args.Length - 1; i++)
            switch (args[i])
            {
                case "--nnue": nnue = args[i + 1]; break;
                case "--games": _ = int.TryParse(args[i + 1], out games); break;
                case "--seed": _ = int.TryParse(args[i + 1], out seed); break;
                case "--passes": _ = int.TryParse(args[i + 1], out passes); break;
                case "--evalrate": _ = double.TryParse(args[i + 1], out evalRate); break;
            }
        if (nnue.Length > 0) Nnue.Load(nnue);
        if (!Nnue.Loaded) { Console.WriteLine("nnuebench: no net loaded"); return 1; }
        Console.WriteLine($"nnuebench: {Path.GetFileName(Nnue.LoadedPath)} (L1={Nnue.L1})  {games} games  seed {seed}  evalrate {evalRate:F2}");

        var pos = new Position();
        Span<Move> moves = stackalloc Move[218];
        long sink = 0;
        double best = double.MaxValue;
        for (int pass = 0; pass < passes; pass++)
        {
            var rng = new Random(seed);
            var evalRng = new Random(seed + 1000); 
            long nodes = 0, evals = 0;
            var sw = Stopwatch.StartNew();
            for (int g = 0; g < games; g++)
            {
                Position.Set(Fens.Startpos, pos);
                for (int ply = 0; ply < 240; ply++)
                {
                    int n = Search.GenerateLegalMoves(pos, moves);
                    if (n == 0 || pos.IsFiftyMoveRule() || pos.IsRepetition()) break;
                    pos.Play(pos.Turn, moves[rng.Next(n)]);
                    nodes++;

                    if (ply == 0 || evalRng.NextDouble() < evalRate) { sink += Nnue.Evaluate(pos); evals++; }
                }
            }
            sw.Stop();
            double ns = sw.Elapsed.TotalMilliseconds * 1e6 / nodes;
            Console.WriteLine($"nnuebench: pass {pass}  {nodes:N0} nodes  {evals:N0} evals  {sw.ElapsedMilliseconds} ms  {ns:F0} ns/node");
            best = Math.Min(best, ns);
        }
        Console.WriteLine($"nnuebench: best {best:F0} ns/node  (sink {sink})");
        return 0;
    }
}
