using System.Runtime.CompilerServices;

namespace Zoomies.Core;

public static class MoveGeneration
{
    private const ulong NotFileA = 0xfefefefefefefefeUL;
    private const ulong NotFileH = 0x7f7f7f7f7f7f7f7fUL;
    private const ulong Rank2 = 0x000000000000ff00UL;
    private const ulong Rank3 = 0x0000000000ff0000UL;
    private const ulong Rank6 = 0x0000ff0000000000UL;
    private const ulong Rank7 = 0x00ff000000000000UL;

    public static int GenerateLegalsInto<TUs>(this Position pos, Span<Move> moveList) where TUs : IColor, new()
    {
        var sink = new EmitSink(moveList);
        pos.GenerateLegals<TUs, EmitSink>(ref sink);
        return sink.Count;
    }

    public static int GenerateNoisyLegalsInto<TUs>(this Position pos, Span<Move> moveList) where TUs : IColor, new()
    {
        var sink = new NoisySink(moveList);
        pos.GenerateLegals<TUs, NoisySink>(ref sink);
        return sink.Count;
    }

    public static int GenerateQuiescenceLegalsInto<TUs>(this Position pos, Span<Move> moveList) where TUs : IColor, new()
    {
        var sink = new QuiescenceSink(moveList);
        pos.GenerateLegals<TUs, QuiescenceSink>(ref sink);
        return sink.Count;
    }

    public static int GenerateCapturesFast<TUs>(this Position pos, Span<Move> moveList) where TUs : IColor, new()
    {
        var us = new TUs().Value;
        var them = us.Flip();
        ulong usBb = pos.AllPieces(us);
        ulong themBb = pos.AllPieces(them);
        ulong all = usBb | themBb;
        ulong targets = themBb & ~pos.BitboardOf(them, PieceType.King);
        int n = 0;
        ulong b, att;
        Square s;

        ulong pawns = pos.BitboardOf(us, PieceType.Pawn);
        ulong promoFrom = us == Color.White ? Rank7 : Rank2;
        int north = (int)Types.RelativeDir(us, Direction.North);
        b = pawns & ~promoFrom;
        while (b != 0)
        {
            s = Bitboard.PopLsb(ref b);
            att = Tables.PawnAttacks(us, s) & targets;
            while (att != 0) moveList[n++] = new Move(s, Bitboard.PopLsb(ref att), MoveFlags.Capture);
        }
        b = pawns & promoFrom;
        while (b != 0)
        {
            s = Bitboard.PopLsb(ref b);
            att = Tables.PawnAttacks(us, s) & targets;
            while (att != 0) moveList[n++] = new Move(s, Bitboard.PopLsb(ref att), MoveFlags.PcQueen);
            int push = (int)s + north;
            if ((all & (1UL << push)) == 0) moveList[n++] = new Move(s, (Square)push, MoveFlags.PrQueen);
        }
        Square epsq = pos.History[pos.Ply].EnPassantSquare;
        if (epsq != Square.NoSquare)
        {
            att = Tables.PawnAttacks(them, epsq) & pawns;
            while (att != 0) moveList[n++] = new Move(Bitboard.PopLsb(ref att), epsq, MoveFlags.EnPassant);
        }

        b = pos.BitboardOf(us, PieceType.Knight);
        while (b != 0)
        {
            s = Bitboard.PopLsb(ref b);
            att = Tables.KnightAttacks(s) & targets;
            while (att != 0) moveList[n++] = new Move(s, Bitboard.PopLsb(ref att), MoveFlags.Capture);
        }
        b = pos.DiagonalSliders(us);
        while (b != 0)
        {
            s = Bitboard.PopLsb(ref b);
            att = Tables.BishopAttacks(s, all) & targets;
            while (att != 0) moveList[n++] = new Move(s, Bitboard.PopLsb(ref att), MoveFlags.Capture);
        }
        b = pos.OrthogonalSliders(us);
        while (b != 0)
        {
            s = Bitboard.PopLsb(ref b);
            att = Tables.RookAttacks(s, all) & targets;
            while (att != 0) moveList[n++] = new Move(s, Bitboard.PopLsb(ref att), MoveFlags.Capture);
        }
        s = Bitboard.Bsf(pos.BitboardOf(us, PieceType.King));
        att = Tables.KingAttacks(s) & targets;
        while (att != 0) moveList[n++] = new Move(s, Bitboard.PopLsb(ref att), MoveFlags.Capture);

        return n;
    }

