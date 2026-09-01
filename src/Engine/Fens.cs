namespace Zoomies.Engine;

public static class Fens
{
    // Standard
    public const string Startpos   = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq -";
    public const string Kiwipete   = "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq -";
    public const string Endgame    = "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - -";
    public const string Tactical   = "r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq -";
    public const string Promotions = "rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8";
    public const string Midgame    = "r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10";

    // FRC
    public const string Frc518     = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w HAha -";
    public const string Frc960a    = "bqnbnrkr/pppppppp/8/8/8/8/PPPPPPPP/BQNBNRKR w KQkq - 0 1";
    public const string Frc960b    = "brnnkbqr/pppppppp/8/8/8/8/PPPPPPPP/BRNNKBQR w KQkq - 0 1";
    public const string Frc960c    = "rbbkqrnn/pppppppp/8/8/8/8/PPPPPPPP/RBBKQRNN w KQkq - 0 1";

    // Standard Test Closed
    public const string C_O = "r1b1kbnr/pp3ppp/1qn1p3/2ppP3/3P4/P1P2N2/1P3PPP/RNBQKB1R b KQkq - 0 6";      // French Advance main line
    public const string C_M = "r1bq1rk1/pppnn1bp/3p4/3Pp1p1/2P1Pp2/2N2P2/PP2BBPP/R2QNRK1 w - - 0 13";      // KID Mar del Plata
    public const string C_E = "2b5/3k1p2/3p2p1/2pPp2p/2P1P2P/3KN1P1/5P2/8 w - - 0 40";                     // Knight vs bad bishop

    // Standard Test Open
    public const string O_O = "r1bqkbnr/pppp1ppp/2n5/8/3NP3/8/PPP2PPP/RNBQKB1R b KQkq - 0 4";              // Scotch
    public const string O_M = "3rr1k1/ppq1bppp/2n1bn2/2p3B1/2P5/1BN2N2/PPQ2PPP/3RR1K1 b - - 0 15";         // Open d/e files
    public const string O_E = "r7/5pkp/6p1/8/pP6/6P1/5PKP/1R6 w - - 0 45";                                 // Rook endgame

    // Standard Test Dynamic
    public const string D_O = "rn1qkb1r/1p3ppp/p2pbn2/4p3/4P3/1NN1BP2/PPP3PP/R2QKB1R b KQkq - 0 8";        // Najdorf English Attack
    public const string D_M = "2rq1rk1/pp1bppbp/3p1np1/4n3/3NP2P/1BN1BP2/PPPQ2P1/2KR3R b - - 0 12";        // Dragon Yugoslav
    public const string D_E = "8/5p1k/6p1/4q2p/P1p5/7P/5PP1/3Q2K1 w - - 0 50";                             // Queen endgame
}
