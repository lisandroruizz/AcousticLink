# Estado de desarrollo de AcousticLink

Referencia al 15-11-2026: rama `feature/bfsk-fdm-comparison`, HEAD inicial `e3043a6`, con la comparación experimental en el árbol de trabajo sin commit. El estado se basa en el código y las verificaciones de esta tarea; las auditorías anteriores no se vuelven a atribuir a esta sesión.

## Estado funcional

- Aplicación WPF `net10.0-windows` con generación/detección BFSK y flujo FDM de dos canales; trama, sincronización, Hamming(7,4), recuperación opcional de borrados, CRC-32 y mensajes de texto FDM.
- Captura y reproducción mediante NAudio, importación WAV, recepción FDM manual, incremental y continua, con diagnósticos, visualizaciones de señal/decodificación y análisis experimental FDM con exportación CSV.
- Comunicación de texto BFSK en un canal A (1200/1900 Hz) con trama compartida por canal, Hamming opcional, CRC-32, transmisión/reproducción, WAV digital, recepción manual, incremental y continua, selección de modo e inspección WPF. Detalle en `Docs/SingleChannelBfsk.md`.
- Comparación experimental BFSK/FDM en **Análisis → Comparación**: pares sintéticos con igual contenido útil, RMS digital objetivo y ruido reproducible; análisis del audio seleccionado; métricas por canal y agregadas; historial JSON, CSV separado y espectro relativo. Ver `Docs/BfskFdmComparison.md`. La medición FDM anterior y su CSV se conservan.

## Verificación y asuntos abiertos

- `checks/` contiene programas de verificación C# independientes con directivas .NET 10 y referencia al proyecto WPF. Se ejecutan individualmente como aplicaciones basadas en archivo; no hay proyecto de `dotnet test`. Para la comparación se agregó `VerifyComparisonExperiment.cs`; se ejecutaron además `VerifyFdmExperimentMetrics.cs`, `VerifyBfskTextModem.cs` y `VerifyBfskWorkspace.cs`, y se compiló la solución. Los checks fueron aprobados; la consulta de avisos NuGet emitió `NU1900` por red restringida. Los resultados previos de `ContinuousFdmListeningAudit.md` no sustituyen nuevas corridas tras cambios funcionales.
- No se identificó en esta revisión documental un defecto funcional actual confirmado. La auditoría de escucha continua no pudo determinar la causa exacta del incidente acústico original de 5,58 s por falta de muestras/telemetría de ese ensayo.
- Sigue pendiente validar con transmisión acústica física entre equipos la recepción de mensajes BFSK/FDM y ejecutar una campaña comparativa física. Las pruebas sintéticas, de WAV y de captura ambiental no demuestran esa transmisión.

## Continuidad entre chats

Al cerrar una tarea significativa, resumir cambio y archivos, comprobaciones ejecutadas con resultado, pendientes y datos que necesita el siguiente chat. Actualizar este estado solo si cambia materialmente la funcionalidad, la evidencia o los problemas conocidos; contrastar siempre con código y Git.
