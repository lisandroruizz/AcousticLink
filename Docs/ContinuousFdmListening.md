# Escucha continua FDM

## Uso y alcance

En Receptor, elegir Hamming y recuperación de borrados antes de pulsar **Iniciar escucha continua**. Se abre una sola entrada de micrófono y permanece abierta entre transmisiones. No hay límite de 10/20/30 segundos para la sesión. **Detener escucha continua** solicita cancelación y libera el dispositivo y el worker sin drenar una cola atrasada. Conserva recepciones completadas y el segmento observado; una recepción interrumpida sigue siendo parcial, sin validar texto ni agregar una trama completada ficticia. Cerrar AcousticLink también espera la liberación del dispositivo y del worker.

La preferencia **Activar escucha continua al iniciar AcousticLink** es optativa y está desactivada por defecto. Se guarda localmente en `%LOCALAPPDATA%/AcousticLink/continuous-listening.txt`. Al iniciar la aplicación se intenta escuchar únicamente si el usuario había guardado `true`. Un error se informa en el estado de escucha; se puede reintentar con el botón. No hay servicio en segundo plano, envío de audio ni guardado automático de WAV.

La captura limitada manual y su opción de decodificación durante captura siguen siendo operaciones separadas. Mientras escucha continua utiliza el dispositivo, Capturar y Abrir WAV quedan bloqueados; el evento de captura también rechaza explícitamente una apertura simultánea. Detener la escucha permite volver a esos flujos. Cambiar de sección, transmitir o seleccionar un histórico no cierra el micrófono. La modalidad Hamming no se autodetecta ni cambia durante una sesión.

## Arquitectura y reutilización

- `AudioCaptureService` comparte inicialización, formato preferido de 48 kHz/mono, alternativa nativa, conversión de muestras, parada y liberación con la captura limitada. En modo continuo no crea un `CapturedAudio` ilimitado: publica bloques mono reales por una salida adicional.
- El callback solo convierte/encola. Una cola acotada de 16 bloques tiene un único consumidor. No espera, demodula ni dibuja en el callback. Un paquete mayor de un segundo o una cola llena termina la sesión con un error visible, en lugar de descartar muestras silenciosamente.
- `ContinuousFdmReceiver` orquesta `IncrementalFdmTextReceiver`, `FdmFrameSynchronizer.HeaderSearch` y el lector canónico `FdmTextReceiver.ReceiveSynchronized`. No introduce otro detector ni cambia umbrales, símbolos, CRC, UTF-8 o Hamming.
- El receptor incremental permite fijar la primera cabecera validada para una trama continua. Esta opción es falsa por defecto, conservando el comportamiento previo de la captura limitada. Así una cabecera posterior no invalida la trama actual como una búsqueda global ambigua.
- El worker recibe los bloques por orden y prepara estados y mapa gráfico aproximadamente cada 200 ms; una recepción completada fuerza una nueva instantánea. Solo hay un worker y una instantánea pendiente por sesión. El timer WPF existente toma la más reciente; el worker no espera al Dispatcher. Las entradas completadas se conservan en una cola independiente de hasta diez entradas, de modo que descartar una imagen gráfica intermedia no descarta una recepción. La interfaz nunca analiza toda la historia del micrófono en cada refresco.

## Búsqueda, finalización y rearme

Se procesan pasos de hasta 50 ms para que un bloque grande no oculte límites de trama. La búsqueda utiliza la cabecera real de 24 símbolos (2,4 s), con los criterios existentes por canal. Mientras no se encuentra cabecera, cada cinco segundos se descarta audio antiguo conservando 2,6 segundos de solapamiento. Una cabecera repartida entre callbacks o entre dos ventanas de búsqueda sigue disponible.

Tras sincronizar se recuperan longitud y símbolos con el lector incremental existente. A y B conservan sus resultados independientes. Que B termine antes no interrumpe A; un canal sin cabecera no recibe símbolos ni texto inventado. El relleno no se interpreta como contenido. Solo el lector canónico puede validar CRC y UTF-8 y entregar texto válido.