    public static int CountLegals<TUs>(this Position pos) where TUs : IColor, new()
    {
        var sink = new CountSink();
        pos.GenerateLegals<TUs, CountSink>(ref sink);
        return sink.Count;
    }

    private static void GenerateLegals<TUs, TSink>(this Position pos, ref TSink sink)
        where TUs : IColor, new()
        where TSink : IMoveSink, allows ref struct
    {
        var usColor = new TUs().Value;
        var themColor = usColor.Flip();

        ulong usBb = pos.AllPieces(usColor);
        ulong themBb = pos.AllPieces(themColor);
        ulong all = usBb | themBb;
        ref readonly UndoInfo state = ref pos.History[pos.Ply];
        Square epsq = state.EnPassantSquare;
        CastlingRights castling = state.Castling;

        Square ourKing = Bitboard.Bsf(pos.BitboardOf(usColor, PieceType.King));
        Square theirKing = Bitboard.Bsf(pos.BitboardOf(themColor, PieceType.King));

        if (ourKing == Square.NoSquare)
        {
            return;
        }

        if (theirKing == Square.NoSquare)
        {
            return;
        }

        ulong theirDiagSliders = pos.DiagonalSliders(themColor);
        ulong theirOrthSliders = pos.OrthogonalSliders(themColor);

        ulong usPawns = pos.BitboardOf(usColor, PieceType.Pawn);
        ulong usKnights = pos.BitboardOf(usColor, PieceType.Knight);
        ulong usDiagSliders = pos.DiagonalSliders(usColor);
        ulong usOrthSliders = pos.OrthogonalSliders(usColor);

        ulong b1;

        ulong themPawns = pos.BitboardOf(themColor, PieceType.Pawn);
        ulong themKnights = pos.BitboardOf(themColor, PieceType.Knight);

        ulong danger = Tables.PawnAttacks(themColor, themPawns)
                     | Tables.KingAttacks(theirKing);

        b1 = themKnights;
        while (b1 != 0) danger |= Tables.KnightAttacks(Bitboard.PopLsb(ref b1));

        ulong allNoKing = all ^ (1UL << (int)ourKing);

        ulong kingTargets = Tables.KingAttacks(ourKing) & ~usBb;
        if (kingTargets != 0 || (!Position.Chess960 && castling != CastlingRights.None && AnyCastleCorridorClear(usColor, all, castling)))
        {
            b1 = theirDiagSliders;
            while (b1 != 0) danger |= Tables.BishopAttacks(Bitboard.PopLsb(ref b1), allNoKing);

            b1 = theirOrthSliders;
            while (b1 != 0) danger |= Tables.RookAttacks(Bitboard.PopLsb(ref b1), allNoKing);
        }

        b1 = kingTargets & ~danger;
        sink.Quiets(ourKing, b1 & ~themBb);
        sink.Captures(ourKing, b1 & themBb);

        ulong captureask;
        ulong quietMask;
        Square s;

        ulong checkers = Tables.KnightAttacks(ourKing) & themKnights
                       | Tables.PawnAttacks(usColor, ourKing) & themPawns;

        ulong candidates = Tables.RookAttacks(ourKing, themBb) & theirOrthSliders
                        | Tables.BishopAttacks(ourKing, themBb) & theirDiagSliders;

        ulong pinned = 0;
        while (candidates != 0)
        {
            s = Bitboard.PopLsb(ref candidates);
            b1 = Tables.SquaresBetween[((int)ourKing << 6) | (int)s] & usBb;

            if (b1 == 0)
                checkers ^= 1UL << (int)s;
            else if ((b1 & (b1 - 1)) == 0)
                pinned ^= b1;
        }

        ulong notPinned = ~pinned;

        switch (Bitboard.SparsePopCount(checkers))
        {
            case 2:
                return;

            case 1:
                {
                    Square checkerSquare = Bitboard.Bsf(checkers);
                    var checkerPiece = pos.At(checkerSquare);

                    if (checkerPiece == Types.MakePiece(themColor, PieceType.Pawn))
                    {
                        if (epsq != Square.NoSquare)
                        {
                            var epTarget = 1UL << (int)epsq;
                            var southDir = Types.RelativeDir(usColor, Direction.South);
                            if (checkers == Bitboard.Shift(southDir, epTarget))
                            {
                                b1 = Tables.PawnAttacks(themColor, epsq) & usPawns & notPinned;
                                sink.FromMask(b1, epsq, MoveFlags.EnPassant);
                            }
                        }
                        captureask = checkers;
                        quietMask = 0;
                        break;
                    }
                    else if (checkerPiece == Types.MakePiece(themColor, PieceType.Knight))
                    {
                        b1 = pos.AttackersFrom(usColor, checkerSquare, all) & notPinned;
                        // a pawn can promote by capturing the checking piece
                        ulong promoPawns = b1 & usPawns & Bitboard.RankMask(Types.RelativeRank(usColor, Rank.Rank7));
                        sink.FromMask(b1 ^ promoPawns, checkerSquare, MoveFlags.Capture);
                        while (promoPawns != 0)
                            sink.PromoCaptures(Bitboard.PopLsb(ref promoPawns), checkers);
                        return;
                    }
                    else
                    {
                        captureask = checkers;
                        quietMask = Tables.SquaresBetween[((int)ourKing << 6) | (int)checkerSquare];
                        break;
                    }
                }
            case 0:
            default:
                captureask = themBb;
                quietMask = ~all;

                if (epsq != Square.NoSquare)
                {
                    HandleEnPassantInto(usColor, usPawns, notPinned, pinned, ourKing, theirOrthSliders, all, epsq, ref sink);
                }

                if (castling != CastlingRights.None)
                {
                    if (Position.Chess960)
                        pos.EmitCastlesFrc(usColor, ourKing, all, castling, ref sink);
                    else
                        HandleCastlingInto(usColor, all, danger, castling, ref sink);
                }

                if (pinned != 0)
                {
                    HandlePinnedPiecesInto(pos, usColor, usPawns, usKnights, themBb, notPinned, ourKing, all, captureask, quietMask, ref sink);
                }

                break;
        }

        HandleNonPinnedPiecesInto(usColor, usPawns, usKnights, usDiagSliders, usOrthSliders, notPinned, all, captureask, quietMask, ref sink);
    }

