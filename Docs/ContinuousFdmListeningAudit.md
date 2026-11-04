# Auditoría de progreso y detención de escucha continua

## Alcance y línea base

Se conservó la escucha continua implementada: una captura NAudio, `ContinuousFdmReceiver` sobre el receptor incremental existente, rearme temporal, canales independientes, buffers de 30 segundos, cola de 16 bloques, historial de diez recepciones y preferencia de inicio opcional. No se cambiaron los detectores, umbrales, sincronización, codificación, CRC, BER ni formatos.

La compilación inicial pasó sin errores/advertencias. La primera ejecución de las verificaciones encontró un fallo reproducible en `VerifyLiveFdmWorkspace`: el monitor contenía audio de escucha continua en una prueba que simulaba captura manual. La causa era la preferencia personal de autoarranque: las pruebas abrían `MainWindow` y ejecutaban su `Loaded` real. Se aisló la preferencia en las seis ventanas de prueba, poniendo su CheckBox en falso antes de `Show`, sin escribir ni cambiar el archivo de preferencias del usuario. Con ese aislamiento las 41 verificaciones previas pasaron antes de modificar la lógica productiva.

## Qué se pudo reproducir

1. **Acoplamiento del worker al Dispatcher.** Una prueba WPF introdujo ocho bloques de 4.800 muestras, manteniendo el Dispatcher sin procesar mensajes. Antes de la corrección solo avanzaron **4.800/38.400 muestras**: el worker esperaba `Dispatcher.InvokeAsync` para seguir consumiendo. Con WPF ocupado, esa espera podía acumular audio hasta llenar la cola. Después de quitar esa espera, la misma prueba consume **38.400/38.400** sin bombear WPF.
2. **Contadores incorrectamente equivalentes.** La presentación anterior usaba `ContinuousFdmReceiver.ReceivedSamples` tanto para recibido como para procesado. Ambos medían avance del worker publicado en pantalla, no entrada desde NAudio. Si se frenaba el worker o la publicación, ambos podían parecer detenidos aunque siguieran llegando paquetes.
3. **Cancelación demasiado tardía.** La parada esperaba al worker antes de cancelar su token. Podía drenar trabajo atrasado y una finalización canónica sin cancelación antes de liberar la sesión. Se corrigió el orden de cancelación y espera.
4. **Etiquetas de captura manual reutilizadas.** El modo continuo no crea `CapturedAudio` ilimitado; por eso indicadores basados solo en `capture.Recording` mostraban “Sin captura activa” o “Sin fuente” mientras el dispositivo estaba abierto. Ahora el contexto continuo y el segmento retenido tienen etiquetas propias.

## Qué NO se pudo atribuir al video original

No se dispone aquí de las muestras del ensayo fallido ni de telemetría anterior suficiente para identificar el motivo exacto de los **5,58 segundos**. No se afirma que el acoplamiento reproducido explique por sí solo ese incidente. Tampoco se puede concluir si Hola/Mundo no llegaron con energía suficiente, si sus tonos fueron inconclusos, si falló la cabecera o si el procesamiento quedó atrasado.

Los tiempos locales de 2,74/4,40 segundos pueden ser correctos: el monitor conserva un segmento y rearma su eje local al rotar el buffer. No son necesariamente la duración global de escucha. El contador global suma muestras nuevas; el buffer local conserva solo el tramo necesario. No se corrigió esta diferencia mediante falsificación de ejes ni reinicios silenciosos.

## Correcciones y diagnósticos

- El callback convierte/encola sin demodular ni dibujar. NAudio cuenta los frames mono realmente leídos y conserva la marca monotónica del último paquete, incluso si después falla la entrega.
- El coordinador distingue muestras observadas en el callback, aceptadas en cola y consumidas por el worker. Solo aumenta consumo después de que `Append` procesa el bloque. Muestra la diferencia como tiempo pendiente usando la tasa real.
- El worker conserva una única instantánea gráfica pendiente, reemplazable. El timer existente de captura lee esa instantánea y los resultados completados. No hay una cola de trabajos de Dispatcher esperando a ser dibujados. No se generan imágenes ni mensajes ficticios para mantener movimiento.
- La transferencia de las diez recepciones completadas usa una sección crítica breve, solo para sus metadatos. Así recortar el historial y drenarlo desde WPF no pueden intercalarse eliminando accidentalmente la recepción más reciente. No hay cálculo de audio, llamadas WPF ni esperas dentro de esa sección.
- El diagnóstico secundario incluye estado real de dispositivo/tarea, muestras NAudio, callback, aceptadas, consumidas, cola/capacidad, antigüedad de entrada/progreso/publicación, etapa del worker, razón del sincronizador, inicios evaluados, buffer retenido, cancelación, descarte por parada y último error.
- Las marcas de antigüedad usan `Stopwatch`, no el reloj de fecha. No hay log por muestra ni grabación permanente. Avisos de más de dos segundos sin paquetes/progreso son advertencias de observación; no cambian umbrales del módem ni reinician la captura.
- Una cola llena cancela la sesión y conserva error explícito. La diferencia callback/aceptadas permite ver el bloque rechazado; nunca se continúa decodificando como si se hubieran recibido sus muestras.
- La finalización no reemplaza un error de cola, dispositivo o worker por un mensaje de éxito. El estado conserva el problema original y habilita reintento después de liberar la sesión.
- La parada cancela antes de esperar. El inicio recibe ese token, de modo que el cierre también puede cancelar la espera de inicialización. Una tarea compartida coordina solicitudes simultáneas de parada/cierre. Se conservan recepciones terminadas; el trabajo pendiente se interrumpe y contabiliza, sin validar mensajes parciales.

