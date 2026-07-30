namespace Zoomies.Engine;

public static class Log
{
    public static string Nps(long nodes, long ms)
    {
        double nps = ms > 0 ? nodes * 1000.0 / ms : nodes;
        return nps >= 1_000_000 ? $"{nps / 1e6:F2} Mnps" : $"{nps / 1e3:F0} knps";
    }
}
