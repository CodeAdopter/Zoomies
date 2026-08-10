using System.Globalization;
using Zoomies.Core;
using Zoomies.Engine;
using Zoomies;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

Tables.Initialize();
Zobrist.Initialize();

// champion autoload
Nnue.AutoLoad();
for (int i = 0; i < args.Length - 1; i++)
    if (args[i] == "--nnue")
    {
        Nnue.Load(args[i + 1]);
        Console.WriteLine($"info string nnue loaded from {args[i + 1]} (L1={Nnue.L1})");
    }

if (args.Length > 0)
{
    switch (args[0])
    {
        case "bench":
            int depth = args.Length > 1 &&
                int.TryParse(args[1], out int requestedDepth)
                    ? requestedDepth
                    : 9;
            Uci.RunBench(depth);
            return;

        case "perft":
            Perft.Suite();
            return;

        case "datagen":
            Environment.Exit(Zoomies.Training.Datagen.Cli(args));
            return;

        case "dgbench":
            Environment.Exit(Zoomies.Training.Datagen.BenchCli(args));
            return;

        case "nnuecheck":
            Environment.Exit(Zoomies.Training.NnueCheck.Cli(args));
            return;

        case "nnuebench":
            Environment.Exit(Zoomies.Training.NnueBench.Cli(args));
            return;

        case "nnueverify":
            Environment.Exit(Zoomies.Training.NnueVerify4.Cli(args));
            return;

        case "eval" when args.Length >= 3:
        {
            Nnue.Load(args[1]);
            var pos = new Position();
            foreach (var fen in System.IO.File.ReadLines(args[2]))
            {
                if (string.IsNullOrWhiteSpace(fen)) continue;
                Position.Set(fen, pos);
                Console.WriteLine(Nnue.EvaluateScratch(pos));
            }
            return;
        }
    }
}

Uci.Run();