Se completa una recepción cuando terminan los canales sincronizados, se alcanza el plazo máximo del protocolo o se detecta otra cabecera validada. La búsqueda de la cabecera siguiente empieza después de la cabecera actual, mientras todavía puede estar recibiéndose el bloque. Si llega una transmisión después de una trama truncada, se conserva desde su cabecera y se rearma el lector sin reiniciar NAudio. También se conserva un solapamiento al finalizar normalmente para no perder una cabecera todavía incompleta. El máximo protegido es 206 símbolos = 20,6 segundos, incluida cabecera; el buffer de trabajo es de 30 segundos.

Cada resultado final usa las muestras y la sincronización ya seleccionada con `ReceiveSynchronized`. La identidad es `(SessionId, Id)` y se registra la posición absoluta `StartSample`: dos textos iguales en instantes diferentes son recepciones distintas. No se deduplica por texto. El ruido o silencio sin cabecera validada no genera entradas. La detención solicitada interrumpe el procesamiento pendiente y conserva la observación parcial sin inventar una recepción completada ni recalcular una búsqueda final sin cancelación.

## Memoria y monitor

Los buffers de trabajo del orquestador, lector y monitor están limitados a 30 segundos cada uno. Al avanzar se descarta audio antiguo y se crea el lector sobre el solapamiento retenido, sin reanalizar todos los minutos escuchados. La cola también tiene capacidad fija. A 48 kHz cada buffer float32 de 30 segundos ocupa aproximadamente 5,76 MB.

El historial conserva como máximo diez entradas y sus fragmentos reales, de hasta 30 segundos por entrada (hasta 57,6 MB de muestras a 48 kHz por colección). El modelo y la lista WPF comparten arrays para la misma recepción. Si WPF permanece ocupado mientras se completan más de diez transmisiones, puede retener diez entradas antiguas mientras el worker conserva las diez nuevas: hasta veinte fragmentos distintos temporalmente, además de una inspección seleccionada que haya sido expulsada de la lista. La cola de resultados pendientes comparte las entradas recientes del modelo. Este máximo sigue acotado; no se conserva la sesión acústica entera ni se escribe audio a disco por defecto.

El monitor reutiliza `IncrementalSignalMonitor`: onda mín/máx y energías reales en ventanas de 20 ms con paso de 10 ms. Se alimenta solo con el sufijo nuevo; cuando rota el buffer, reconstruye únicamente el segmento retenido. La energía observada no es una decisión de bits. Miniatura y monitor general reciben el mismo mapa. El eje del monitor es **tiempo local del segmento gráfico**, no el tiempo absoluto de toda la sesión; el estado muestra por separado la duración total recibida. La onda de la vista Mensajes también recibe el resumen real.

Si se pausa la inspección, la escucha y el procesamiento continúan. Al rotar el buffer se mantiene el fragmento visual pausado con sus trazas, evitando mezclar posiciones locales diferentes. Volver al vivo vuelve a las muestras nuevas. El historial permite inspeccionar una recepción sin que otras cambien la selección. Al detener se conserva el segmento observado y el resultado reciente; una selección histórica hecha por el usuario se mantiene.

Mientras se inspecciona un histórico se deshabilitan Guardar WAV, Recibir mensajes, Medir y Exportar CSV para no operar silenciosamente sobre el audio manual anterior. El histórico es de consulta en esta etapa. Después de detener, **Volver a la escucha en vivo** sale del histórico y devuelve el flujo al audio manual previamente seleccionado; también puede capturarse o importarse otro audio. Los formatos WAV/CSV y los cálculos experimentales no cambian.

## Concurrencia y errores

La sesión de escucha tiene su propia cola, cancelación y tarea de parada compartida. Una llamada de cierre espera incluso si ya había una parada en curso. Primero se marca detención, se cancela el token enlazado a captura/worker y se completa la entrada de la cola. Se espera la liberación de NAudio y del worker; después se liberan los bloques pendientes y se informa cuántas muestras se descartaron por la parada solicitada. No se espera un análisis gráfico ni se drena una cola atrasada para detener. Los callbacks nunca esperan al Dispatcher. Las publicaciones comprueban identidad de sesión, detención y cierre para rechazar resultados antiguos. Una captura nueva invalida el análisis visual previo; una importación fallida conserva la selección anterior como antes.

