namespace Zoomies.Engine;

internal static class Tune
{
    private static int P(string name, int def)
    {
        string? v = Environment.GetEnvironmentVariable(name);
        return v is not null && int.TryParse(v, out int x) ? x : def;
    }

    // ===========================================================================
    // reverse futility pruning
    // ===========================================================================
    public static readonly int RfpMaxDepth = P("ZT_RFP_DEPTH", 8);              // max depth
    public static readonly int RfpBase = P("ZT_RFP_BASE", 50);                  // margin per ply
    public static readonly int RfpImp = P("ZT_RFP_IMP", 0);                     // margin bump when improving
    public static readonly int RfpNonImp = P("ZT_RFP_NONIMP", 10);              // margin cut when not improving

    // ===========================================================================
    // null move pruning
    // ===========================================================================
    public static readonly int NmpMinDepth = P("ZT_NMP_MINDEPTH", 2);           // min depth
    public static readonly int NmpBase = P("ZT_NMP_BASE", 4);                   // base reduction
    public static readonly int NmpDiv = P("ZT_NMP_DIV", 9);                     // +1 reduction every N plies

    // ===========================================================================
    // futility pruning
    // ===========================================================================
    public static readonly int FutMaxDepth = P("ZT_FUT_DEPTH", 3);              // max depth
    public static readonly int FutBase = P("ZT_FUT_BASE", 138);                 // base margin
    public static readonly int FutSlope = P("ZT_FUT_SLOPE", 201);               // margin per ply
    public static readonly int FutImp = P("ZT_FUT_IMP", 98);                    // margin bump when improving
    public static readonly int FutNonImp = P("ZT_FUT_NONIMP", 57);              // margin cut when not improving

    // ===========================================================================
    // history pruning
    // ===========================================================================
    public static readonly int HistPruneMaxDepth = P("ZT_HISTP_DEPTH", 5);      // max depth
    public static readonly int HistPruneMult = P("ZT_HISTP_MULT", 3724);        // skip quiets below -N per ply

    // ===========================================================================
    // SEE pruning (captures)
    // ===========================================================================
    public static readonly int SeePruneMaxDepth = P("ZT_SEEP_DEPTH", 8);        // max depth
    public static readonly int SeePruneMult = P("ZT_SEEP_MULT", 98);            // SEE cutoff per ply

    // ===========================================================================
    // quiet SEE pruning
    // ===========================================================================
    public static readonly int QuietSeeMaxDepth = P("ZT_QSEE_DEPTH", 6);        // max depth
    public static readonly int QuietSeeMargin = P("ZT_QSEE_MARGIN", 65);        // SEE cutoff per ply

    // ===========================================================================
    // late move pruning
    // ===========================================================================
    public static readonly int LmpMaxDepth = P("ZT_LMP_DEPTH", 4);              // max depth (capped at 6)
    public static readonly int LmpImp = P("ZT_LMP_IMP", 4);                     // extra quiets when improving
    public static readonly int LmpNonImp = P("ZT_LMP_NONIMP", 1);               // fewer quiets when not improving

    // ===========================================================================
    // move ordering
    // ===========================================================================
    public static readonly int BadCapDemote = P("ZT_BADCAP_DEMOTE", 1);         // order SEE losing captures after quiets

