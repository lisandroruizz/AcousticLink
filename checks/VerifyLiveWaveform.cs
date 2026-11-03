#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:project ../AcousticLink.App/AcousticLink.App.csproj

using System.Windows;
using System.IO;
using AcousticLink.App.Audio;
using AcousticLink.App.Visualization;

foreach (int rate in new[] { 48000, 44100 })
{
    var captured = new CapturedAudio(rate, 1, 20);
    var live = new IncrementalWaveform(captured.SampleCapacity);
    var original = new float[rate * 12];
    original[1] = 0.001f; original[rate * 8] = -0.002f;
    var scratch = new float[8192];
    for (int position = 0; position < original.Length; position += 731)
    {
        int count = Math.Min(731, original.Length - position);
        captured.Append(original[position..(position + count)], count);
        int read = captured.CopySamples(live.SampleCount, scratch);
        live.Append(scratch.AsSpan(0, read));
        Require(live.SampleCount == position + count && live.Snapshot().Length <= 1200 &&
            live.DisplayFraction >= live.SampleCount / (double)captured.SampleCapacity, "Progreso incremental acotado");
    }
    var before = captured.GetMetrics();
    captured.Complete(CaptureStopReason.Manual);
    Require(captured.CopySamples(live.SampleCount, scratch) == 0 && captured.GetMetrics().Count == original.Length, "Fin de captura sin repetir muestras");
    var summary = live.Snapshot();
    for (int i = 0; i < summary.Length; i++)
    {
        var interval = original.Skip(i * live.SamplesPerBucket).Take(live.SamplesPerBucket).ToArray();
        Require(summary[i].Minimum == interval.Min() && summary[i].Maximum == interval.Max(), "Mínimos/máximos reales por intervalo");
    }
    Require(captured.GetSamples().SequenceEqual(original) && captured.GetMetrics().Rms == before.Rms &&
        captured.GetMetrics().Peak == before.Peak && before.DurationSeconds == 12, "Muestras, RMS y duración intactos");
    string path = Path.Combine(Path.GetTempPath(), "AcousticLink-live-" + Guid.NewGuid() + ".wav");
    try
    {
        captured.SaveWav(path);
        var selection = new AudioRecordingSelection(); selection.ImportWav(path);
        Require(selection.Current!.Audio.SampleRate == rate && selection.Current.Audio.GetSamples().SequenceEqual(original), "WAV íntegro tras dibujar");
    }
    finally { File.Delete(path); }
    var cancelled = new CapturedAudio(rate, 1, 10);
    cancelled.Append([0.1f, -0.1f], 2); cancelled.Complete(CaptureStopReason.ApplicationClosing);
    cancelled.Append([1], 1);
    Require(cancelled.GetStatus().Count == 2 && cancelled.CopySamples(0, scratch) == 2, "Cancelación conserva prefijo real");
    var restarted = new CapturedAudio(rate, 1, 10);
    restarted.Append([0], 1);
    var newView = new IncrementalWaveform(restarted.SampleCapacity);
    newView.Append(scratch.AsSpan(0, restarted.CopySamples(0, scratch)));
    Require(newView.SampleCount == 1 && newView.Snapshot()[0].Maximum == 0, "Reinicio no hereda picos");
}

Exception? failure = null;
var thread = new Thread(() =>
{
    try
    {
        var view = new WaveformView();
        view.SetData([new(-0.001f, 0.002f)], 20, 0.1);
        Require(view.PeakAbsolute == (double)0.002f && Math.Abs(view.VerticalLimit - 0.002f * 1.1) < 1e-12, "Escala automática con margen y pico real");
        view.UseAutoScale = false;
        Require(view.VerticalLimit == 1, "Escala original ±1");
        view.UseAutoScale = true;
        var live = new IncrementalWaveform(100);
        live.Append([0.001f]); view.SetData(live.Snapshot(), 20, live.DisplayFraction);
        double initial = view.VerticalLimit;
        live.Append([0, 0, 0]); view.SetData(live.Snapshot(), 20, live.DisplayFraction);
        Require(view.VerticalLimit == initial, "El silencio no contrae la escala acumulada");
        view.SetData([new(0, 0)], 1);
        Require(view.PeakAbsolute == 0 && view.VerticalLimit == 1, "Silencio estable sin división por cero");
        view.SetData([new(-float.Epsilon, float.Epsilon)], 1);
        Require(double.IsFinite(view.VerticalLimit) && view.VerticalLimit > 0, "Señal extremadamente pequeña");
        view.SetData([], 0);
        Require(view.PeakAbsolute == 0 && view.VerticalLimit == 1, "Estado vacío reinicia escala");
        view.Measure(new Size(800, 180)); view.Arrange(new Rect(0, 0, 800, 180));
    }
    catch (Exception ex) { failure = ex; }
});
thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
if (failure is not null) throw new InvalidOperationException("Escala WPF", failure);
Console.WriteLine("Captura incremental, escala, silencio, progreso, fin/reinicio, muestras/métricas y WAV a ambas tasas: OK");
static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
