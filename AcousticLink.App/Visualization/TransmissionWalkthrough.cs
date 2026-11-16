using System.Text;
using AcousticLink.App.Protocol;
using AcousticLink.App.Signals;

namespace AcousticLink.App.Visualization;

public sealed record WalkthroughItem(string Label, string Value, string Detail = "",
    double? StartSeconds = null, double? EndSeconds = null);
public sealed record WalkthroughStep(string Title, string Explanation, IReadOnlyList<WalkthroughItem> Items);
public sealed record WalkthroughData(string Source, string Mode, IReadOnlyList<WalkthroughStep> Steps,
    WaveformBucket[] Waveform, double Duration, string WaveformLabel);

// Proyecta resultados del módem. No codifica, demodula ni valida datos por su cuenta.
public static class TransmissionWalkthrough
{
    public static WalkthroughData Transmitter(string textA, string? textB, bool hamming, float[] samples)
    {
        bool fdm = textB is not null;
        var frames = fdm ? new FdmTextFrameBuilder().Build(textA, textB!, hamming) : null;
        var a = frames?.ChannelA ?? new BfskTextModem().Build(textA, hamming);
        var channels = fdm ? new[] { ("A", textA, a), ("B", textB!, frames!.ChannelB) } : new[] { ("A", textA, a) };
        var steps = new List<WalkthroughStep>();
        steps.Add(new("Texto", "Cada canal conserva su propio mensaje. La longitud del protocolo se mide en bytes UTF-8.",
            channels.Select(c => new WalkthroughItem($"Canal {c.Item1}", c.Item2, $"{c.Item3.Utf8ByteCount} bytes UTF-8")).ToArray()));
        steps.Add(new("UTF-8 y bits útiles", "Seleccioná una unidad Unicode para ver sus bytes y ocho bits por byte, en orden MSB.",
            channels.SelectMany(c => Characters(c.Item1, c.Item2, c.Item3)).ToArray()));
        steps.Add(new("Trama y CRC-32", "El CRC protege longitud y contenido. Detecta alteraciones; no corrige errores.",
            channels.SelectMany(c => FrameItems(c.Item1, c.Item3)).ToArray()));
        steps.Add(new("Hamming (7,4)", hamming
            ? "Solo longitud, contenido y CRC se codifican como P1 P2 D1 P4 D2 D3 D4. La cabecera queda fuera."
            : "Hamming desactivado: el bloque se transmite sin palabras de redundancia.",
            channels.SelectMany(c => HammingItems(c.Item1, c.Item3)).ToArray()));
        steps.Add(new("Bits transmitidos", "En FDM el canal más corto termina con ceros de relleno. No pertenecen al mensaje ni al CRC.",
            channels.SelectMany(c => new[] {
                new WalkthroughItem($"{c.Item1} · trama", c.Item3.FrameBits, $"{c.Item3.RealBitCount} bits reales"),
                new WalkthroughItem($"{c.Item1} · relleno", new string('0', c.Item3.PaddingBitCount), $"{c.Item3.PaddingBitCount} bits fuera de la trama")
            }).ToArray()));
        steps.Add(new("Símbolos y frecuencias", "Seleccioná un símbolo para ver su bit, frecuencia e intervalo en el buffer digital. Se muestran hasta 16 por página.",
            channels.SelectMany(c => c.Item3.TransmittedBits.Select((bit, i) =>
                new WalkthroughItem($"{c.Item1} · símbolo {i + 1}", $"{bit} → {CarrierEnergyMap.Frequencies[(c.Item1 == "B" ? 2 : 0) + (bit == '1' ? 1 : 0)]:0} Hz",
                    $"{i * FdmBitstreamGenerator.SymbolDurationSeconds:0.000}–{(i + 1) * FdmBitstreamGenerator.SymbolDurationSeconds:0.000} s · {(i >= c.Item3.RealBitCount ? "relleno" : "trama")}",
                    i * FdmBitstreamGenerator.SymbolDurationSeconds, (i + 1) * FdmBitstreamGenerator.SymbolDurationSeconds))).ToArray()));
        steps.Add(new("Señal digital", "Muestras generadas para reproducción. No son una captura del micrófono.",
            new[] { new WalkthroughItem("Buffer mono", $"{samples.Length} muestras · {BfskToneGenerator.SampleRate} Hz",
                $"{samples.Length / (double)BfskToneGenerator.SampleRate:0.000} s digitales · {(fdm ? "A+B" : "solo A")}") }));
        steps.Add(new("Resumen", "Conteos de la trama generada por canal; el relleno FDM se contabiliza por separado.",
            channels.Select(c => new WalkthroughItem($"Canal {c.Item1} · {c.Item2}",
                $"{c.Item3.Utf8ByteCount} bytes · {c.Item3.Utf8ByteCount * 8} bits útiles · {c.Item3.RealBitCount} bits de trama · {c.Item3.PaddingBitCount} de relleno",
                $"cabecera {c.Item3.HeaderBitCount} · bloque original {c.Item3.OriginalBlockBitCount} · bloque codificado {c.Item3.EncodedBlockBitCount} · {c.Item3.TransmittedBits.Length} símbolos · Hamming {(hamming ? "sí" : "no")}")).ToArray()));
        return new("Mensaje preparado en el emisor", fdm ? "FDM" : "BFSK", steps,
            WaveformView.Reduce(samples), samples.Length / (double)BfskToneGenerator.SampleRate, "Forma de onda del buffer digital · amplitud normalizada");
    }

