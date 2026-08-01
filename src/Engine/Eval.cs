using Zoomies.Core;

namespace Zoomies.Engine;

public static class Eval
{
    public static readonly int[] PieceValue = [100, 320, 330, 500, 900, 0];

    public const int MateValue = 30000;
    public const int MateBound = MateValue - 1000;

    public static int Evaluate(Position pos) => Nnue.Loaded ? Nnue.Evaluate(pos) : Taper(pos, pos.WhiteEvaluation);

    public static int EvaluateScratch(Position pos)
    {
        int packed = 0;
        for (int c = 0; c < 2; c++)
            for (int pt = 0; pt < 6; pt++)
            {
                ulong bb = pos.BitboardOf((Color)c, (PieceType)pt);
                Piece pc = Types.MakePiece((Color)c, (PieceType)pt);
                while (bb != 0)
                    packed += Psqt.Value(pc, Bitboard.PopLsb(ref bb));
            }
        return Taper(pos, packed);
    }

    private static int Taper(Position pos, int packed)
    {
        int minors = Bitboard.PopCount(
            pos.BitboardOf(Color.White, PieceType.Knight) | pos.BitboardOf(Color.Black, PieceType.Knight) |
            pos.BitboardOf(Color.White, PieceType.Bishop) | pos.BitboardOf(Color.Black, PieceType.Bishop));
        int rooks = Bitboard.PopCount(
            pos.BitboardOf(Color.White, PieceType.Rook)   | pos.BitboardOf(Color.Black, PieceType.Rook));
        int queens = Bitboard.PopCount(
            pos.BitboardOf(Color.White, PieceType.Queen)  | pos.BitboardOf(Color.Black, PieceType.Queen));

        int phase = minors + 2 * rooks + 4 * queens;
        if (phase > Psqt.PhaseMax) phase = Psqt.PhaseMax; // early promotions

        int whitePov = (Psqt.Mg(packed) * phase + Psqt.Eg(packed) * (Psqt.PhaseMax - phase)) / Psqt.PhaseMax;
        return pos.Turn == Color.White ? whitePov : -whitePov;
    }
}
