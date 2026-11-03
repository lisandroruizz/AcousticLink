# Monitor real de señales y portadoras FDM

El **Panel general → Monitor de señales** muestra la forma de onda y un mapa limitado a las cuatro portadoras de AcousticLink. El Emisor tiene una vista **Señal generada** con el mismo control. No es un espectrograma completo ni un receptor adicional.

## Fuentes y trazabilidad

- **Audio seleccionado**: instantánea de una captura de micrófono finalizada o de las muestras originales de un WAV importado. Conserva su tasa real. No se procesa el espectro durante la captura.
- **Señal digital generada**: el mismo arreglo preparado por el generador FDM y entregado a reproducción, sin regenerarlo. Se conserva también si la reproducción falla: representa audio preparado, no prueba de emisión acústica ni de recepción. Su descripción guarda el modo y el perfil utilizados al generarlo.
- Una nueva captura o una importación correcta invalida el monitor del audio anterior. Una importación fallida lo conserva. El último buffer generado es una fuente independiente.

No se deduce el perfil del emisor a partir de un WAV. El monitor no usa referencias experimentales ni altera resultados, muestras, WAV o CSV.

## Cálculo de energía

`CarrierEnergyMap` reutiliza `GoertzelEnergy.Estimate`, sin modificarlo y sin invocar criterios de decisión del detector. Calcula potencia en 1200, 1900, 3000 y 3700 Hz sobre ventanas rectangulares de aproximadamente **20 ms**, separadas por **10 ms**, usando números de muestras redondeados según la tasa real. A 44.100 y 48.000 Hz ambas duraciones son exactas. La última ventana se ancla al final si es necesario. Un audio más corto utiliza las muestras que realmente existen, e informa ese soporte en el cursor.

Se resta la media únicamente dentro del cálculo de energía; no se modifica el audio. La estimación es `2 × |proyección|² / N²`: un seno ideal de amplitud A produce aproximadamente A²/2. Hay fuga y efectos de borde, especialmente cerca de transiciones; la presencia de energía no certifica un bit válido.

El color usa una escala fija **−100 a 0 dB digitales**, `10 log10(P/1)`. No hay normalización por grabación ni por canal. Cero corresponde a −∞. El piso y techo solo limitan el color: el inspector conserva los valores numéricos reales, incluso por debajo del piso. No son dB SPL ni una calibración del micrófono.

Cada ventana ocupa visualmente la región más cercana a su centro. Si varias ventanas caen en un píxel, se representa el máximo por portadora para conservar actividad breve. El cursor informa las energías de la ventana más cercana y sus límites reales; no calcula una medición instantánea por muestra. Azul identifica A y naranja identifica B, sin clasificar recepción correcta o incorrecta.

## Exploración

- Clic en cualquiera de los gráficos: cursor común y cuatro energías.
- Arrastre: selección temporal resaltada en ambos gráficos.
- **Ampliar intervalo**: ajusta ambos ejes horizontales al intervalo; también se pueden escribir los tiempos en segundos.
- **＋ / −**: zoom alrededor del cursor; **Ver completa**: restaura la duración total.
- Flechas izquierda/derecha con el gráfico enfocado: mover el cursor.
- Escala vertical original ±1 o ajuste al pico global con margen del 10 %. El silencio usa ±1. El ajuste solo cambia el gráfico.

La forma de onda completa conserva mínimos y máximos de hasta 1600 grupos, con el mismo criterio que `WaveformView`. Para intervalos de hasta 1600 muestras se dibujan los puntos originales y líneas que los conectan; no se crean muestras intermedias. El mapa mantiene su resolución de análisis de 20/10 ms aunque se amplíe hasta observar las oscilaciones temporales.

## Rendimiento y cancelación

La instantánea del audio finalizado, la reducción inicial y Goertzel se calculan fuera del hilo WPF. La captura y su callback no cambian. Se conserva un mapa por fuente (seleccionada y generada), compartido entre vistas. Mover el cursor consulta ventanas almacenadas; no vuelve a analizar audio. La geometría del mapa se conserva hasta cambiar fuente, zoom o tamaño. Un zoom prepara solo la reducción temporal visible en una tarea cancelable.

**Cancelar monitor** cancela únicamente la observación; no detiene captura, reproducción o recepción. **Analizar / reintentar** utiliza la misma fuente. El cierre cancela las tareas mediante la vida de la ventana. Los resultados obsoletos se descartan al cambiar la fuente. El monitor admite el límite actual de 30 segundos.

## Verificación

`checks/VerifySignalMonitor.cs` comprueba portadoras simultáneas, perfiles y tasas, silencio, bajo nivel, límites temporales, picos, muestras intactas, cancelación/reintento, origen de archivos y buffers, selección/zoom, invalidación y conservación de las decisiones del módem. Puede recibir una ruta PNG para renderizar un ensayo sintético WPF fuera de pantalla.

Pendiente de comprobación manual: captura física, interacción de arrastre y teclado, reproducción real, legibilidad y controles a 1366×768 / 1920×1080 y escalado Windows 125 %. Las comprobaciones sintéticas y el renderizado fuera de pantalla no validan una transmisión acústica real.
