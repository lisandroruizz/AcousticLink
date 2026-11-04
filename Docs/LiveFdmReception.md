# Recepción FDM incremental durante captura

## Uso

En Receptor → Opciones, activar **Decodificar durante la captura** antes de iniciar el micrófono. Está desactivado por defecto. Seleccionar Hamming y recuperación de borrados de acuerdo con el emisor; la configuración queda fijada durante la captura. El flujo manual Capturar → Detener → Recibir mensajes sigue disponible. Un WAV importado no se convierte en una captura en vivo.

Los canales muestran búsqueda, cabecera validada, recepción de longitud y bloque, y resultado final independiente. El texto solo se publica después de CRC y UTF-8 válidos. El laboratorio recibe los símbolos y diagnósticos reales del prefijo procesado. Pausar el seguimiento visual o inspeccionar un símbolo no pausa al worker de recepción. Cancelar recepción tampoco detiene el micrófono. Los resultados ya validados se conservan al cancelar; una captura nueva invalida los anteriores.

## Arquitectura

`IncrementalFdmTextReceiver` es independiente de WPF. Cada instancia corresponde a una sola captura/transmisión, con frecuencia real, opciones inmutables y capacidad máxima de 30 segundos. Un arreglo fijo conserva el prefijo acústico necesario: no se descarta un límite entre callbacks, no hay cola ilimitada ni normalización. La grabación original continúa en `CapturedAudio` sin modificaciones.

`MainWindow.LiveReception.cs` consulta la captura mediante el timer existente de 100 ms. Un único worker por sesión copia únicamente las muestras nuevas usando `CapturedAudio.CopySamples` y un bloque reutilizable de 8192 muestras. El callback NAudio no ejecuta sincronización, demodulación ni renderizado. Si el worker está ocupado no se crea otro; el siguiente ciclo recoge todo el sufijo pendiente. La identidad de sesión y la cancelación impiden publicaciones tardías después de cambiar de fuente, reiniciar o cerrar.

La observación periódica anterior del monitor no vuelve a analizar todo el prefijo cuando esta modalidad es propietaria de la captura. La captura, monitor de energía, onda y reproducción mantienen sus rutas anteriores.

## Sincronización y decisiones

`FdmFrameSynchronizer.HeaderSearch` conserva el cursor de la grilla gruesa de 20 ms y los grupos de candidatas. Solo evalúa nuevos inicios que ya disponen de los 24 símbolos completos. Reutiliza `Evaluate`, `Better`, las validaciones por canal y el refinamiento de 1 ms del sincronizador manual. No publica una cabecera hasta que cierre su grupo temporal y pueda refinarse; esto agrega aproximadamente 120 ms, más la cadencia del worker, después de disponer de toda la cabecera. No hay umbrales nuevos.

La posición final no alineada con la grilla se incorpora al cerrar la captura, igual que en la búsqueda manual. Se mantiene la política de ambigüedad: si posteriormente aparece otra cabecera separada, se rechaza la selección arbitraria. No se implementa escucha indefinida de mensajes consecutivos. Una validación durante captura puede invalidarse por una ambigüedad posterior, igual que ocurriría al analizar ese audio completo.

`FdmSequenceDecoder.DecodeMessageSymbol` reutiliza el núcleo del decodificador. Todos los límites parten del origen absoluto; las decisiones usan la misma ventana central 20–80 % y el mismo detector. Se espera el símbolo completo de 0,1 s y no se vuelve a detectar cada símbolo del bloque. Los canales acumulan solo sus bits necesarios; no se incorpora el relleno del canal corto.

La interpretación progresiva reutiliza `Hamming74Codec` y el validador de `FdmTextReceiver`/`MessageCodec`. Solo las palabras completas disponibles pasan a Hamming. Se distinguen los símbolos `?` recibidos de los bits reconstruidos. Longitud inválida o irrecuperable detiene ese canal sin extender arbitrariamente el bloque; el otro continúa. Hamming no protege la cabecera. Las referencias experimentales no participan.

## Cierre y equivalencia

Al finalizar se usa la sincronización acumulada exacta y el lector manual existente sobre el prefijo capturado. Este paso acotado relee los bloques del mensaje, sin volver a buscar cabeceras en toda la grabación; conserva estados, alcance de diagnósticos y semántica del receptor manual, incluso en truncaciones. El resultado canónico puede disponer de menos detalle de bloque que el prefijo mostrado durante recepción, porque el lector manual no entrega todos los bloques parciales. Ahora `ObservedResult` conserva esas decisiones y trazas reales para la interfaz cuando la sincronización sigue siendo la misma, sin alterar estado final, CRC ni texto del resultado canónico. No se conserva una asociación invalidada por sincronización ambigua o por un origen diferente. El WAV conserva todas las muestras.

Las pruebas comparan sincronización, inicio, ventanas, energías, decisiones, bits, longitud, reconstrucción Hamming, CRC y texto. También se comparan los mensajes completos publicados **antes** de llamar a `Complete`, para que la equivalencia no dependa únicamente del cierre canónico.

## Archivos de esta etapa