    public static WalkthroughData Receiver(FdmTextReceiveResult result, bool bfsk, string source,
        int sampleRate, int sampleCount, WaveformBucket[] waveform)
    {
        var channels = bfsk ? new[] { ("A", result.ChannelA) } : new[] { ("A", result.ChannelA), ("B", result.ChannelB) };
        var steps = new List<WalkthroughStep> {
            new("Audio disponible", "Muestras observadas de la fuente seleccionada; la onda no identifica por sí sola una trama.",
                new[] { new WalkthroughItem(source, $"{sampleCount} muestras · {sampleRate} Hz", $"{sampleCount / (double)sampleRate:0.000} s disponibles") }),
            new("Búsqueda y sincronización", "El sincronizador decide si encontró una cabecera; la energía exploratoria del monitor no valida bits.",
                new[] { new WalkthroughItem(result.Synchronization.State.ToString(), result.Synchronization.Reason,
                    result.Synchronization.StartSeconds is double start ? $"Inicio estimado: {start:0.000} s" : "Posición no disponible") }),
            new("Símbolos estimados", "0, 1 o ? son decisiones del detector sobre ventanas de audio. Se muestran hasta 16 por página.",
                channels.SelectMany(c => c.Item2.Block?.Symbols.Select((s, i) =>
                    new WalkthroughItem($"{c.Item1} · símbolo {i + 1}", s.Detection.Bit?.ToString() ?? "?",
                        $"{s.StartSample / (double)sampleRate:0.000}–{s.EndSample / (double)sampleRate:0.000} s · energías {s.Detection.Energy0:G3} / {s.Detection.Energy1:G3} · {s.Detection.Reason}",
                        s.StartSample / (double)sampleRate, s.EndSample / (double)sampleRate))
                    ?? []).ToArray()),
            new("Longitud y trama", "La cabecera, longitud y bloque solo figuran si el receptor los identificó. Los bits del bloque pueden contener ?.",
                channels.SelectMany(c => new[] {
                    new WalkthroughItem($"{c.Item1} · cabecera", c.Item2.HeaderValid ? "Validada" : "No validada"),
                    new WalkthroughItem($"{c.Item1} · longitud", c.Item2.ByteLength is int n ? $"{n} bytes" : "No disponible"),
                    new WalkthroughItem($"{c.Item1} · bloque observado", c.Item2.Block?.Bits ?? "No disponible", c.Item2.State.ToString())
                }.Concat(RecoveredFields(c.Item1, c.Item2))).ToArray()),
            new("Hamming", "Las inversiones por síndrome o recuperaciones de borrados son intentos; el CRC decide la integridad.",
                channels.SelectMany(c => c.Item2.HammingDiagnostics?.Traces.Select(t =>
                    new WalkthroughItem($"{c.Item1} · palabra {t.WordIndex + 1}", t.ReceivedBits,
                        $"{t.State} · datos: {t.DataBits ?? "?"} · {t.Reason}"))
                    ?? [new WalkthroughItem($"{c.Item1}", c.Item2.UsesHamming ? "Sin palabras disponibles" : "Hamming desactivado")]).ToArray()),
            new("CRC y UTF-8", "Solo se publica texto cuando las validaciones del receptor fueron superadas.",
                channels.SelectMany(c => ReceivedCrc(c.Item1, c.Item2).Concat(new[] {
                    new WalkthroughItem($"{c.Item1} · CRC", c.Item2.CrcValid is true ? "Válido" : c.Item2.CrcValid is false ? "Incorrecto" : "No comprobado"),
                    new WalkthroughItem($"{c.Item1} · resultado", c.Item2.State == FdmTextReceiveState.Valid ? c.Item2.Text! : c.Item2.Reason,
                        c.Item2.State.ToString())
                })).ToArray())
        };
        return new(source, bfsk ? "BFSK" : "FDM", steps, waveform,
            sampleCount / (double)sampleRate, "Forma de onda de la fuente seleccionada · amplitud normalizada");
    }

