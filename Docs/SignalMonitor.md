# Monitor real de señales y portadoras FDM

El Panel general contiene una miniatura funcional y el monitor completo. El Emisor conserva su vista de señal generada, las contribuciones A/B y la preparación sin reproducción. Todas las vistas utilizan muestras publicadas por captura o salida; no animan señales ficticias.

## Captura en curso

Un temporizador WPF de 50 ms solicita novedades. Una sola tarea de observación puede estar activa: si sigue trabajando, no se encola otra. CopySamples lee únicamente el sufijo publicado por CapturedAudio. El callback de NAudio no dibuja ni calcula energía.

IncrementalSignalMonitor conserva un historial float acotado a la capacidad original (máximo 30 s), un resumen mín/máx incremental y las ventanas de energía ya terminadas. Se utiliza un buffer incremental completo, no un anillo que descartaría los intervalos que el usuario quiere inspeccionar. Las muestras publicadas no se sobrescriben. La memoria de observación es independiente del audio usado por el receptor.

Se calculan las cuatro portadoras con el Goertzel compartido, ventanas rectangulares de 20 ms y paso 10 ms. Solo se procesan ventanas completas nuevas, sin recalcular el prefijo ni utilizar muestras futuras. La cola menor de 20 ms espera datos nuevos; el análisis final puede incluirla según el tratamiento habitual del monitor estático.

## Ventana móvil e inspección

El selector permite los últimos 1, 2, 3 o 5 segundos y Desde el inicio. El extremo derecho coincide con el último número de muestra publicado, no con tiempo inventado. La escala vertical es original ±1 o pico real con margen del 10 %, únicamente visual.

Navegar manualmente suspende el seguimiento visual, pero no el micrófono ni la reproducción. Volver al vivo / pausar reanuda o pausa el seguimiento. Se conservan zoom alrededor del mouse (Ctrl+rueda), selección izquierda, pan central o Mayús+arrastre, Mayús+rueda, botones, intervalos escritos y navegador completo. Los campos Vista reflejan los límites efectivamente dibujados. El cursor consulta las ventanas calculadas más cercanas.

## Reproducción

AudioPlaybackService publica una instantánea de progreso consultando WasapiPlayer.GetPosition cada 40 ms. Convierte bytes del formato de salida a muestras de la señal fuente; no usa un cronómetro independiente para avanzar. Si el reloj falla, publica explícitamente la cantidad leída por la ruta de reproducción: puede adelantar por buffering y se identifica en el monitor. Esta alternativa nunca representa una medición acústica.

El monitor solo incorpora el prefijo indicado por ese contador. Comparte los mismos resúmenes entre la miniatura y el monitor completo, y deja de seguir al terminar o cancelar. El buffer digital completo se conserva para inspección posterior. Las contribuciones A/B se obtienen con los parámetros y el generador existentes y se habilitan solo si A+B coincide exactamente, muestra por muestra, con el buffer compuesto. Durante reproducción se limitan también al prefijo publicado. No se separan ondas A/B de un micrófono o WAV.

## Mapa y miniatura

El mapa representa únicamente energía en 1200/1900 Hz (A azul) y 3000/3700 Hz (B naranja), no un espectrograma completo ni decisiones de bits. La escala fija es −100 a 0 dB digitales, 10 log10(P/1); cero es −∞. El color limita ese rango pero el inspector conserva la energía real. No son dB SPL, umbrales de recepción ni normalización del audio.

La miniatura recibe el mismo resumen visible ya calculado por el monitor completo mediante FrameDisplayed. No vuelve a ejecutar Goertzel ni copia el audio. Identifica micrófono, salida digital o WAV, estado y rango temporal. Al detener conserva la vista final. Un WAV es estático: importarlo no inicia reproducción ni captura. Las importaciones fallidas conservan la selección previa y sus resultados.

## Comprobaciones y límites

