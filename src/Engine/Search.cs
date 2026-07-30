using System.Diagnostics;
using Zoomies.Core;

namespace Zoomies.Engine;

public struct SearchLimits
{
    public int MaxDepth;
    public long MoveTimeMilliseconds;
    public long MaxNodes;
    public bool SearchUntilStopped;

    public static SearchLimits Depth(int depth) => new() { MaxDepth = depth };

    public static SearchLimits Time(long milliseconds) =>
        new() { MoveTimeMilliseconds = milliseconds };

    public static SearchLimits Nodes(long nodeCount) => new() { MaxNodes = nodeCount };
}

public sealed class Search
{
    private const int Infinity = 32000;
    private const int MaximumPly = 128;
    private const int DeltaMargin = 200;

    private readonly Move[,] killerMoves = new Move[MaximumPly, 2];
    private readonly Stopwatch searchClock = new();

    private long nodeCount;
    private long quiescenceNodeCount;
    private long evaluationCount;
    private long timeLimitMilliseconds;
    private long nodeLimit;
    private bool searchUntilStopped;
    private volatile bool stopRequested;
    private Move principalVariationMove;

    public bool SuppressOutput { get; set; }
    public long LastNodeCount { get; private set; }
    public long LastQuiescenceNodeCount { get; private set; }
    public long LastEvaluationCount { get; private set; }
    public long LastElapsedMilliseconds { get; private set; }
    public int LastScore { get; private set; }
    public int LastDepth { get; private set; }

    public void Stop() => stopRequested = true;

    public void ResetStopRequest() => stopRequested = false;

    public Move FindBestMove(Position position, SearchLimits limits)
    {
        nodeCount = 0;
        quiescenceNodeCount = 0;
        evaluationCount = 0;
        searchUntilStopped = limits.SearchUntilStopped;
        searchClock.Restart();
        timeLimitMilliseconds = limits.MoveTimeMilliseconds > 0
            ? limits.MoveTimeMilliseconds
            : long.MaxValue;
        nodeLimit = limits.MaxNodes;

        Array.Clear(killerMoves, 0, killerMoves.Length);

        Span<Move> rootMoves = stackalloc Move[256];
        int rootMoveCount = GenerateLegalMoves(position, rootMoves);
        if (rootMoveCount == 0)
        {
            LastScore = 0;
            LastDepth = 0;
            return default;
        }

        principalVariationMove = rootMoves[0];
        int maxDepth = limits.MaxDepth > 0 ? limits.MaxDepth : MaximumPly - 1;
        int lastScore = 0;
        int completedDepth = 0;

        for (int depth = 1; depth <= maxDepth; depth++)
        {
            int score = AlphaBeta(position, depth, -Infinity, Infinity, 0);
            if (stopRequested && depth > 1) break;

            lastScore = score;
            completedDepth = depth;

            if (!SuppressOutput)
            {
                long elapsedMilliseconds = searchClock.ElapsedMilliseconds;
                long nodesPerSecond = elapsedMilliseconds > 0
                    ? nodeCount * 1000 / elapsedMilliseconds
                    : nodeCount;
                Console.WriteLine(
                    $"info depth {depth} score cp {score} nodes {nodeCount} " +
                    $"nps {nodesPerSecond} time {elapsedMilliseconds} pv {principalVariationMove}");
            }

            if (Math.Abs(score) >= Eval.MateBound) break;
            if (ReachedSearchLimit()) break;
        }

        stopRequested = false;
        LastNodeCount = nodeCount;
        LastQuiescenceNodeCount = quiescenceNodeCount;
        LastEvaluationCount = evaluationCount;
        LastElapsedMilliseconds = searchClock.ElapsedMilliseconds;
        LastScore = lastScore;
        LastDepth = completedDepth;
        return principalVariationMove;
    }