Un fallo de inicialización o desconexión se informa y desactiva la escucha; no se reinicia automáticamente en un bucle. Se puede reintentar. Las cancelaciones esperadas no se presentan como errores; las excepciones inesperadas del worker se registran y muestran.

## Verificaciones automáticas

`checks/VerifyContinuousFdm.cs` verifica fragmentos variables, inicio arbitrario, silencio largo y ruido sin falsas recepciones, tres transmisiones consecutivas con texto repetido, ambos modos Hamming, CRC incorrecto independiente, un canal sin cabecera, borrado de longitud con recuperación apagada/encendida, truncación seguida de otra transmisión, dos tramas máximas multibyte a 44.100 Hz, límites de memoria/historial, finalización idempotente y cancelación. Para las recepciones válidas compara inicio, estados, ventanas, energías, símbolos, longitud, reconstrucción Hamming, CRC y texto con el receptor manual sobre las mismas muestras.

`checks/VerifyContinuousWorkspace.cs` ejecuta WPF con un dispositivo simulado e introduce bloques al worker real: tres recepciones sin reiniciarlo, histórico conservado, navegación entre secciones, protección contra captura simultánea, parada y cierre. No abre un micrófono físico. Se conservan las verificaciones anteriores de captura manual, cierre, importación transaccional, receptor incremental, monitor, Hamming, CRC, métricas y CSV.

La auditoría de progreso, los nuevos diagnósticos, las evidencias y las pruebas de regresión adicionales están en [ContinuousFdmListeningAudit.md](ContinuousFdmListeningAudit.md).

## Limitaciones y prueba física pendiente

- No resuelve colisiones arbitrarias ni cambios de Hamming entre transmisiones en una misma sesión.
- El protocolo no tiene delimitador de fin acústico. Una trama truncada puede terminar como CRC inválido por silencio recibido o esperar a una nueva cabecera/plazo máximo; no puede diagnosticarse siempre como truncación física inequívoca. Una cabecera válida accidental dentro del contenido también es una limitación del encuadre existente.
- El monitor publica a 5 Hz y reinicia su eje local al rotar el segmento. No es una grabadora indefinida ni ofrece exportación directa del historial.
- La preferencia de inicio y los fallos reales de permisos/dispositivo requieren ensayo en el equipo de uso. Las pruebas sintéticas no acreditan transmisión acústica.

Procedimiento físico sugerido:

1. En dos equipos o con parlante/micrófono separados, fijar distancia, orientación, volumen y ruido. Configurar el mismo Hamming en emisor y receptor; recuperación apagada por defecto.
2. Iniciar escucha una sola vez. Confirmar indicador de micrófono activo, onda/energía real y búsqueda sin recepciones falsas durante 60 segundos de silencio/ambiente.
3. Transmitir Hola/Mundo. Esperar resultado A/B y CRC; dejar pasar diez segundos. Transmitir Prueba/Datos y luego Hola/Mundo nuevamente. Deben aparecer tres identidades sin tocar Capturar/Detener/Recibir ni reabrir el dispositivo.
4. Cambiar de pestaña y seleccionar la primera recepción mientras llega otra. Debe conservarse la inspección y crecer el contador. Volver al vivo y verificar correspondencia de muestras, energías y símbolos.
5. Repetir con Hamming activo, ambos perfiles, silencios cortos y una transmisión interrumpida seguida de una válida. Ensayar canales degradados independientemente y recuperación explícita de borrados.
6. Detener durante cabecera/datos, reiniciar y cerrar durante recepción. Confirmar que cesa el indicador del micrófono de Windows, sin tareas retenidas. Probar desconexión y reintento.
7. Activar expresamente la preferencia, cerrar y abrir: debe indicar el micrófono activo sin entrar en Receptor. Desactivarla y comprobar que el siguiente inicio no abre el micrófono.
8. Finalmente detener la escucha y comprobar los flujos anteriores: captura manual, guardar/importar WAV, recibir, medir y exportar CSV. Comparar con el mismo audio y configuración cuando se disponga de una grabación física reproducible.
