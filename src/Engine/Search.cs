using Zoomies.Core;

namespace Zoomies.Engine;

public struct SearchLimits
{
    public int MaxDepth;
    public long MoveTimeMilliseconds;
    public long SoftTimeMilliseconds;
    public long MaxNodes;
    public bool SearchUntilStopped;

    public static SearchLimits Depth(int depth) => new() { MaxDepth = depth };

    public static SearchLimits Time(long milliseconds) =>
        new() { MoveTimeMilliseconds = milliseconds };

    public static SearchLimits Nodes(long nodeCount) => new() { MaxNodes = nodeCount };
}

public sealed class Search
{
    private const double StabilityPeak = 2.6;
    private const double StabilityFloor = 0.5;
    private const double StabilityDecay = 0.3;
    private static readonly int[] StabilityScale = BuildStabilityScale();

    private const int EffortMinDepth = 8;
    private const int EffortHighThreshold = 96;
    private const int EffortHighScale = 65;
    private const int EffortMediumThreshold = 85;
    private const int EffortMediumScale = 85;
    private const int EffortLowThreshold = 33;
    private const int EffortLowScale = 135;
    private const int SoftScaleFloor = 40;
    private const int SoftScaleCeiling = 300;
    private const int ScoreDropMinDepth = 6;

    private static int[] BuildStabilityScale()
    {
        var table = new int[8];
        for (int s = 0; s < table.Length; s++)
            table[s] = (int)Math.Round(
                100 * (StabilityFloor + (StabilityPeak - StabilityFloor) * Math.Pow(StabilityDecay, s)));
        return table;
    }

    private static readonly int[] SkipSize  = [1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 4, 4, 4, 4];
    private static readonly int[] SkipPhase = [0, 1, 0, 1, 2, 3, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 0, 1, 2, 3];

    private readonly SearchState state = new();
    private int threadCount = 1;
    private SearchState[] helperStates = [];

    private struct ThreadResult
    {
        public Move BestMove;
        public int Score;
        public int Depth;
    }

    public bool SuppressOutput { get; set; }
    public long LastNodeCount { get; private set; }
    public long LastQuiescenceNodeCount { get; private set; }
    public long LastEvaluationCount { get; private set; }
    public long LastElapsedMilliseconds { get; private set; }
    public int LastScore { get; private set; }
    public int LastDepth { get; private set; }
    public long LastSingularAttempts { get; private set; }
    public long LastSingularExtensions { get; private set; }
    public long LastSingularMulticuts { get; private set; }
    public long LastSingularNegativeExtensions { get; private set; }
    public long LastSingularDoubleExtensions { get; private set; }

    public void Stop()
    {
        state.StopRequested = true;
        for (int i = 0; i < helperStates.Length; i++)
            helperStates[i].StopRequested = true;
    }

    public void ResetStopRequest()
    {
        state.StopRequested = false;
        for (int i = 0; i < helperStates.Length; i++)
            helperStates[i].StopRequested = false;
    }

    // Number of search threads
    public void SetThreads(int count)
    {
        threadCount = Math.Max(1, count);
        EnsureHelpers();
    }

    private void EnsureHelpers()
    {
        int wanted = threadCount - 1;
        if (helperStates.Length == wanted) return;

        var newStates = new SearchState[wanted];
        for (int i = 0; i < wanted; i++)
            newStates[i] = i < helperStates.Length ? helperStates[i] : new SearchState(state.Tt);
        helperStates = newStates;
    }

    public void NewGame()
    {
        state.Tt.Clear();
        state.ClearHistory();
        for (int i = 0; i < helperStates.Length; i++)
            helperStates[i].ClearHistory();
    }

    public void ResizeHash(int sizeMb) => state.Tt.Resize(sizeMb);

    public void ClearHash() => state.Tt.Clear();

    public Move FindBestMove(Position position, SearchLimits limits)
    {
        if (threadCount <= 1 || helperStates.Length == 0)
        {
            ThreadResult solo = RunIterativeDeepening(state, position, in limits, 0, isMain: true);
            state.StopRequested = false;
            PublishStats(solo);
            state.Stats.PrintReport(state, SuppressOutput);
            return solo.BestMove;
        }

        return FindBestMoveParallel(position, in limits);
    }

