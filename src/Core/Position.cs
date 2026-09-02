using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Zoomies.Core;

public struct UndoInfo
{
    public CastlingRights Castling;
    public Piece Captured;
    public Square EnPassantSquare;
    public ulong Hash;
    public int HalfMoveClock;

    public UndoInfo()
    {
        Castling = CastlingRights.None;
        Captured = Piece.NoPiece;
        EnPassantSquare = Square.NoSquare;
        Hash = 0;
        HalfMoveClock = 0;
    }

    public UndoInfo(UndoInfo prev)
    {
        Castling = prev.Castling;
        Captured = Piece.NoPiece;
        EnPassantSquare = Square.NoSquare;
        Hash = 0;
        HalfMoveClock = prev.HalfMoveClock;
    }
}

public class Position
{
    private readonly ulong[] pieceBB = new ulong[Types.PieceCount];

    private readonly ulong[] colorBB = new ulong[Types.ColorCount];

    private readonly Piece[] board = new Piece[Types.SquareCount];

    private Color sideToPlay;

    private int gamePly;

    private ulong hash;

    public readonly UndoInfo[] History = new UndoInfo[4096];

    public ulong Checkers { get; internal set; }

    public ulong Pinned { get; internal set; }

    public static bool Chess960;

    private readonly Square[] castleRookFrom = new Square[4];
    private readonly Square[] castleKingTo = new Square[4];
    private readonly Square[] castleRookTo = new Square[4];
    private readonly ulong[] castleEmpty = new ulong[4];
    private readonly ulong[] castleKingPath = new ulong[4];

    private readonly CastlingRights[] castleClear = new CastlingRights[Types.SquareCount];

    private static int CastleIndex(Color c, bool kingside) => ((int)c << 1) | (kingside ? 0 : 1);
    private static CastlingRights CastleBit(int idx) => (CastlingRights)(1 << idx);

    public Square CastleRookOrigin(Color c, bool kingside) => castleRookFrom[CastleIndex(c, kingside)];

    public string FormatUci(Move m)
    {
        if (Chess960 && m.Flags is MoveFlags.OO or MoveFlags.OOO)
            return Types.SquareNames[(int)m.From] + Types.SquareNames[(int)CastleRookOrigin(sideToPlay, m.Flags == MoveFlags.OO)];
        return m.ToString();
    }

    internal void CastleSquaresFrc(Color us, bool kingside, out int kingTo, out int rookFrom, out int rookTo)
    {
        int idx = CastleIndex(us, kingside);
        kingTo = (int)castleKingTo[idx];
        rookFrom = (int)castleRookFrom[idx];
        rookTo = (int)castleRookTo[idx];
    }

    private void BuildCastleMeta(int idx, Square kingFrom, Square rookFrom, bool kingside)
    {
        Rank rank = Types.RankOf(kingFrom);
        Square kingTo = Types.CreateSquare(kingside ? File.FileG : File.FileC, rank);
        Square rookTo = Types.CreateSquare(kingside ? File.FileF : File.FileD, rank);

        castleRookFrom[idx] = rookFrom;
        castleKingTo[idx] = kingTo;
        castleRookTo[idx] = rookTo;

        ulong betweenKing = kingFrom == kingTo ? 0 : Tables.SquaresBetween[((int)kingFrom << 6) | (int)kingTo];
        ulong betweenRook = rookFrom == rookTo ? 0 : Tables.SquaresBetween[((int)rookFrom << 6) | (int)rookTo];
        ulong empty = (betweenKing | (1UL << (int)kingTo) | betweenRook | (1UL << (int)rookTo)) & ~(1UL << (int)kingFrom) & ~(1UL << (int)rookFrom);

        castleEmpty[idx] = empty;
        castleKingPath[idx] = betweenKing | (1UL << (int)kingTo);

        castleClear[(int)rookFrom] |= CastleBit(idx);
        castleClear[(int)kingFrom] |= idx < 2 ? CastlingRights.White : CastlingRights.Black;
    }

    private Square FindCastleRook(Piece rook, int rank, int kingFile, bool kingside)
    {
        if (kingside)
        {
            for (int f = 7; f > kingFile; f--)
            {
                var sq = (Square)(rank * 8 + f);
                if (At(sq) == rook) return sq;
            }
        }
        else
        {
            for (int f = 0; f < kingFile; f++)
            {
                var sq = (Square)(rank * 8 + f);
                if (At(sq) == rook) return sq;
            }
        }
        return Square.NoSquare;
    }

    internal void EmitCastlesFrc<TSink>(Color us, Square kingFrom, ulong all, CastlingRights rights, ref TSink sink) where TSink : IMoveSink, allows ref struct
    {
        if (us == Color.White)
        {
            if ((rights & CastlingRights.WhiteOO) != 0) TryEmitCastleFrc(0, us, kingFrom, MoveFlags.OO, all, ref sink);
            if ((rights & CastlingRights.WhiteOOO) != 0) TryEmitCastleFrc(1, us, kingFrom, MoveFlags.OOO, all, ref sink);
        }
        else
        {
            if ((rights & CastlingRights.BlackOO) != 0) TryEmitCastleFrc(2, us, kingFrom, MoveFlags.OO, all, ref sink);
            if ((rights & CastlingRights.BlackOOO) != 0) TryEmitCastleFrc(3, us, kingFrom, MoveFlags.OOO, all, ref sink);
        }
    }

