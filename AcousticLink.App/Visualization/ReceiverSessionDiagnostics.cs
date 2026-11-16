using System.Diagnostics;
using AcousticLink.App.Protocol;

namespace AcousticLink.App.Visualization;

public sealed record ReceiverDiagnosticEvent(TimeSpan At, string Stage, string Detail);

public sealed record ReceiverDiagnosticSnapshot(Guid SessionId, string Mode, string Source, bool IsLive,
    bool IsBfsk, bool IsAcousticSource,
    bool IsActive, TimeSpan Elapsed, long CapturedSamples, long ProcessedSamples, int SampleRate,
    int CandidateHeaders, int UnconfirmedCandidates, int ConfirmedHeaders, int CompletedTransmissions,
    int EvaluatedChannelFrames, int ValidMessages, int RejectedChannelFrames, int CrcRejections,
    int IncompleteChannelFrames, int SynchronizationFailures, int DeviceErrors,
    bool AcousticActivityObserved, string? ChannelAState, string? ChannelBState,
    IReadOnlyList<ReceiverDiagnosticEvent> Events);

// Solo conserva contadores y eventos breves; nunca audio, símbolos ni vistas WPF.
// Un objeto corresponde a una sesión de micrófono o a un análisis posterior de un audio.
public sealed class ReceiverSessionDiagnostics
{
    public const int EventLimit = 80;
    public const double ActivityPeakThreshold = 0.005; // Amplitud digital |x|, no detector de portadora.
    private readonly object gate = new();
    private readonly long started = Stopwatch.GetTimestamp();
    private readonly Queue<ReceiverDiagnosticEvent> events = new();
    private long lastReceptionId, currentFrameKey = long.MinValue;
    private bool active = true, candidate, headerA, headerB, payloadA, payloadB, hammingA, hammingB,
        activity, finalCounted, sawSamples;
    private TimeSpan? stoppedAt;
    private long captured, processed;
    private string? stateA, stateB;
    private int rate, candidates, unconfirmed, headers, transmissions, evaluated, valid, rejected,
        crcRejected, incomplete, syncFailures, deviceErrors;
    public Guid SessionId { get; }
    public string Mode { get; }
    public string Source { get; }
    public bool IsLive { get; }
    public bool IsBfsk { get; }
    public bool IsAcousticSource { get; }

    public ReceiverSessionDiagnostics(string mode, string source, bool isLive, bool isBfsk, int sampleRate = 0,
        Guid? sessionId = null, bool acousticSource = true)
    {
        Mode = mode; Source = source; IsLive = isLive; IsBfsk = isBfsk; rate = sampleRate;
        SessionId = sessionId ?? Guid.NewGuid(); IsAcousticSource = acousticSource;
        Add("Inicio", isLive ? "Sesión de recepción iniciada; el inicio del dispositivo se confirma por separado."
            : "Análisis posterior iniciado; micrófono no activo.");
    }

    public void SetSamples(long capturedSamples, long processedSamples, int sampleRate)
    {
        lock (gate)
        {
            if (!active) return;
            if (sampleRate > 0) rate = sampleRate;
            captured = Math.Max(captured, capturedSamples);
            processed = Math.Max(processed, processedSamples);
            if (captured > 0 && !sawSamples)
            { sawSamples = true; Add("Muestras", "Audio recibido; todavía no implica una transmisión reconocida."); }
        }
    }

    public void ObserveAcousticPeak(double peak)
    {
        lock (gate)
        {
            if (!active || activity || !double.IsFinite(peak) || peak < ActivityPeakThreshold) return;
            activity = true;
            Add(IsAcousticSource ? "Actividad acústica" : "Amplitud del WAV",
                $"Pico digital ≥ {ActivityPeakThreshold:0.###}; no acredita preámbulo ni mensaje.");
        }
    }

    public void ObserveProgress(FdmTextReceiveResult? result, long frameKey = -1)
    {
        if (result is null) return;
        lock (gate)
        {
            if (!active) return;
            if (frameKey != currentFrameKey)
            {
                if (candidate && !headerA && !headerB &&
                    !result.ChannelA.HeaderValid && !result.ChannelB.HeaderValid)
                { unconfirmed++; Add("Candidato no confirmado", "La búsqueda avanzó sin una cabecera validada para ese candidato."); }
                currentFrameKey = frameKey;
                candidate = headerA = headerB = payloadA = payloadB = hammingA = hammingB = false;
            }
            bool possible = result.Synchronization.Reason.Contains("Candidata", StringComparison.OrdinalIgnoreCase);
            if (possible && !candidate)
            { candidate = true; candidates++; Add("Candidato", "Cabecera completa candidata; el sincronizador aún evalúa su posición temporal."); }
            if (!possible && candidate && !headerA && !headerB &&
                !result.ChannelA.HeaderValid && !result.ChannelB.HeaderValid)
            { candidate = false; unconfirmed++; Add("Candidato no confirmado", "No se confirmó una cabecera para el candidato observado."); }
            ObserveChannel(result.ChannelA, "A", ref headerA, ref payloadA, ref hammingA);
            if (!IsBfsk) ObserveChannel(result.ChannelB, "B", ref headerB, ref payloadB, ref hammingB);
            stateA = result.ChannelA.State.ToString();
            if (!IsBfsk) stateB = result.ChannelB.State.ToString();
        }
    }

