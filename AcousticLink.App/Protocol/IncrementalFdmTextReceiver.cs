using AcousticLink.App.Signals;

namespace AcousticLink.App.Protocol;

public sealed record LiveFdmReception(FdmTextReceiveResult Result, int SampleCount, bool IsFinal,
    string ChannelAStatus, string ChannelBStatus)
{
    // El resultado canónico sigue coincidiendo con el manual. La UI conserva además trazas parciales reales.
    public FdmTextReceiveResult ObservedResult { get; init; } = Result;
}

// Una sesión, una transmisión. El propietario serializa Append/Complete en su worker.
// El límite es el mismo de captura: no hay cola ilimitada ni copias del prefijo completo.
public interface IIncrementalTextReceiver
{
    int Count { get; }
    LiveFdmReception Append(ReadOnlySpan<float> added, CancellationToken token = default);
    LiveFdmReception Complete(CancellationToken token = default);
}

public sealed class IncrementalFdmTextReceiver : IIncrementalTextReceiver
{
    private readonly float[] samples;
    private readonly int rate;
    private readonly bool hamming, erasures, lockFirstHeader;
    private readonly FdmFrameSynchronizer.HeaderSearch search;
    private readonly FdmSequenceDecoder decoder = new();
    private readonly FdmTextReceiver receiver = new();
    private readonly List<BfskSymbolResult> a = [], b = [];
    private FdmSynchronizationResult? synchronization;
    private FdmTextChannelResult? resultA, resultB;
    private int nextSymbol;
    private bool completed;
    public int Count { get; private set; }
    public int Capacity => samples.Length;
    public int EvaluatedHeaderStarts => search.EvaluatedStarts;
    public int DetectedSymbolPairs { get; private set; }
    public LiveFdmReception? Current { get; private set; }

    public IncrementalFdmTextReceiver(int sampleRate, bool useHamming = false, bool recoverErasures = false,
        int maximumSeconds = 30, bool lockFirstHeader = false)
    {
        if (maximumSeconds is < 1 or > 30) throw new ArgumentOutOfRangeException(nameof(maximumSeconds));
        search = new FdmFrameSynchronizer().CreateHeaderSearch(sampleRate);
        this.lockFirstHeader = lockFirstHeader; rate = sampleRate; hamming = useHamming; erasures = useHamming && recoverErasures;
        samples = new float[checked(rate * maximumSeconds)];
    }

    public LiveFdmReception Append(ReadOnlySpan<float> added, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (completed) throw new InvalidOperationException("La sesión ya finalizó; iniciar una sesión nueva.");
        if (added.Length > Capacity - Count) throw new ArgumentException("Se excede el límite de captura; no se truncan muestras.");
        foreach (float value in added) if (!float.IsFinite(value)) throw new ArgumentException("Las muestras deben ser finitas.");
        added.CopyTo(samples.AsSpan(Count)); Count += added.Length;
        return Advance(false, token);
    }

    public LiveFdmReception Complete(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (completed) return Current!;
        var previous = Current;
        var snapshot = Advance(true, token);
        if (previous is not null && snapshot.Result.Synchronization.StartSeconds == previous.Result.Synchronization.StartSeconds && !snapshot.Result.Synchronization.IsAmbiguous)
        {
            static FdmTextChannelResult KeepPartial(FdmTextChannelResult final, FdmTextChannelResult before) =>
                final.State == FdmTextReceiveState.Truncated && final.HeaderValid && before.HeaderValid && final.Block is null && before.Block is not null
                ? final with { Block = before.Block, HammingDiagnostics = before.HammingDiagnostics, HammingCoversCompleteBlock = false } : final;
            snapshot = snapshot with { ObservedResult = snapshot.Result with
            { ChannelA = KeepPartial(snapshot.Result.ChannelA, previous.ObservedResult.ChannelA), ChannelB = KeepPartial(snapshot.Result.ChannelB, previous.ObservedResult.ChannelB) } };
        }
        Current = snapshot; completed = true; return snapshot;
    }

