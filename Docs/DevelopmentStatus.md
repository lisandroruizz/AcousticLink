# Estado de desarrollo de AcousticLink

Referencia al 15-11-2026: rama `feature/codex-workflow-quality`, commit `b660964` (`feat: implement continuous FDM listening and improve receiver stability`). El estado se basa en el árbol de código y los documentos de auditoría existentes; los resultados de auditorías anteriores no se vuelven a atribuir a esta sesión.

## Estado funcional

- Aplicación WPF `net10.0-windows` con generación/detección BFSK y flujo FDM de dos canales; trama, sincronización, Hamming(7,4), recuperación opcional de borrados, CRC-32 y mensajes de texto FDM.
- Captura y reproducción mediante NAudio, importación WAV, recepción FDM manual, incremental y continua, con diagnósticos, visualizaciones de señal/decodificación y análisis experimental FDM con exportación CSV.
- El código contiene piezas BFSK aisladas (tonos, detección, trama y sincronización). La comunicación BFSK completa de texto en un canal y la comparación experimental BFSK/FDM siguen pendientes.

## Verificación y asuntos abiertos

- `checks/` contiene 42 programas de verificación C# independientes con directivas .NET 10 y referencia al proyecto WPF. Se ejecutan individualmente como aplicaciones basadas en archivo; no hay proyecto de `dotnet test`. La auditoría `ContinuousFdmListeningAudit.md` registra una ejecución anterior de 42 checks y compilación, pero esos resultados no sustituyen una corrida nueva tras cambios funcionales.
- No se identificó en esta revisión documental un defecto funcional actual confirmado. La auditoría de escucha continua no pudo determinar la causa exacta del incidente acústico original de 5,58 s por falta de muestras/telemetría de ese ensayo.
- Sigue pendiente validar con transmisión acústica física entre equipos la recepción de mensajes y, cuando se implementen, BFSK completo y la comparación BFSK/FDM. Las pruebas sintéticas, de WAV y de captura ambiental no demuestran esa transmisión.

## Continuidad entre chats

Al cerrar una tarea significativa, resumir cambio y archivos, comprobaciones ejecutadas con resultado, pendientes y datos que necesita el siguiente chat. Actualizar este estado solo si cambia materialmente la funcionalidad, la evidencia o los problemas conocidos; contrastar siempre con código y Git.