    private int AlphaBeta(Position position, int depth, int alpha, int beta, int ply)
    {
        if (stopRequested) return 0;
        if ((nodeCount & 8191) == 0 && ReachedSearchLimit())
        {
            stopRequested = true;
            return 0;
        }

        if (depth <= 0)
            return QuiescenceSearch(position, alpha, beta, ply);

        nodeCount++;

        Span<Move> moves = stackalloc Move[256];
        int moveCount = GenerateLegalMoves(position, moves);
        if (moveCount == 0)
            return position.Checkers != 0 ? -Eval.MateValue + ply : 0;

        if (ply == 0 && principalVariationMove.EncodedValue != 0)
        {
            for (int i = 1; i < moveCount; i++)
            {
                if (moves[i] != principalVariationMove) continue;
                (moves[0], moves[i]) = (moves[i], moves[0]);
                break;
            }
        }

        int fixedMoveCount = ply == 0 ? 1 : 0;
        int tacticalMoveEnd = fixedMoveCount +
            OrderTacticalMoves(
                position,
                moves.Slice(fixedMoveCount, moveCount - fixedMoveCount));

        for (int killerIndex = 0, insertAt = tacticalMoveEnd;
            killerIndex < 2 && insertAt < moveCount;
            killerIndex++)
        {
            Move killer = killerMoves[ply, killerIndex];
            if (killer.EncodedValue == 0) continue;

            for (int i = insertAt; i < moveCount; i++)
            {
                if (moves[i] != killer) continue;
                (moves[insertAt], moves[i]) = (moves[i], moves[insertAt]);
                insertAt++;
                break;
            }
        }

        Color sideToMove = position.Turn;
        int bestScore = -Infinity;

        for (int i = 0; i < moveCount; i++)
        {
            Move move = moves[i];
            position.PlayPerft(sideToMove, move);
            int score = -AlphaBeta(position, depth - 1, -beta, -alpha, ply + 1);
            position.UndoPerft(sideToMove, move);

            if (stopRequested) return 0;
            if (score > bestScore)
            {
                bestScore = score;
                if (ply == 0) principalVariationMove = move;
            }

            if (score > alpha) alpha = score;
            if (alpha < beta) continue;

            if (!move.IsCapture &&
                (move.Flags & MoveFlags.Promotions) == 0 &&
                killerMoves[ply, 0] != move)
            {
                killerMoves[ply, 1] = killerMoves[ply, 0];
                killerMoves[ply, 0] = move;
            }
            break;
        }

        return bestScore;
    }