    private void TryEmitCastleFrc<TSink>(int idx, Color us, Square kingFrom, MoveFlags flag, ulong all, ref TSink sink) where TSink : IMoveSink, allows ref struct
    {
        if ((all & castleEmpty[idx]) != 0) 
            return;

        Color them = us.Flip();
        Square kingTo = castleKingTo[idx];

        ulong baseOcc = (all & ~(1UL << (int)kingFrom) & ~(1UL << (int)castleRookFrom[idx])) | (1UL << (int)castleRookTo[idx]);

        ulong path = castleKingPath[idx];
        while (path != 0)
        {
            Square s = Bitboard.PopLsb(ref path);
            if (AttackersFrom(them, s, baseOcc | (1UL << (int)s)) != 0) return;
        }

        sink.One(kingFrom, kingTo, flag);
    }

    private void DoCastleFrc(Color us, Square kingFrom, int idx, bool hashed)
    {
        Piece king = Types.MakePiece(us, PieceType.King);
        Piece rook = Types.MakePiece(us, PieceType.Rook);

        if (hashed)
        {
            RemovePiece(kingFrom);
            RemovePiece(castleRookFrom[idx]);
            PutPiece(king, castleKingTo[idx]);
            PutPiece(rook, castleRookTo[idx]);
        }
        else
        {
            RemovePieceNoHash(kingFrom);
            RemovePieceNoHash(castleRookFrom[idx]);
            PutPieceNoHash(king, castleKingTo[idx]);
            PutPieceNoHash(rook, castleRookTo[idx]);
        }
    }

    private void UndoCastleFrc(Color us, Square kingFrom, int idx, bool hashed)
    {
        Piece king = Types.MakePiece(us, PieceType.King);
        Piece rook = Types.MakePiece(us, PieceType.Rook);
        if (hashed)
        {
            RemovePiece(castleKingTo[idx]);
            RemovePiece(castleRookTo[idx]);
            PutPiece(king, kingFrom);
            PutPiece(rook, castleRookFrom[idx]);
        }
        else
        {
            RemovePieceNoHash(castleKingTo[idx]);
            RemovePieceNoHash(castleRookTo[idx]);
            PutPieceNoHash(king, kingFrom);
            PutPieceNoHash(rook, castleRookFrom[idx]);
        }
    }

    public Position()
    {
        sideToPlay = Color.White;
        gamePly = 0;
        hash = 0;
        Pinned = 0;
        Checkers = 0;

        for (int i = 0; i < 64; i++)
            board[i] = Piece.NoPiece;

        for (int i = 0; i < History.Length; i++)
            History[i] = new UndoInfo();
    }

    private void PutPiece(Piece pc, Square s)
    {
        board[(int)s] = pc;
        ulong bb = 1UL << (int)s;
        pieceBB[(int)pc] |= bb;
        colorBB[((int)pc >> 3) & 1] |= bb;
        hash ^= Zobrist.Piece(pc, s);
    }
    private void RemovePiece(Square s)
    {
        Piece pc = board[(int)s];
        ulong bb = 1UL << (int)s;
        hash ^= Zobrist.Piece(pc, s);
        pieceBB[(int)pc] &= ~bb;
        colorBB[((int)pc >> 3) & 1] &= ~bb;
        board[(int)s] = Piece.NoPiece;
    }
    private void MovePiece(Square from, Square to)
    {
        var movingPiece = board[(int)from];
        var capturedPiece = board[(int)to];

        hash ^= Zobrist.Piece(movingPiece, from)
             ^ Zobrist.Piece(movingPiece, to);

        if (capturedPiece != Piece.NoPiece)
            hash ^= Zobrist.Piece(capturedPiece, to);

        ulong toBB = 1UL << (int)to;
        ulong fromTo = (1UL << (int)from) | toBB;

        pieceBB[(int)movingPiece] ^= fromTo;
        colorBB[((int)movingPiece >> 3) & 1] ^= fromTo;
        if (capturedPiece != Piece.NoPiece)
        {
            pieceBB[(int)capturedPiece] &= ~toBB;
            colorBB[((int)capturedPiece >> 3) & 1] &= ~toBB;
        }

        board[(int)to] = movingPiece;
        board[(int)from] = Piece.NoPiece;
    }

    public void MakeNullMove()
    {
        if (NnueSt != null) Engine.Nnue.OnPlayNull(this);
        hash ^= StateHash(History[gamePly].Castling, History[gamePly].EnPassantSquare);
        hash ^= Zobrist.SideToMove;
        sideToPlay = sideToPlay.Flip();
        ++gamePly;
        History[gamePly] = new UndoInfo(History[gamePly - 1])
        {
            EnPassantSquare = Square.NoSquare
        };
        History[gamePly].HalfMoveClock++;

        hash ^= StateHash(History[gamePly].Castling, History[gamePly].EnPassantSquare);
        History[gamePly].Hash = hash;
    }

    public void UnmakeNullMove()
    {
        sideToPlay = sideToPlay.Flip();
        --gamePly;
        hash = History[gamePly].Hash;
        if (NnueSt != null) Engine.Nnue.OnUndo(this);
    }