    // ===========================================================================
    // late move reductions
    // ===========================================================================
    public static readonly int LmrBase = P("ZT_LMR_BASE", 56);                  // table base, /100
    public static readonly int LmrDiv = P("ZT_LMR_DIV", 271);                   // log-log divisor, /100
    public static readonly int LmrHistDiv = P("ZT_LMR_HISTDIV", 8373);          // history per reduction step
    public static readonly int LmrBadCap = P("ZT_LMR_BADCAP", 1);               // flat reduction for SEE losing captures
    public static readonly int DoDeeperMargin = P("ZT_DODEEPER", 50);           // re-search deeper
    // adaptive reduction inputs
    public static readonly int LmrTtPv = P("ZT_LMR_TTPV", 1);                   // reduce less on ttPv nodes
    public static readonly int LmrImp = P("ZT_LMR_IMP", 1);                     // reduce less when improving
    public static readonly int LmrNonImp = P("ZT_LMR_NONIMP", 1);               // reduce more when not improving
    public static readonly int LmrCutNode = P("ZT_LMR_CUTNODE", 1);             // reduce more on cut nodes
    public static readonly int LmrTtCapture = P("ZT_LMR_TTCAP", 1);             // reduce more when TT move captures
    public static readonly int LmrGivesCheck = P("ZT_LMR_CHECK", 1);            // reduce less for checking moves
    // Zoom (zone of overlapping momentum)
    public static readonly int Zoom = P("ZT_ZOOM", 1);                          // reduction magnitude inside zoom
    public static readonly int ZoomMinDepth = P("ZT_ZOOM_MINDEPTH", 3);         // min zone depth to draw
    public static readonly int ZoomCold = P("ZT_ZOOM_COLD", 0);                 // reduce stinkies
    public static readonly int ZoomFrom = P("ZT_ZOOM_FROM", 0);                 // zoom in on the from square

    // ===========================================================================
    // internal iterative reduction
    // ===========================================================================
    public static readonly int IirMinDepth = P("ZT_IIR_MINDEPTH", 4);           // min depth

    // ===========================================================================
    // check extensions
    // ===========================================================================
    public static readonly int CheckExtMaxEvasions = P("ZT_CHECKEXT_MAXEV", 8); // evasion threshold

    // ===========================================================================
    // singular extensions
    // ===========================================================================
    public static readonly int SingularMinDepth = P("ZT_SING_MINDEPTH", 4);     // min depth
    public static readonly int SingularMargin = P("ZT_SING_MARGIN", 4);         // beta margin per ply
    public static readonly int SingularTtSlack = P("ZT_SING_TTSLACK", 4);       // TT depth slack
    public static readonly int DoubleExtensionMargin = P("ZT_DEXT_MARGIN", 51); // extra fail-low for double ext
    public static readonly int DoubleExtensionLimit = P("ZT_DEXT_LIMIT", 10);   // max double exts per path

    // ===========================================================================
    // quiet history updates
    // ===========================================================================
    public static readonly int HistBonusCap = P("ZT_HIST_CAP", 928);            // max bonus
    public static readonly int HistBonusQuad = P("ZT_HIST_QUAD", 13);           // quadratic depth term
    public static readonly int HistBonusLin = P("ZT_HIST_LIN", 61);             // linear depth term

    // ===========================================================================
    // correction history
    // ===========================================================================
    public static readonly int CorrWeightCap = P("ZT_CORR_WCAP", 19);           // max update weight

    // ===========================================================================
    // aspiration windows
    // ===========================================================================
    public static readonly int AspMinDepth = P("ZT_ASP_MINDEPTH", 3);           // min depth for narrow window
    public static readonly int AspDelta = P("ZT_ASP_DELTA", 17);                // initial half-width
    public static readonly int AspWiden = P("ZT_ASP_WIDEN", 53);                // widen factor, /16

    // ===========================================================================
    // quiescence delta pruning
    // ===========================================================================
    public static readonly int DeltaMargin = P("ZT_DELTA_MARGIN", 303);         // margin over stand-pat + gain

    // ===========================================================================
    // time management
    // ===========================================================================
    public static readonly int TmScoreDropMargin = P("ZT_TM_SDROP_MARGIN", 8);  // cp of drop tolerated margin
    public static readonly int TmScoreDropSlope = P("ZT_TM_SDROP_SLOPE", 2);    // scale % per cp beyond margin
    public static readonly int TmScoreDropMax = P("ZT_TM_SDROP_MAX", 180);      // cap on the score drop factor
}
