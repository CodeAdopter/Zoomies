using System.Reflection;
using System.Text.Json;

namespace Zoomies.Engine;

[AttributeUsage(AttributeTargets.Field)]
internal sealed class TuneAttribute(
    string name, int min, int max, int step, string group, bool tunable = true)
    : Attribute
{
    public readonly string Name = name;
    public readonly int Min = min;
    public readonly int Max = max;
    public readonly int Step = step;
    public readonly string Group = group;
    public readonly bool Tunable = tunable;
}

[AttributeUsage(AttributeTargets.Field)]
internal sealed class HyperTuneAttribute : Attribute { }

internal static class Tune
{
    // ===========================================================================
    // reverse futility pruning
    // ===========================================================================
    [Tune("ZT_RFP_DEPTH", 4, 12, 1, "rfp")] public static int RfpMaxDepth = 7;
    [Tune("ZT_RFP_BASE", 25, 100, 4, "rfp")] public static int RfpBase = 49;
    [Tune("ZT_RFP_IMP", -20, 60, 4, "rfp")] public static int RfpImp = -4;
    [Tune("ZT_RFP_NONIMP", -20, 60, 4, "rfp")] public static int RfpNonImp = 6;

    // ===========================================================================
    // null move pruning
    // ===========================================================================
    [Tune("ZT_NMP_MINDEPTH", 1, 5, 1, "nmp")] public static int NmpMinDepth = 2;
    [Tune("ZT_NMP_BASE", 2, 6, 1, "nmp")] public static int NmpBase = 4;
    [Tune("ZT_NMP_DIV", 4, 16, 1, "nmp")] public static int NmpDiv = 9;

    // ===========================================================================
    // futility pruning
    // ===========================================================================
    [Tune("ZT_FUT_DEPTH", 2, 8, 1, "futility")] public static int FutMaxDepth = 3;
    [Tune("ZT_FUT_BASE", 60, 280, 8, "futility")] public static int FutBase = 131;
    [Tune("ZT_FUT_SLOPE", 80, 320, 10, "futility")] public static int FutSlope = 197;
    [Tune("ZT_FUT_IMP", 0, 200, 8, "futility")] public static int FutImp = 96;
    [Tune("ZT_FUT_NONIMP", 0, 160, 8, "futility")] public static int FutNonImp = 49;

    // ===========================================================================
    // history pruning
    // ===========================================================================
    [Tune("ZT_HISTP_DEPTH", 2, 8, 1, "histprune")] public static int HistPruneMaxDepth = 5;
    [Tune("ZT_HISTP_MULT", 1500, 7000, 200, "histprune")] public static int HistPruneMult = 3710;

    // ===========================================================================
    // SEE pruning (captures)
    // ===========================================================================
    [Tune("ZT_SEEP_DEPTH", 4, 12, 1, "seeprune")] public static int SeePruneMaxDepth = 8;
    [Tune("ZT_SEEP_MULT", 40, 180, 6, "seeprune")] public static int SeePruneMult = 99;

    // ===========================================================================
    // quiet SEE pruning
    // ===========================================================================
    [Tune("ZT_QSEE_DEPTH", 3, 10, 1, "seeprune")] public static int QuietSeeMaxDepth = 6;
    [Tune("ZT_QSEE_MARGIN", 20, 140, 6, "seeprune")] public static int QuietSeeMargin = 67;

    // ===========================================================================
    // late move pruning
    // ===========================================================================
    [Tune("ZT_LMP_DEPTH", 2, 6, 1, "lmp")] public static int LmpMaxDepth = 4;
    [Tune("ZT_LMP_IMP", 0, 10, 1, "lmp")] public static int LmpImp = 4;
    [Tune("ZT_LMP_NONIMP", 0, 6, 1, "lmp")] public static int LmpNonImp = 1;

    // ===========================================================================
    // late move reductions
    // ===========================================================================
    [Tune("ZT_LMR_BASE", 20, 120, 5, "lmr")] public static int LmrBase = 56;
    [Tune("ZT_LMR_DIV", 150, 400, 10, "lmr")] public static int LmrDiv = 263;
    [Tune("ZT_LMR_HISTDIV", 4000, 16000, 400, "lmr")] public static int LmrHistDiv = 8250;
    [Tune("ZT_DODEEPER", 20, 120, 5, "lmr")] public static int DoDeeperMargin = 47;

