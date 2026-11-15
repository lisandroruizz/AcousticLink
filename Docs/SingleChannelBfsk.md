# Comunicación BFSK de un canal

## Alcance y señal

La modalidad **BFSK — Un canal** transmite un mensaje UTF-8 por el canal A: 1200 Hz representa `0` y 1900 Hz representa `1`. Cada símbolo dura 0,10 s y la salida digital es mono a 48 kHz. `BfskTextModem` usa el mismo generador de senoide, fase continua y envolvente que los canales FDM; no llama al mezclador y no genera portadoras de 3000/3700 Hz. La duración digital es exactamente el número de símbolos de la trama multiplicado por 0,10 s. No hay segundo mensaje ni relleno.

FDM conserva dos tramas simultáneas, la banda A y la banda B, y el perfil de amplitud actual. La selección BFSK no cambia las frecuencias, los umbrales, las duraciones ni el formato de FDM.

## Trama y protección

La trama BFSK reutiliza la trama por canal de FDM:

`preámbulo (16 bits) | marcador (8 bits) | longitud (8 bits) | contenido UTF-8 | CRC-32 (32 bits)`

`MessageCodec` define UTF-8 estricto, longitud en **bytes**, orden de bits y CRC calculado sobre longitud más contenido. Se aceptan entre 1 y 8 bytes UTF-8 por mensaje en esta modalidad acústica. Opcionalmente, `Hamming74Codec` codifica el bloque completo de longitud, contenido y CRC en palabras (7,4); el preámbulo y el marcador no están protegidos. El emisor y el receptor deben seleccionar la misma opción Hamming. La recuperación opcional de borrados `?` también reutiliza el códec existente. Una inversión por síndrome o una reconstrucción de borrados no demuestra integridad: el texto solo se publica si el CRC y UTF-8 son válidos.

## Emisión y recepción

En **Emisor → Mensajes de texto**, elegir BFSK, escribir el mensaje en el canal A y usar **Transmitir** o **Preparar vista previa**. El Panel general comparte la misma selección. **Guardar última señal generada como WAV** guarda las muestras digitales que se prepararon; el WAV no contiene un campo de modalidad. La vista previa y el monitor de señal generada muestran muestras digitales, no mediciones acústicas.

En **Receptor**, elegir BFSK antes de capturar o analizar. El selector es independiente del emisor y queda bloqueado durante una captura o escucha activa. Para un WAV importado, se debe conocer y elegir manualmente la modalidad y la opción Hamming; la aplicación no la infiere del archivo. **Recibir mensajes** analiza una grabación finalizada. **Decodificar durante la captura** procesa solo bloques nuevos en segundo plano. **Escucha continua** conserva el micrófono abierto, rearma el lector por trama y registra recepciones separadas por identidad y posición, aun si el texto se repite. El historial identifica BFSK y permite inspeccionar el segmento observado sin presentarlo como captura nueva.

`BfskFrameSynchronizer` busca una cabecera exacta y concluyente en pasos gruesos y la refina temporalmente. `BfskSequenceDecoder` usa ventanas centrales del símbolo y `BfskToneDetector` compara las energías de 1200/1900 Hz con los umbrales existentes. `BfskTextReceiver` interpreta longitud y bloque mediante la misma lógica de canal de FDM. Los resultados parciales mantienen bits `?`, estados de truncación o error y trazas Hamming; `Valid` exige CRC correcto y UTF-8 estricto. Si hay varias cabeceras separadas en un análisis manual, se informa sincronización ambigua; la escucha continua las procesa como recepciones consecutivas.

Los callbacks de audio solo encolan muestras. Los lectores incremental y continuo trabajan en segundo plano con cancelación y buffers de hasta 30 s. La sesión mantiene el modo elegido al comenzar. La interfaz oculta el canal B en BFSK y conserva forma de onda, mapa de energía 1200/1900 Hz, ventanas y decisiones de símbolos, trama, Hamming, CRC y texto validado. Las energías observadas no se etiquetan como bits confirmados ni como mediciones BER. El módulo de análisis experimental y su CSV siguen siendo **FDM**; el CSV agrega `modalidad=FDM` al final para conservar la posición de las columnas anteriores. No se agregan métricas comparativas en esta tarea.

## Verificación y límites

`checks/VerifyBfskTextModem.cs` cubre ida y vuelta de `Hola`, `Mundo` y UTF-8 multibyte, Hamming, CRC, truncación, silencio, ruido, ausencia de portadoras B, WAV importado, 44,1 kHz remuestreado, procesamiento incremental, escucha continua, texto repetido y una trama truncada seguida de una válida. `checks/VerifyBfskWorkspace.cs` comprueba los selectores y el contenido WPF por modalidad sin hardware. La compilación y los checks FDM pertinentes se ejecutaron en esta tarea y deben repetirse tras cambios posteriores.

**Pendiente:** validar transmisión acústica física entre dos equipos y calibrar el detector con esas mediciones. Los checks sintéticos, WAV y WPF no prueban un enlace acústico real. La comparación experimental BFSK/FDM y las métricas medidas por modalidad pertenecen a una funcionalidad posterior.
