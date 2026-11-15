using AcousticLink.App.Signals;

namespace AcousticLink.App.Protocol;

public sealed record LiveBfskReception(BfskTextReceiveResult Result, int SampleCount, bool IsFinal);

// Una sesión acotada; Append y Complete se serializan en el worker propietario.
public sealed class IncrementalBfskTextReceiver : IIncrementalTextReceiver
{
    private readonly float[] samples;
    private readonly int rate;
    private readonly bool hamming, erasures, lockFirstHeader;
    private readonly BfskFrameSynchronizer.HeaderSearch search;
    private readonly BfskSequenceDecoder decoder = new();
    private readonly FdmTextReceiver interpreter = new();
    private readonly BfskTextReceiver receiver = new();
    private readonly List<BfskSymbolResult> symbols = [];
    private BfskSynchronizationResult? synchronization;
    private bool completed;
    public int Count { get; private set; }
    public int Capacity => samples.Length;
    public int EvaluatedHeaderStarts => search.EvaluatedStarts;
    public LiveBfskReception? Current { get; private set; }

    public IncrementalBfskTextReceiver(int sampleRate, bool useHamming = false, bool recoverErasures = false,
        int maximumSeconds = 30, bool lockFirstHeader = false)
    {
        if (maximumSeconds is < 1 or > 30) throw new ArgumentOutOfRangeException(nameof(maximumSeconds));
        search = new BfskFrameSynchronizer().CreateHeaderSearch(sampleRate);
        rate = sampleRate; hamming = useHamming; erasures = useHamming && recoverErasures;
        this.lockFirstHeader = lockFirstHeader;
        samples = new float[checked(sampleRate * maximumSeconds)];
    }

    public LiveBfskReception Append(ReadOnlySpan<float> added, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (completed) throw new InvalidOperationException("La sesión ya finalizó.");
        if (added.Length > Capacity - Count) throw new ArgumentException("Se excede el límite de captura.");
        foreach (float value in added) if (!float.IsFinite(value)) throw new ArgumentException("Las muestras deben ser finitas.");
        added.CopyTo(samples.AsSpan(Count)); Count += added.Length;
        return Advance(false, token);
    }

    public LiveBfskReception Complete(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (completed) return Current!;
        Current = Advance(true, token); completed = true; return Current;
    }

    LiveFdmReception IIncrementalTextReceiver.Append(ReadOnlySpan<float> added, CancellationToken token) =>
        Present(Append(added, token));
    LiveFdmReception IIncrementalTextReceiver.Complete(CancellationToken token) => Present(Complete(token));
    private static LiveFdmReception Present(LiveBfskReception snapshot) =>
        new(BfskPresentation.AsSingleChannel(snapshot.Result), snapshot.SampleCount, snapshot.IsFinal,
            snapshot.Result.Channel.Reason, "Sin canal B");

    private LiveBfskReception Advance(bool final, CancellationToken token)
    {
        var prefix = samples.AsSpan(0, Count);
        var sync = lockFirstHeader && synchronization?.DataStartSeconds is not null
            ? synchronization : search.Advance(prefix, final, token);
        if (sync.StartSeconds != synchronization?.StartSeconds) symbols.Clear();
        synchronization = sync;
        if (sync.DataStartSeconds is not double start)
            return Current = new(receiver.ReceiveSynchronized(prefix, rate, sync, token, hamming, erasures), Count, final);

        int maximum = (hamming ? FdmTextFrameBuilder.MaximumProtectedFrameBits : FdmTextFrameBuilder.MaximumFrameBits)
            - BfskFrameBuilder.Preamble.Length - BfskFrameBuilder.StartMarker.Length;
        FdmTextChannelResult result = interpreter.InterpretPrefix(new(Array.AsReadOnly(symbols.ToArray())), true, hamming, erasures);
        while (result.State == FdmTextReceiveState.Truncated && symbols.Count < maximum &&
            Math.Round(start * rate + (symbols.Count + 1) * BfskSequenceDecoder.BitDurationSeconds * rate) <= Count)
        {
            token.ThrowIfCancellationRequested();
            var next = decoder.DecodeMessageBlock(prefix, rate, start, 1, token, symbols.Count);
            symbols.Add(next.Symbols[0]);
            result = interpreter.InterpretPrefix(new(Array.AsReadOnly(symbols.ToArray())), true, hamming, erasures);
        }
        if (final) result = receiver.ReceiveSynchronized(prefix, rate, sync, token, hamming, erasures).Channel;
        else result = result with { IsReceiving = result.State == FdmTextReceiveState.Truncated };
        return Current = new(new(sync, result), Count, final);
    }
}