    // ===========================================================================
    // adaptive reduction inputs
    // ===========================================================================
    [Tune("ZT_LMR_TTPV", 0, 2, 1, "lmr-flags")] public static int LmrTtPv = 1;
    [Tune("ZT_LMR_CUTNODE", 0, 2, 1, "lmr-flags")] public static int LmrCutNode = 2;
    [Tune("ZT_LMR_TTCAP", 0, 2, 1, "lmr-flags")] public static int LmrTtCapture = 1;
    [Tune("ZT_LMR_PAWN", -3, 3, 1, "lmr-flags")] public static int LmrPawn = -1;

    // ===========================================================================
    // zoom
    // ===========================================================================
    [Tune("ZT_ZOOM", 0, 3, 1, "zoom")] public static int Zoom = 1;
    [Tune("ZT_ZOOM_MINDEPTH", 4, 14, 1, "zoom")] public static int ZoomMinDepth = 9;

    // ===========================================================================
    // mutual attack ordering
    // ===========================================================================
    [Tune("ZT_MAO_FLAT", 0, 300, 12, "zoom")] public static int MaoFlat = 99;
    [Tune("ZT_MAO_WEIGHT", 0, 400, 15, "zoom")] public static int MaoWeight = 147;

    // ===========================================================================
    // internal iterative reduction
    // ===========================================================================
    [Tune("ZT_IIR_MINDEPTH", 2, 8, 1, "iir")] public static int IirMinDepth = 3;

    // ===========================================================================
    // check extensions
    // ===========================================================================
    [Tune("ZT_CHECKEXT_MAXEV", 2, 20, 1, "extensions")] public static int CheckExtMaxEvasions = 8;

    // ===========================================================================
    // singular extensions
    // ===========================================================================
    [Tune("ZT_SING_MINDEPTH", 4, 10, 1, "singular")] public static int SingularMinDepth = 4;
    [Tune("ZT_SING_MARGIN", 1, 12, 1, "singular")] public static int SingularMargin = 3;
    [Tune("ZT_SING_TTSLACK", 0, 8, 1, "singular")] public static int SingularTtSlack = 4;
    [Tune("ZT_DEXT_MARGIN", 10, 120, 5, "singular")] public static int DoubleExtensionMargin = 54;
    [Tune("ZT_DEXT_LIMIT", 2, 20, 1, "singular")] public static int DoubleExtensionLimit = 10;

    // ===========================================================================
    // quiet history updates
    // ===========================================================================
    [Tune("ZT_HIST_CAP", 400, 2400, 60, "history")] public static int HistBonusCap = 911;
    [Tune("ZT_HIST_QUAD", 2, 32, 1, "history")] public static int HistBonusQuad = 12;
    [Tune("ZT_HIST_LIN", 10, 160, 6, "history")] public static int HistBonusLin = 60;

    // ===========================================================================
    // correction history
    // ===========================================================================
    [Tune("ZT_CORR_WCAP", 8, 40, 1, "corrhist")] public static int CorrWeightCap = 19;

    // ===========================================================================
    // aspiration windows
    // ===========================================================================
    [Tune("ZT_ASP_MINDEPTH", 2, 8, 1, "aspiration")] public static int AspMinDepth = 3;
    [Tune("ZT_ASP_DELTA", 6, 40, 2, "aspiration")] public static int AspDelta = 15;
    [Tune("ZT_ASP_WIDEN", 20, 96, 3, "aspiration")] public static int AspWiden = 53;

    // ===========================================================================
    // quiescence delta pruning
    // ===========================================================================
    [Tune("ZT_DELTA_MARGIN", 120, 600, 16, "qsearch")] public static int DeltaMargin = 294;

    // ===========================================================================
    // time management
    // ===========================================================================
    [Tune("ZT_TM_SDROP_MARGIN", 0, 30, 2, "tm")] public static int TmScoreDropMargin = 8;
    [Tune("ZT_TM_SDROP_SLOPE", 1, 8, 1, "tm")] public static int TmScoreDropSlope = 3;
    [Tune("ZT_TM_SDROP_MAX", 110, 300, 8, "tm")] public static int TmScoreDropMax = 189;

    // ===========================================================================
    // minor outpost
    // ===========================================================================
    [Tune("ZT_OUTPOST", 0, 4, 1, "outpost")] public static int Outpost = 2;

