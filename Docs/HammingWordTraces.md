# Trazas reales de palabras Hamming

`Hamming74Codec.Decode` registra la entrada recibida y la salida que efectivamente calculó: siete símbolos, palabra resultante, cuatro bits reconstruidos, síndrome solo para palabras binarias completas, posición invertida y posiciones de borrados recuperados. La codificación, comparación de candidatas, paridades y síndrome conservan su matemática. Las trazas no intervienen en decisiones ni en contadores.

El receptor identifica el canal en sus diagnósticos. El analizador experimental conserva la misma traza del códec y asigna canal, índice y offset dentro del bloque protegido, excluyendo los 24 símbolos de cabecera. Cuando la recuperación está desactivada, registra la palabra real omitida sin síndrome, salida ni inversiones. Una cola incompleta conserva únicamente los símbolos que llegaron; no se inventan siete posiciones. No hay registros para palabras totalmente ausentes.

Las métricas conservan el alcance de la referencia experimental. El detalle se limita además al bloque determinado por la longitud recuperada para evitar incluir relleno si la referencia es más larga. Si la longitud es inválida o desconocida, solo pueden delimitarse sus dos palabras. Una longitud corrupta pero dentro del rango sigue siendo una hipótesis hasta verificar CRC; no se puede inferir la longitud original verdadera en ese caso. Una referencia demasiado corta también limita los datos disponibles en el análisis experimental.

## Uso con un WAV como `90con3.wav`

1. Importar el WAV y seleccionar en Análisis los textos que realmente se transmitieron y la modalidad Hamming utilizada.
2. Medir con recuperación desactivada. Abrir la vista interna Hamming de Análisis, elegir A o B y seleccionar una palabra en la grilla numerada. Los borrados omitidos no tienen síndrome ni palabra resultante.
3. Volver a medir el mismo audio con recuperación activada. Se reemplaza la lista entera. Elegir el mismo índice y comparar entrada, salida y posiciones recuperadas; la entrada debe seguir siendo la misma.
4. Revisar CRC y cobertura junto a las métricas. Recuperar una palabra no garantiza validar el mensaje. Tres o más borrados, o la falta de candidata única, dejan la palabra irrecuperable.

Fila superior: recibido. Fila inferior: resultante. Gris: bit recibido concluyente. Amarillo: `?` original. Rojo: inversión intentada por síndrome. Verde: posición reconstruida por borrado. `—` significa dato no disponible, no es un símbolo recibido ni un borrado detectado. «Sin cambios · síndrome cero» no significa ausencia garantizada de errores múltiples.

No se modifica el CSV. Las trazas se limpian al cambiar el audio, al iniciar otro análisis y si este falla o se cancela. Importar un WAV inválido conserva la medición y las trazas anteriores. No se agregan resultados visuales a partir de los textos de referencia.

La prueba automatizada utiliza señales controladas y una ventana WPF de comprobación de controles. Quedan pendientes inspección visual y prueba acústica con archivos reales; no se presupone ningún resultado para `90con3.wav`.

## Visualizador didáctico de Análisis

La vista Hamming utiliza un Grid acotado por el área disponible: canal/estado arriba, seis contadores horizontales y, en el centro, lista de palabras a la izquierda y transformación a la derecha. En anchos pequeños se apilan selección y detalle. No hay un ScrollViewer exterior envolviendo estos controles; la lista y el detalle tienen desplazamiento independiente si lo necesitan. Los índices y los contadores exactos permanecen como consulta secundaria.

La lista identifica número, estado y sección calculada desde la longitud leída. Al cargar otro resultado se prioriza una palabra con intervención, borrados o fallo. Al elegir manualmente otra, se conserva por canal mientras se navega por ese mismo resultado. Analizar nuevamente reemplaza las trazas, sin comparar sesiones ni inventar pasos.

El detalle conserva las siete posiciones convencionales, recibido y resultante en filas diferenciadas, y los cuatro bits de información solo si existen. P violeta identifica paridad; D azul, datos. Amarillo destaca el '?' recibido, rojo una inversión y verde un borrado recuperado. '—' indica ausencia de dato, nunca un símbolo detectado. El texto breve depende del estado y del diagnóstico de la traza. El byte/carácter asociado solo se muestra como validado si procede del bloque aceptado por CRC y UTF-8 del receptor; con CRC pendiente o inválido no se utiliza la referencia experimental para rellenarlo.

Las tarjetas resumen mantienen los contadores existentes y no se suman: mezclan cantidades de palabras y de posiciones. «Procesadas¹» corresponde exactamente al contador histórico de palabras decodificables del bloque experimental esperado. Se muestra aparte la cantidad de trazas disponibles. Con longitud no validada, el detalle puede limitarse a dos palabras aunque los contadores experimentales cubran más posiciones; no faltan registros por un error gráfico.

El selector opcional de paridad resalta P1={1,3,5,7}, P2={2,3,6,7} o P4={4,5,6,7}, con paridad par. Son propiedades estáticas del código, comprobadas contra las 16 palabras válidas. No calcula ni anima otra decodificación: en borrados, la operación real fue comparar las 16 candidatas; en palabras binarias completas, calcular síndrome e invertir su posición indicada. La leyenda evita presentar una inversión intentada como corrección demostrada de errores múltiples.

Para la validación manual, abrir el mismo WAV en Análisis con recuperación apagada y encendida. Comparar la misma palabra: el recibido debe conservar sus '?' mientras cambia la disponibilidad del resultante y de sus cuatro datos. Revisar CRC real, resaltado de paridades, navegación por teclado y tamaño de detalle a 1366×768.