    private static void HandleEnPassantInto<TSink>(Color usColor, ulong usPawns, ulong notPinned,
        ulong pinned, Square ourKing, ulong theirOrthSliders, ulong all, Square epsq, ref TSink sink)
        where TSink : IMoveSink, allows ref struct
    {
        ulong b1, b2;
        Square s;

        b2 = Tables.PawnAttacks(usColor.Flip(), epsq) & usPawns;
        b1 = b2 & notPinned;
        var southDir = Types.RelativeDir(usColor, Direction.South);
        var epCaptureSquare = (Square)((int)epsq + (int)southDir);
        var epCaptureSquareBb = 1UL << (int)epCaptureSquare;
        var rankMask = Bitboard.RankMask(Types.RankOf(ourKing));

        while (b1 != 0)
        {
            s = Bitboard.PopLsb(ref b1);

            var newOcc = all ^ (1UL << (int)s) ^ epCaptureSquareBb;

            if ((Tables.SlidingAttacks(ourKing, newOcc, rankMask) & theirOrthSliders) == 0)
                sink.One(s, epsq, MoveFlags.EnPassant);
        }

        b1 = b2 & pinned & Tables.LineMasks[((int)epsq << 6) | (int)ourKing];
        if (b1 != 0)
        {
            sink.One(Bitboard.Bsf(b1), epsq, MoveFlags.EnPassant);
        }
    }