    private void MovePieceQuietNoHash(Square from, Square to)
    {
        Piece pc = board[(int)from];
        ulong fromTo = (1UL << (int)from) | (1UL << (int)to);
        pieceBB[(int)pc] ^= fromTo;
        colorBB[((int)pc >> 3) & 1] ^= fromTo;
        board[(int)to] = pc;
        board[(int)from] = Piece.NoPiece;
    }
    private void PutPieceNoHash(Piece pc, Square s)
    {
        board[(int)s] = pc;
        ulong bb = 1UL << (int)s;
        pieceBB[(int)pc] |= bb;
        colorBB[((int)pc >> 3) & 1] |= bb;
    }
    private void RemovePieceNoHash(Square s)
    {
        Piece pc = board[(int)s];
        ulong bb = 1UL << (int)s;
        pieceBB[(int)pc] &= ~bb;
        colorBB[((int)pc >> 3) & 1] &= ~bb;
        board[(int)s] = Piece.NoPiece;
    }

    private void RemovePieceKnownNoHash(Square s, Piece pc)
    {
        ulong bb = 1UL << (int)s;
        pieceBB[(int)pc] &= ~bb;
        colorBB[((int)pc >> 3) & 1] &= ~bb;
        board[(int)s] = Piece.NoPiece;
    }

    private void MovePieceKnownCaptureNoHash(Square from, Square to, Piece captured)
    {
        var movingPiece = board[(int)from];
        ulong toBB = 1UL << (int)to;
        ulong fromTo = (1UL << (int)from) | toBB;
        pieceBB[(int)movingPiece] ^= fromTo;
        colorBB[((int)movingPiece >> 3) & 1] ^= fromTo;
        pieceBB[(int)captured] &= ~toBB;
        colorBB[((int)captured >> 3) & 1] &= ~toBB;
        board[(int)to] = movingPiece;
        board[(int)from] = Piece.NoPiece;
    }
    private void MovePieceNoHash(Square from, Square to)
    {
        var movingPiece = board[(int)from];
        var capturedPiece = board[(int)to];
        ulong toBB = 1UL << (int)to;
        ulong fromTo = (1UL << (int)from) | toBB;
        pieceBB[(int)movingPiece] ^= fromTo;
        colorBB[((int)movingPiece >> 3) & 1] ^= fromTo;
        if (capturedPiece != Piece.NoPiece)
        {
            pieceBB[(int)capturedPiece] &= ~toBB;
            colorBB[((int)capturedPiece >> 3) & 1] &= ~toBB;
        }
        board[(int)to] = movingPiece;
        board[(int)from] = Piece.NoPiece;
    }
    
    public Position(Position other)
    {
        Array.Copy(other.pieceBB, pieceBB, Types.PieceCount);
        Array.Copy(other.colorBB, colorBB, Types.ColorCount);
        Array.Copy(other.board, board, Types.SquareCount);
        sideToPlay = other.sideToPlay;
        gamePly = other.gamePly;
        hash = other.hash;
        Array.Copy(other.History, History, gamePly + 1);
        Checkers = other.Checkers;
        Pinned = other.Pinned;
        Array.Copy(other.castleRookFrom, castleRookFrom, 4);
        Array.Copy(other.castleKingTo, castleKingTo, 4);
        Array.Copy(other.castleRookTo, castleRookTo, 4);
        Array.Copy(other.castleEmpty, castleEmpty, 4);
        Array.Copy(other.castleKingPath, castleKingPath, 4);
        Array.Copy(other.castleClear, castleClear, Types.SquareCount);
    }