VerifyLiveSignalMonitor comprueba incrementos, capacidad, ventanas idénticas al análisis completo, snapshots estables, silencio, cancelación y reinicio a 44.100/48.000 Hz. VerifySignalMonitor comprueba seguimiento/pausa, navegación, fuentes, vista previa y miniatura, incluyendo una ruta de salida con contador simulado (no hardware).

Se comprobó además una reproducción real FDM en la aplicación: la miniatura avanzó hasta el intervalo 5,002–8,002 s. En una captura real a 48 kHz se observó el monitor en 6,57–9,57 s, con energía incremental, y la detención a 10 s conservó el audio completo. Esto verifica visualización y rutas de audio, no la recuperación acústica de mensajes.

Pendiente: ensayos prolongados a 30 s, carga elevada, otros dispositivos/tasas, desconexión y cancelación de salida, pausa/reanudación visual con mouse, Windows al 125 % y validación acústica de CRC/BER. La cadencia objetivo es 20 refrescos/s; se omiten solicitudes si una tarea sigue ocupada. No se añadió reproducción de WAV ni decodificación de bits en streaming.

## Miniatura con dos escalas

La tarjeta Señal actual dibuja un detalle de aproximadamente 10 ms de muestras originales de A+B, conectado sin suavizado, con límites verticales derivados del pico local y margen de 10 %. No utiliza las contribuciones individuales seleccionadas en el monitor grande. La ventana sigue el prefijo publicado por captura o la posición de reproducción existente; no introduce otro temporizador. Para WAV/preparación estáticos se muestra inicialmente el comienzo (o el cursor de inspección disponible).

La zona global reutiliza Overview, el resumen mín/máx completo ya preparado. Durante una emisión usa el mapa completo generado en cuanto está disponible; mientras se prepara ese mapa solo muestra el prefijo real disponible. Su cursor indica el progreso sobre la duración total. Al finalizar conserva el buffer global y la posición publicada, sin seguir avanzando. La miniatura mantiene origen digital/micrófono/WAV y estado explícitos.

## Cómo se forma FDM

El monitor ofrece dos herramientas: Señal y energía conserva la vista habitual; Cómo se forma FDM utiliza una ventana inicial de 10 ms y dibuja simultáneamente A, B y A+B sobre una escala común. Lee los mismos arreglos digitales verificados, con sus fases, ganancias y rampas; no sintetiza otra onda ni separa audio acústico. Para intervalos largos usa reducción mín/máx en segundo plano.

Los bits, frecuencias nominales y número de símbolo proceden de FdmTransmissionInfo, metadatos de solo lectura de las secuencias exactas entregadas al generador. Cuando existe una trama de texto, se conservan los FdmTextFrames construidos con los mismos parámetros. Los campos preámbulo/marcador/longitud/UTF-8/CRC/relleno se calculan desde sus longitudes reales: con Hamming solo longitud/datos/CRC se expanden 4→7. Cabecera y relleno quedan fuera. Las secuencias o tonos de prueba no se presentan como tramas UTF-8.

Las dos bandas de protocolo muestran la trama completa conocida por el Emisor, proporcional a símbolos, con cursor y marco del intervalo ampliado. Un clic explora ese instante; durante reproducción no permite acceder a muestras todavía no publicadas. El ToolTip ofrece los rangos exactos de símbolos y segundos de cada campo. En las ondas se marcan los límites de símbolos visibles. El contexto de bit/frecuencia corresponde a la muestra visible más cercana al cursor si este quedó fuera del zoom.

Las cuatro tarjetas A0/A1/B0/B1 consultan la misma ventana real de Goertzel que el cursor del monitor. El brillo representa energía, no confirma recepción ni un bit válido. Captura/WAV muestran su onda real y mapa; la herramienta didáctica indica que A/B originales no están disponibles.

