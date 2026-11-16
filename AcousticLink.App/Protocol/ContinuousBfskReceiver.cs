using AcousticLink.App.Signals;

namespace AcousticLink.App.Protocol;

// El micrófono permanece abierto; se rearma solamente el lector de una trama.
public sealed class ContinuousBfskReceiver : IContinuousTextReceiver
{
    public Guid SessionId { get; }
    private readonly int rate;
    private readonly bool hamming, erasures;
    private readonly float[] buffer;
    private IncrementalBfskTextReceiver reader;
    private BfskFrameSynchronizer.HeaderSearch? nextHeader;
    private int nextHeaderOffset;
    private int? pendingNextStart;
    private int count;
    private long origin, nextId, priorSearches;
    private bool completed;
    public event Action<ContinuousReception>? Received;
    public int BufferedSamples => count;
    public long BufferOrigin => origin;
    public ReadOnlyMemory<float> AvailableSamples => buffer.AsMemory(0, count);
    public long ReceptionCount => nextId;
    public long EvaluatedHeaderStarts => priorSearches + reader.EvaluatedHeaderStarts
        + (nextHeader?.EvaluatedStarts ?? 0);
    public LiveFdmReception? Current => reader.Current is { } current
        ? new(BfskPresentation.AsSingleChannel(current.Result), current.SampleCount, current.IsFinal,
            current.Result.Channel.Reason, "Sin canal B") : null;

    public ContinuousBfskReceiver(int sampleRate, bool useHamming = false, bool recoverErasures = false,
        Guid? sessionId = null)
    {
        SessionId = sessionId ?? Guid.NewGuid();
        if (sampleRate <= 2 * BfskToneGenerator.Bit1Frequency) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        rate = sampleRate; hamming = useHamming; erasures = useHamming && recoverErasures;
        buffer = new float[checked(rate * 30)]; reader = NewReader();
    }
    private IncrementalBfskTextReceiver NewReader() => new(rate, hamming, erasures, lockFirstHeader: true);

    public void Append(ReadOnlySpan<float> samples, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (completed) throw new InvalidOperationException("Escucha finalizada.");
        while (!samples.IsEmpty)
        {
            token.ThrowIfCancellationRequested();
            int n = Math.Min(samples.Length, Math.Max(1, rate / 20));
            if (count + n > buffer.Length) Rebase(Math.Max(1, count - (int)Math.Ceiling(rate * 2.6)), token);
            samples[..n].CopyTo(buffer.AsSpan(count)); count += n;
            reader.Append(samples[..n], token); samples = samples[n..];
            Advance(token);
        }
    }

    private void Advance(CancellationToken token)
    {
        var current = reader.Current?.Result;
        if (current?.Synchronization.DataStartSeconds is not double dataStart)
        {
            if (count >= rate * 5) Rebase(count - (int)Math.Ceiling(rate * 2.6), token);
            return;
        }
        if (nextHeader is null)
        {
            nextHeaderOffset = (int)Math.Round(dataStart * rate);
            nextHeader = new BfskFrameSynchronizer().CreateHeaderSearch(rate);
        }
        var following = nextHeader.Advance(buffer.AsSpan(nextHeaderOffset, count - nextHeaderOffset), false, token);
        var channel = current.Channel;
        int? expectedEnd = channel.ByteLength is int length && length is >= 1 and <= FdmTextFrameBuilder.MaximumUtf8Bytes
            ? FrameEnd(current.Synchronization.StartSeconds!.Value, length) : null;
        if (following.StartSeconds is double next)
        {
            int boundary = nextHeaderOffset + (int)Math.Round(next * rate);
            if (expectedEnd is null || boundary >= expectedEnd.Value)
            { Finish(boundary, token); Rebase(boundary, token); return; }
            pendingNextStart ??= boundary;
        }
        if (channel.State == FdmTextReceiveState.Truncated)
        {
            int maximum = (int)Math.Round((current.Synchronization.StartSeconds!.Value +
                (hamming ? FdmTextFrameBuilder.MaximumProtectedFrameBits : FdmTextFrameBuilder.MaximumFrameBits)
                * BfskSequenceDecoder.BitDurationSeconds) * rate);
            if (count < (expectedEnd ?? maximum)) return;
        }
        if (channel.State != FdmTextReceiveState.Valid && pendingNextStart is int pending)
        { Finish(pending, token); Rebase(pending, token); return; }
        int end = count;
        if (expectedEnd.HasValue) end = Math.Min(count, expectedEnd.Value);
        Finish(end, token);
        // Conservar una cabecera parcial posterior, sin volver a recorrer audio ya descartado.
        Rebase(expectedEnd.HasValue ? end
            : Math.Max(1, end - (int)Math.Ceiling(rate * 2.6)), token);
    }

    private int FrameEnd(double start, int byteLength)
    {
        int original = (1 + byteLength + MessageCodec.CrcBytes) * 8;
        int frameBits = BfskFrameBuilder.Preamble.Length + BfskFrameBuilder.StartMarker.Length
            + (hamming ? original / 4 * 7 : original);
        return (int)Math.Round((start + frameBits * BfskSequenceDecoder.BitDurationSeconds) * rate);
    }

    private void Finish(int end, CancellationToken token)
    {
        var current = reader.Current?.Result;
        if (current?.Synchronization.StartSeconds is not double start) return;
        var audio = buffer.AsSpan(0, end).ToArray();
        var result = new BfskTextReceiver().ReceiveSynchronized(audio, rate, current.Synchronization, token, hamming, erasures);
        var entry = new ContinuousReception(++nextId, origin + (long)Math.Round(start * rate),
            DateTimeOffset.Now, rate, audio, BfskPresentation.AsSingleChannel(result), hamming)
        { SessionId = SessionId, IsBfsk = true };
        Received?.Invoke(entry);
    }

    private void Rebase(int discard, CancellationToken token)
    {
        priorSearches = EvaluatedHeaderStarts;
        var rest = buffer.AsSpan(discard, count - discard).ToArray();
        origin += discard; count = rest.Length; rest.CopyTo(buffer, 0);
        reader = NewReader();
        nextHeader = null;
        pendingNextStart = null;
        if (count > 0) reader.Append(buffer.AsSpan(0, count), token);
    }

    public void Complete(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (completed) return;
        reader.Complete(token);
        if (reader.Current?.Result.Synchronization.Found == true) Finish(count, token);
        completed = true;
    }
}