    private static IEnumerable<WalkthroughItem> Characters(string channel, string text, FdmTextChannelFrame frame)
    {
        int offset = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            int count = rune.Utf8SequenceLength;
            string bits = frame.OriginalBlockBits.Substring(8 + offset * 8, count * 8);
            yield return new($"{channel} · {rune}", string.Join(" ", Enumerable.Range(0, count).Select(i => bits.Substring(i * 8, 8))),
                $"bytes {offset + 1}–{offset + count} · {string.Join(" ", Enumerable.Range(0, count).Select(i => Convert.ToInt32(bits.Substring(i * 8, 8), 2).ToString("X2")))}");
            offset += count;
        }
    }

    private static IEnumerable<WalkthroughItem> FrameItems(string channel, FdmTextChannelFrame frame)
    {
        int n = frame.Utf8ByteCount;
        yield return new($"{channel} · preámbulo", BfskFrameBuilder.Preamble, "16 bits · sincronización");
        yield return new($"{channel} · marcador", BfskFrameBuilder.StartMarker, "8 bits · inicio de bloque");
        yield return new($"{channel} · longitud", frame.OriginalBlockBits[..8], $"{n} bytes UTF-8");
        yield return new($"{channel} · contenido", string.Join(" ", Enumerable.Range(0, n).Select(i => frame.OriginalBlockBits.Substring(8 + i * 8, 8))), $"{n * 8} bits útiles");
        yield return new($"{channel} · CRC-32", frame.OriginalBlockBits[(8 + n * 8)..],
            $"Protege longitud + contenido ({1 + n} bytes) · 0x{Convert.ToUInt32(frame.OriginalBlockBits[(8 + n * 8)..], 2):X8}");
    }

    private static IEnumerable<WalkthroughItem> HammingItems(string channel, FdmTextChannelFrame frame)
    {
        if (!frame.UsesHamming) { yield return new(channel, "Omitido", "No se añaden bits de protección."); yield break; }
        for (int i = 0; i < frame.OriginalBlockBits.Length / 4; i++)
            yield return new($"{channel} · palabra {i + 1}", $"{frame.OriginalBlockBits.Substring(i * 4, 4)} → {frame.EncodedBlockBits.Substring(i * 7, 7)}",
                "D1 D2 D3 D4 → P1 P2 D1 P4 D2 D3 D4");
    }

    private static string? ReconstructedBits(FdmTextChannelResult channel)
    {
        string? bits = channel.UsesHamming ? channel.HammingCoversCompleteBlock ? channel.HammingDiagnostics?.DataBits : null
            : channel.Block?.Bits;
        return channel.ByteLength is int n && bits?.Length == (1 + n + MessageCodec.CrcBytes) * 8 ? bits : null;
    }

    private static IEnumerable<WalkthroughItem> RecoveredFields(string name, FdmTextChannelResult channel)
    {
        string? bits = ReconstructedBits(channel);
        if (bits is null) yield break;
        int count = channel.ByteLength!.Value;
        yield return new($"{name} · longitud reconstruida", bits[..8], "Bloque disponible; todavía depende del estado CRC.");
        yield return new($"{name} · contenido reconstruido", string.Join(" ", Enumerable.Range(0, count).Select(i => bits.Substring(8 + i * 8, 8))), "No se publica como texto antes de CRC y UTF-8.");
        yield return new($"{name} · CRC recibido", bits[(8 + count * 8)..], "Bits reconstruidos del bloque recibido.");
    }

    private static IEnumerable<WalkthroughItem> ReceivedCrc(string name, FdmTextChannelResult channel)
    {
        string? bits = ReconstructedBits(channel);
        if (bits is null || bits.Contains('?')) yield break;
        int count = channel.ByteLength!.Value;
        byte[] protectedBytes = Enumerable.Range(0, 1 + count).Select(i => Convert.ToByte(bits.Substring(i * 8, 8), 2)).ToArray();
        yield return new($"{name} · recibido / calculado",
            $"0x{Convert.ToUInt32(bits[(8 + count * 8)..], 2):X8} / 0x{Crc32.Compute(protectedBytes):X8}",
            "CRC del bloque reconstruido por el receptor; la decisión de validez procede del receptor.");
    }
}
