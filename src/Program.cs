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

        case "nnueverify":
            Environment.Exit(Zoomies.Training.NnueVerify4.Cli(args));
            return;
    }
}

Uci.Run();