    // ===========================================================================
    // nnue output scale
    // ===========================================================================
    [Tune("ZT_SCALE", 450, 650, 1, "eval")] public static int NnueScale = 550;

    // ===========================================================================
    // locked
    // ===========================================================================
    [Tune("ZT_BADCAP_DEMOTE", 0, 1, 1, "ordering", tunable: false)] public static int BadCapDemote = 1;
    [Tune("ZT_COUNTERMOVE", 0, 1, 1, "ordering", tunable: false)] public static int CounterMove = 1;
    [Tune("ZT_ZOOM_FROM", 0, 1, 1, "zoom", tunable: false)] public static int ZoomFrom = 1;
    [Tune("ZT_SEE_EARLYEXIT", 0, 1, 1, "see", tunable: false)] public static int SeeEarlyExit = 1;
    [Tune("ZT_INSTAMOVE", 0, 1, 1, "tm", tunable: false)] public static int Instamove = 1;

    // ===========================================================================
    // unshelved
    // ===========================================================================
    [Tune("ZT_R50_DAMP", 64, 512, 16, "eval")] public static int R50Damp = 258;
    [Tune("ZT_ASP_FH_REDUCE", 0, 4, 1, "aspiration")] public static int AspFailHighReduce = 2;
    [Tune("ZT_ROOT_NODEORD", 0, 32768, 1024, "ordering")] public static int RootNodeOrd = 8967;

    // ===========================================================================
    // disabled
    // ===========================================================================
    [Tune("ZT_ZOOM_COLD", 0, 1, 1, "zoom")] public static int ZoomCold = 0;
    [Tune("ZT_SEE_VERIFY", 0, 1, 1, "see")] public static int SeeVerify = 0;
    [Tune("ZT_LMR_BADCAP", 0, 3, 1, "lmr")] public static int LmrBadCap = 0;
    [Tune("ZT_LMR_IMP", 0, 2, 1, "lmr-flags")] public static int LmrImp = 0;
    [Tune("ZT_LMR_NONIMP", 0, 2, 1, "lmr-flags")] public static int LmrNonImp = 0;
    [Tune("ZT_LMR_CHECK", 0, 2, 1, "lmr-flags")] public static int LmrGivesCheck = 0;

    public readonly record struct Entry(FieldInfo Field, string Name, int Default, int Min, int Max, int Step, string Group, bool Tunable, bool HyperTune);
    private static readonly Entry[] Entries;
    private static readonly Dictionary<string, int> Index;
    public static IReadOnlyList<Entry> All => Entries;

    static Tune()
    {
        var entries = new List<Entry>();
        foreach (FieldInfo field in typeof(Tune).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var attr = field.GetCustomAttribute<TuneAttribute>();

            if (attr is null) 
                continue;

            bool hyper = field.GetCustomAttribute<HyperTuneAttribute>() is not null;
            entries.Add(new Entry(field, attr.Name, (int)field.GetValue(null)!, attr.Min, attr.Max, attr.Step, attr.Group, attr.Tunable, hyper));

            string? v = Environment.GetEnvironmentVariable(attr.Name);
            if (v is not null && int.TryParse(v, out int x))
                field.SetValue(null, x);
        }

        Entries = [.. entries];
        Index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < Entries.Length; i++)
            Index[Entries[i].Name] = i;
    }


    public static bool TrySet(string name, string? value)
    {
        if (!Index.TryGetValue(name, out int i) ||
            !int.TryParse(value, out int parsed))
            return false;

        Entry e = Entries[i];
        e.Field.SetValue(null, Math.Clamp(parsed, e.Min, e.Max));
        Refresh();
        return true;
    }

    public static void Refresh()
    {
        Pruning.Refresh();
        See.Refresh();
        Search.RefreshTune();
        Eval.Refresh();
        Nnue.RefreshTune();
    }

    private sealed record DumpEntry(string Name, int Value, int Default, int Min, int Max, int Step, string Group, bool Tunable, bool HyperTune);

    private static readonly JsonSerializerOptions DumpOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static void Dump(TextWriter writer)
    {
        DumpEntry[] entries = [.. Entries.Select(e => new DumpEntry(e.Name, (int)e.Field.GetValue(null)!, e.Default, e.Min, e.Max, e.Step, e.Group, e.Tunable, e.HyperTune))];
        writer.WriteLine(JsonSerializer.Serialize(entries, DumpOptions));
    }
}