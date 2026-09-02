namespace Zoomies.Solvers.Kbnk;

public static class KbnkTableManager
{
    private const int TableSize = 1 << 23;
    private static byte[]? mateDistanceTable;
    private static readonly Lock BuildLock = new();

    public static void Release()
    {
        lock (BuildLock)
        {
            Volatile.Write(ref mateDistanceTable, null);
        }
    }

    public static bool EnsureBuilt(int threads, out byte[] table)
    {
        if (Volatile.Read(ref mateDistanceTable) != null)
        {
            table = mateDistanceTable!;
            return false;
        }
        lock (BuildLock)
        {
            if (mateDistanceTable != null)
            {
                table = mateDistanceTable;
                return false;
            }
            Volatile.Write(ref mateDistanceTable, KbnkTableBuilder.Build(threads));
            table = mateDistanceTable;
            return true;
        }
    }

    public static int GetTableSize() => TableSize;
}
