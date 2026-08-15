using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;

namespace Zoomies.Core;

public static class Tables
{
    private const ulong NotFileA = 0xfefefefefefefefeUL;
    private const ulong NotFileH = 0x7f7f7f7f7f7f7f7fUL;
    private const ulong NotFilesAB = 0xfcfcfcfcfcfcfcfcUL;
    private const ulong NotFilesGH = 0x3f3f3f3f3f3f3f3fUL;

    private static readonly ulong[] KingAttackMasks = new ulong[Types.SquareCount];
    private static readonly ulong[] KnightAttackMasks = new ulong[Types.SquareCount];
    private static readonly ulong[] WhitePawnAttackMasks = new ulong[Types.SquareCount];
    private static readonly ulong[] BlackPawnAttackMasks = new ulong[Types.SquareCount];

    private static void InitializeStepAttacks()
    {
        for (Square square = Square.a1; square <= Square.h8; square++)
        {
            ulong b = Bitboard.SquareMask(square);

            ulong adjacent = ((b << 1) & NotFileA) | ((b >> 1) & NotFileH);
            ulong kingRow = adjacent | b;
            KingAttackMasks[(int)square] = adjacent | (kingRow << 8) | (kingRow >> 8);

            ulong oneFileOut = adjacent;
            ulong twoFilesOut = ((b << 2) & NotFilesAB) | ((b >> 2) & NotFilesGH);
            KnightAttackMasks[(int)square] =
                (oneFileOut << 16) | (oneFileOut >> 16) |
                (twoFilesOut << 8) | (twoFilesOut >> 8);

            WhitePawnAttackMasks[(int)square] = PawnAttacks(Color.White, b);
            BlackPawnAttackMasks[(int)square] = PawnAttacks(Color.Black, b);
        }
    }

    public static ulong Reverse(ulong b)
    {
        b = (b & 0x5555555555555555UL) << 1 | (b >> 1) & 0x5555555555555555UL;
        b = (b & 0x3333333333333333UL) << 2 | (b >> 2) & 0x3333333333333333UL;
        b = (b & 0x0f0f0f0f0f0f0f0fUL) << 4 | (b >> 4) & 0x0f0f0f0f0f0f0f0fUL;
        b = (b & 0x00ff00ff00ff00ffUL) << 8 | (b >> 8) & 0x00ff00ff00ff00ffUL;

        return (b << 48) | ((b & 0xffff0000UL) << 16) |
            ((b >> 16) & 0xffff0000UL) | (b >> 48);
    }

    public static ulong SlidingAttacks(Square square, ulong occupancy, ulong lineMask)
    {
        ulong slider = Bitboard.SquareMask(square);
        ulong occupiedLine = lineMask & occupancy;

        return ((occupiedLine - slider * 2) ^
            Reverse(Reverse(occupiedLine) - Reverse(slider) * 2)) & lineMask;
    }

    private static ulong CalculateRookAttacks(Square square, ulong occupancy)
    {
        ulong fileAttacks = SlidingAttacks(
            square,
            occupancy,
            Bitboard.FileMask(Types.FileOf(square)));
        ulong rankAttacks = SlidingAttacks(
            square,
            occupancy,
            Bitboard.RankMask(Types.RankOf(square)));

        return fileAttacks | rankAttacks;
    }

    public static readonly ulong[] RookRelevantOccupancyMasks = new ulong[64];
    public static readonly int[] RookIndexShifts = new int[64];

    public static readonly int[] RookTableOffsets = new int[64];

    #pragma warning disable CA2211
    public static ulong[] RookAttackTable = null!;
    #pragma warning restore CA2211