    private void ObserveChannel(FdmTextChannelResult channel, string name, ref bool header,
        ref bool payload, ref bool hamming)
    {
        if (!header && channel.HeaderValid)
        { header = true; headers++; Add("Cabecera validada", $"Canal {name}: preámbulo y marcador confirmados por el receptor."); }
        if (!payload && channel.HeaderValid && channel.Block?.Symbols.Count > 0)
        { payload = true; Add("Símbolos", $"Canal {name}: comenzó la recuperación del bloque; las decisiones pueden incluir '?'."); }
        if (!hamming && channel.UsesHamming && channel.HammingDiagnostics?.WordCount > 0)
        { hamming = true; Add("Hamming", $"Canal {name}: palabras procesadas; la integridad aún depende del CRC."); }
    }

    // Se llama una sola vez por recepción finalizada. El Id monotónico distingue textos repetidos.
    public void RecordCompleted(long receptionId, FdmTextReceiveResult result, bool countMissingHeaders)
    {
        lock (gate)
        {
            if (!active || receptionId <= lastReceptionId) return;
            lastReceptionId = receptionId;
            bool anyHeader = result.ChannelA.HeaderValid || !IsBfsk && result.ChannelB.HeaderValid;
            if (anyHeader) transmissions++;
            stateA = result.ChannelA.State.ToString();
            if (!IsBfsk) stateB = result.ChannelB.State.ToString();
            CountChannel(result.ChannelA, "A", countMissingHeaders, anyHeader);
            if (!IsBfsk) CountChannel(result.ChannelB, "B", countMissingHeaders, anyHeader);
            Add(anyHeader ? "Trama finalizada" : "Análisis sin cabecera", anyHeader
                ? IsBfsk ? "BFSK: resultado del canal A registrado." : "FDM: resultados independientes de A y B registrados."
                : "El análisis terminó sin transmisión reconocida.");
        }
    }

    // Para una captura incremental/manual de una sola transmisión; no cuenta dos veces Complete.
    public void RecordFinalAnalysis(FdmTextReceiveResult result)
    {
        lock (gate)
        {
            if (finalCounted || !active) return;
            finalCounted = true;
        }
        RecordCompleted(1, result, countMissingHeaders: true);
    }

    private void CountChannel(FdmTextChannelResult channel, string name, bool countMissingHeaders,
        bool anotherHeaderConfirmed)
    {
        if (!channel.HeaderValid)
        {
            if (countMissingHeaders && (channel.State == FdmTextReceiveState.AmbiguousSynchronization ||
                anotherHeaderConfirmed && channel.State == FdmTextReceiveState.HeaderNotSynchronized))
            { syncFailures++; Add("Sin sincronización", $"Canal {name}: {channel.Reason}"); }
            return;
        }
        evaluated++;
        if (channel.State == FdmTextReceiveState.Valid && channel.CrcValid == true && channel.Text is not null)
        { valid++; Add("Mensaje validado", $"Canal {name}: CRC y UTF-8 válidos; texto entregado."); return; }
        rejected++;
        if (channel.CrcValid == false)
        { crcRejected++; Add("CRC rechazado", $"Canal {name}: el bloque reconstruido no superó CRC-32."); }
        else if (channel.State == FdmTextReceiveState.Truncated)
        { incomplete++; Add("Trama incompleta", $"Canal {name}: terminó sin el bloque completo."); }
        else Add("Trama rechazada", $"Canal {name}: {channel.Reason}");
    }

    public void DeviceError(string detail)
    {
        lock (gate)
        {
            if (!active) return;
            deviceErrors++; Add("Error de dispositivo", detail);
        }
    }

    public void Note(string stage, string detail)
    {
        lock (gate) if (active) Add(stage, detail);
    }

    public void Stop(string reason)
    {
        lock (gate)
        {
            if (!active) return;
            stoppedAt = Stopwatch.GetElapsedTime(started); active = false; Add("Fin", reason);
        }
    }

    public ReceiverDiagnosticSnapshot Snapshot()
    {
        lock (gate)
            return new(SessionId, Mode, Source, IsLive, IsBfsk, IsAcousticSource,
                active, stoppedAt ?? Stopwatch.GetElapsedTime(started), captured,
                processed, rate, candidates, unconfirmed, headers, transmissions, evaluated, valid, rejected,
                crcRejected, incomplete, syncFailures, deviceErrors, activity, stateA, stateB, events.ToArray());
    }

    private void Add(string stage, string detail)
    {
        events.Enqueue(new(Stopwatch.GetElapsedTime(started), stage, detail));
        while (events.Count > EventLimit) events.Dequeue();
    }
}
