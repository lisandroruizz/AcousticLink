# Laboratorio de demodulación del Receptor

Esta vista reemplaza las cuatro franjas de ondas de «Cómo la interpreta». Ya no dibuja senoides ideales de A, B ni su suma estimada. «Cómo se forma FDM» permanece en el Emisor; el laboratorio nuevo solo es visible en el Receptor.

## Tres zonas

1. **Muestras reales**: canal, selector de símbolo sincronizado, origen y cabecera, gráfica local del símbolo completo con la ventana central realmente analizada sombreada, tiempos y cantidad de muestras. RMS y pico se calculan sobre muestras reales; los rangos precisos y medidas del símbolo completo están en ToolTip. La gráfica usa mín/máx para preservar picos y solo ajusta su escala visual.
2. **Energías → decisión BFSK**: dos barras lineales con escala común y valores numéricos; marca de energía mínima; candidato de mayor energía; energía ganadora/perdedora; razón; criterios y resultado 0/1/?. El motivo se toma del resultado original del detector. No hay probabilidades de confianza inventadas.
3. **Reconstrucción**: CRC y mensaje validado destacados; posición y campo; palabra Hamming seleccionable, siete posiciones P1/P2/D1/P4/D2/D3/D4; recibido→resultante; síndrome, inversión intentada y borrados recuperados; cuatro bits reconstruidos; byte, binario/hexadecimal y carácter solo cuando está validado. El contenido avanzado puede desplazarse dentro del inspector.

## Datos y criterios

Para símbolos sincronizados, `DemodulationSelection` conserva el mismo `BfskDetectionResult` y `BfskSymbolResult` que devolvió el receptor. No llama otra vez a un detector para alimentar esa decisión. La ventana es `AnalysisStartSample` / `AnalysisSampleCount`, generalmente el 20–80 % del período de 0,1 s.

Sin un símbolo sincronizado en ese instante, se reutiliza `FdmToneDetector.Analyze` sobre una ventana acústica exploratoria de hasta 60 ms. Se identifica expresamente como exploratoria, sin número ni campo de trama atribuibles. Una ventana insuficiente sigue siendo no concluyente. No se activa escucha continua.

`FdmDecisionDiagnostics` añade datos de solo lectura al resultado: duración, mínimos configurados y comprobaciones realmente alcanzadas. La energía se obtiene del Goertzel existente. El detector conserva el orden y las condiciones anteriores: duración mínima; energía ganadora; relación respecto de la perdedora. Si la energía falla, la relación se muestra como no evaluada. Los valores iniciales siguen siendo 40 ms, 9e-6 y 4; un detector configurado con otros valores conserva esos valores en su traza. No hay una segunda implementación de aceptación en la interfaz.

Las barras comparan energías medidas, no amplitud ni confianza. Si ambas energías son cero, la razón es no definida; si solo la perdedora es cero, es infinita. La decisión siempre proviene de `Bit` y el motivo de `Reason`. No se deduce un bit del brillo del mapa de 20 ms.

## Vínculo con reconstrucción

El símbolo en posición de trama 25 corresponde al primer bit del bloque después de los 24 bits de cabecera. Antes de Hamming, el índice codificado se divide por siete para localizar la palabra real conservada. Las posiciones 3, 5, 6 y 7 corresponden a los cuatro bits de información; 1, 2 y 4 son paridad y no se presentan como bits originales directos.

Las palabras y correcciones se leen de `HammingDiagnostics.Traces`. Un borrado recibido sigue mostrado como `?`, aun cuando el resultado reconstruido sea concluyente. Un resultado no disponible se muestra con `—`. No se completa una traza a partir de contadores. Si solo existen trazas de longitud, no se inventan palabras posteriores. Se mantiene la diferencia entre inversiones intentadas y borrados recuperados.

