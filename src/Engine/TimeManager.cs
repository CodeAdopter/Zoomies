namespace Zoomies.Engine;

public static class TimeManager
{
    public static long MoveOverheadMilliseconds = 30;
    public const int DefaultSoftDivisor = 20;

    public static SearchLimits Allocate(long clockMilliseconds, long incrementMilliseconds, int movesToGo)
    {
        long remaining = Math.Max(1, clockMilliseconds - Math.Max(0, MoveOverheadMilliseconds));
        long soft, hardCap;

        if (movesToGo > 0)
        {
            soft = remaining / (movesToGo + 1) + incrementMilliseconds / 2;
            hardCap = movesToGo == 1 ? remaining * 3 / 4 : remaining / 3;
        }
        else
        {
            // soft is the target thinking time;
            // hard is the absolute time limit
            soft = remaining / DefaultSoftDivisor + incrementMilliseconds / 2;
            hardCap = remaining / 3;
        }

        soft = Math.Clamp(soft, 1, remaining);
        long hard = Math.Clamp(Math.Min(soft * 4, hardCap), soft, remaining);
        return new SearchLimits { SoftTimeMilliseconds = soft, MoveTimeMilliseconds = hard };
    }
}