    private void MovePieceQuiet(Square from, Square to)
    {
        Piece pc = board[(int)from];
        hash ^= Zobrist.Piece(pc, from)
             ^ Zobrist.Piece(pc, to);
        ulong fromTo = (1UL << (int)from) | (1UL << (int)to);
        pieceBB[(int)pc] ^= fromTo;
        colorBB[((int)pc >> 3) & 1] ^= fromTo;
        board[(int)to] = pc;
        board[(int)from] = Piece.NoPiece;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong BitboardOf(Piece pc) => Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(pieceBB), (int)pc);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong BitboardOf(Color c, PieceType pt) => Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(pieceBB), (int)Types.MakePiece(c, pt));
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Piece At(Square sq) => Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(board), (int)sq);
    public ReadOnlySpan<Piece> Board => board;
    public Color Turn => sideToPlay;
    public int Ply => gamePly;
    public ulong GetHash() => hash;

    private static ulong StateHash(CastlingRights castling, Square epsq)
    {
        ulong stateHash = Zobrist.Castling[(int)castling & 0xF];
        if (epsq != Square.NoSquare)
            stateHash ^= Zobrist.EnPassantFile[(int)Types.FileOf(epsq)];
        return stateHash;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong DiagonalSliders(Color c)
    {
        ref ulong pb = ref MemoryMarshal.GetArrayDataReference(pieceBB);
        return c == Color.White ?
            Unsafe.Add(ref pb, (int)Piece.WhiteBishop) | Unsafe.Add(ref pb, (int)Piece.WhiteQueen) :
            Unsafe.Add(ref pb, (int)Piece.BlackBishop) | Unsafe.Add(ref pb, (int)Piece.BlackQueen);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong OrthogonalSliders(Color c)
    {
        ref ulong pb = ref MemoryMarshal.GetArrayDataReference(pieceBB);
        return c == Color.White ?
            Unsafe.Add(ref pb, (int)Piece.WhiteRook) | Unsafe.Add(ref pb, (int)Piece.WhiteQueen) :
            Unsafe.Add(ref pb, (int)Piece.BlackRook) | Unsafe.Add(ref pb, (int)Piece.BlackQueen);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong AllPieces(Color c) => Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(colorBB), (int)c);

    public ulong AttackersFrom(Color c, Square s, ulong occ)
    {
        ulong knight = Tables.KnightAttacks(s);
        ulong bishop = Tables.BishopAttacks(s, occ);
        ulong rook = Tables.RookAttacks(s, occ);
        return c == Color.White ?
            (Tables.PawnAttacks(Color.Black, s) & pieceBB[(int)Piece.WhitePawn]) |
            (knight & pieceBB[(int)Piece.WhiteKnight]) |
            (bishop & (pieceBB[(int)Piece.WhiteBishop] | pieceBB[(int)Piece.WhiteQueen])) |
            (rook & (pieceBB[(int)Piece.WhiteRook] | pieceBB[(int)Piece.WhiteQueen])) :
            (Tables.PawnAttacks(Color.White, s) & pieceBB[(int)Piece.BlackPawn]) |
            (knight & pieceBB[(int)Piece.BlackKnight]) |
            (bishop & (pieceBB[(int)Piece.BlackBishop] | pieceBB[(int)Piece.BlackQueen])) |
            (rook & (pieceBB[(int)Piece.BlackRook] | pieceBB[(int)Piece.BlackQueen]));
    }
    public bool InCheck(Color c)
    {
        var kingSquare = Bitboard.Bsf(BitboardOf(c, PieceType.King));
        return AttackersFrom(c.Flip(), kingSquare, AllPieces(Color.White) | AllPieces(Color.Black)) != 0;
    }

    // NNUE state
    public Engine.Nnue.State? NnueSt;

    public void Play(Color us, Move m)
    {
        if (NnueSt != null) Engine.Nnue.OnPlay(this, us, m);
        hash ^= StateHash(History[gamePly].Castling, History[gamePly].EnPassantSquare);
        sideToPlay = sideToPlay.Flip();
        ++gamePly;
        var type = m.Flags;

        History[gamePly] = new UndoInfo(History[gamePly - 1]);
        var piece = board[(int)m.From];

        if (Types.TypeOf(piece) == PieceType.Pawn || m.IsCapture)
        {
            History[gamePly].HalfMoveClock = 0;
        }
        else
        {
            History[gamePly].HalfMoveClock++;
        }

        History[gamePly].Castling &= ~(castleClear[(int)m.From] | castleClear[(int)m.To]);

        switch (type)
        {
            case MoveFlags.Quiet:
                MovePieceQuiet(m.From, m.To);
                break;

            case MoveFlags.DoublePush:
                MovePieceQuiet(m.From, m.To);
                History[gamePly].EnPassantSquare =
                    (Square)((int)m.From + (int)Types.RelativeDir(us, Direction.North));
                break;

            case MoveFlags.OO:
                if (Chess960){
                    DoCastleFrc(us, m.From, CastleIndex(us, true), hashed: true);
                }
                else if (us == Color.White)
                {
                    MovePieceQuiet(Square.e1, Square.g1);
                    MovePieceQuiet(Square.h1, Square.f1);
                }
                else
                {
                    MovePieceQuiet(Square.e8, Square.g8);
                    MovePieceQuiet(Square.h8, Square.f8);
                }
                break;

            case MoveFlags.OOO:
                if (Chess960)
                {
                    DoCastleFrc(us, m.From, CastleIndex(us, false), hashed: true);
                }
                else if (us == Color.White)
                {
                    MovePieceQuiet(Square.e1, Square.c1);
                    MovePieceQuiet(Square.a1, Square.d1);
                }
                else
                {
                    MovePieceQuiet(Square.e8, Square.c8);
                    MovePieceQuiet(Square.a8, Square.d8);
                }
                break;

            case MoveFlags.EnPassant:
                MovePieceQuiet(m.From, m.To);
                RemovePiece((Square)((int)m.To + (int)Types.RelativeDir(us, Direction.South)));
                break;

            case MoveFlags.PrKnight:
                RemovePiece(m.From);
                PutPiece(Types.MakePiece(us, PieceType.Knight), m.To);
                break;

            case MoveFlags.PrBishop:
                RemovePiece(m.From);
                PutPiece(Types.MakePiece(us, PieceType.Bishop), m.To);
                break;

            case MoveFlags.PrRook:
                RemovePiece(m.From);
                PutPiece(Types.MakePiece(us, PieceType.Rook), m.To);
                break;

            case MoveFlags.PrQueen:
                RemovePiece(m.From);
                PutPiece(Types.MakePiece(us, PieceType.Queen), m.To);
                break;

            case MoveFlags.PcKnight:
                RemovePiece(m.From);
                History[gamePly].Captured = board[(int)m.To];
                RemovePiece(m.To);
                PutPiece(Types.MakePiece(us, PieceType.Knight), m.To);
                break;

            case MoveFlags.PcBishop:
                RemovePiece(m.From);
                History[gamePly].Captured = board[(int)m.To];
                RemovePiece(m.To);
                PutPiece(Types.MakePiece(us, PieceType.Bishop), m.To);
                break;

            case MoveFlags.PcRook:
                RemovePiece(m.From);
                History[gamePly].Captured = board[(int)m.To];
                RemovePiece(m.To);
                PutPiece(Types.MakePiece(us, PieceType.Rook), m.To);
                break;

            case MoveFlags.PcQueen:
                RemovePiece(m.From);
                History[gamePly].Captured = board[(int)m.To];
                RemovePiece(m.To);
                PutPiece(Types.MakePiece(us, PieceType.Queen), m.To);
                break;

            case MoveFlags.Capture:
                History[gamePly].Captured = board[(int)m.To];
                MovePiece(m.From, m.To);
                break;
        }

        hash ^= Zobrist.SideToMove;
        hash ^= StateHash(History[gamePly].Castling, History[gamePly].EnPassantSquare);
        History[gamePly].Hash = hash;
    }
    public void Undo(Color us, Move m)
    {
        hash = History[gamePly].Hash;
        hash ^= StateHash(History[gamePly].Castling, History[gamePly].EnPassantSquare);

        var type = m.Flags;
        switch (type)
        {
            case MoveFlags.Quiet:
                MovePieceQuiet(m.To, m.From);
                break;

            case MoveFlags.DoublePush:
                MovePieceQuiet(m.To, m.From);
                break;

            case MoveFlags.OO:
                if (Chess960) UndoCastleFrc(us, m.From, CastleIndex(us, true), hashed: true);
                else if (us == Color.White)
                {
                    MovePieceQuiet(Square.g1, Square.e1);
                    MovePieceQuiet(Square.f1, Square.h1);
                }
                else
                {
                    MovePieceQuiet(Square.g8, Square.e8);
                    MovePieceQuiet(Square.f8, Square.h8);
                }
                break;

            case MoveFlags.OOO:
                if (Chess960) UndoCastleFrc(us, m.From, CastleIndex(us, false), hashed: true);
                else if (us == Color.White)
                {
                    MovePieceQuiet(Square.c1, Square.e1);
                    MovePieceQuiet(Square.d1, Square.a1);
                }
                else
                {
                    MovePieceQuiet(Square.c8, Square.e8);
                    MovePieceQuiet(Square.d8, Square.a8);
                }
                break;

            case MoveFlags.EnPassant:
                MovePieceQuiet(m.To, m.From);
                PutPiece(Types.MakePiece(us.Flip(), PieceType.Pawn),
                        (Square)((int)m.To + (int)Types.RelativeDir(us, Direction.South)));
                break;

            case MoveFlags.PrKnight:
            case MoveFlags.PrBishop:
            case MoveFlags.PrRook:
            case MoveFlags.PrQueen:
                RemovePiece(m.To);
                PutPiece(Types.MakePiece(us, PieceType.Pawn), m.From);
                break;

            case MoveFlags.PcKnight:
            case MoveFlags.PcBishop:
            case MoveFlags.PcRook:
            case MoveFlags.PcQueen:
                RemovePiece(m.To);
                PutPiece(Types.MakePiece(us, PieceType.Pawn), m.From);
                PutPiece(History[gamePly].Captured, m.To);
                break;

            case MoveFlags.Capture:
                MovePieceQuiet(m.To, m.From);
                PutPiece(History[gamePly].Captured, m.To);
                break;
        }
        hash ^= Zobrist.SideToMove;
        hash ^= StateHash(
            History[gamePly - 1].Castling,
            History[gamePly - 1].EnPassantSquare);

        sideToPlay = sideToPlay.Flip();
        --gamePly;
        if (NnueSt != null) Engine.Nnue.OnUndo(this);
    }

    public void PlayPerft(Color us, Move m)
    {
        if (NnueSt != null) Engine.Nnue.OnPlay(this, us, m);
        sideToPlay = sideToPlay.Flip();
        ++gamePly;
        var type = m.Flags;
        var from = m.From;
        var to = m.To;

        ref CastlingRights cc = ref MemoryMarshal.GetArrayDataReference(castleClear);
        History[gamePly].Castling = History[gamePly - 1].Castling & ~(Unsafe.Add(ref cc, (int)from) | Unsafe.Add(ref cc, (int)to));
        History[gamePly].EnPassantSquare = Square.NoSquare;
        History[gamePly].Captured = Piece.NoPiece;

        switch (type)
        {
            case MoveFlags.Quiet:
                MovePieceQuietNoHash(from, to);
                break;
            case MoveFlags.DoublePush:
                MovePieceQuietNoHash(from, to);
                History[gamePly].EnPassantSquare =
                    (Square)((int)from + (int)Types.RelativeDir(us, Direction.North));
                break;
            case MoveFlags.OO:
                if (Chess960) DoCastleFrc(us, from, CastleIndex(us, true), hashed: false);
                else if (us == Color.White) { MovePieceQuietNoHash(Square.e1, Square.g1); MovePieceQuietNoHash(Square.h1, Square.f1); }
                else { MovePieceQuietNoHash(Square.e8, Square.g8); MovePieceQuietNoHash(Square.h8, Square.f8); }
                break;
            case MoveFlags.OOO:
                if (Chess960) DoCastleFrc(us, from, CastleIndex(us, false), hashed: false);
                else if (us == Color.White) { MovePieceQuietNoHash(Square.e1, Square.c1); MovePieceQuietNoHash(Square.a1, Square.d1); }
                else { MovePieceQuietNoHash(Square.e8, Square.c8); MovePieceQuietNoHash(Square.a8, Square.d8); }
                break;
            case MoveFlags.EnPassant:
                MovePieceQuietNoHash(from, to);
                RemovePieceNoHash((Square)((int)to + (int)Types.RelativeDir(us, Direction.South)));
                break;
            case MoveFlags.PrKnight:
                RemovePieceNoHash(from); PutPieceNoHash(Types.MakePiece(us, PieceType.Knight), to);
                break;
            case MoveFlags.PrBishop:
                RemovePieceNoHash(from); PutPieceNoHash(Types.MakePiece(us, PieceType.Bishop), to);
                break;
            case MoveFlags.PrRook:
                RemovePieceNoHash(from); PutPieceNoHash(Types.MakePiece(us, PieceType.Rook), to);
                break;
            case MoveFlags.PrQueen:
                RemovePieceNoHash(from); PutPieceNoHash(Types.MakePiece(us, PieceType.Queen), to);
                break;
            case MoveFlags.PcKnight:
                {
                    Piece cap = board[(int)to]; History[gamePly].Captured = cap;
                    RemovePieceNoHash(from); RemovePieceKnownNoHash(to, cap);
                    PutPieceNoHash(Types.MakePiece(us, PieceType.Knight), to);
                    break;
                }
            case MoveFlags.PcBishop:
                {
                    Piece cap = board[(int)to]; History[gamePly].Captured = cap;
                    RemovePieceNoHash(from); RemovePieceKnownNoHash(to, cap);
                    PutPieceNoHash(Types.MakePiece(us, PieceType.Bishop), to);
                    break;
                }
            case MoveFlags.PcRook:
                {
                    Piece cap = board[(int)to]; History[gamePly].Captured = cap;
                    RemovePieceNoHash(from); RemovePieceKnownNoHash(to, cap);
                    PutPieceNoHash(Types.MakePiece(us, PieceType.Rook), to);
                    break;
                }
            case MoveFlags.PcQueen:
                {
                    Piece cap = board[(int)to]; History[gamePly].Captured = cap;
                    RemovePieceNoHash(from); RemovePieceKnownNoHash(to, cap);
                    PutPieceNoHash(Types.MakePiece(us, PieceType.Queen), to);
                    break;
                }
            case MoveFlags.Capture:
                {
                    Piece cap = board[(int)to]; History[gamePly].Captured = cap;
                    MovePieceKnownCaptureNoHash(from, to, cap);
                    break;
                }
        }
    }

    public void UndoPerft(Color us, Move m)
    {
        var from = m.From;
        var to = m.To;
        switch (m.Flags)
        {
            case MoveFlags.Quiet:
            case MoveFlags.DoublePush:
                MovePieceQuietNoHash(to, from);
                break;
            case MoveFlags.OO:
                if (Chess960) UndoCastleFrc(us, from, CastleIndex(us, true), hashed: false);
                else if (us == Color.White) { MovePieceQuietNoHash(Square.g1, Square.e1); MovePieceQuietNoHash(Square.f1, Square.h1); }
                else { MovePieceQuietNoHash(Square.g8, Square.e8); MovePieceQuietNoHash(Square.f8, Square.h8); }
                break;
            case MoveFlags.OOO:
                if (Chess960) UndoCastleFrc(us, from, CastleIndex(us, false), hashed: false);
                else if (us == Color.White) { MovePieceQuietNoHash(Square.c1, Square.e1); MovePieceQuietNoHash(Square.d1, Square.a1); }
                else { MovePieceQuietNoHash(Square.c8, Square.e8); MovePieceQuietNoHash(Square.d8, Square.a8); }
                break;
            case MoveFlags.EnPassant:
                MovePieceQuietNoHash(to, from);
                PutPieceNoHash(Types.MakePiece(us.Flip(), PieceType.Pawn),
                        (Square)((int)to + (int)Types.RelativeDir(us, Direction.South)));
                break;
            case MoveFlags.PrKnight:
            case MoveFlags.PrBishop:
            case MoveFlags.PrRook:
            case MoveFlags.PrQueen:
                RemovePieceNoHash(to); PutPieceNoHash(Types.MakePiece(us, PieceType.Pawn), from);
                break;
            case MoveFlags.PcKnight:
            case MoveFlags.PcBishop:
            case MoveFlags.PcRook:
            case MoveFlags.PcQueen:
                RemovePieceNoHash(to); PutPieceNoHash(Types.MakePiece(us, PieceType.Pawn), from);
                PutPieceNoHash(History[gamePly].Captured, to);
                break;
            case MoveFlags.Capture:
                MovePieceQuietNoHash(to, from);
                PutPieceNoHash(History[gamePly].Captured, to);
                break;
        }
        sideToPlay = sideToPlay.Flip();
        --gamePly;
        if (NnueSt != null) Engine.Nnue.OnUndo(this);
    }

    public ulong KeyAfter(Color us, Move m)
    {
        ref readonly UndoInfo cur = ref History[gamePly];
        ulong k = hash ^ StateHash(cur.Castling, cur.EnPassantSquare) ^ Zobrist.SideToMove;
        Square from = m.From, to = m.To;
        Piece pc = board[(int)from];
        CastlingRights castling = cur.Castling & ~(castleClear[(int)from] | castleClear[(int)to]);
        Square ep = Square.NoSquare;
        switch (m.Flags)
        {
            case MoveFlags.Quiet:
                k ^= Zobrist.Piece(pc, from) ^ Zobrist.Piece(pc, to);
                break;
            case MoveFlags.DoublePush:
                k ^= Zobrist.Piece(pc, from) ^ Zobrist.Piece(pc, to);
                ep = (Square)((int)from + (int)Types.RelativeDir(us, Direction.North));
                break;
            case MoveFlags.OO:
            case MoveFlags.OOO:
            {
                Piece king = Types.MakePiece(us, PieceType.King);
                Piece rook = Types.MakePiece(us, PieceType.Rook);
                Square kFrom, kTo, rFrom, rTo;
                if (Chess960)
                {
                    int idx = CastleIndex(us, m.Flags == MoveFlags.OO);
                    kFrom = from; kTo = castleKingTo[idx]; rFrom = castleRookFrom[idx]; rTo = castleRookTo[idx];
                }
                else
                {
                    int e = us == Color.White ? (int)Square.e1 : (int)Square.e8;
                    bool kingside = m.Flags == MoveFlags.OO;
                    kFrom = (Square)e; kTo = (Square)(kingside ? e + 2 : e - 2);
                    rFrom = (Square)(kingside ? e + 3 : e - 4); rTo = (Square)(kingside ? e + 1 : e - 1);
                }
                k ^= Zobrist.Piece(king, kFrom) ^ Zobrist.Piece(king, kTo) ^ Zobrist.Piece(rook, rFrom) ^ Zobrist.Piece(rook, rTo);
                break;
            }
            case MoveFlags.EnPassant:
                k ^= Zobrist.Piece(pc, from) ^ Zobrist.Piece(pc, to)
                   ^ Zobrist.Piece(Types.MakePiece(us.Flip(), PieceType.Pawn), (Square)((int)to + (int)Types.RelativeDir(us, Direction.South)));
                break;
            case MoveFlags.PrKnight:
            case MoveFlags.PrBishop:
            case MoveFlags.PrRook:
            case MoveFlags.PrQueen:
                k ^= Zobrist.Piece(pc, from) ^ Zobrist.Piece(Types.MakePiece(us, (PieceType)(((int)m.Flags & 3) + 1)), to);
                break;
            case MoveFlags.PcKnight:
            case MoveFlags.PcBishop:
            case MoveFlags.PcRook:
            case MoveFlags.PcQueen:
                k ^= Zobrist.Piece(pc, from) ^ Zobrist.Piece(board[(int)to], to)
                   ^ Zobrist.Piece(Types.MakePiece(us, (PieceType)(((int)m.Flags & 3) + 1)), to);
                break;
            case MoveFlags.Capture:
                k ^= Zobrist.Piece(pc, from) ^ Zobrist.Piece(pc, to) ^ Zobrist.Piece(board[(int)to], to);
                break;
        }
        return k ^ StateHash(castling, ep);
    }

    public ulong KeyAfterNull()
    {
        ref readonly UndoInfo cur = ref History[gamePly];
        return hash ^ StateHash(cur.Castling, cur.EnPassantSquare) ^ Zobrist.SideToMove ^ StateHash(cur.Castling, Square.NoSquare);
    }

    public bool IsRepetition()
    {
        if (gamePly < 4) return false;

        int count = 0;
        for (int i = gamePly - 2; i >= 0; i -= 2)
        {
            if (i < 0) break;

            if (History[i].Hash == hash)
            {
                count++;
                if (count >= 2) return true;
            }

            if (History[i].HalfMoveClock == 0)
                break;
        }

        return false;
    }

    public bool HasRepeated()
    {
        if (gamePly < 4) return false;

        for (int i = gamePly - 2; i >= 0; i -= 2)
        {
            if (History[i].Hash == hash) return true;
            if (History[i].HalfMoveClock == 0) break;
        }

        return false;
    }

    public bool IsFiftyMoveRule()
    {
        return History[gamePly].HalfMoveClock >= 100;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        const string s = "   +---+---+---+---+---+---+---+---+\n";
        const string t = "     A   B   C   D   E   F   G   H\n";

        sb.Append(t);
        for (int i = 56; i >= 0; i -= 8)
        {
            sb.Append(s);
            sb.Append($" {i / 8 + 1} ");
            for (int j = 0; j < 8; j++)
                sb.Append($"| {Types.PieceSymbols[(int)board[i + j]]} ");
            sb.Append($"| {i / 8 + 1}\n");
        }
        sb.Append(s);
        sb.Append(t);
        sb.Append('\n');

        sb.Append($"FEN: {Fen()}\n");
        sb.Append($"Hash: 0x{hash:X}\n");

        return sb.ToString();
    }

    public string Fen()
    {
        var fen = new StringBuilder();
        int empty;

        for (int i = 56; i >= 0; i -= 8)
        {
            empty = 0;
            for (int j = 0; j < 8; j++)
            {
                Piece p = board[i + j];
                if (p == Piece.NoPiece)
                {
                    empty++;
                }
                else
                {
                    if (empty != 0)
                    {
                        fen.Append(empty);
                        empty = 0;
                    }
                    fen.Append(Types.PieceSymbols[(int)p]);
                }
            }

            if (empty != 0) fen.Append(empty);
            if (i > 0) fen.Append('/');
        }

        fen.Append(sideToPlay == Color.White ? " w " : " b ");

        var rights = History[gamePly].Castling;
        if (rights == CastlingRights.None)
        {
            fen.Append('-');
        }
        else if (Chess960)
        {
            if ((rights & CastlingRights.WhiteOO)  != 0) fen.Append((char)('A' + (int)Types.FileOf(castleRookFrom[0])));
            if ((rights & CastlingRights.WhiteOOO) != 0) fen.Append((char)('A' + (int)Types.FileOf(castleRookFrom[1])));
            if ((rights & CastlingRights.BlackOO)  != 0) fen.Append((char)('a' + (int)Types.FileOf(castleRookFrom[2])));
            if ((rights & CastlingRights.BlackOOO) != 0) fen.Append((char)('a' + (int)Types.FileOf(castleRookFrom[3])));
        }
        else
        {
            if ((rights & CastlingRights.WhiteOO)  != 0) fen.Append('K');
            if ((rights & CastlingRights.WhiteOOO) != 0) fen.Append('Q');
            if ((rights & CastlingRights.BlackOO)  != 0) fen.Append('k');
            if ((rights & CastlingRights.BlackOOO) != 0) fen.Append('q');
        }
        fen.Append(' ');

        fen.Append(History[gamePly].EnPassantSquare == Square.NoSquare
            ? "-"
            : Types.SquareNames[(int)History[gamePly].EnPassantSquare]);

        return fen.ToString();
    }

    public static void Set(string fen, Position p)
    {
        for (int i = 0; i < Types.SquareCount; i++)
            p.board[i] = Piece.NoPiece;
        for (int i = 0; i < Types.PieceCount; i++)
            p.pieceBB[i] = 0;
        p.colorBB[0] = 0;
        p.colorBB[1] = 0;
        p.sideToPlay = Color.White;
        p.gamePly = 0;
        p.hash = 0;
        p.Checkers = 0;
        p.Pinned = 0;
        p.History[0] = new UndoInfo();
        p.NnueSt?.Reset();

        int square = (int)Square.a8;
        int fenIdx = 0;

        while (fenIdx < fen.Length && fen[fenIdx] != ' ')
        {
            char ch = fen[fenIdx++];
            if (char.IsDigit(ch))
            {
                square += (ch - '0') * (int)Direction.East;
            }
            else if (ch == '/')
            {
                square += 2 * (int)Direction.South;
            }
            else
            {
                int pieceIdx = Types.PieceSymbols.IndexOf(ch);
                if (pieceIdx >= 0)
                {
                    p.PutPiece((Piece)pieceIdx, (Square)square);
                    square++;
                }
            }
        }

        if (fenIdx < fen.Length) fenIdx++;

        if (fenIdx < fen.Length)
        {
            p.sideToPlay = fen[fenIdx] == 'w' ? Color.White : Color.Black;
            fenIdx++;
            if (fenIdx < fen.Length) fenIdx++;
        }

        p.History[p.gamePly].Castling = CastlingRights.None;
        Array.Clear(p.castleClear, 0, Types.SquareCount);
        for (int i = 0; i < 4; i++)
        {
            p.castleRookFrom[i] = Square.NoSquare;
            p.castleKingTo[i] = Square.NoSquare;
            p.castleRookTo[i] = Square.NoSquare;
            p.castleEmpty[i] = 0;
            p.castleKingPath[i] = 0;
        }

        Square wK = Bitboard.Bsf(p.BitboardOf(Color.White, PieceType.King));
        Square bK = Bitboard.Bsf(p.BitboardOf(Color.Black, PieceType.King));

        while (fenIdx < fen.Length && fen[fenIdx] != ' ')
        {
            char ch = fen[fenIdx++];
            if (ch == '-') continue;

            Color c = char.IsUpper(ch) ? Color.White : Color.Black;
            Square k = c == Color.White ? wK : bK;
            if (k == Square.NoSquare) continue;

            int rank = (int)Types.RankOf(k);
            int kingFile = (int)Types.FileOf(k);
            Piece rookPiece = Types.MakePiece(c, PieceType.Rook);
            char up = char.ToUpperInvariant(ch);

            Square rookFrom;
            bool kingside;
            if (up == 'K')
            {
                kingside = true;
                rookFrom = p.FindCastleRook(rookPiece, rank, kingFile, true);
            }
            else if (up == 'Q')
            {
                kingside = false;
                rookFrom = p.FindCastleRook(rookPiece, rank, kingFile, false);
            }
            else if (up >= 'A' && up <= 'H')
            {
                int file = up - 'A';
                rookFrom = (Square)(rank * 8 + file);
                kingside = file > kingFile;
            }
            else continue;

            if (rookFrom == Square.NoSquare) continue;

            int idx = CastleIndex(c, kingside);
            p.History[p.gamePly].Castling |= CastleBit(idx);
            p.BuildCastleMeta(idx, k, rookFrom, kingside);
        }

        if (fenIdx < fen.Length) fenIdx++;

        if (fenIdx < fen.Length && fen[fenIdx] != '-')
        {
            if (fenIdx + 1 < fen.Length)
            {
                File f = (File)(fen[fenIdx] - 'a');
                Rank r = (Rank)(fen[fenIdx + 1] - '1');
                p.History[p.gamePly].EnPassantSquare = Types.CreateSquare(f, r);
            }
        }

        while (fenIdx < fen.Length && fen[fenIdx] != ' ') fenIdx++;
        if (fenIdx < fen.Length) fenIdx++;

        int halfMoveClock = 0;
        while (fenIdx < fen.Length && char.IsDigit(fen[fenIdx]))
            halfMoveClock = halfMoveClock * 10 + (fen[fenIdx++] - '0');
        p.History[p.gamePly].HalfMoveClock = halfMoveClock;

        if (p.sideToPlay == Color.Black)
            p.hash ^= Zobrist.SideToMove;

        p.hash ^= StateHash(
            p.History[p.gamePly].Castling,
            p.History[p.gamePly].EnPassantSquare);
        p.History[p.gamePly].Hash = p.hash;
    }
}
