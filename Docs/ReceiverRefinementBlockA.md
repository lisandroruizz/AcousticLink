# Refinamiento del flujo — Bloque A

Se completa únicamente el Bloque A autorizado: cancelación, origen de audio, duplicación del Receptor y decisiones sincronizadas. El rediseño de interpretación/reconstrucción (B) y el centro de operaciones/Emisor (C) quedan pendientes. La navegación global, el protocolo y los cálculos no cambian.

## Problemas identificados

- La reproducción actualizaba `ReceiverSignalMonitor` con el buffer del Emisor, aunque hubiera un WAV acústico seleccionado.
- El resultado del monitor interpretativo se publicaba en varias vistas sin comprobar su identidad de fuente.
- Las decisiones de ventana móvil de 60 ms se podían interpretar como símbolos sincronizados de 100 ms.
- El Receptor tenía dos formas de onda principales equivalentes en herramientas distintas.
- Cambiar de fuente u opciones cancelaba una búsqueda en curso. Iniciar una vista digital podía reemplazar esa búsqueda acústica y provocar otra cancelación.

## Cancelación

La ruta comprobable es `ResetReceptionObservation → CancellationTokenSource.Cancel → FdmTextReceiver → FdmFrameSynchronizer.ThrowIfCancellationRequested`. Es cancelación cooperativa esperada al cambiar audio, opciones o cerrar. La captura aportada no permite identificar con certeza cuál de esas acciones originó aquella excepción concreta ni si Visual Studio se detuvo por primera oportunidad.

Ahora la observación digital no inicia ni reemplaza búsquedas acústicas. Se captura `OperationCanceledException` solo cuando el token propio está cancelado; las demás excepciones se registran y se presentan como fallo de observación. Se preservan todos los puntos de cancelación del sincronizador. Las pruebas fuerzan una cancelación durante la búsqueda y comprueban que no se publican resultados antiguos.

Visual Studio puede detenerse en el lanzamiento de una excepción aunque el límite asíncrono la maneje después. Si sucede, continuar la ejecución permite comprobar el estado final. Revisar Exception Settings → System.OperationCanceledException → Thrown solo si se confirma que es una cancelación manejada; no retirar `ThrowIfCancellationRequested` ni desactivar indiscriminadamente excepciones no controladas.

## Fuentes y presentación

Las etiquetas persistentes son GENERADA · SALIDA DIGITAL, CAPTURADA · ENTRADA ACÚSTICA y WAV IMPORTADO · ANÁLISIS. La vista del Receptor rechaza buffers generados, incluso si se intenta asignarlos directamente. La salida sigue disponible en Emisor y en el monitor compartido, explícitamente como prueba digital. Iniciar una reproducción no reemplaza el audio del Receptor.

Los resultados se publican solo en vistas con el mismo SourceId. Cambiar de audio conserva la invalidación existente. La instrumentación anterior de captura se conserva como detalle secundario cerrado; la onda principal con mapa y decisiones está en Señal y detección. No hay tres ondas completas simultáneas dentro de esa perspectiva.

## Símbolos reales

`ReceivedSymbols.From` adapta únicamente `Synchronization.ChannelA/B.Header.Symbols` y `ChannelA/B.Block.Symbols`. Cada elemento conserva el mismo `BfskSymbolResult`, su decisión, energías, motivo y límites de muestras. No llama a otro detector, no usa las referencias experimentales ni los bits conocidos del Emisor.

La numeración visible empieza en 1; los índices internos permanecen en base cero. El período nominal es 0,1 s y el soporte de análisis es el intervalo central real del decodificador. Al hacer clic se selecciona el símbolo completo en la onda y mapa y se sitúa el cursor en su análisis central. Las tarjetas muestran sus energías originales; el mapa sigue usando sus propias ventanas de 20 ms. Los soportes no se confunden.

Antes de sincronizar no se construye una grilla de símbolos supuestos. Si solo existe longitud, solo se muestran esas decisiones y la cabecera disponible. Un canal sin cabecera válida no hereda los símbolos del otro. No se añaden decisiones para el relleno. Los símbolos son anteriores a Hamming y CRC; un 0/1 no equivale a mensaje válido.

Durante captura continúan la onda y energías en vivo. El monitor de trama existente reprocesa prefijos periódicamente con el receptor original, por lo que puede aportar símbolos cuando ese receptor los devuelve. No es un motor incremental nuevo ni escucha continua. Cuando no hay datos suficientes o un bloque truncado no expone sus decisiones parciales, la vista indica ausencia de símbolos; no los completa artificialmente. El recorrido WAV sigue siendo inspección posterior sin sonido.

## Archivos de este bloque

- Nuevos: `Visualization/ReceivedSymbolTrace.cs`, `Visualization/ReceivedSymbolsView.cs`, este informe.
- Adaptados: `MainWindow.Interpretation.cs`, `MainWindow.Signals.cs`, `MainWindow.xaml`, `Visualization/CarrierEnergyMap.cs`, `Visualization/SignalMonitorView.cs`, `Visualization/ReceiverFrameStrip.cs`.
- Comprobaciones extendidas: `VerifyReceiverObservation.cs`, `VerifySignalMonitor.cs`, `VerifyCompactLayout.cs`.

## Comprobaciones y pendientes

Se verifica identidad de símbolos/energías con resultados reales, selección por límites absolutos, Hamming activado/desactivado, cabecera ambigua/parcial, conservación del audio acústico al transmitir y cancelación controlada. La suite anterior conserva validaciones de CRC, borrados, BER, cobertura, WAV y CSV.

Los renderizados opcionales son sintéticos y fuera de pantalla. Falta comprobar esta nueva disposición con parlante/micrófono, selección por mouse, pausa y retorno al vivo, cambios rápidos de WAV, captura de 30 s, cierre durante captura y DPI real del 125 %.

No se afirma demodulación continua por muestra ni validación acústica de mensajes. La interpretación exploratoria con senoides aún pertenece a la implementación anterior; su sustitución por comparación de energías, trazas Hamming y reconstrucción de información corresponde al Bloque B. El Panel general y el recorrido didáctico completo del Emisor no se rediseñan en este bloque.
