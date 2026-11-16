# Recorrido de la comunicación

## Objetivo y ubicación

**Análisis → Recorrido** permite estudiar una emisión preparada y una recepción seleccionada mediante etapas navegables. Cada etapa combina una explicación breve con datos concretos del módem. Los botones permiten avanzar, retroceder, seleccionar una etapa, reiniciar y reproducirlas automáticamente. La reproducción didáctica avanza cada 1,5 s; este tiempo no representa la duración digital de la señal ni un tiempo medido de captura.

El recorrido del emisor se inicia con **Preparar emisor actual** y utiliza los textos, modalidad, Hamming y perfil de amplitud actuales del Emisor. Genera el buffer con `BfskTextModem` o `FdmBitstreamGenerator`, sin reproducir audio. El recorrido del receptor se inicia con **Inspeccionar recepción seleccionada** tras analizar una captura o WAV, durante recepción incremental cuando ya hay un resultado, o tras seleccionar una entrada del historial de escucha continua. La fuente y el resultado se comprueban nuevamente antes de publicar una proyección preparada en segundo plano.

No existe un recorrido combinado automático. Ni el texto coincidente ni la misma modalidad prueban que dos resultados pertenezcan al mismo ensayo. Una comparación experimental conserva su propia referencia declarada y sus criterios de alineación; el recorrido nunca la usa para completar símbolos o datos recibidos.

## Emisor: etapas y procedencia

1. **Texto:** mensajes independientes de A y, en FDM, B. BFSK usa solo A.
2. **UTF-8 y bits útiles:** `MessageCodec` genera bytes con UTF-8 estricto. La proyección agrupa bytes por `Rune`, que puede ocupar varios bytes. Cada byte se muestra con ocho bits MSB primero.
3. **Trama y CRC-32:** `FdmTextFrameBuilder` entrega el bloque original. La trama real es preámbulo de 16 bits, marcador de 8 bits, longitud de 8 bits en bytes, contenido y CRC-32 de 32 bits. `MessageCodec` calcula CRC sobre longitud más contenido y lo serializa en orden big endian. El CRC detecta alteraciones, sin corregirlas.
4. **Hamming:** cuando se activa, `Hamming74Codec` codifica longitud, contenido y CRC en palabras de 4 a 7 bits. La convención es P1 P2 D1 P4 D2 D3 D4. Preámbulo y marcador quedan fuera. Sin Hamming, la etapa explica la omisión.
5. **Bits transmitidos:** se presentan `FrameBits` y `TransmittedBits` del constructor. FDM completa el canal más corto con ceros posteriores a su trama; ese relleno está fuera del CRC y Hamming. BFSK no añade relleno.
6. **Símbolos y frecuencias:** los bits de `TransmittedBits` se relacionan con las frecuencias definidas por `CarrierEnergyMap.Frequencies` y la duración de símbolo de `FdmBitstreamGenerator`: A 0→1200 Hz y 1→1900 Hz; B 0→3000 Hz y 1→3700 Hz. Seleccionar una fila resalta su intervalo en la forma de onda.
7. **Señal digital:** `WaveformView` presenta una reducción del buffer producido por el transmisor, con su cantidad de muestras, tasa y duración digital. Es salida digital preparada, no audio observado en un micrófono.
8. **Resumen:** bytes, bits útiles, longitud de trama, relleno, símbolos y Hamming por canal.

Los elementos largos se paginan de 16 en 16. El inspector muestra el valor completo de la fila elegida. Los resúmenes detallados de energía y las contribuciones separadas A/B siguen disponibles en **Emisor → Señal generada**; Recorrido muestra la onda conjunta del buffer digital.

## Receptor: etapas y procedencia

1. **Audio disponible:** muestras de la captura, WAV o histórico seleccionado; forma de onda reducida por `WaveformView`. Un WAV importado no declara BFSK/FDM: el usuario elige la modalidad conocida antes de analizar.
2. **Búsqueda y sincronización:** estado, motivo y posición que informa `FdmSynchronizationResult`. La actividad exploratoria del monitor de portadoras no se presenta como cabecera validada.
3. **Símbolos estimados:** decisiones `0`, `1` o `?` y energías de `BfskSymbolResult.Detection`, con intervalos de muestras del detector. El resaltado de onda corresponde al intervalo del símbolo observado. Sin bloque disponible, no se inventan decisiones.
4. **Longitud y trama:** cabecera válida, longitud conocida y `Block.Bits` observados. Los campos reconstruidos se muestran solo si el receptor dispone del bloque completo; pueden seguir siendo inválidos por CRC. Una captura truncada mantiene el motivo del receptor y no atribuye campos ausentes.
5. **Hamming:** `HammingDiagnostics.Traces` informa palabras, inversiones, borrados recuperados o irrecuperables. Una corrección no garantiza integridad.
6. **CRC y UTF-8:** se muestran CRC recibido y calculado solo cuando el bloque reconstruido está completo y es binario. El valor calculado usa `Crc32.Compute`; la decisión de validez y la publicación del texto proceden exclusivamente de `FdmTextReceiver` o `BfskTextReceiver`. Con CRC incorrecto, UTF-8 inválido, bits inconclusos o sincronización ausente, la vista muestra el rechazo o la falta de datos, nunca el texto de una referencia.

Las energías de los símbolos son observaciones en las ventanas utilizadas por el detector. El mapa exploratorio de energía de **Receptor → Señal** tiene otro propósito y no determina por sí mismo bits, CRC o validez. El historial de escucha conserva identidad de sesión, número de recepción e inicio en muestras; al cambiar o expulsar la entrada seleccionada se invalida el recorrido.

## Arquitectura y límites

`TransmissionWalkthrough` crea etapas e ítems inmutables a partir de tramas y resultados existentes. `TransmissionWalkthroughView` controla navegación, paginación e inspector WPF. `MainWindow.Walkthrough.cs` captura la selección, ejecuta generación o reducción de onda fuera del Dispatcher y comprueba la identidad antes de publicar. La vista guarda únicamente cubos de onda reducidos, no buffers completos. `WaveformView.SetSelection` resalta el símbolo elegido. No se modificaron algoritmos de modulación, sincronización, demodulación, Hamming, CRC ni UTF-8.

La herramienta no muestra una traza de cada operación interna del CRC ni una medida acústica de potencia. La onda FDM es la mezcla digital; las contribuciones A/B se consultan en el monitor existente. Una emisión preparada para recorrer no equivale a una transmisión reproducida. La captura de micrófono puede contener sonido ambiental y no prueba una comunicación válida por sí sola. No se calcula BER en esta vista.

## Verificación

`checks/VerifyTransmissionWalkthrough.cs` comprueba ASCII, Unicode multibyte, mensaje vacío, BFSK/FDM, Hamming activado/desactivado, campos y CRC de la trama, relleno FDM, correspondencia bit/símbolo/frecuencia, recepción válida y rechazo CRC. Aprobaron además los checks de texto BFSK, presentación de recepción, escucha continua, comparación, Hamming, recepción FDM, explorador de trama, visualizaciones y compatibilidad WPF. La solución compiló con 0 errores y 0 advertencias con salida aislada y `NuGetAudit=false`; la salida predeterminada estaba bloqueada por una instancia abierta y la red impidió la auditoría NuGet. `git diff --check` aprobó. Las revisiones técnicas y visuales motivaron correcciones de identidad, acceso a WPF, legibilidad y uso del espacio. La compilación y checks sintéticos no sustituyen una transmisión física entre dos equipos. No se pudo inspeccionar visualmente Recorrido a 1180 × 740, 1366 × 768 ni DPI 125 %.
