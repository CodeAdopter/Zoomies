namespace Zoomies.Training;

// Imported from Imp
public sealed class DatagenOptions
{
    public int Threads = Environment.ProcessorCount;
    public long NodesPerMove = 5000;
    public long MaxGames;
    public long MaxPositions;
    public string OutPath = "datagen.impb";
    public int BookPlies = 8;
    public int Seed;
    public int TtMb = 4;
    public bool AdaptiveNodes = true;
    public int RecordEvery = 1;
    public string Eval = "";

    public static DatagenOptions Parse(string[] args)
    {
        var o = new DatagenOptions();
        if (Array.IndexOf(args, "--no-adaptive-nodes") >= 0) o.AdaptiveNodes = false;
        for (int i = 1; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--threads": _ = int.TryParse(args[i + 1], out o.Threads); break;
                case "--nodes": _ = long.TryParse(args[i + 1], out o.NodesPerMove); break;
                case "--games": _ = long.TryParse(args[i + 1], out o.MaxGames); break;
                case "--positions": _ = long.TryParse(args[i + 1], out o.MaxPositions); break;
                case "--out": o.OutPath = args[i + 1]; break;
                case "--book-plies": _ = int.TryParse(args[i + 1], out o.BookPlies); break;
                case "--seed": _ = int.TryParse(args[i + 1], out o.Seed); break;
                case "--tt-mb": _ = int.TryParse(args[i + 1], out o.TtMb); break;
                case "--record-every": _ = int.TryParse(args[i + 1], out o.RecordEvery); break;
                case "--eval": o.Eval = args[i + 1]; break;
            }
        }
        o.Threads = Math.Clamp(o.Threads, 1, 512);
        o.NodesPerMove = Math.Max(16, o.NodesPerMove);
        o.BookPlies = Math.Clamp(o.BookPlies, 0, 40);
        o.TtMb = Math.Clamp(o.TtMb, 1, 1024);
        o.RecordEvery = Math.Clamp(o.RecordEvery, 1, 16);
        return o;
    }
}
