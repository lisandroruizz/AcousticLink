#:property TargetFramework=net10.0-windows
#:project ../AcousticLink.App/AcousticLink.App.csproj

using AcousticLink.App.Protocol;
using AcousticLink.App.Signals;
using AcousticLink.App.Visualization;

static void Require(bool ok, string name)
{
    if (!ok) throw new Exception(name);
}

static FdmTextChannelResult Channel(FdmTextReceiveState state, bool header, bool? crc = null, string? text = null) =>
    new(state, header, text?.Length, crc, text, null, state.ToString());
static FdmTextReceiveResult Result(FdmTextChannelResult a, FdmTextChannelResult b, string reason = "Cabecera validada") =>
    new(new(FdmSynchronizationState.Failed, null, null, null, null, reason), a, b);
var absent = Channel(FdmTextReceiveState.HeaderNotSynchronized, false);
var valid = Channel(FdmTextReceiveState.Valid, true, true, "Hola");
var crcBad = Channel(FdmTextReceiveState.CrcIncorrect, true, false);
var truncated = Channel(FdmTextReceiveState.Truncated, true);

var idle = new ReceiverSessionDiagnostics("BFSK", "Micrófono", true, true, 48000);
idle.SetSamples(48000, 0, 48000);
idle.ObserveAcousticPeak(0.003);
idle.Stop("Detenida");
Require(idle.Snapshot().ValidMessages == 0 && idle.Snapshot().CompletedTransmissions == 0 &&
    idle.Snapshot().SynchronizationFailures == 0 && !idle.Snapshot().AcousticActivityObserved,
    "Espera con audio no cuenta fallos ni mensajes");

var bfs = new ReceiverSessionDiagnostics("BFSK", "Micrófono continuo", true, true, 48000);
bfs.ObserveAcousticPeak(0.01);
bfs.ObserveProgress(Result(absent, absent, "Candidata completa en evaluación temporal"));
bfs.ObserveProgress(Result(absent, absent, "Buscando cabecera"));
bfs.ObserveProgress(Result(valid, absent), 100);
bfs.RecordCompleted(1, Result(valid, absent), true);
bfs.RecordCompleted(2, Result(valid, absent), true); // Mismo texto, transmisión distinta.
bfs.RecordCompleted(2, Result(valid, absent), true); // Publicación duplicada.
bfs.RecordCompleted(3, Result(crcBad, absent), true);
bfs.RecordCompleted(4, Result(truncated, absent), true);
var count = bfs.Snapshot();
Require(count.AcousticActivityObserved && count.CandidateHeaders == 1 && count.UnconfirmedCandidates == 1 &&
    count.ConfirmedHeaders == 1, "Actividad, candidato y cabecera independientes");
Require(count.CompletedTransmissions == 4 && count.EvaluatedChannelFrames == 4 && count.ValidMessages == 2 &&
    count.RejectedChannelFrames == 2 && count.CrcRejections == 1 && count.IncompleteChannelFrames == 1,
    "Repeticiones, CRC y truncación");
Require(count.ChannelBState is null && count.Events.All(e => !e.Detail.Contains("Canal B")), "BFSK sin canal B");
for (int i = 5; i <= 35; i++) bfs.RecordCompleted(i, Result(valid, absent), true);
Require(bfs.Snapshot().ValidMessages == 33 && bfs.Snapshot().Events.Count <= ReceiverSessionDiagnostics.EventLimit,
    "Contadores independientes de historial acotado");
bfs.Stop("Detenida");
bfs.RecordCompleted(36, Result(valid, absent), true);
Require(bfs.Snapshot().ValidMessages == 33 && !bfs.Snapshot().IsActive, "Publicación tardía ignorada");

var fdm = new ReceiverSessionDiagnostics("FDM", "Micrófono continuo", true, false, 48000);
fdm.ObserveProgress(Result(valid, crcBad), 200);
fdm.RecordCompleted(1, Result(valid, crcBad), true);
Require(fdm.Snapshot().ValidMessages == 1 && fdm.Snapshot().CrcRejections == 1 &&
    fdm.Snapshot().ChannelAState == "Valid" && fdm.Snapshot().ChannelBState == "CrcIncorrect", "Canales FDM independientes");
fdm.RecordCompleted(2, Result(valid, absent), true);
Require(fdm.Snapshot().SynchronizationFailures == 1, "FDM: falta de cabecera B con A confirmada");
var next = new ReceiverSessionDiagnostics("BFSK", "WAV importado", false, true, 48000);
next.RecordFinalAnalysis(Result(absent, absent, "No se encontró cabecera"));
next.RecordFinalAnalysis(Result(valid, absent));
Require(next.Snapshot().SynchronizationFailures == 0 && next.Snapshot().ValidMessages == 0 &&
    next.Snapshot().CompletedTransmissions == 0 && !next.Snapshot().IsLive &&
    next.SessionId != bfs.SessionId, "Sesión nueva y análisis WAV sin captura ficticia");
Console.WriteLine("Diagnóstico de sesión: contadores, candidatos, CRC, límites, modos e identidad OK");
