# Recuperación experimental de borrados Hamming (7,4)

El emisor y las tramas no cambian. En el receptor, la recuperación de borrados se activa explícitamente y está desactivada por defecto. Hay opciones independientes en «Mensajes FDM — Receptor» y en la sección de mediciones experimentales; seleccionar la misma configuración para comparar sus resultados.

Cada palabra binaria completa conserva la corrección por síndrome anterior. Una palabra con uno o dos `?` se compara contra las 16 palabras generadas por el codificador existente. Solo se reconstruye si exactamente una coincide con todos los bits conocidos. No se aplica el síndrome a estas palabras ni se invierte ningún bit conocido. Con tres o más borrados, ninguna candidata o varias candidatas, se mantienen cuatro `?` en los datos originales.

Se supone que los bits conocidos son correctos. Borrados combinados con bits erróneos pueden impedir la recuperación o producir una candidata incorrecta. El receptor exige longitud válida, bloque completo, CRC-32 válido y UTF-8 estricto antes de mostrar el texto; el CRC tampoco garantiza detectar absolutamente todo patrón de alteraciones.

La longitud protegida se reconstruye primero con dos palabras Hamming (14 símbolos). Su valor se limita a 1–8 bytes antes de leer el resto. La lectura completa incluye esas mismas palabras una sola vez en los contadores. El relleno posterior al bloque queda excluido.

## Métricas y CSV

Los bits previos siguen siendo las decisiones originales de Goertzel. Los `?` reducen cobertura pero no se cuentan como errores de BER ni como inversiones. La cobertura posterior compara los cuatro bits originales de cada palabra reconstruida; una palabra irrecuperable conserva cuatro `?`. Las referencias experimentales solo sirven para comparar y nunca llegan al receptor.

Las columnas anteriores del CSV mantienen su orden. Se agregan al final:

| Columna | Significado |
| --- | --- |
| `recuperacion_borrados` | `si`/`no`: opción efectiva del análisis, siempre `no` sin Hamming. |
| `borrados_recuperados` | Posiciones `?` reconstruidas mediante coincidencia única. No son inversiones. |
| `palabras_recuperadas_por_borrados` | Palabras con uno o dos borrados reconstruidas. |
| `palabras_irrecuperables` | Palabras completas con borrados que permanecen sin reconstruir. Las truncadas conservan su columna separada. |

Sin Hamming, las tres cantidades quedan vacías. `hamming_palabras_con_interrogantes` sigue contando palabras originalmente recibidas con `?`, aun si después se recuperan. Las palabras recuperadas por borrados no se clasifican como síndrome cero ni como síndrome no nulo: no se calculó síndrome sobre ellas. Las estadísticas experimentales pueden examinar el bloque de referencia aun si el receptor se detuvo por longitud inválida; las del receptor indican si alcanzó solo la longitud o el bloque completo.

## Comparación con un mismo WAV

1. Cargar el WAV original y seleccionar Hamming si así se transmitió.
2. Analizar con «Recuperar borrados (?) con Hamming» desactivado. Guardar el CSV si se miden referencias.
3. Activar la recuperación y analizar nuevamente, sin grabar ni modificar/importar otro audio. Guardar un segundo CSV.
4. Comparar CRC, texto, cobertura posterior y los contadores nuevos. La BER y cobertura previas deben conservarse exactamente. La cobertura completa no implica por sí sola CRC válido.

Una cabecera no sincronizada, una longitud irrecuperable, truncaciones y más de dos borrados en una palabra siguen impidiendo aceptar el mensaje. Las verificaciones sintéticas no sustituyen el análisis de los WAV físicos ni miden BER acústica.