    private int QuiescenceSearch(Position position, int alpha, int beta, int ply)
    {
        if (stopRequested) return 0;
        if ((nodeCount & 8191) == 0 && ReachedSearchLimit())
        {
            stopRequested = true;
            return 0;
        }

        nodeCount++;
        quiescenceNodeCount++;

        evaluationCount++;
        int standingPatScore = Eval.Evaluate(position);
        if (standingPatScore >= beta) return standingPatScore;
        if (standingPatScore > alpha) alpha = standingPatScore;
        if (ply >= MaximumPly - 1) return standingPatScore;

        if (alpha > -Eval.MateBound)
        {
            Color opponent = position.Turn.Flip();
            int maxGain =
                  position.BitboardOf(opponent, PieceType.Queen)  != 0 ? Eval.PieceValue[(int)PieceType.Queen]
                : position.BitboardOf(opponent, PieceType.Rook)   != 0 ? Eval.PieceValue[(int)PieceType.Rook]
                : position.BitboardOf(opponent, PieceType.Bishop) != 0 ? Eval.PieceValue[(int)PieceType.Bishop]
                : position.BitboardOf(opponent, PieceType.Knight) != 0 ? Eval.PieceValue[(int)PieceType.Knight]
                : Eval.PieceValue[(int)PieceType.Pawn];
            ulong promotionRank = position.Turn == Color.White
                ? 0x00FF_0000_0000_0000UL
                : 0x0000_0000_0000_FF00UL;

            if ((position.BitboardOf(position.Turn, PieceType.Pawn) & promotionRank) != 0)
            {
                maxGain += Eval.PieceValue[(int)PieceType.Queen] -
                    Eval.PieceValue[(int)PieceType.Pawn];
            }

            if (standingPatScore + maxGain + DeltaMargin <= alpha)
                return standingPatScore;
        }

        Span<Move> moves = stackalloc Move[64];
        int moveCount = position.Turn == Color.White
            ? position.GenerateCapturesFast<White>(moves)
            : position.GenerateCapturesFast<Black>(moves);
        if (moveCount == 0)
            return standingPatScore;

        OrderTacticalMoves(position, moves.Slice(0, moveCount));

        Color sideToMove = position.Turn;
        int bestScore = standingPatScore;

        for (int i = 0; i < moveCount; i++)
        {
            Move move = moves[i];

            if (alpha > -Eval.MateBound)
            {
                int gain = 0;
                if (move.IsCapture)
                {
                    gain += move.Flags == MoveFlags.EnPassant
                        ? Eval.PieceValue[(int)PieceType.Pawn]
                        : Eval.PieceValue[(int)Types.TypeOf(position.At(move.To))];
                }

                if ((move.Flags & MoveFlags.Promotions) != 0)
                {
                    gain += Eval.PieceValue[((int)move.Flags & 0b11) + 1] -
                        Eval.PieceValue[(int)PieceType.Pawn];
                }

                if (standingPatScore + gain + DeltaMargin <= alpha)
                    continue;
            }

            position.PlayPerft(sideToMove, move);
            if (position.InCheck(sideToMove))
            {
                position.UndoPerft(sideToMove, move);
                continue;
            }

            int score = -QuiescenceSearch(position, -beta, -alpha, ply + 1);
            position.UndoPerft(sideToMove, move);

            if (stopRequested) return 0;
            if (score > bestScore) bestScore = score;
            if (score >= beta) return score;
            if (score > alpha) alpha = score;
        }

        return bestScore;
    }

    private static int OrderTacticalMoves(Position position, Span<Move> moves)
    {
        Span<int> scores = stackalloc int[16];
        int tacticalMoveCount = 0;

        for (int i = 0; i < moves.Length && tacticalMoveCount < 16; i++)
        {
            Move move = moves[i];
            if (!move.IsCapture && (move.Flags & MoveFlags.Promotions) == 0)
                continue;

            int victimValue = move.IsCapture ? PieceTypeValue(position.At(move.To)) : 0;
            int score = victimValue * 16 - PieceTypeValue(position.At(move.From))
                + ((move.Flags & MoveFlags.Promotions) != 0
                    ? Eval.PieceValue[(int)PieceType.Queen]
                    : 0);

            (moves[i], moves[tacticalMoveCount]) =
                (moves[tacticalMoveCount], moves[i]);
            scores[tacticalMoveCount++] = score;
        }

        for (int i = 1; i < tacticalMoveCount; i++)
        {
            Move move = moves[i];
            int score = scores[i];
            int insertionIndex = i - 1;

            while (insertionIndex >= 0 && scores[insertionIndex] < score)
            {
                moves[insertionIndex + 1] = moves[insertionIndex];
                scores[insertionIndex + 1] = scores[insertionIndex];
                insertionIndex--;
            }

            moves[insertionIndex + 1] = move;
            scores[insertionIndex + 1] = score;
        }

        return tacticalMoveCount;
    }

    private static int PieceTypeValue(Piece piece) =>
        piece == Piece.NoPiece
            ? Eval.PieceValue[(int)PieceType.Pawn]
            : Eval.PieceValue[(int)Types.TypeOf(piece)];

    private bool ReachedSearchLimit()
    {
        if (nodeLimit > 0 && nodeCount >= nodeLimit) return true;
        if (searchUntilStopped) return false;
        return searchClock.ElapsedMilliseconds >= timeLimitMilliseconds;
    }

    public static int GenerateLegalMoves(Position position, Span<Move> buffer) =>
        position.Turn == Color.White
            ? position.GenerateLegalsInto<White>(buffer)
            : position.GenerateLegalsInto<Black>(buffer);
}