- Nuevos: `Protocol/IncrementalFdmTextReceiver.cs`, `MainWindow.LiveReception.cs`, esta documentación, `checks/VerifyIncrementalFdmReception.cs` y `checks/VerifyLiveFdmWorkspace.cs`.
- Adaptados: `Protocol/FdmTextReceiver.cs`, `Signals/FdmFrameSynchronizer.cs`, `Signals/FdmSequenceDecoder.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `MainWindow.Interpretation.cs`, `Visualization/ReceptionView.cs` y `Visualization/FrameExplorerView.cs`.

No se cambiaron frecuencias, ganancias, decisiones/umbrales Goertzel, matemática Hamming, CRC, UTF-8, BER, WAV ni CSV. Se conserva el resto de las modificaciones existentes del workspace.

## Verificación y límites

Los checks nuevos usan fragmentos variables, ruido reproducible, dos tasas, ambos perfiles, Hamming sí/no, borrados de longitud/datos/CRC, errores binarios deliberados, cabeceras parciales, truncaciones, silencio, ambigüedad, cancelación y límites de memoria. El check WPF utiliza muestras sintéticas sin abrir dispositivos de audio y comprueba publicación progresiva, invalidación y cierre.

Pendiente: una prueba acústica con parlante y micrófono, WAV físicos que no están en el repositorio, carga de CPU y latencia con dispositivos reales, captura de 30 segundos y navegación durante captura. Los ensayos sintéticos y el renderizado WPF no sustituyen esa validación. La RAM adicional de la sesión está acotada a unos 5,76 MB de muestras a 48 kHz/30 s, además de la captura y el monitor existentes.

La rama encontrada al comenzar fue `feature/desktop-workspace-ui`. Se respetó la prohibición de crear/cambiar ramas y hacer commits; no se creó `feature/live-fdm-reception`.

## Auditoría de integración y visibilidad

Se comprobó el procesamiento previo al final con los checks existentes antes de modificarlo: sí había búsqueda, símbolos, Hamming y validación durante la captura. El principal problema de observabilidad era que abrir el monitor ocultaba el inspector donde estaba el estado, mientras los resultados A/B quedaban en la vista Mensajes. Además, el cursor al final del audio no pertenece al último símbolo completo: el laboratorio podía terminar mostrando exploración acústica en vez de una decisión histórica.

La nueva franja de estado A/B está en el monitor; el inspector utiliza la misma proyección `ReceptionActivity`. Los contadores salen de Block y HammingDiagnostics, el progreso solo se define con una longitud válida, y el texto exige CRC y estado Valid. CRC se verifica de forma síncrona al reunir el bloque; no se agrega una espera artificial para exhibir una fase de verificación. Se distinguen captura activa, decodificación cancelada/detenida, finalización del worker e inspección posterior/WAV. Los controles se bloquean inmediatamente al iniciar captura para fijar la configuración.

El laboratorio sigue el último símbolo completo disponible mientras está habilitado el seguimiento, conservando el cursor general de captura. Navegar manualmente suspende el seguimiento, no el worker. Finalizar conserva la selección histórica y publicar el buffer final de la misma fuente no la borra. La navegación global/zoom/mapa completo sigue en Señal y detección; Cómo la interpreta prioriza la ventana local sin esos paneles adicionales.

Muestras reales muestra ahora el símbolo completo, sombrea exactamente el intervalo utilizado por el detector y muestra las medidas de RMS/pico central, duración, frecuencia real y conteos. Los rangos exactos y medidas de la ventana completa están en ToolTip. La tira local conserva 0/1/? y distingue inversiones intentadas y borrados recuperados por posición real.

Archivos añadidos para presentación: `MainWindow.ReceptionActivity.cs`, `Visualization/ReceptionActivity.cs`, `Visualization/ReceptionProgressView.cs`, `Visualization/SymbolWindowObservation.cs`, `checks/VerifyReceptionPresentation.cs`. Se adaptaron la integración de MainWindow, el monitor/laboratorio, el snapshot incremental y los checks incrementales/WPF existentes. No se cambiaron detector, sincronizador, CRC, Hamming, BER, WAV ni CSV en este refinamiento.

La prueba WPF simula únicamente la publicación de buffers del dispositivo y recorre QueueLiveReception, el mismo punto de entrada del timer. Verifica que la captura siga activa cuando la UI ya muestra longitud/palabras, la selección del último símbolo, pausa visual, conservación al terminar y cancelación/reinicio. Los PNG son renderizados fuera de pantalla con muestras sintéticas; no constituyen una prueba acústica con micrófono.

### Inventario del refinamiento de observabilidad

- Creados: `AcousticLink.App/MainWindow.ReceptionActivity.cs`, `AcousticLink.App/Visualization/ReceptionActivity.cs`, `AcousticLink.App/Visualization/ReceptionProgressView.cs`, `AcousticLink.App/Visualization/SymbolWindowObservation.cs` y `checks/VerifyReceptionPresentation.cs`.
- Modificados: `AcousticLink.App/MainWindow.LiveReception.cs`, `AcousticLink.App/MainWindow.Interpretation.cs`, `AcousticLink.App/MainWindow.Signals.cs`, `AcousticLink.App/MainWindow.xaml`, `AcousticLink.App/MainWindow.xaml.cs`, `AcousticLink.App/Protocol/IncrementalFdmTextReceiver.cs`, `AcousticLink.App/Visualization/SignalMonitorView.cs`, `AcousticLink.App/Visualization/ReceiverInterpretationView.cs`, `checks/VerifyIncrementalFdmReception.cs`, `checks/VerifyLiveFdmWorkspace.cs`, `Docs/LiveFdmReception.md` y `Docs/DemodulationLab.md`.

Resultado de esta pasada: compilación sin advertencias ni errores y 37 verificaciones aprobadas. Los checks de distribución incluyen 1366×768, 1920×1080 y equivalencia lógica del 125 %, sin afirmar una prueba interactiva con DPI físico.

La corrección posterior de modalidades está documentada en `Docs/CaptureModesRegression.md`: el monitor ya no lanza recepción completa automática en ningún modo. Las tramas proceden exclusivamente de la sesión incremental o de Recibir mensajes; el recorrido WAV solo inspecciona la fuente existente.