## Cómo leer el diagnóstico ante un nuevo incidente

| Observación | Interpretación sustentada | Siguiente comprobación |
|---|---|---|
| NAudio/callback crecen, consumo no; cola/pendiente crecen | El dispositivo entrega audio y el consumidor no lo alcanza | Etapa del worker, antigüedad de progreso, error y saturación |
| Consumo crece, pendiente crece | El worker funciona pero está retrasado | Comparar tasas durante varios refrescos; no confundir retraso con congelamiento |
| NAudio/callback no crecen y envejece el último paquete | No llegan nuevos paquetes a la aplicación | Estado real del dispositivo/tarea y error; un micrófono abierto no garantiza paquetes |
| Captura y consumo crecen, envejece publicación WPF | Procesamiento activo con presentación detenida/ocupada | Reanudar interacción WPF y revisar selección histórica/seguimiento pausado |
| Tarea de captura/worker finalizada inesperadamente | La sesión terminó; no es una búsqueda activa | Se muestra error, se cierra la sesión y se habilita reintento |
| Todo avanza, sin cabecera | Búsqueda real sin sincronización validada | Guardar segmento y comparar manualmente energías/decisiones/cabecera |

Los nombres “recibido”, “consumido” y “buffer local” no son intercambiables. Los contadores son acumulados de sesión; el mapa y sus trazas usan posiciones locales del segmento. Pausar seguimiento visual o seleccionar un histórico no detiene el micrófono ni el worker.

## Estados y controles

- **Iniciar escucha continua:** micrófono cerrado. **Iniciando micrófono…:** inicialización pendiente. **Detener escucha continua:** sesión activa. **Deteniendo escucha…:** cancelación/limpieza en curso; evita solicitudes duplicadas. **Error:** problema visible con posibilidad de reintentar tras liberar la sesión.
- El encabezado muestra escucha/búsqueda, cabecera/recepción o mensaje según resultados reales. No se mantiene artificialmente un estado “verificando CRC”: la comprobación existente es síncrona y se publica su resultado cuando existe.
- A y B muestran por separado cabecera, longitud, símbolos, palabras Hamming y CRC. Un resultado inconcluso no se convierte en error confirmado.
- La preferencia de iniciar al abrir es independiente del botón operativo. Cambiarla no inicia ni detiene inmediatamente la sesión actual.
- Al detener queda identificado el segmento observado retenido, con micrófono apagado. Sus muestras no se convierten silenciosamente en el audio manual seleccionado. Se puede guardar explícitamente o usar “Volver a la escucha en vivo” para salir de esa inspección y recuperar el flujo manual.

## Comparar con el receptor manual usando muestras reales

En Opciones → Diagnóstico de escucha continua, **Guardar segmento observado como WAV** guarda las muestras reales del segmento mostrado en el monitor del Receptor, hasta 30 segundos. Si se está inspeccionando un histórico, guarda ese segmento histórico. No guarda todos los minutos anteriores ni promete incluir muestras todavía pendientes de consumo. No modifica las muestras ni el formato WAV existente.

Procedimiento: conservar el diagnóstico copiado, guardar el segmento que contiene la transmisión, detener escucha, importar ese mismo WAV y ejecutar Recibir mensajes con iguales opciones Hamming/borrados. Comparar tiempos locales, cabecera, símbolos, longitud, Hamming y CRC. Si el segmento no incluye la cabecera completa, no permite evaluar toda la trama; repetir el ensayo conservando el histórico apropiado o una captura manual limitada de referencia.

## Evidencia automática

`VerifyContinuousProgress.cs` conserva el caso que fallaba antes de corregir el worker. Además procesa cinco minutos acelerados de silencio/ruido, sin bombear el Dispatcher: 14.438.400 muestras contando el prefijo de prueba, cero recepciones falsas, buffer final de 153.600 muestras y capacidad fija de 1.440.000 a 48 kHz. En una ejecución observada duró 4,19 segundos de reloj y el delta de memoria administrada retenida tras GC fue aproximadamente 5,6 MiB. No es una medición de latencia física ni de CPU aislada; el test impone límites de retención y crecimiento, no un tiempo exacto dependiente del equipo.