    private LiveFdmReception Advance(bool final, CancellationToken token)
    {
        var prefix = samples.AsSpan(0, Count);
        var sync = lockFirstHeader && synchronization?.DataStartSeconds is not null ? synchronization : search.Advance(prefix, final, token);
        if (synchronization?.StartSeconds != sync.StartSeconds || synchronization?.ChannelA?.HeaderValid != sync.ChannelA?.HeaderValid
            || synchronization?.ChannelB?.HeaderValid != sync.ChannelB?.HeaderValid)
        {
            a.Clear(); b.Clear(); nextSymbol = 0; resultA = resultB = null;
        }
        synchronization = sync;
        if (final || sync.DataStartSeconds is null)
        {
            // Cierre canónico: el mismo lector manual y la búsqueda acumulada exacta, sin volver a buscar todo el audio.
            var result = receiver.ReceiveSynchronized(prefix, rate, sync, token, hamming, erasures);
            if (!final) result = result with { ChannelA = MarkPending(result.ChannelA), ChannelB = MarkPending(result.ChannelB) };
            return Current = new(result, Count, final, Status(result.ChannelA, final), Status(result.ChannelB, final));
        }
        double start = sync.DataStartSeconds.Value;
        resultA ??= Initial(sync.ChannelA!.HeaderValid, true);
        resultB ??= Initial(sync.ChannelB!.HeaderValid, false);
        while ((Needs(resultA, a.Count) || Needs(resultB, b.Count)) &&
            Math.Round(start * rate + (nextSymbol + 1) * FdmSequenceDecoder.SymbolDurationSeconds * rate) <= Count)
        {
            token.ThrowIfCancellationRequested();
            var pair = decoder.DecodeMessageSymbol(prefix, rate, start, nextSymbol, token); DetectedSymbolPairs++;
            if (Needs(resultA, a.Count)) { a.Add(pair.ChannelA.Symbols[0]); resultA = Read(a, true); }
            if (Needs(resultB, b.Count)) { b.Add(pair.ChannelB.Symbols[0]); resultB = Read(b, false); }
            nextSymbol++;
        }
        var reception = new FdmTextReceiveResult(sync, MarkPending(resultA), MarkPending(resultB));
        return Current = new(reception, Count, false, Status(resultA, false), Status(resultB, false));
    }

    private FdmTextChannelResult Initial(bool valid, bool channelA) => valid ? Read([], channelA)
        : new(FdmTextReceiveState.HeaderNotSynchronized, false, null, null, null, null, "Buscando cabecera válida de este canal.")
        { UsesHamming = hamming, ErasureRecoveryEnabled = erasures };
    private FdmTextChannelResult Read(List<BfskSymbolResult> symbols, bool channelA) =>
        receiver.InterpretPrefix(new(Array.AsReadOnly(symbols.ToArray())), channelA, hamming, erasures);
    private bool Needs(FdmTextChannelResult result, int count)
    {
        if (!result.HeaderValid || result.State != FdmTextReceiveState.Truncated) return false;
        int original = result.ByteLength is int length ? 8 + 8 * length + 32 : 8;
        return count < (hamming ? original / 4 * 7 : original);
    }
    private static FdmTextChannelResult MarkPending(FdmTextChannelResult channel) => channel with
    { IsReceiving = channel.State is FdmTextReceiveState.Truncated or FdmTextReceiveState.HeaderNotSynchronized };
    private static string Status(FdmTextChannelResult channel, bool final) => channel.State switch
    {
        FdmTextReceiveState.Valid => "Mensaje válido · CRC y UTF-8 comprobados",
        FdmTextReceiveState.HeaderNotSynchronized => final ? "Sin cabecera validada" : "Buscando cabecera",
        FdmTextReceiveState.AmbiguousSynchronization => "Sincronización ambigua",
        FdmTextReceiveState.Truncated => final ? "Recepción incompleta al finalizar captura" : channel.ByteLength.HasValue ? "Longitud disponible · recibiendo datos y CRC" : channel.HeaderValid ? "Cabecera validada · recibiendo longitud" : "Buscando cabecera",
        _ => channel.Reason
    };
}