    public static ReadOnlySpan<ulong> RookMagicNumbers =>
    [
        0x0080001020400080UL, 0x0040001000200040UL, 0x0080081000200080UL, 0x0080040800100080UL,
        0x0080020400080080UL, 0x0080010200040080UL, 0x0080008001000200UL, 0x0080002040800100UL,
        0x0000800020400080UL, 0x0000400020005000UL, 0x0000801000200080UL, 0x0000800800100080UL,
        0x0000800400080080UL, 0x0000800200040080UL, 0x0000800100020080UL, 0x0000800040800100UL,
        0x0000208000400080UL, 0x0000404000201000UL, 0x0000808010002000UL, 0x0000808008001000UL,
        0x0000808004000800UL, 0x0000808002000400UL, 0x0000010100020004UL, 0x0000020000408104UL,
        0x0000208080004000UL, 0x0000200040005000UL, 0x0000100080200080UL, 0x0000080080100080UL,
        0x0000040080080080UL, 0x0000020080040080UL, 0x0000010080800200UL, 0x0000800080004100UL,
        0x0000204000800080UL, 0x0000200040401000UL, 0x0000100080802000UL, 0x0000080080801000UL,
        0x0000040080800800UL, 0x0000020080800400UL, 0x0000020001010004UL, 0x0000800040800100UL,
        0x0000204000808000UL, 0x0000200040008080UL, 0x0000100020008080UL, 0x0000080010008080UL,
        0x0000040008008080UL, 0x0000020004008080UL, 0x0000010002008080UL, 0x0000004081020004UL,
        0x0000204000800080UL, 0x0000200040008080UL, 0x0000100020008080UL, 0x0000080010008080UL,
        0x0000040008008080UL, 0x0000020004008080UL, 0x0000800100020080UL, 0x0000800041000080UL,
        0x00FFFCDDFCED714AUL, 0x007FFCDDFCED714AUL, 0x003FFFCDFFD88096UL, 0x0000040810002101UL,
        0x0001000204080011UL, 0x0001000204000801UL, 0x0001000082000401UL, 0x0001FFFAABFAD1A2UL
    ];

    private static void InitializeRookAttacks()
    {
        int tableSize = 0;
        for (Square square = Square.a1; square <= Square.h8; square++)
        {
            int squareIndex = (int)square;
            ulong occupancyMask = RookOccupancyMask(square);
            int relevantBitCount = Bitboard.PopCount(occupancyMask);

            RookRelevantOccupancyMasks[squareIndex] = occupancyMask;
            RookIndexShifts[squareIndex] = 64 - relevantBitCount;
            RookTableOffsets[squareIndex] = tableSize;
            tableSize += 1 << relevantBitCount;
        }

        RookAttackTable = new ulong[tableSize];
        for (Square square = Square.a1; square <= Square.h8; square++)
        {
            int squareIndex = (int)square;
            ulong occupancyMask = RookRelevantOccupancyMasks[squareIndex];
            int tableOffset = RookTableOffsets[squareIndex];
            ulong occupancy = 0;

            do
            {
                ulong index = Bmi2.X64.IsSupported
                    ? Bmi2.X64.ParallelBitExtract(occupancy, occupancyMask)
                    : (occupancy * RookMagicNumbers[squareIndex]) >> RookIndexShifts[squareIndex];

                RookAttackTable[tableOffset + (int)index] =
                    CalculateRookAttacks(square, occupancy);
                occupancy = (occupancy - occupancyMask) & occupancyMask;
            } while (occupancy != 0);
        }
    }

