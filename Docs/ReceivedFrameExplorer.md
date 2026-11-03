# Explorador de trama recibida

El Receptor conserva sus tarjetas A/B, la forma de onda y los indicadores de procesamiento. El panel compartido tiene tres niveles: navegación de secciones, tira de palabras/bytes y detalle de un único grupo seleccionado. Las tarjetas de sección son de igual ancho y están identificadas como navegación no proporcional. Cabecera es gris azulado; Longitud violeta; Datos azul; CRC verde. Su borde no representa calidad del bit. Hamming es una operación sobre longitud + datos + CRC, nunca un quinto campo transmitido.

La navegación principal del explorador utiliza Trama / Texto / UTF-8 / Hamming (tres pestañas; «Texto / UTF-8» es una sola vista). Su altura se adapta a la ventana dentro de límites de lectura. En Trama y Hamming, selección y detalle son columnas hermanas con desplazamiento propio solo si lo necesitan, no una pila de expanders. En anchos pequeños se apilan dentro de la misma área. El mapa de un grupo no crea otro ScrollViewer interno. Los índices y el diagnóstico de palabra siguen como desplegables secundarios.

La primera selección de una recepción nueva prioriza una traza con intervención/fallo. Una selección posterior se conserva por canal, sección y modalidad al cambiar de vista. Hamming filtra exclusivamente las trazas reales, sin crear entradas para palabras supuestas o ausentes; si solo existen las dos de longitud, ese es todo el detalle disponible. La información breve de un bit reemplaza el contexto del panel; no agrega otro bloque creciente. Los índices completos permanecen en «Índices y correspondencias».

## Rangos y datos disponibles

La cabecera toma su tamaño del preámbulo y marcador centralizados. Consta de 16 + 8 símbolos sin Hamming. El bloque posterior contiene 8 bits de longitud, 8 × N bits de texto y 32 bits de CRC. Con Hamming cada tamaño se multiplica por 7/4; cada byte ocupa dos palabras. No se añade relleno en el explorador.

Las posiciones se muestran como intervalos `[inicio, fin)`, base cero. Las posiciones de trama incluyen cabecera; las del bloque original/transmitido la excluyen. El mapa mantiene esos offsets y los números reales de palabra, sin reiniciarlos en cada sección.

Si el receptor leyó una longitud de 1–8 bytes, los límites se calculan desde ella. Solo CRC válido confirma su integridad. Una longitud corrupta pero dentro del rango puede producir límites incorrectos: la interfaz indica que todavía no están confirmados. Con longitud inválida o no concluyente no se delimitan Datos ni CRC. El Receptor no utiliza referencias experimentales para completar la estructura; estas siguen exclusivamente en Análisis.

La cabecera utiliza las decisiones individuales guardadas en la sincronización, si existen; nunca se dibuja el patrón esperado como una traza recibida. Los demás símbolos proceden de `Block.Bits`; la vista original reconstruida usa `HammingDiagnostics.DataBits`. Si la decodificación se detuvo en longitud, puede no existir información posterior. Los datos ausentes se indican como no disponibles o `—`, nunca como símbolos `?` inventados. No se generan palabras para llenar espacios.

La tira muestra las palabras delimitadas por la longitud leída y su disponibilidad/estado real, sin afirmar recepción cuando faltan datos. Solo se despliega el detalle de una palabra de siete posiciones (P1 P2 D1 P4 D2 D3 D4). En la vista original reconstruida, la tira agrupa directamente bytes de ocho bits; cada byte puede abarcar dos palabras, identificando D1–D4 y sus correspondencias de código. Sin Hamming se agrupa en bytes sin inventar palabras. El resumen expandible de trazas permite consultar entrada, salida, cuatro bits, síndrome aplicable, inversión y posiciones recuperadas, sin duplicar el visualizador completo de Análisis.

El inspector diferencia índice de bloque original, índice codificado y posición absoluta de trama (base cero) de posiciones dentro de palabra/byte (base uno, MSB primero). En un bit de paridad no se inventa un bit original. En vista original se selecciona D1–D4; su posición 3/5/6/7 en el código se etiqueta como correspondencia. El rojo indica inversión intentada; el verde, reconstrucción de borrado; '?' original y valores ausentes no se convierten en información recibida. No hay referencias experimentales en este panel.

## Del texto a los bits

Solo aparece con estado de mensaje válido, CRC válido y texto UTF-8 aceptado. Los bytes se extraen directamente de los bits originales reconstruidos (o del bloque recibido sin Hamming), empezando después de longitud y terminando antes del CRC. No se calculan desde el texto esperado ni se recodifica el texto final para simular bytes recibidos.

El texto validado se recorre por escalares Unicode para agrupar los bytes reales que pertenecen a cada carácter. Las tarjetas horizontales viven exclusivamente en Texto / UTF-8; las tarjetas A/B ofrecen acceso directo a esa vista. Mensaje y cantidad de bytes encabezan el panel. Cada carácter muestra cantidad de bytes, binario y hexadecimal por byte. La secuencia completa del contenido aparece una sola vez, agrupada en bytes. No es segmentación por grafemas: una letra y un acento combinante pueden aparecer en tarjetas separadas. Elegir un byte muestra su detalle a la derecha y resalta ocho bits sin cambiar de pestaña. «Localizar este byte en Trama» permite navegar explícitamente. Elegir el carácter abre su primer byte; los restantes bytes multibyte son seleccionables individualmente. Elegir un bit de contenido con asociación validada resalta su tarjeta. Si no hay asociación cierta (por ejemplo un bit de paridad, longitud o CRC), el inspector informa No disponible. No se usan referencias ni se interviene en la recepción.

## Comprobación manual

1. Recibir una grabación Hola/Mundo con y sin Hamming. Elegir cada sección y comprobar rangos, palabras y etiqueta de modalidad; la cabecera nunca se expande con Hamming.
2. Abrir «Del texto a los bits», seleccionar un carácter y comprobar el resaltado. Repetir con ñ y un carácter de cuatro bytes UTF-8.
3. Repetir con CRC inválido, longitud no concluyente y grabaciones truncadas. No debe aparecer contenido UTF-8 validado ni divisiones supuestamente confirmadas.
4. Cambiar de audio y confirmar que se vacía el explorador. Intentar un WAV inválido y verificar conservación. Revisar legibilidad y desplazamiento interno a 1366 × 768.

Las pruebas numéricas y WPF automatizadas no equivalen a una inspección visual ni a validación acústica real.
