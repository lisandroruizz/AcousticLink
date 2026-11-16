# Estado de desarrollo de AcousticLink

Referencia al 15-11-2026: rama `feature/acoustic-calibration`, HEAD inicial `3128f8d`, con la calibración acústica asistida en el árbol de trabajo sin commit. La evidencia de esta tarea se informa por separado de las auditorías anteriores.

## Estado funcional

- Aplicación WPF `net10.0-windows` con generación/detección BFSK y flujo FDM de dos canales; trama, sincronización, Hamming(7,4), recuperación opcional de borrados, CRC-32 y mensajes de texto FDM.
- Captura y reproducción mediante NAudio, importación WAV, recepción FDM manual, incremental y continua, con diagnósticos, visualizaciones de señal/decodificación y análisis experimental FDM con exportación CSV.
- Comunicación de texto BFSK en un canal A (1200/1900 Hz) con trama compartida por canal, Hamming opcional, CRC-32, transmisión/reproducción, WAV digital, recepción manual, incremental y continua, selección de modo e inspección WPF. Detalle en `Docs/SingleChannelBfsk.md`.
- Comparación experimental BFSK/FDM en **Análisis → Comparación**: pares sintéticos con igual contenido útil, RMS digital objetivo y ruido reproducible; análisis del audio seleccionado; métricas por canal y agregadas; historial JSON, CSV separado y espectro relativo. Ver `Docs/BfskFdmComparison.md`. La medición FDM anterior y su CSV se conservan.
- Diagnóstico avanzado en **Receptor → Sesión**: estado de muestras, actividad acústica observada, candidatos de cabecera, validaciones, resultados por canal, contadores acumulados y cronología acotada. Funciona en captura manual, recepción incremental, escucha continua y análisis posterior WAV. Ver `Docs/LiveReceptionDiagnostics.md`.
- **Calibración acústica**: sección independiente con referencia de ruido capturado, emisión de secuencia conocida BFSK/FDM, captura o importación WAV de prueba, energías por portadora, amplitud, SNR estimada solo cuando las condiciones son comparables y recomendaciones orientativas. No aplica ajustes al módem. Ver `Docs/AcousticCalibration.md`.

## Verificación y asuntos abiertos

- `checks/` contiene programas de verificación C# independientes con directivas .NET 10 y referencia al proyecto WPF. Se ejecutan individualmente como aplicaciones basadas en archivo; no hay proyecto de `dotnet test`. La comparación anterior agregó `VerifyComparisonExperiment.cs`. Esta tarea agregó y aprobó `VerifyReceiverSessionDiagnostics.cs`; aprobaron además `VerifyContinuousFdm.cs`, `VerifyIncrementalFdmReception.cs`, `VerifyBfskTextModem.cs`, `VerifyContinuousProgress.cs`, `VerifyContinuousWorkspace.cs`, `VerifyManualCaptureWorkflow.cs` y `VerifyReceptionPresentation.cs`. La solución compiló sin errores ni advertencias con salida aislada; la salida predeterminada quedó bloqueada por una instancia abierta de AcousticLink. Los checks emitieron `NU1900` al no poder consultar avisos de NuGet por la red restringida. Los resultados previos de `ContinuousFdmListeningAudit.md` no sustituyen nuevas corridas tras cambios funcionales.
- No se identificó en esta revisión documental un defecto funcional actual confirmado. La auditoría de escucha continua no pudo determinar la causa exacta del incidente acústico original de 5,58 s por falta de muestras/telemetría de ese ensayo.
- Sigue pendiente validar con transmisión acústica física entre equipos la recepción de mensajes BFSK/FDM y ejecutar una campaña comparativa física. Las pruebas sintéticas, de WAV y de captura ambiental no demuestran esa transmisión.
- La calibración nueva requiere ensayo físico entre dos equipos y revisión visual interactiva con DPI real. `VerifyAcousticCalibration.cs` comprueba cálculos y casos inconclusos con muestras controladas; su resultado debe distinguirse de una medición acústica real.
- En esta tarea compilaron los cambios con salida aislada (`dotnet build AcousticLink.slnx --no-restore -p:OutputPath=..\\.build\\calibration\\`, 0 errores y 0 advertencias). Aprobaron `VerifyAcousticCalibration`, `VerifyCalibrationWorkspace`, `VerifyAudioCapture`, `VerifyBfskTextModem`, `VerifyContinuousFdm`, `VerifyComparisonExperiment`, `VerifyBfskWorkspace`, `VerifyContinuousWorkspace` y `VerifyReceptionPresentation`. La salida predeterminada estaba bloqueada por una instancia abierta. Las revisiones de `core-reviewer` y `wpf-reviewer` produjeron correcciones; se inspeccionó un render WPF sintético a 1366 × 768, sin interacción física ni DPI real 125 %. Estas evidencias no incluyen ensayo acústico físico.

## Continuidad entre chats

Al cerrar una tarea significativa, resumir cambio y archivos, comprobaciones ejecutadas con resultado, pendientes y datos que necesita el siguiente chat. Actualizar este estado solo si cambia materialmente la funcionalidad, la evidencia o los problemas conocidos; contrastar siempre con código y Git.
