# Comparación experimental BFSK/FDM

## Alcance y uso

La pestaña **Comparación** del área **Análisis** conserva ensayos en memoria durante la sesión. **Guardar historial** escribe JSON y **Abrir historial** lo vuelve a cargar; **Exportar CSV** escribe una fila por canal/trama y otra total por ensayo. El CSV FDM anterior de **Medir grabación** conserva sus columnas y comportamiento. El formato nuevo tiene su propio encabezado, con `tipo_fila=canal|total`, identificador, par, fecha UTC, fuente, parámetros y motivos de ausencia de métricas.

Para un par generado, introducir textos A y B (1–8 bytes UTF-8 cada uno), Hamming, perfil FDM, SNR opcional, semilla y repeticiones. **Ejecutar** procesa dos tramas BFSK en secuencia y una transmisión FDM simultánea con los mismos textos. Cada repetición crea un par identificado; la semilla avanza tres posiciones por par. Dentro de cada par, BFSK A usa la semilla base, BFSK B la base + 1 y FDM la base + 2. El perfil FDM conserva la relación entre ganancias A/B definida por el emisor. La señal limpia completa de cada modalidad se escala a **RMS digital 0,05** antes de añadir ruido blanco gaussiano. En BFSK cada trama se escala individualmente; en FDM se escala la mezcla. Así se iguala potencia digital media total, **no** potencia por canal ni presión acústica. El ruido usa la misma SNR nominal relativa al RMS limpio, pero realizaciones independientes con semilla registrada. Sin SNR se procesa una copia limpia. No se cambian tonos, duración de símbolo, trama, Hamming, CRC ni los receptores reales.

Para WAV o micrófono, seleccionar primero una grabación finalizada en **Receptor**. En Comparación elegir **Audio seleccionado** y la modalidad conocida. El WAV no la indica. Marcar **Conozco los textos transmitidos** solo si hay un registro externo fiable de los textos exactos y del modo Hamming. En caso contrario se muestran estados de recepción, CRC, RMS y espectro, pero BER, bits emitidos y rendimiento efectivo quedan no disponibles. Una referencia declarada habilita una BER sobre símbolos alineados; sigue siendo una declaración del usuario, no una prueba del contenido emitido. El mismo WAV no se cuenta varias veces como repeticiones independientes; para una campaña física, capturar un WAV nuevo por intento e incorporarlo al historial.

## Métricas y denominadores

| Magnitud | Cálculo y condición |
|---|---|
| Duración de transmisión | Solo señal generada: muestras emitidas / 48 000 Hz. BFSK agregado suma las dos tramas; FDM usa la duración de la mezcla simultánea. En WAV/micrófono solo se conoce duración del audio analizado, que puede contener silencio antes y después. |
| Tiempo de procesamiento | Cronómetro de demodulación y evaluación; no incluye reproducción, propagación, captura, generación ni cálculo de espectro. Nunca es latencia acústica. |
| Velocidad nominal | 1 / 0,10 s = 10 símbolos binarios/s por canal; 20 bit/s agregados en FDM. No descuenta protocolo ni errores. |
| Bits útiles | 8 × bytes UTF-8 por texto. La longitud es en bytes, no caracteres. |
| Cabecera/control/redundancia/relleno | Longitudes de la trama generada: preámbulo y marcador; longitud y CRC; bits extra de Hamming(7,4); relleno FDM del canal más corto. En audio externo no se llaman bits efectivamente transmitidos, aunque se conozca la referencia. |
| Rendimiento efectivo | Bits útiles cuyo texto recibido coincide con la referencia **y** tiene CRC/UTF-8 válidos, divididos por duración de emisión digital. Para BFSK por canal se usa la duración de su trama; agregado, la suma. Para FDM por canal y agregado se usa la duración simultánea. Sin duración de emisión o referencia, no disponible. |
| BER previa | Bits incorrectos / bits comparados, sobre bloque codificado antes de Hamming, excluyendo cabecera y relleno. Solo se calcula con referencia y cabecera sincronizada; `?` y bits faltantes reducen cobertura, no cuentan como errores. BER cero con cobertura parcial no demuestra recepción íntegra. |
| BER posterior | Con Hamming, comparación de bits originales reconstruidos tras el códec. Se registra solo cuando hay bits comparables. La BER agregada de ensayos suma errores y bits comparados; no promedia porcentajes de distinta cobertura. |
| Tramas | Esperadas: referencias declaradas; detectadas: cabecera validada; válidas: CRC y UTF-8 válidos; rechazadas: CRC explícitamente falso; truncadas: cabecera detectada y estado truncado. La coincidencia con referencia se distingue de la validez de una trama. |
| Hamming | Síndromes no nulos, inversiones intentadas y borrados recuperados disponibles desde el decodificador. Una inversión por síndrome no garantiza corrección; CRC decide integridad. |
| Latencia acústica | No disponible. Faltan marcas temporales comunes de inicio de emisión y entrega en recepción. |

Los valores de canal y total figuran en filas separadas del CSV. `bits_transmitidos` y descomposición de overhead quedan vacíos para audio externo. Un CRC incorrecto no revela una BER. Una cabecera ausente no es un rechazo CRC. Los mensajes recibidos con CRC válido pero distintos de la referencia cuentan como tramas válidas, pero no como bits útiles correctos para rendimiento.

## Comparabilidad y espectro

La interfaz marca como directamente comparable solo un par generado en la misma repetición, con textos, volumen útil, Hamming, tasa, RMS objetivo y SNR nominal iguales. Los perfiles de potencia **por canal** son diferentes entre BFSK y FDM, y la estructura temporal y espectral también. Con ruido, las realizaciones independientes pueden producir resultados distintos entre repeticiones. La sección permite seleccionar otros ensayos para inspección descriptiva, pero no los declara equivalentes automáticamente. Cada resumen indica `n` y las cantidades observadas; un `n` pequeño no justifica una conclusión general.

El espectro usa cuatro ventanas Hann de 4096 muestras repartidas en el audio procesado; la resolución es frecuencia de muestreo / 4096 Hz. En el par BFSK se concatena el audio de las dos tramas secuenciales para esta vista. El eje vertical expresa dB **relativos al pico espectral** del mismo ensayo; no representa SPL, SNR medida ni ancho de banda ocupado. La separación de portadoras 1200/1900 y 3000/3700 Hz tampoco se presenta como ancho de banda ocupado. La ventana, los símbolos de 0,10 s y la energía de transición influyen en el espectro mostrado.

## Límites pendientes

Los pares generados y los WAV digitales prueban procesamiento de software, no un enlace entre parlante y micrófono. Para comparar físicamente hay que fijar equipos, distancia, volumen, geometría, opción Hamming, fuente WAV/captura y referencias por intento, y conservar **cada** captura. La aplicación registra resultados de micrófono, pero aún no mide latencia extremo a extremo ni potencia acústica calibrada. El historial JSON es un archivo de resultados, no de muestras WAV: conservar los WAV originales por separado y registrar sus rutas. No hay conclusiones físicas sobre superioridad de ninguna modalidad.