Cada botón de la palabra/byte permite volver al símbolo recibido que lo originó mediante `Block.Symbols`. El cursor y la selección temporal se actualizan en el monitor. Sin Hamming se agrupa por bytes reales; no se atribuyen palabras Hamming. Las asociaciones al byte respetan el byte inicial de longitud y el CRC final. Los caracteres multibyte se obtienen mediante `FramePresentation.Characters`, que exige CRC, longitud y UTF-8 válidos y utiliza el bloque reconstruido. Las referencias experimentales no intervienen.

## Fuentes, captura y resultados parciales

Se conserva la observación de onda y energía en vivo y la búsqueda sobre prefijos ya existente. El laboratorio muestra símbolos únicamente cuando el receptor los ha devuelto. Sin cabecera o longitud no se inventan posiciones. Los canales se inspeccionan independientemente. La salida digital no sustituye al audio acústico del Receptor. La herramienta está oculta en el monitor del Emisor y Panel general.

El recorrido de un WAV sigue siendo inspección posterior sin sonido, no reproducción simulada ni demodulación continua. El CRC «No comprobado» permanece distinto de «Inválido». Un CRC correcto con UTF-8 inválido tampoco autoriza mostrar texto como recibido válido.

## Archivos y verificación

- Reemplazado: `Visualization/ReceiverInterpretationView.cs`. Eliminado el helper de senoides estimadas de `Visualization/ReceiverObservation.cs`, sin consumidores en la aplicación.
- Nuevos: `Visualization/DemodulationSelection.cs`, `checks/VerifyDemodulationLab.cs`.
- Adaptados: `Signals/FdmToneDetector.cs`, `Signals/BfskToneDetector.cs` (solo diagnóstico adicional), `Visualization/SignalMonitorView.cs`.

La comprobación nueva cubre 0/1/?, mínimos, silencio/ambigüedad/duración, identidad de la decisión guardada, ventanas reales, Hamming, posiciones de paridad, correspondencia inversa palabra→símbolo, borrado controlado e independencia de canales. Las verificaciones anteriores mantienen cobertura de protocolo, fuentes, captura, CRC, métricas, WAV y CSV.

Se revisó un renderizado WPF fuera de pantalla a 1366×768 con un WAV sintético. Falta comprobar clic/teclado reales, capturas de micrófono con ruido y atenuación, palabras con borrados de WAV físicos, pausa/reinicio, DPI real al 125 % y carga durante captura prolongada. No se afirma que las pruebas numéricas validen una transmisión acústica real.

## Refinamiento del laboratorio

La barra común permite elegir canal, símbolo anterior/siguiente, primer/siguiente `?` y siguiente palabra Hamming con incidencias. Los destinos proceden exclusivamente de las decisiones conservadas; sin coincidencia el acceso está deshabilitado. La selección se mantiene al cambiar de vista. Cambiar de fuente invalida la selección y los accesos.

Las tres zonas usan filas proporcionales para los gráficos reales y desplazan los rangos y diagnósticos extensos al detalle secundario. Los criterios de energía, separación y duración se muestran como SÍ, NO o No evaluado, leyendo el diagnóstico del detector. Los valores y mínimos exactos permanecen disponibles.

Los cuatro bits de una palabra se denominan **Nibble decodificado**. Su campo se calcula sobre las posiciones originales: byte de longitud, contenido UTF-8 y cuatro bytes CRC-32. Dos palabras consecutivas aportan los nibbles alto y bajo de cada byte (MSB-first). Sin longitud recuperada no se atribuyen campos posteriores ni caracteres. Las posiciones de paridad no se presentan como bits originales directos. El recorrido inferior distingue muestras, energías, bit recibido, tratamiento Hamming, campo y validación final; no representa ejecución incremental en vivo.

Se agregaron comprobaciones de navegación, límites, destinos `?`, palabras con incidencias, clasificación de campos y ausencia de resultados obsoletos. El caso B/símbolo 110 comprueba explícitamente la etiqueta Campo CRC-32 y Nibble decodificado. El renderizado fuera de pantalla comprueba tamaños de escritorio y equivalencia lógica del 125 %; no sustituye una inspección interactiva con DPI real ni pruebas acústicas.