    private static void HandleCastlingInto<TSink>(Color usColor, ulong all, ulong danger,
        CastlingRights rights, ref TSink sink)
        where TSink : IMoveSink, allows ref struct
    {
        if (usColor == Color.White)
        {
            if ((rights & CastlingRights.WhiteOO) != 0 &&
                ((all & 0x60UL) | (danger & 0x70UL)) == 0)
            {
                sink.One(Square.e1, Square.g1, MoveFlags.OO);
            }

            if ((rights & CastlingRights.WhiteOOO) != 0 &&
                ((all & 0xeUL) | (danger & 0x1cUL)) == 0)
            {
                sink.One(Square.e1, Square.c1, MoveFlags.OOO);
            }
        }
        else
        {
            if ((rights & CastlingRights.BlackOO) != 0 &&
                ((all & 0x6000000000000000UL) | (danger & 0x7000000000000000UL)) == 0)
            {
                sink.One(Square.e8, Square.g8, MoveFlags.OO);
            }

            if ((rights & CastlingRights.BlackOOO) != 0 &&
                ((all & 0x0e00000000000000UL) | (danger & 0x1c00000000000000UL)) == 0)
            {
                sink.One(Square.e8, Square.c8, MoveFlags.OOO);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool AnyCastleCorridorClear(Color us, ulong all, CastlingRights rights)
    {
        return us == Color.White
            ? ((rights & CastlingRights.WhiteOO)     != 0 && (all & 0x60UL) == 0)
              || ((rights & CastlingRights.WhiteOOO) != 0 && (all & 0xeUL) == 0)
            : ((rights & CastlingRights.BlackOO)     != 0 && (all & 0x6000000000000000UL) == 0)
              || ((rights & CastlingRights.BlackOOO) != 0 && (all & 0x0e00000000000000UL) == 0);
    }

    private static void HandlePinnedPiecesInto<TSink>(Position pos, Color usColor, ulong usPawns, ulong usKnights,
        ulong themBb, ulong notPinned, Square ourKing, ulong all, ulong captureask, ulong quietMask, ref TSink sink)
        where TSink : IMoveSink, allows ref struct
    {
        ulong b1, b2, b3;
        Square s;

        b1 = ~(notPinned | usKnights);
        while (b1 != 0)
        {
            s = Bitboard.PopLsb(ref b1);
            var pt = Types.TypeOf(pos.At(s));
            if (pt == PieceType.Pawn) continue;
            ulong line = Tables.LineMasks[((int)ourKing << 6) | (int)s];
            b2 = Tables.Attacks(pt, s, all) & line;
            sink.Quiets(s, b2 & quietMask);
            sink.Captures(s, b2 & captureask);
        }

        b1 = ~notPinned & usPawns;
        while (b1 != 0)
        {
            s = Bitboard.PopLsb(ref b1);

            ulong line = Tables.LineMasks[((int)ourKing << 6) | (int)s];
            if (Types.RankOf(s) == Types.RelativeRank(usColor, Rank.Rank7))
            {
                b2 = Tables.PawnAttacks(usColor, s) & captureask & line;
                sink.PromoCaptures(s, b2);

                var northDir = Types.RelativeDir(usColor, Direction.North);
                b2 = Bitboard.Shift(northDir, 1UL << (int)s) & ~all & line;
                sink.PromoQuiets(s, b2);
            }
            else
            {
                b2 = Tables.PawnAttacks(usColor, s) & themBb & line;

                sink.Captures(s, b2);

                var northDir = Types.RelativeDir(usColor, Direction.North);
                b2 = Bitboard.Shift(northDir, 1UL << (int)s) & ~all & line;

                b3 = Bitboard.Shift(northDir, b2 & Bitboard.RankMask(Types.RelativeRank(usColor, Rank.Rank3)))
                   & ~all & line;

                sink.Quiets(s, b2);
                sink.DoublePushes(s, b3);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleNonPinnedPiecesInto<TSink>(Color usColor, ulong usPawns, ulong usKnights,
        ulong usDiagSliders, ulong usOrthSliders, ulong notPinned, ulong all, ulong captureask, ulong quietMask,
        ref TSink sink) where TSink : IMoveSink, allows ref struct
    {
        ulong b1, b2;
        Square s;

        b1 = usKnights & notPinned;
        while (b1 != 0)
        {
            s = Bitboard.PopLsb(ref b1);
            b2 = Tables.KnightAttacks(s);
            sink.Quiets(s, b2 & quietMask);
            sink.Captures(s, b2 & captureask);
        }

        b1 = usDiagSliders & notPinned;
        while (b1 != 0)
        {
            s = Bitboard.PopLsb(ref b1);
            b2 = Tables.BishopAttacks(s, all);
            sink.Quiets(s, b2 & quietMask);
            sink.Captures(s, b2 & captureask);
        }

        b1 = usOrthSliders & notPinned;
        while (b1 != 0)
        {
            s = Bitboard.PopLsb(ref b1);
            b2 = Tables.RookAttacks(s, all);
            sink.Quiets(s, b2 & quietMask);
            sink.Captures(s, b2 & captureask);
        }

        HandleNonPinnedPawnsInto(usPawns, usColor, notPinned, all, captureask, quietMask, ref sink);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleNonPinnedPawnsInto<TSink>(ulong usPawns, Color usColor, ulong notPinned,
        ulong all, ulong captureask, ulong quietMask, ref TSink sink) where TSink : IMoveSink, allows ref struct
    {
        ulong b1, b2, b3;

        ulong pawns = usPawns & notPinned;
        if (usColor == Color.White)
        {
            b1 = pawns & ~Rank7;
            b2 = (b1 << 8) & ~all;
            b3 = ((b2 & Rank3) << 8) & quietMask;
            b2 &= quietMask;

            sink.PawnMoves(b2, (int)Direction.North, MoveFlags.Quiet);
            sink.PawnMoves(b3, (int)Direction.NorthNorth, MoveFlags.DoublePush);

            b2 = ((b1 & NotFileA) << 7) & captureask;
            b3 = ((b1 & NotFileH) << 9) & captureask;

            sink.PawnMoves(b2, (int)Direction.NorthWest, MoveFlags.Capture);
            sink.PawnMoves(b3, (int)Direction.NorthEast, MoveFlags.Capture);

            b1 = pawns & Rank7;
            if (b1 != 0)
            {
                b2 = (b1 << 8) & quietMask;
                sink.PawnPromos(b2, (int)Direction.North, capture: false);

                b2 = ((b1 & NotFileA) << 7) & captureask;
                b3 = ((b1 & NotFileH) << 9) & captureask;

                sink.PawnPromos(b2, (int)Direction.NorthWest, capture: true);
                sink.PawnPromos(b3, (int)Direction.NorthEast, capture: true);
            }
        }
        else
        {
            b1 = pawns & ~Rank2;
            b2 = (b1 >> 8) & ~all;
            b3 = ((b2 & Rank6) >> 8) & quietMask;
            b2 &= quietMask;

            sink.PawnMoves(b2, (int)Direction.South, MoveFlags.Quiet);
            sink.PawnMoves(b3, (int)Direction.SouthSouth, MoveFlags.DoublePush);

            b2 = ((b1 & NotFileA) >> 9) & captureask;
            b3 = ((b1 & NotFileH) >> 7) & captureask;

            sink.PawnMoves(b2, (int)Direction.SouthWest, MoveFlags.Capture);
            sink.PawnMoves(b3, (int)Direction.SouthEast, MoveFlags.Capture);

            b1 = pawns & Rank2;
            if (b1 != 0)
            {
                b2 = (b1 >> 8) & quietMask;
                sink.PawnPromos(b2, (int)Direction.South, capture: false);

                b2 = ((b1 & NotFileA) >> 9) & captureask;
                b3 = ((b1 & NotFileH) >> 7) & captureask;

                sink.PawnPromos(b2, (int)Direction.SouthWest, capture: true);
                sink.PawnPromos(b3, (int)Direction.SouthEast, capture: true);
            }
        }
    }
}
