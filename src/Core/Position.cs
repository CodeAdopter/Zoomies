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

    private int whiteEvaluation;

    public int WhiteEvaluation => whiteEvaluation;

    public readonly UndoInfo[] History = new UndoInfo[2048];

    public ulong Checkers { get; internal set; }

    public ulong Pinned { get; internal set; }

    private static readonly CastlingRights[] CastleClear = BuildCastleClear();
    private static CastlingRights[] BuildCastleClear()
    {
        var m = new CastlingRights[Types.SquareCount];
        m[(int)Square.e1] = CastlingRights.White;
        m[(int)Square.a1] = CastlingRights.WhiteOOO;
        m[(int)Square.h1] = CastlingRights.WhiteOO;
        m[(int)Square.e8] = CastlingRights.Black;
        m[(int)Square.a8] = CastlingRights.BlackOOO;
        m[(int)Square.h8] = CastlingRights.BlackOO;
        return m;
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
        whiteEvaluation += Psqt.WhitePov[((int)pc << 6) | (int)s];
    }
    private void RemovePiece(Square s)
    {
        Piece pc = board[(int)s];
        ulong bb = 1UL << (int)s;
        hash ^= Zobrist.Piece(pc, s);
        pieceBB[(int)pc] &= ~bb;
        colorBB[((int)pc >> 3) & 1] &= ~bb;
        board[(int)s] = Piece.NoPiece;
        whiteEvaluation -= Psqt.WhitePov[((int)pc << 6) | (int)s];
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
        int pm = (int)movingPiece << 6;
        whiteEvaluation += Psqt.WhitePov[pm | (int)to] - Psqt.WhitePov[pm | (int)from];
        if (capturedPiece != Piece.NoPiece)
        {
            pieceBB[(int)capturedPiece] &= ~toBB;
            colorBB[((int)capturedPiece >> 3) & 1] &= ~toBB;
            whiteEvaluation -= Psqt.WhitePov[((int)capturedPiece << 6) | (int)to];
        }

        board[(int)to] = movingPiece;
        board[(int)from] = Piece.NoPiece;
    }

    public void MakeNullMove()
    {
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
    }

    private void MovePieceQuietNoHash(Square from, Square to)
    {
        Piece pc = board[(int)from];
        ulong fromTo = (1UL << (int)from) | (1UL << (int)to);
        pieceBB[(int)pc] ^= fromTo;
        colorBB[((int)pc >> 3) & 1] ^= fromTo;
        board[(int)to] = pc;
        board[(int)from] = Piece.NoPiece;
        int pmq = (int)pc << 6;
        whiteEvaluation += Psqt.WhitePov[pmq | (int)to] - Psqt.WhitePov[pmq | (int)from];
    }
    private void PutPieceNoHash(Piece pc, Square s)
    {
        board[(int)s] = pc;
        ulong bb = 1UL << (int)s;
        pieceBB[(int)pc] |= bb;
        colorBB[((int)pc >> 3) & 1] |= bb;
        whiteEvaluation += Psqt.WhitePov[((int)pc << 6) | (int)s];
    }
    private void RemovePieceNoHash(Square s)
    {
        Piece pc = board[(int)s];
        ulong bb = 1UL << (int)s;
        pieceBB[(int)pc] &= ~bb;
        colorBB[((int)pc >> 3) & 1] &= ~bb;
        board[(int)s] = Piece.NoPiece;
        whiteEvaluation -= Psqt.WhitePov[((int)pc << 6) | (int)s];
    }

    private void RemovePieceKnownNoHash(Square s, Piece pc)
    {
        ulong bb = 1UL << (int)s;
        pieceBB[(int)pc] &= ~bb;
        colorBB[((int)pc >> 3) & 1] &= ~bb;
        board[(int)s] = Piece.NoPiece;
        whiteEvaluation -= Psqt.WhitePov[((int)pc << 6) | (int)s];
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
        int pmk = (int)movingPiece << 6;
        whiteEvaluation += Psqt.WhitePov[pmk | (int)to] - Psqt.WhitePov[pmk | (int)from]
             - Psqt.WhitePov[((int)captured << 6) | (int)to];
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
            whiteEvaluation -= Psqt.WhitePov[((int)capturedPiece << 6) | (int)to];
        }
        board[(int)to] = movingPiece;
        board[(int)from] = Piece.NoPiece;
        int pmm = (int)movingPiece << 6;
        whiteEvaluation += Psqt.WhitePov[pmm | (int)to] - Psqt.WhitePov[pmm | (int)from];
    }
    
    public Position(Position other)
    {
        Array.Copy(other.pieceBB, pieceBB, Types.PieceCount);
        Array.Copy(other.colorBB, colorBB, Types.ColorCount);
        Array.Copy(other.board, board, Types.SquareCount);
        sideToPlay = other.sideToPlay;
        gamePly = other.gamePly;
        hash = other.hash;
        whiteEvaluation = other.whiteEvaluation;
        Array.Copy(other.History, History, gamePly + 1);
        Checkers = other.Checkers;
        Pinned = other.Pinned;
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
        int pm = (int)pc << 6;
        whiteEvaluation += Psqt.WhitePov[pm | (int)to] - Psqt.WhitePov[pm | (int)from];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong BitboardOf(Piece pc) => Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(pieceBB), (int)pc);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong BitboardOf(Color c, PieceType pt) =>
        Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(pieceBB), (int)Types.MakePiece(c, pt));
    public Piece At(Square sq) => board[(int)sq];
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
        return c == Color.White ?
            (Tables.PawnAttacks(Color.Black, s) & pieceBB[(int)Piece.WhitePawn]) |
            (Tables.Attacks(PieceType.Knight, s, occ) & pieceBB[(int)Piece.WhiteKnight]) |
            (Tables.Attacks(PieceType.Bishop, s, occ) & (pieceBB[(int)Piece.WhiteBishop] | pieceBB[(int)Piece.WhiteQueen])) |
            (Tables.Attacks(PieceType.Rook, s, occ) & (pieceBB[(int)Piece.WhiteRook] | pieceBB[(int)Piece.WhiteQueen])) :
            (Tables.PawnAttacks(Color.White, s) & pieceBB[(int)Piece.BlackPawn]) |
            (Tables.Attacks(PieceType.Knight, s, occ) & pieceBB[(int)Piece.BlackKnight]) |
            (Tables.Attacks(PieceType.Bishop, s, occ) & (pieceBB[(int)Piece.BlackBishop] | pieceBB[(int)Piece.BlackQueen])) |
            (Tables.Attacks(PieceType.Rook, s, occ) & (pieceBB[(int)Piece.BlackRook] | pieceBB[(int)Piece.BlackQueen]));
    }
    public bool InCheck(Color c)
    {
        var kingSquare = Bitboard.Bsf(BitboardOf(c, PieceType.King));
        return AttackersFrom(c.Flip(), kingSquare, AllPieces(Color.White) | AllPieces(Color.Black)) != 0;
    }

    public void Play(Color us, Move m)
    {
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

        History[gamePly].Castling &= ~(CastleClear[(int)m.From] | CastleClear[(int)m.To]);

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
                if (us == Color.White)
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
                if (us == Color.White)
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
                if (us == Color.White)
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
                if (us == Color.White)
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
    }

    public void PlayPerft(Color us, Move m)
    {
        sideToPlay = sideToPlay.Flip();
        ++gamePly;
        var type = m.Flags;
        var from = m.From;
        var to = m.To;

        ref CastlingRights cc = ref MemoryMarshal.GetArrayDataReference(CastleClear);
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
                if (us == Color.White) { MovePieceQuietNoHash(Square.e1, Square.g1); MovePieceQuietNoHash(Square.h1, Square.f1); }
                else { MovePieceQuietNoHash(Square.e8, Square.g8); MovePieceQuietNoHash(Square.h8, Square.f8); }
                break;
            case MoveFlags.OOO:
                if (us == Color.White) { MovePieceQuietNoHash(Square.e1, Square.c1); MovePieceQuietNoHash(Square.a1, Square.d1); }
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
                if (us == Color.White) { MovePieceQuietNoHash(Square.g1, Square.e1); MovePieceQuietNoHash(Square.f1, Square.h1); }
                else { MovePieceQuietNoHash(Square.g8, Square.e8); MovePieceQuietNoHash(Square.f8, Square.h8); }
                break;
            case MoveFlags.OOO:
                if (us == Color.White) { MovePieceQuietNoHash(Square.c1, Square.e1); MovePieceQuietNoHash(Square.d1, Square.a1); }
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
            if (i < 0) break;
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
        else
        {
            if ((rights & CastlingRights.WhiteOO) != 0) fen.Append('K');
            if ((rights & CastlingRights.WhiteOOO) != 0) fen.Append('Q');
            if ((rights & CastlingRights.BlackOO) != 0) fen.Append('k');
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
        p.whiteEvaluation = 0;
        p.Checkers = 0;
        p.Pinned = 0;
        p.History[0] = new UndoInfo();

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
        while (fenIdx < fen.Length && fen[fenIdx] != ' ')
        {
            switch (fen[fenIdx++])
            {
                case 'K': p.History[p.gamePly].Castling |= CastlingRights.WhiteOO; break;
                case 'Q': p.History[p.gamePly].Castling |= CastlingRights.WhiteOOO; break;
                case 'k': p.History[p.gamePly].Castling |= CastlingRights.BlackOO; break;
                case 'q': p.History[p.gamePly].Castling |= CastlingRights.BlackOOO; break;
            }
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

        if (p.sideToPlay == Color.Black)
            p.hash ^= Zobrist.SideToMove;

        p.hash ^= StateHash(
            p.History[p.gamePly].Castling,
            p.History[p.gamePly].EnPassantSquare);
        p.History[p.gamePly].Hash = p.hash;
    }
}
