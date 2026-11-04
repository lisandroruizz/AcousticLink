# Corrección de captura manual, publicación y laboratorio

## Hallazgos

No se encontró una espera síncrona del Dispatcher que demostrara un deadlock. Sí había una dependencia incorrecta: `QueueReceptionObservation` ejecutaba periódicamente `FdmTextReceiver.Receive` sobre el audio acumulado aun sin activar la recepción incremental. Esa ruta añadía carga y publicaba cabeceras únicamente en el laboratorio, mientras el resumen seguía sin análisis. Se eliminó esa recepción automática: observar onda/energías, recorrer WAV y recibir mensajes son acciones independientes.

El monitor además reconstruía vistas aunque no hubiera muestras nuevas, y forzaba el refresco del laboratorio repetidamente para la misma selección. Ahora evita esos trabajos duplicados. No se cambiaron NAudio, el detector, sincronización, CRC, Hamming, BER, formatos ni protocolo.

## Modalidades

- Manual: captura/onda/energía continúan, pero procesamiento de mensajes queda en cero. Solo **Recibir mensajes** inicia sincronización y reconstrucción del audio seleccionado.
- Incremental: el worker existente consume muestras nuevas y publica estados y resultados parciales antes de detener la captura. Se mantienen independencia A/B, cancelación, identidad de sesión y equivalencia manual.
- Recorrer WAV: accesible también desde el laboratorio. Mueve el cursor sobre una fuente ya disponible; no reproduce audio, no usa el micrófono ni lanza recepción nueva.

Los resultados manuales se identifican como análisis manual completo; los incrementales finalizados, como resultados incrementales retenidos. Durante captura, inspeccionar un símbolo anterior se distingue del seguimiento visual y no detiene el worker. No se inicia un receptor oculto al abrir un WAV o mirar el laboratorio.

## Ventanas y reconstrucción

La observación no sincronizada se etiqueta explícitamente como exploración acústica, sin atribuir cabecera ni posición de trama. El tiempo visible usa seis decimales como el ToolTip; ambos identifican el símbolo completo y distinguen el intervalo central del detector.

Con trazas válidas, la columna 3 mantiene palabra seleccionada, roles P1/P2/D1/P4/D2/D3/D4, recibido/resultante y nibble decodificado. Sin traza no muestra un selector vacío: identifica los bits disponibles, el paso pendiente y los símbolos faltantes. Las palabras incompletas muestran solo posiciones recibidas y ningún nibble inventado. Una longitud no concluyente o recuperación desactivada conserva el motivo real del receptor. Longitud, UTF-8 y CRC permanecen separados.

## Pruebas

`VerifyManualCaptureWorkflow.cs` simula exclusivamente el dispositivo y alimenta buffers al flujo WPF real: crecimiento de muestras/onda, heartbeat e input del Dispatcher, botón Detener, finalización antes del límite, WAV sin cambios, recepción manual, importación, recorrido histórico y cierre. No sustituye pruebas con hardware.

Se amplió `VerifyLiveFdmWorkspace.cs` para comprobar, además del progreso durante captura, la reconstrucción completa de siete posiciones y nibble y la igualdad temporal entre etiqueta y ToolTip. Se actualizó `VerifySignalMonitor.cs`: un monitor por sí solo ya no debe lanzar sincronización automática.

También se ejecutaron sondas puntuales con **micrófono real y WPF fuera de pantalla**, sin guardar audio ni emitir una transmisión:

- Servicio: 46.560 → 71.520 muestras a 48 kHz; parada manual sin error.
- WPF manual, aproximadamente 8 segundos: 394.080 muestras capturadas, 390.720 publicadas al monitor al tomar la lectura, cero muestras procesadas por el receptor, 254 pulsos de heartbeat; demora máxima observada de 232 ms incluyendo inicialización. Parada Manual sin error.
- WPF hasta el límite de 10 segundos: 479.520 muestras capturadas y publicadas, cero procesadas por mensajes, 351 pulsos; demora máxima de 241 ms incluyendo inicialización. Finalización DurationLimit sin error.

Después de los cambios no se reprodujo un bloqueo sostenido en esas pruebas. Esto no permite afirmar retrospectivamente que toda la congelación física reportada tuviera una única causa ni garantiza fluidez en todos los dispositivos. Las demoras medidas no equivalen a latencia acústica ni BER.

Pendiente: transmisión acústica Hola/Mundo, DualSense/configuración específica del usuario, volumen reducido, captura de 30 segundos y revisión visual interactiva en diferentes DPI. La captura real anterior prueba inicio/flujo/parada y respuesta WPF, no recuperación física de mensajes.

## Archivos de esta corrección

- `MainWindow.Interpretation.cs`: publicación exclusiva de resultados de la fuente y modalidad correctas.
- `MainWindow.Signals.cs`: omitir refrescos sin muestras nuevas.
- `MainWindow.ReceptionActivity.cs`: distinguir procedencia y modalidad de resultados.
- `Visualization/SignalMonitorView.cs`: refrescos coalescidos y recorrido WAV accesible en el laboratorio.
- `Visualization/ReceiverInterpretationView.cs`: ventanas coherentes y reconstrucción/provisionales explícitos.
- `Visualization/DemodulationSelection.cs`: campo conocido de palabras todavía incompletas sin atribuir bits reconstruidos.
- `checks/VerifyManualCaptureWorkflow.cs`, `checks/VerifyLiveFdmWorkspace.cs`, `checks/VerifySignalMonitor.cs` y esta documentación.

Se conservaron las demás modificaciones del workspace. Sin cambio de rama ni commits.
