using System.Globalization;
using Zoomies.Core;
using Zoomies.Engine;
using Zoomies;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

Tables.Initialize();
Zobrist.Initialize();

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
    }
}

Uci.Run();
