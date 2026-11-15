using AcousticLink.App.Signals;

namespace AcousticLink.App.Protocol;

public sealed record ContinuousReception(long Id, long StartSample, DateTimeOffset DetectedAt,
    int SampleRate, float[] Samples, FdmTextReceiveResult Result, bool UsesHamming)
{
    public Guid SessionId { get; init; }
    public bool IsBfsk { get; init; }
    public string Label => IsBfsk
        ? $"#{Id} · BFSK · {DetectedAt:HH:mm:ss} · {Result.ChannelA.Text ?? Result.ChannelA.State.ToString()}"
        : $"#{Id} · FDM · {DetectedAt:HH:mm:ss} · A: {Result.ChannelA.Text ?? Result.ChannelA.State.ToString()} · B: {Result.ChannelB.Text ?? Result.ChannelB.State.ToString()}";
}

public interface IContinuousTextReceiver
{
    int BufferedSamples { get; }
    long BufferOrigin { get; }
    ReadOnlyMemory<float> AvailableSamples { get; }
    long ReceptionCount { get; }
    long EvaluatedHeaderStarts { get; }
    LiveFdmReception? Current { get; }
    event Action<ContinuousReception>? Received;
    void Append(ReadOnlySpan<float> samples, CancellationToken token = default);
    void Complete(CancellationToken token = default);
}

// Un escritor. El dispositivo permanece abierto; solo se rearma el lector de una trama.
public sealed class ContinuousFdmReceiver : IContinuousTextReceiver
{
    public const int HistoryLimit = 10;
    public Guid SessionId { get; } = Guid.NewGuid();
    private bool completed;
    private readonly int rate;
    private readonly bool hamming, erasures;
    private float[] buffer;
    private int count;
    private long origin, nextId;
    private long previousHeaderStarts;
    private IncrementalFdmTextReceiver reader;
    private FdmFrameSynchronizer.HeaderSearch? nextHeader;
    private int nextHeaderOffset;
    private readonly Queue<ContinuousReception> history = new();
    public IReadOnlyList<ContinuousReception> History => history.ToArray();
    public LiveFdmReception? Current => reader.Current;
    public long ReceivedSamples { get; private set; }
    public long ReceptionCount => nextId;
    public long BufferOrigin => origin;
    public ReadOnlyMemory<float> AvailableSamples => buffer.AsMemory(0, count);
    public int BufferedSamples => count;
    public int Capacity => buffer.Length;
    public long EvaluatedHeaderStarts => previousHeaderStarts + reader.EvaluatedHeaderStarts + (nextHeader?.EvaluatedStarts ?? 0);
    public event Action<ContinuousReception>? Received;
    public ContinuousFdmReceiver(int sampleRate, bool useHamming = false, bool recoverErasures = false)
    {
        rate = sampleRate; hamming = useHamming; erasures = useHamming && recoverErasures;
        reader = NewReader(); buffer = new float[checked(rate * 30)];
    }
    private IncrementalFdmTextReceiver NewReader() => new(rate, hamming, erasures, lockFirstHeader: true);
    public void Append(ReadOnlySpan<float> samples, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (completed) throw new InvalidOperationException("Escucha finalizada; iniciar otra sesión.");
        // Pasos acotados preservan transiciones aunque un callback contenga varias tramas.
        while (!samples.IsEmpty)
        {
            token.ThrowIfCancellationRequested();
            int n = Math.Min(samples.Length, Math.Max(1, rate / 20));
            samples[..n].CopyTo(buffer.AsSpan(count)); count += n; ReceivedSamples += n;
            reader.Append(samples[..n], token); samples = samples[n..];
            Advance(token);
        }
    }
    private void Advance(CancellationToken token)
    {
        var sync = Current!.Result.Synchronization;
        if (sync.DataStartSeconds is not double start)
        {
            if (count >= rate * 5) Rebase(count - (int)Math.Ceiling(rate * 2.6), token);
            return;
        }
        if (nextHeader is null)
        {
            nextHeaderOffset = (int)Math.Round(start * rate);
            nextHeader = new FdmFrameSynchronizer().CreateHeaderSearch(rate);
        }
        var following = nextHeader.Advance(buffer.AsSpan(nextHeaderOffset, count - nextHeaderOffset), false, token);
        if (following.StartSeconds is double next)
        {
            int boundary = nextHeaderOffset + (int)Math.Round(next * rate);
            Finish(boundary, token); Rebase(boundary, token); return;
        }
        bool Pending(FdmTextChannelResult c) => c.HeaderValid && c.IsReceiving;
        bool done = !Pending(Current.Result.ChannelA) && !Pending(Current.Result.ChannelB);
        int maximumEnd = (int)Math.Round((sync.StartSeconds!.Value + (hamming ? FdmTextFrameBuilder.MaximumProtectedFrameBits : FdmTextFrameBuilder.MaximumFrameBits) * .1) * rate);
        if (done || count >= maximumEnd)
        {
            Finish(count, token);
            // Conservar el solapamiento que pueda contener una cabecera nueva todavía incompleta.
            Rebase(Math.Max(nextHeaderOffset, count - (int)Math.Ceiling(rate * 2.6)), token);
        }
    }
    private void Finish(int end, CancellationToken token)
    {
        if (Current?.Result.Synchronization.StartSeconds is null) return;
        var audio = buffer.AsSpan(0, end).ToArray();
        // Inicio ya validado: no buscar varias cabeceras en el historial completo.
        var result = new FdmTextReceiver().ReceiveSynchronized(audio, rate, Current.Result.Synchronization, token, hamming, erasures);
        var entry = new ContinuousReception(++nextId, origin + (long)Math.Round(result.Synchronization.StartSeconds!.Value * rate),
            DateTimeOffset.Now, rate, audio, result, hamming) { SessionId = SessionId };
        history.Enqueue(entry); while (history.Count > HistoryLimit) history.Dequeue();
        Received?.Invoke(entry);
    }
    private void Rebase(int discard, CancellationToken token)
    {
        previousHeaderStarts = EvaluatedHeaderStarts;
        var rest = buffer.AsSpan(discard, count - discard).ToArray();
        origin += discard; count = rest.Length; buffer = new float[rate * 30]; rest.CopyTo(buffer, 0);
        reader = NewReader(); nextHeader = null; nextHeaderOffset = 0;
        if (count > 0) reader.Append(rest, token);
    }
    public void Complete(CancellationToken token = default) { token.ThrowIfCancellationRequested(); if (completed) return; reader.Complete(token); Finish(count, token); count = 0; reader = NewReader(); nextHeader = null; completed = true; }
}