    private static ulong RookOccupancyMask(Square square)
    {
        ulong movementLines =
            Bitboard.RankMask(Types.RankOf(square)) |
            Bitboard.FileMask(Types.FileOf(square));

        return movementLines & ~BoardEdgesExceptSquare(square) & ~Bitboard.SquareMask(square);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong RookAttacks(Square square, ulong occupancy)
    {
        int squareIndex = (int)square;
        ulong occupancyMask = RookRelevantOccupancyMasks[squareIndex];
        int tableIndex = Bmi2.X64.IsSupported
            ? RookTableOffsets[squareIndex] +
                (int)Bmi2.X64.ParallelBitExtract(occupancy, occupancyMask)
            : RookTableOffsets[squareIndex] +
                (int)(((occupancy & occupancyMask) * RookMagicNumbers[squareIndex]) >>
                    RookIndexShifts[squareIndex]);

        return Unsafe.Add(
            ref MemoryMarshal.GetArrayDataReference(RookAttackTable),
            tableIndex);
    }

    public static ulong XrayRookAttacks(Square square, ulong occupancy, ulong blockers)
    {
        ulong attacks = RookAttacks(square, occupancy);
        blockers &= attacks;
        return attacks ^ RookAttacks(square, occupancy ^ blockers);
    }

    private static ulong CalculateBishopAttacks(Square square, ulong occupancy)
    {
        ulong diagonalAttacks = SlidingAttacks(
            square,
            occupancy,
            Bitboard.DiagonalMask(Types.DiagonalOf(square)));
        ulong antiDiagonalAttacks = SlidingAttacks(
            square,
            occupancy,
            Bitboard.AntiDiagonalMask(Types.AntiDiagonalOf(square)));

        return diagonalAttacks | antiDiagonalAttacks;
    }

    public static readonly ulong[] BishopRelevantOccupancyMasks = new ulong[64];
    public static readonly int[] BishopIndexShifts = new int[64];
    public static readonly int[] BishopTableOffsets = new int[64];

    #pragma warning disable CA2211
    public static ulong[] BishopAttackTable = null!;
    #pragma warning restore CA2211

    public static ReadOnlySpan<ulong> BishopMagicNumbers =>
    [
        0x0002020202020200UL, 0x0002020202020000UL, 0x0004010202000000UL, 0x0004040080000000UL,
        0x0001104000000000UL, 0x0000821040000000UL, 0x0000410410400000UL, 0x0000104104104000UL,
        0x0000040404040400UL, 0x0000020202020200UL, 0x0000040102020000UL, 0x0000040400800000UL,
        0x0000011040000000UL, 0x0000008210400000UL, 0x0000004104104000UL, 0x0000002082082000UL,
        0x0004000808080800UL, 0x0002000404040400UL, 0x0001000202020200UL, 0x0000800802004000UL,
        0x0000800400A00000UL, 0x0000200100884000UL, 0x0000400082082000UL, 0x0000200041041000UL,
        0x0002080010101000UL, 0x0001040008080800UL, 0x0000208004010400UL, 0x0000404004010200UL,
        0x0000840000802000UL, 0x0000404002011000UL, 0x0000808001041000UL, 0x0000404000820800UL,
        0x0001041000202000UL, 0x0000820800101000UL, 0x0000104400080800UL, 0x0000020080080080UL,
        0x0000404040040100UL, 0x0000808100020100UL, 0x0001010100020800UL, 0x0000808080010400UL,
        0x0000820820004000UL, 0x0000410410002000UL, 0x0000082088001000UL, 0x0000002011000800UL,
        0x0000080100400400UL, 0x0001010101000200UL, 0x0002020202000400UL, 0x0001010101000200UL,
        0x0000410410400000UL, 0x0000208208200000UL, 0x0000002084100000UL, 0x0000000020880000UL,
        0x0000001002020000UL, 0x0000040408020000UL, 0x0004040404040000UL, 0x0002020202020000UL,
        0x0000104104104000UL, 0x0000002082082000UL, 0x0000000020841000UL, 0x0000000000208800UL,
        0x0000000010020200UL, 0x0000000404080200UL, 0x0000040404040400UL, 0x0002020202020200UL
    ];

    private static void InitializeBishopAttacks()
    {
        int tableSize = 0;
        for (Square square = Square.a1; square <= Square.h8; square++)
        {
            int squareIndex = (int)square;
            ulong occupancyMask = BishopOccupancyMask(square);
            int relevantBitCount = Bitboard.PopCount(occupancyMask);

            BishopRelevantOccupancyMasks[squareIndex] = occupancyMask;
            BishopIndexShifts[squareIndex] = 64 - relevantBitCount;
            BishopTableOffsets[squareIndex] = tableSize;
            tableSize += 1 << relevantBitCount;
        }

        BishopAttackTable = new ulong[tableSize];
        for (Square square = Square.a1; square <= Square.h8; square++)
        {
            int squareIndex = (int)square;
            ulong occupancyMask = BishopRelevantOccupancyMasks[squareIndex];
            int tableOffset = BishopTableOffsets[squareIndex];
            ulong occupancy = 0;

            do
            {
                ulong index = Bmi2.X64.IsSupported
                    ? Bmi2.X64.ParallelBitExtract(occupancy, occupancyMask)
                    : (occupancy * BishopMagicNumbers[squareIndex]) >> BishopIndexShifts[squareIndex];

                BishopAttackTable[tableOffset + (int)index] =
                    CalculateBishopAttacks(square, occupancy);
                occupancy = (occupancy - occupancyMask) & occupancyMask;
            } while (occupancy != 0);
        }
    }

    private static ulong BishopOccupancyMask(Square square)
    {
        ulong movementLines =
            Bitboard.DiagonalMask(Types.DiagonalOf(square)) |
            Bitboard.AntiDiagonalMask(Types.AntiDiagonalOf(square));

        return movementLines & ~BoardEdgesExceptSquare(square) & ~Bitboard.SquareMask(square);
    }

    private static ulong BoardEdgesExceptSquare(Square square)
    {
        ulong firstAndLastRanks =
            Bitboard.RankMask(Rank.Rank1) |
            Bitboard.RankMask(Rank.Rank8);
        ulong firstAndLastFiles =
            Bitboard.FileMask(File.FileA) |
            Bitboard.FileMask(File.FileH);

        ulong squareRank = Bitboard.RankMask(Types.RankOf(square));
        ulong squareFile = Bitboard.FileMask(Types.FileOf(square));

        return (firstAndLastRanks & ~squareRank) |
            (firstAndLastFiles & ~squareFile);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong BishopAttacks(Square square, ulong occupancy)
    {
        int squareIndex = (int)square;
        ulong occupancyMask = BishopRelevantOccupancyMasks[squareIndex];
        int tableIndex = Bmi2.X64.IsSupported
            ? BishopTableOffsets[squareIndex] +
                (int)Bmi2.X64.ParallelBitExtract(occupancy, occupancyMask)
            : BishopTableOffsets[squareIndex] +
                (int)(((occupancy & occupancyMask) * BishopMagicNumbers[squareIndex]) >>
                    BishopIndexShifts[squareIndex]);

        return Unsafe.Add(
            ref MemoryMarshal.GetArrayDataReference(BishopAttackTable),
            tableIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong KnightAttacks(Square square) => KnightAttackMasks[(int)square];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong KingAttacks(Square square) => KingAttackMasks[(int)square];

    public static ulong XrayBishopAttacks(Square square, ulong occupancy, ulong blockers)
    {
        ulong attacks = BishopAttacks(square, occupancy);
        blockers &= attacks;
        return attacks ^ BishopAttacks(square, occupancy ^ blockers);
    }

    public static readonly ulong[] SquaresBetween = new ulong[64 * 64];

    private static void InitializeSquaresBetween()
    {
        ulong sqs;
        for (Square sq1 = Square.a1; sq1 <= Square.h8; sq1++)
        {
            for (Square sq2 = Square.a1; sq2 <= Square.h8; sq2++)
            {
                sqs = Bitboard.SquareMask(sq1) | Bitboard.SquareMask(sq2);
                if (Types.FileOf(sq1) == Types.FileOf(sq2) || Types.RankOf(sq1) == Types.RankOf(sq2))
                    SquaresBetween[((int)sq1 << 6) | (int)sq2] =
                        CalculateRookAttacks(sq1, sqs) & CalculateRookAttacks(sq2, sqs);
                else if (Types.DiagonalOf(sq1) == Types.DiagonalOf(sq2) || Types.AntiDiagonalOf(sq1) == Types.AntiDiagonalOf(sq2))
                    SquaresBetween[((int)sq1 << 6) | (int)sq2] =
                        CalculateBishopAttacks(sq1, sqs) & CalculateBishopAttacks(sq2, sqs);
            }
        }
    }

    public static readonly ulong[] LineMasks = new ulong[64 * 64];

    private static void InitializeLines()
    {
        for (Square sq1 = Square.a1; sq1 <= Square.h8; sq1++)
        {
            for (Square sq2 = Square.a1; sq2 <= Square.h8; sq2++)
            {
                if (Types.FileOf(sq1) == Types.FileOf(sq2) || Types.RankOf(sq1) == Types.RankOf(sq2))
                    LineMasks[((int)sq1 << 6) | (int)sq2] =
                        CalculateRookAttacks(sq1, 0) & CalculateRookAttacks(sq2, 0)
                        | Bitboard.SquareMask(sq1) | Bitboard.SquareMask(sq2);
                else if (Types.DiagonalOf(sq1) == Types.DiagonalOf(sq2) || Types.AntiDiagonalOf(sq1) == Types.AntiDiagonalOf(sq2))
                    LineMasks[((int)sq1 << 6) | (int)sq2] =
                        CalculateBishopAttacks(sq1, 0) & CalculateBishopAttacks(sq2, 0)
                        | Bitboard.SquareMask(sq1) | Bitboard.SquareMask(sq2);
            }
        }
    }

    public static readonly ulong[][] PawnAttackMasksByColor = new ulong[Types.ColorCount][];
    public static readonly ulong[][] PseudoLegalAttackMasks = new ulong[Types.PieceTypeCount][];

    private static void InitializePseudoLegalAttacks()
    {
        PawnAttackMasksByColor[(int)Color.White] = WhitePawnAttackMasks;
        PawnAttackMasksByColor[(int)Color.Black] = BlackPawnAttackMasks;

        for (int i = 0; i < Types.PieceTypeCount; i++)
        {
            PseudoLegalAttackMasks[i] = new ulong[Types.SquareCount];
        }

        PseudoLegalAttackMasks[(int)PieceType.Knight] = KnightAttackMasks;
        PseudoLegalAttackMasks[(int)PieceType.King] = KingAttackMasks;

        for (Square s = Square.a1; s <= Square.h8; s++)
        {
            PseudoLegalAttackMasks[(int)PieceType.Rook][(int)s] = CalculateRookAttacks(s, 0);
            PseudoLegalAttackMasks[(int)PieceType.Bishop][(int)s] = CalculateBishopAttacks(s, 0);
            PseudoLegalAttackMasks[(int)PieceType.Queen][(int)s] =
                PseudoLegalAttackMasks[(int)PieceType.Rook][(int)s] |
                PseudoLegalAttackMasks[(int)PieceType.Bishop][(int)s];
        }
    }
    public static void Initialize()
    {
        InitializeStepAttacks();
        InitializeRookAttacks();
        InitializeBishopAttacks();
        InitializeSquaresBetween();
        InitializeLines();
        InitializePseudoLegalAttacks();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Attacks(PieceType pt, Square s, ulong occ)
    {
        if (s == Square.NoSquare || (int)s >= 64)
            return 0UL;

        return pt switch
        {
            PieceType.Pawn => throw new ArgumentException("The piece type may not be a pawn; use PawnAttacks instead"),
            PieceType.Rook => RookAttacks(s, occ),
            PieceType.Bishop => BishopAttacks(s, occ),
            PieceType.Queen => RookAttacks(s, occ) | BishopAttacks(s, occ),
            _ => PseudoLegalAttackMasks[(int)pt][(int)s],
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong PawnAttacks(Color c, ulong p)
    {
        return c == Color.White ?
            ((p & NotFileA) << 7) | ((p & NotFileH) << 9) :
            ((p & NotFileA) >> 9) | ((p & NotFileH) >> 7);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong PawnAttacks(Color c, Square s)
    {
        if (s == Square.NoSquare)
            return 0;
        return c == Color.White
            ? WhitePawnAttackMasks[(int)s]
            : BlackPawnAttackMasks[(int)s];
    }
}