También verifica tres cabeceras parciales fallidas antes de dos transmisiones válidas repetidas, progreso con UI sin publicar, cancelación durante datos sin bombear Dispatcher, cola saturada con consumidor detenido, conteo explícito de rechazo y cancelación de inicio sin abrir un dispositivo. La regresión WPF continua agrega desconexión simulada, error visible y reintento; mantiene histórico, pestañas, tres transmisiones, conflicto manual, parada compartida, reinicio y cierre activo.

El check de progreso también completa once transmisiones adicionales mientras WPF no publica, luego comprueba que llegan exactamente las diez últimas al historial, incluida la más reciente y sin IDs duplicados. El recorte se basa en identidad temporal de recepción, nunca en texto.

Las 41 verificaciones anteriores conservan sincronización/fronteras, dos tasas de muestreo, cabeceras divididas, ambos modos Hamming, independencia A/B, CRC inválido seguido de rearme, truncaciones, WAV, CSV, métricas y captura manual/incremental limitada. Las ventanas de prueba están aisladas de la preferencia personal para no abrir hardware inadvertidamente.

## Comprobación WPF con dispositivo real y pendientes

Se abrió AcousticLink con la preferencia personal de autoarranque ya activada, sin modificarla. La entrada predeterminada informó **48.000 Hz, mono, IEEE float32**; no se identificó el modelo físico del micrófono. Se observaron mini onda y monitor reales, navegación Panel general → Receptor, contadores por encima de 5,58 segundos y una primera sesión de aproximadamente **68,45 segundos / 3.285.600 muestras**. Al consultar diagnóstico se observó cola **0/16**, pendiente **0,000 s**, progreso reciente y sin errores. Detener cambió a dispositivo apagado/tarea finalizada; se reinició, se observó nuevo conteo desde cero (4,53 s en una captura de pantalla) y se cerró durante la segunda escucha. La ventana desapareció y el proceso terminó.

Esto comprueba captura real del ambiente, UI, parada/reinicio/cierre y autoarranque en ese equipo. **No se reprodujo una transmisión FDM por parlante**, no se validó comunicación acústica Hola/Mundo, desconexión física ni permisos bloqueados. Las desconexiones de regresión son simuladas.

Para el ensayo físico final: fijar hardware/distancia/volumen, Hamming apagado en ambos extremos como en el incidente, copiar el diagnóstico antes/durante/después, enviar Hola/Mundo dos veces y conservar el WAV del tramo relevante. Si reaparece una pausa, comparar los tres contadores y las tres antigüedades antes de detener. Repetir desconexión física, reintento y escucha prolongada de varios minutos. No modificar umbrales ni declarar fallo del detector hasta comparar las mismas muestras con recepción manual.

## Archivos de esta auditoría

Se modificaron implementaciones existentes, incluidas las que ya estaban sin seguimiento de Git; no se reemplazó la recepción continua:

- `AcousticLink.App/Audio/AudioCaptureService.cs`: contadores NAudio, marcas monotónicas y cancelación enlazada al inicio.
- `AcousticLink.App/MainWindow.ContinuousListening.cs`: publicación desacoplada, contadores de etapas, parada/cierre, estados, conservación explícita de segmento y guardado diagnóstico solicitado.
- `AcousticLink.App/Protocol/ContinuousFdmReceiver.cs`: contador acumulado de inicios de cabecera evaluados, de solo lectura.
- `AcousticLink.App/MainWindow.xaml`, `MainWindow.xaml.cs` y `MainWindow.Signals.cs`: estado operativo, diagnóstico secundario y separación entre fuente manual, escucha e inspección retenida.
- `checks/VerifyContinuousWorkspace.cs`: uso del timer de instantáneas y regresión de desconexión simulada/reintento.
- `checks/VerifyCompactLayout.cs`, `VerifyLiveFdmWorkspace.cs`, `VerifyManualCaptureWorkflow.cs`, `VerifySignalMonitor.cs` y `VerifyWpfDashboard.cs`: aislamiento de autoarranque, sin cambiar la preferencia guardada.
- Nuevo `checks/VerifyContinuousProgress.cs`: reproducción del acoplamiento y cobertura de progreso, carga, retención y cancelación.
- `Docs/ContinuousFdmListening.md` y este nuevo informe: arquitectura actualizada, evidencias y procedimiento de diagnóstico.

La revisión conserva las modificaciones previas de `IncrementalFdmTextReceiver` y los checks de transmisiones consecutivas. No cambia de rama ni crea commits.

## Resultado de cierre

Compilación sin errores ni advertencias. Las **42 verificaciones** (41 previas más el check nuevo de progreso) terminaron aprobadas en una ejecución secuencial. Se repitieron los checks afectados por los últimos ajustes de liberación de recursos y conservación de errores. `git diff --check` limpio. La validación WPF/micrófono se limita a la observación del ambiente descrita arriba; queda pendiente la transmisión acústica física y la comparación del WAV del incidente original.