    private Move FindBestMoveParallel(Position position, in SearchLimits limits)
    {
        SearchLimits mainLimits = limits;
        SearchLimits helperLimits = limits;
        if (limits.MaxNodes > 0)
        {
            long share = Math.Max(1, limits.MaxNodes / threadCount);
            mainLimits.MaxNodes = share;
            helperLimits.MaxNodes = share;
        }

        var clones = new Position[helperStates.Length];
        for (int i = 0; i < helperStates.Length; i++)
            clones[i] = new Position(position);

        var workers = new Thread[helperStates.Length];
        var results = new ThreadResult[helperStates.Length];
        for (int i = 0; i < helperStates.Length; i++)
        {
            int idx = i;
            SearchState st = helperStates[idx];
            Position pos = clones[idx];
            workers[idx] = new Thread(() =>
            {
                try { results[idx] = RunIterativeDeepening(st, pos, in helperLimits, idx + 1, isMain: false); }
                catch { results[idx] = default; }
            })
            { IsBackground = true, Name = $"search-helper-{idx + 1}" };
            workers[idx].Start();
        }

        ThreadResult mainResult = RunIterativeDeepening(state, position, in mainLimits, 0, isMain: true);
        Stop();
        for (int i = 0; i < workers.Length; i++)
            workers[i].Join();

        state.StopRequested = false;
        for (int i = 0; i < helperStates.Length; i++)
            helperStates[i].StopRequested = false;

        ThreadResult best = mainResult;
        for (int i = 0; i < results.Length; i++)
        {
            ThreadResult r = results[i];
            if (r.BestMove.EncodedValue == 0) continue;
            if (r.Depth > best.Depth || (r.Depth == best.Depth && r.Score > best.Score))
                best = r;
        }

        PublishStats(mainResult, best);
        state.Stats.PrintReport(state, SuppressOutput);
        return best.BestMove;
    }

    private void PublishStats(ThreadResult mainResult) => PublishStats(mainResult, mainResult);

    private void PublishStats(ThreadResult mainResult, ThreadResult voted)
    {
        long nodes = state.NodeCount;
        long qNodes = state.QuiescenceNodeCount;
        long evals = state.EvaluationCount;
        for (int i = 0; i < helperStates.Length; i++)
        {
            nodes += helperStates[i].NodeCount;
            qNodes += helperStates[i].QuiescenceNodeCount;
            evals += helperStates[i].EvaluationCount;
        }

        LastNodeCount = nodes;
        LastQuiescenceNodeCount = qNodes;
        LastEvaluationCount = evals;
        LastElapsedMilliseconds = state.Clock.ElapsedMilliseconds;
        LastScore = voted.Score;
        LastDepth = voted.Depth;
        LastSingularAttempts = state.SingularAttempts;
        LastSingularExtensions = state.SingularExtensions;
        LastSingularMulticuts = state.SingularMulticuts;
        LastSingularNegativeExtensions = state.SingularNegativeExtensions;
        LastSingularDoubleExtensions = state.SingularDoubleExtensions;
    }