VerifyFdmFormation verifica segmentación, símbolos, frecuencias, UTF-8 de 1–8 bytes, Hamming y relleno. VerifySignalMonitor comprueba que los metadatos llegan a la vista previa y que la herramienta comparte zoom/cursor. Se revisó un renderizado fuera de pantalla con datos sintéticos; quedan pendientes la interacción real, reproducción/captura prolongada y DPI al 125 % de esta nueva herramienta.

## Monitor del Receptor: real e interpretado

Receptor → Monitor de recepción comparte los mapas y prefijos publicados de captura/salida, manteniendo sus propios controles de inspección. La fuente digital de salida se identifica como digital; no se la presenta como micrófono. Se conservan Señal y energía y se agrega Cómo la interpreta. La miniatura incorpora actividad A0/A1/B0/B1 a partir de las mismas ventanas de energía.

ReceiverObservation llama al FdmToneDetector existente, con parámetros predeterminados intactos, sobre una ventana real de hasta 60 ms. Con menos de 40 ms conserva el diagnóstico de fragmento insuficiente. A tiempo cero de un archivo finalizado puede usar el primer tramo disponible; el soporte exacto se muestra. Las decisiones son hipótesis de ventana deslizante, no símbolos sincronizados ni bits CRC válidos. Las energías del mapa continúan usando ventanas de 20 ms: tienen otro soporte temporal. No se calcula una probabilidad de confianza; los diagnósticos conservan motivo y diferencia de energía.

La vista didáctica dibuja un detalle real de 10 ms y senoides hipotéticas de frecuencia nominal y amplitud raíz(2E). La fase cero es elegida para la ilustración: NO se estima la fase acústica ni se separan los canales originales. Si un canal es ?, no se inventa su onda ni la suma completa. Esta representación nunca alimenta la recepción, Hamming, CRC o métricas.

La orquestación ejecuta FdmTextReceiver sin cambios sobre prefijos inmutables, aproximadamente cada segundo desde que existen 2,4 s, con una sola tarea activa y el último prefijo pendiente, cancelable al cambiar audio/opciones/cerrar. Las opciones Hamming/borrados proceden de Recepción. El resultado de monitoreo es independiente del resultado final de los botones Recibir/Medir y no se exporta al CSV. Repetir la búsqueda puede consumir CPU en grabaciones largas; el audio y los gráficos no esperan su finalización.

Antes de localizar una cabecera no se atribuyen campos temporales. Tras validarla se ubican preámbulo, marcador y longitud; datos/CRC solo con longitud leída en rango. Esos límites son estimaciones del receptor y los campos futuros se atenúan. Relleno solo se marca como esperado cuando ambos CRC son válidos y sus longitudes difieren; no se afirma que esos bits de relleno hayan sido verificados. La sincronización ambigua oculta la segmentación. No se usan textos de referencia ni los metadatos de transmisión para ubicar campos. Por ello no puede mostrarse un preámbulo validado mientras aún está llegando su primera parte.

Recorrer análisis (sin audio) desplaza el cursor por muestras de una grabación finalizada, con pausa e inspección manual. Es un recorrido visual explícito, no reproducción WAV ni recepción en vivo ficticia. Los resultados de trama corresponden al análisis ya disponible; no se animan supuestos pasos internos del algoritmo.

Las nuevas comprobaciones cubren igualdad con el detector, hipótesis dudosas, ventanas y fuentes, campos parciales, longitudes desconocidas, Hamming, independencia y sincronización ambigua. Se revisó un renderizado WPF con un WAV sintético. Pendiente para esta nueva vista: parlante/micrófono, seguimiento y pausa reales, carga a 30 s, desconexiones y DPI 125 %. No se afirma validación acústica del mensaje.

**Actualización del laboratorio del Receptor:** las senoides interpretativas descritas en la sección histórica anterior fueron eliminadas de la interfaz. La implementación vigente, las trazas de criterio y la reconstrucción por símbolo se documentan en [DemodulationLab.md](DemodulationLab.md).
