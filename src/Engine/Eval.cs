using Zoomies.Core;

namespace Zoomies.Engine;

public static class Eval
{
    public static readonly int[] PieceValue = Psqt.PieceValue;

    public const int MateValue = 30000;
    public const int MateBound = MateValue - 1000;

    public static int Evaluate(Position pos) =>
        pos.Turn == Color.White ? pos.WhiteEvaluation : -pos.WhiteEvaluation;

    public static int EvaluateScratch(Position pos)
    {
        int whitePov = 0;
        for (int c = 0; c < 2; c++)
            for (int pt = 0; pt < 6; pt++)
            {
                ulong bb = pos.BitboardOf((Color)c, (PieceType)pt);
                Piece pc = Types.MakePiece((Color)c, (PieceType)pt);
                while (bb != 0)
                    whitePov += Psqt.Value(pc, Bitboard.PopLsb(ref bb));
            }
        return pos.Turn == Color.White ? whitePov : -whitePov;
    }
}
