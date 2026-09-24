using Zoomies.Core;
using Zoomies.Engine;

namespace Zoomies.Training;

public static class HashCheck
{
    public static int Cli(string[] args)
    {
        int depth = 4;
        for (int i = 1; i < args.Length - 1; i++)
            if (args[i] == "--depth") _ = int.TryParse(args[i + 1], out depth);

        string[] fens =
        [
            Fens.Startpos, Fens.Kiwipete, Fens.Endgame, Fens.Tactical, Fens.Promotions, Fens.Midgame,
            "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R b KQkq -",
            "8/2p5/3p4/KP5r/1R2Pp1k/8/6P1/8 b - e3",
        ];

        long nodes = 0, failures = 0;
        var position = new Position();
        var scratch = new Position();
        foreach (string fen in fens)
        {
            Position.Set(fen, position);
            Walk(position, scratch, depth, ref nodes, ref failures);
        }

        Console.WriteLine($"hashcheck: {nodes:N0} nodes depth {depth} {failures:N0} mismatches {(failures == 0 ? "PASS" : "FAIL")}");
        return failures == 0 ? 0 : 1;
    }

    private static void Fail(ref long failures, string what, Position position)
    {
        if (++failures <= 5) Console.WriteLine($"hashcheck: MISMATCH {what} at {position.Fen()}");
    }

    private static void Walk(Position position, Position scratch, int depth, ref long nodes, ref long failures)
    {
        nodes++;
        Position.Set(position.Fen(), scratch);
        if (scratch.GetHash() != position.GetHash()) Fail(ref failures, "incremental hash", position);
        if (depth == 0) return;

        Span<Move> moves = stackalloc Move[256];
        int count = Search.GenerateLegalMoves(position, moves);
        Color us = position.Turn;
        ulong before = position.GetHash();
        for (int i = 0; i < count; i++)
        {
            ulong predicted = position.KeyAfter(us, moves[i]);
            position.Play(us, moves[i]);
            if (predicted != position.GetHash()) Fail(ref failures, $"KeyAfter {moves[i]}", position);
            Walk(position, scratch, depth - 1, ref nodes, ref failures);
            position.Undo(us, moves[i]);
            if (position.GetHash() != before) Fail(ref failures, $"hash after undo {moves[i]}", position);
        }

        if (!position.InCheck(us))
        {
            ulong predicted = position.KeyAfterNull();
            position.MakeNullMove();
            if (predicted != position.GetHash()) Fail(ref failures, "KeyAfterNull", position);
            Walk(position, scratch, 0, ref nodes, ref failures);
            position.UnmakeNullMove();
            if (position.GetHash() != before) Fail(ref failures, "hash after null undo", position);
        }
    }
}