    private ThreadResult RunIterativeDeepening(
        SearchState st, Position position, in SearchLimits limits, int threadIndex, bool isMain)
    {
        st.Reset(in limits, bumpTtGeneration: isMain);

        Span<Move> rootMoves = stackalloc Move[256];
        int rootMoveCount = GenerateLegalMoves(position, rootMoves);
        if (rootMoveCount == 0)
            return default;

        st.PrincipalVariationMove = rootMoves[0];
        int maxDepth = limits.MaxDepth > 0 ? limits.MaxDepth : SearchState.MaximumPly - 1;
        int lastScore = 0;
        int completedDepth = 0;
        Move previousBest = default;
        int stability = 0;
        int skip = (threadIndex - 1) % SkipSize.Length;

        for (int depth = 1; depth <= maxDepth; depth++)
        {
            if (!isMain && (depth + SkipPhase[skip]) / SkipSize[skip] % 2 != 0)
                continue;

            st.RootDepth = depth;
            long iterationStartNodes = st.NodeCount;
            st.BestMoveEffortNodes = 0;

            // aspiration windows: start narrow around the previous score,
            // widen exponentially on fail until the score fits
            int alpha = -SearchState.Infinity;
            int beta = SearchState.Infinity;
            int delta = Tune.AspDelta;
            if (depth >= Tune.AspMinDepth)
            {
                alpha = Math.Max(lastScore - delta, -SearchState.Infinity);
                beta = Math.Min(lastScore + delta, SearchState.Infinity);
            }

            int score;
            int aspirationFailHighs = 0;
            int aspirationFailLows = 0;
            while (true)
            {
                score = Pruning.AlphaBeta(st, position, depth, alpha, beta, 0);
                if (st.StopRequested) break;

                if (score <= alpha)
                {
                    alpha = Math.Max(score - delta, -SearchState.Infinity);
                    aspirationFailLows++;
                    st.Stats.AspFailLow();
                }
                else if (score >= beta)
                {
                    beta = Math.Min(score + delta, SearchState.Infinity);
                    aspirationFailHighs++;
                    st.Stats.AspFailHigh();
                }
                else
                    break;

                delta = delta * Tune.AspWiden / 16;
            }
            if (st.StopRequested && depth > 1) break;

            int previousScore = lastScore;
            lastScore = score;
            completedDepth = depth;

            long iterationNodes = Math.Max(1, st.NodeCount - iterationStartNodes);
            int effortPercent = (int)(100 * st.BestMoveEffortNodes / iterationNodes);
            st.Stats.RecordIteration(
                depth, st.NodeCount - iterationStartNodes, st.NodeCount,
                st.Clock.ElapsedMilliseconds, score, st.SelectiveDepth,
                aspirationFailHighs, aspirationFailLows);

            if (isMain && !SuppressOutput)
            {
                long totalNodes = AggregateNodes();
                long elapsedMilliseconds = st.Clock.ElapsedMilliseconds;
                long nodesPerSecond = elapsedMilliseconds > 0
                    ? totalNodes * 1000 / elapsedMilliseconds
                    : totalNodes;
                string scoreText = score >= Eval.MateBound
                    ? $"mate {(Eval.MateValue - score + 1) / 2}"
                    : score <= -Eval.MateBound
                        ? $"mate {-((Eval.MateValue + score + 1) / 2)}"
                        : $"cp {score}";
                Console.WriteLine(
                    $"info depth {depth} seldepth {st.SelectiveDepth} score {scoreText} " +
                    $"nodes {totalNodes} nps {nodesPerSecond} " +
                    $"hashfull {st.Tt.Hashfull()} time {elapsedMilliseconds} " +
                    $"effort {effortPercent} pv {BuildPrincipalVariation(position, depth)}");
            }

            if (st.ReachedSearchLimit()) break;
            if (!isMain) continue;

            // don't start another iteration if time is running out;
            // the hard limit still ends the search immediately
            stability = st.PrincipalVariationMove == previousBest
                ? Math.Min(stability + 1, StabilityScale.Length - 1)
                : 0;
            previousBest = st.PrincipalVariationMove;

            int effortScale = 100;
            if (depth >= EffortMinDepth)
                effortScale = effortPercent >= EffortHighThreshold ? EffortHighScale
                    : effortPercent >= EffortMediumThreshold ? EffortMediumScale
                    : effortPercent <= EffortLowThreshold ? EffortLowScale
                    : 100;

            // when the root score drops from the previous depth
            // the best move may be refuted at the next depth
            // so extend the soft limit
            int scoreDropScale = 100;
            if (depth >= ScoreDropMinDepth)
                scoreDropScale = Math.Clamp(
                    100 + (previousScore - score - Tune.TmScoreDropMargin) * Tune.TmScoreDropSlope,
                    100, Tune.TmScoreDropMax);
            long combinedScale = Math.Clamp(
                StabilityScale[stability] * effortScale * scoreDropScale / 10000,
                SoftScaleFloor, SoftScaleCeiling);
            long softLimit = st.HasSoftLimit
                ? st.SoftTimeLimitMilliseconds * combinedScale / 100
                : st.SoftTimeLimitMilliseconds;
            if (!st.SearchUntilStopped &&
                st.Clock.ElapsedMilliseconds >= softLimit) break;
        }

        return new ThreadResult
        {
            BestMove = st.PrincipalVariationMove,
            Score = lastScore,
            Depth = completedDepth,
        };
    }

    private long AggregateNodes()
    {
        long nodes = state.NodeCount;
        for (int i = 0; i < helperStates.Length; i++)
            nodes += helperStates[i].NodeCount;
        return nodes;
    }

    private string BuildPrincipalVariation(Position position, int maxLength)
    {
        var line = new System.Text.StringBuilder();
        Span<Move> moves = stackalloc Move[256];
        Span<Move> played = stackalloc Move[SearchState.MaximumPly];
        int count = 0;
        Move current = state.PrincipalVariationMove;

        while (count < maxLength && count < played.Length && current.EncodedValue != 0)
        {
            int legalCount = GenerateLegalMoves(position, moves);
            bool legal = false;
            for (int i = 0; i < legalCount; i++)
            {
                if (moves[i] != current) continue;
                legal = true;
                break;
            }
            if (!legal) break;

            if (count > 0) line.Append(' ');
            line.Append(current);
            position.Play(position.Turn, current);
            played[count++] = current;

            if (!state.Tt.Probe(position.History[position.Ply].Hash, out TranspositionTable.Entry entry))
                break;
            current = new Move(entry.Move);
        }

        for (int i = count - 1; i >= 0; i--)
            position.Undo(position.Turn.Flip(), played[i]);

        return line.ToString();
    }

    public static int GenerateLegalMoves(Position position, Span<Move> buffer) =>
        position.Turn == Color.White
            ? position.GenerateLegalsInto<White>(buffer)
            : position.GenerateLegalsInto<Black>(buffer);
}
