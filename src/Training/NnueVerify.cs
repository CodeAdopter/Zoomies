using Zoomies.Core;
using Zoomies.Engine;

namespace Zoomies.Training;

// Plays random games and at every node compares the incremental Nnue.Evaluate against the
// stateless scratch rebuild: same int math, so they must be bit-equal; any difference is a
// delta bug in the accumulator, the kind that never crashes but silently costs Elo.
// Playouts hit castling/EP/promotions, with null-move and PlayPerft probes and a full undo-walk.
public static class NnueVerify4
{
    public static int Cli(string[] args)
    {
        string nnue = "";
        int games = 2000, seed = 1;
        for (int i = 1; i < args.Length - 1; i++)
            switch (args[i])
            {
                case "--nnue": nnue = args[i + 1]; break;
                case "--games": _ = int.TryParse(args[i + 1], out games); break;
                case "--seed": _ = int.TryParse(args[i + 1], out seed); break;
            }
        if (nnue.Length > 0) Nnue.Load(nnue);
        if (!Nnue.Loaded) { Console.WriteLine("nnueverify: no net loaded"); return 1; }
        Console.WriteLine($"nnueverify: {Path.GetFileName(Nnue.LoadedPath)} (L1={Nnue.L1})  {games} random games  seed {seed}");

        var rng = new Random(seed);
        var pos = new Position();
        Span<Move> moves = stackalloc Move[218];
        var line = new Move[512];
        long nodes = 0, mismatches = 0;
        long castles = 0, eps = 0, promos = 0, nulls = 0, perfts = 0;

        for (int g = 0; g < games; g++)
        {
            Position.Set(Fens.Startpos, pos);
            int plies = 0;
            // anchor the accumulator base mid-line sometimes (a root-in-check search does this:
            // first eval happens plies deep, and the undo-walk later crosses below the anchor)
            int evalFrom = rng.Next(3) == 0 ? rng.Next(12) : 0;
            for (int ply = 0; ply < 240; ply++)
            {
                int n = Search.GenerateLegalMoves(pos, moves);
                if (n == 0 || pos.IsFiftyMoveRule() || pos.IsRepetition()) break;

                // occasionally probe a null-move make/unmake at this node (not when in check)
                if (rng.Next(8) == 0 && !pos.InCheck(pos.Turn))
                {
                    pos.MakeNullMove();
                    nulls++;
                    if (!Check(pos, ref mismatches, "after null")) Report(pos, g, ply);
                    pos.UnmakeNullMove();
                    if (!Check(pos, ref mismatches, "after unnull")) Report(pos, g, ply);
                }

                Move m = moves[rng.Next(n)];
                switch (m.Flags)
                {
                    case MoveFlags.OO or MoveFlags.OOO: castles++; break;
                    case MoveFlags.EnPassant: eps++; break;
                    default: if (((int)m.Flags & (int)MoveFlags.Promotions) != 0) promos++; break;
                }

                // probe the qsearch fast path: PlayPerft/UndoPerft must keep the chain in sync too
                if (rng.Next(4) == 0)
                {
                    Move pm = moves[rng.Next(n)];
                    pos.PlayPerft(pos.Turn, pm);
                    perfts++;
                    if (!Check(pos, ref mismatches, "after playperft")) Report(pos, g, ply);
                    pos.UndoPerft(pos.Turn.Flip(), pm);
                    if (!Check(pos, ref mismatches, "after undoperft")) Report(pos, g, ply);
                }

                line[plies++] = m;
                pos.Play(pos.Turn, m);
                nodes++;
                if (ply >= evalFrom && !Check(pos, ref mismatches, "after play")) Report(pos, g, ply);
            }

            // unwind the whole game, rechecking every level on the way down
            for (int k = plies - 1; k >= 0; k--)
            {
                pos.Undo(pos.Turn.Flip(), line[k]);
                if (!Check(pos, ref mismatches, "after undo")) Report(pos, g, k);
            }
        }

        Console.WriteLine($"nnueverify: {nodes:N0} nodes  ({castles:N0} castles, {eps:N0} ep, {promos:N0} promos, {nulls:N0} null-probes, {perfts:N0} perft-probes)");
        Console.WriteLine($"nnueverify: {mismatches} mismatches  {(mismatches == 0 ? "PASS" : "FAIL")}");
        return mismatches == 0 ? 0 : 1;
    }

    private static bool Check(Position pos, ref long mismatches, string where)
    {
        int inc = Nnue.Evaluate(pos);
        int scr = Nnue.EvaluateScratch(pos);
        if (inc == scr) return true;
        mismatches++;
        if (mismatches <= 5) Console.WriteLine($"nnueverify: MISMATCH {where}  inc {inc}  scratch {scr}");
        return false;
    }

    private static void Report(Position pos, int game, int ply)
    {
        Console.WriteLine($"nnueverify:   at game {game} ply {ply}:");
        Console.WriteLine(pos.Fen());
    }
}
