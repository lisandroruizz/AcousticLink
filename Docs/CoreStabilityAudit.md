# Auditoría funcional y estabilización — Fase 1

## Línea base y alcance

Se comenzó en `fix/core-stability-validation`, con el árbol de trabajo limpio. Antes de editar código se ejecutaron `dotnet build` y los 38 checks existentes: compilación sin advertencias ni errores, todos aprobados. Se inspeccionaron captura/reproducción NAudio, selección de audio, recepción manual e incremental, sincronizador/decodificador, mensajes, métricas/CSV, orquestación asíncrona y documentación. No se modificó la interfaz ni se añadieron controles, dependencias o modalidades.

Fuentes documentales: los Markdown de Docs y el texto XML del preproyecto DOCX, leído sin modificar el documento académico.

## Hallazgos clasificados y correcciones

### Error reproducible: muestras no finitas

`CapturedAudio.Append` aceptaba NaN/infinito: podían contaminar muestras y RMS, y producir un WAV que el propio importador rechaza. Ahora valida el paquete completo bajo el bloqueo existente, antes de publicar cualquier muestra. El error se propaga al manejo ya existente del callback/captura. No hay normalización ni sustitución de valores. Paquetes válidos mantienen exactamente su conversión a mono.

La prueba nueva comprueba NaN y ambos infinitos, ausencia de publicación parcial, métricas intactas y reutilización del buffer tras el rechazo. Es una inyección de fallo, no una perturbación acústica ni una medición BER.

### Error temporal reproducible: ventanas de medición

El analizador experimental llamaba `Decode` con `inicio + i * duración`. Eso vuelve a redondear el origen por símbolo, mientras el receptor calcula cada límite desde un único origen absoluto. En tasas admitidas distintas de las habituales puede variar una muestra: por ejemplo, a 48.025 Hz, origen 0, símbolo base cero 1, límite central final 8645 frente a 8644; a 44.101 Hz también aparecen diferencias de límites completos.

Se cambió solo esa llamada a `DecodeMessageSymbol`, ya utilizada por el receptor incremental. No se cambiaron Goertzel, umbrales ni fórmulas/denominadores BER y cobertura. En casos límite podría cambiar legítimamente una decisión de medición al usar ahora las mismas muestras que el receptor. La prueba compara símbolos/ventanas/energías y medición/receptor a 44.101, 44.125 y 48.025 Hz. Los casos previos a 44.100/48.000 Hz siguen cubiertos por la suite completa.

### Riesgo funcional justificado: excepción durante parada

`StopCapture_Click` no tenía catch/finally: una excepción de `StopAsync` escaparía del evento async void y dejaría `captureBusy` activo. Se extrajo una tarea privada acotada, llamada por el mismo evento, que registra y comunica excepciones y restaura estados en finally. Una cancelación se presenta como esperada. `StartCapture_Click` también restaura su estado mediante finally y, si comenzó el cierre durante la inicialización, no publica una sesión nueva después del await.

El check WPF de captura manual inyecta una tarea de parada fallida, verifica mensaje comprensible, `captureBusy=false` y posibilidad de volver a iniciar. No simula una desconexión física. `AudioCaptureService` conserva su serialización y liberación de recursos; no se introdujo otro servicio ni un timeout arbitrario del controlador.

## Flujos revisados y resultados conservados

| Área | Comprobación y alcance |
|---|---|
| Captura manual | Muestras y monitor avanzan sin receptor de mensajes automático; Dispatcher responde; se detiene, exporta WAV y permite recepción explícita. |
| Incremental | Worker único, copia de sufijos, buffer acotado, símbolos completos y opciones por sesión. Cabeceras, longitud y resultados aparecen antes del final en pruebas por fragmentos. |
| Coherencia | Resultado final canónico manual/incremental equivalente en inicio, ventanas, símbolos, longitud, Hamming, CRC y texto. `ObservedResult` retiene diagnósticos parciales, sin cambiar el resultado canónico. |
| A/B | Independencia ante cabecera inválida, CRC incorrecto, borrados y truncaciones; sin asignar datos posteriores a una longitud irrecuperable. |
| Hamming/CRC | Cabecera sin Hamming, bloque protegido completo, relleno fuera del bloque; inversiones distintas de borrados. Texto solo con CRC y UTF-8 válidos. |
| Emisor | Frecuencias 1200/1900 y 3000/3700 Hz; 0,10 s; salida 48 kHz; perfiles 0,075/0,075 y 0,10/0,05; suma máxima teórica 0,15, fases y rampas. Reutilización sin cambios. |
| Reproducción | Inicialización/espera fuera de WPF, recursos por llamada y liberación al cancelar. Los handlers existentes restauran controles. Posición WASAPI con alternativa explícita de muestras entregadas; no equivale a medición acústica. |
| WAV/fuentes | Mono float32, validación completa antes de reemplazar audio, máximo 30 s, sin remuestreo/normalización. Importación fallida conserva selección/resultados. Cambio válido invalida recepción/medición/CSV. |
| Métricas | BER solo sobre comparados, indefinida si no hay comparados; cobertura separada. Sin alineación validada no hay BER/cobertura inventadas. Referencias solo experimentales. |
| CSV | Sin cambios de columnas/formato/semántica. Se exporta la medición capturada por la acción, con origen propio; invalidación impide presentar una medición anterior como actual. |
| Concurrencia | Manual e incremental no se lanzan juntos; identidad de sesión, cancelación y cierre guardan publicaciones. Inspeccionar WAV o pausar seguimiento no inicia ni cancela recepción. |

## Riesgos y limitaciones no convertidos en nuevas funcionalidades

- Dispositivos de entrada y salida predeterminados al abrir cada sesión; no existe selector manual ni migración implementada a otro dispositivo durante una operación. Cambios de predeterminado/desconexión requieren ensayo real. Las excepciones del backend se comunican; no se garantiza recuperación transparente.
- Un controlador nativo que se bloquee indefinidamente dentro de inicialización/liberación no queda resuelto por tener await: no se añadieron cancelaciones forzadas ni temporizadores que abandonen recursos. Debe verificarse con el hardware utilizado.
- Hay tareas gráficas auxiliares cancelables que no se esperan todas al cerrar; poseen guardas de cierre/fuente. No se encontró una publicación tardía reproducible en los checks; no se introdujo una arquitectura global de seguimiento de tareas.
- La sesión incremental es una transmisión y hasta 30 segundos. Una cabecera adicional separada puede hacer ambigua la captura, igual que el análisis manual; no es un receptor permanente multitrama.
- La longitud se protege según el modo seleccionado; Hamming no garantiza corrección múltiple y recuperar borrados supone correctos los bits conocidos. CRC no constituye garantía absoluta para cualquier patrón.
- Problemas de tipografía, recortes o presentación ajenos a la estabilidad quedan como visuales; no se rediseñaron pantallas. Selección manual de dispositivos, nuevos modos experimentales o instrumentación adicional son mejoras opcionales con aprobación de alcance.

## Diferencias con el preproyecto: decisiones pendientes

| Documento académico | Software confirmado | Propuesta para revisión del usuario |
|---|---|---|
| Hasta 32 bytes, primeras pruebas 1–16 | FDM acústico 1–8 bytes UTF-8; MessageCodec genérico admite 1–255 | Actualizar el alcance experimental a 8 bytes o aprobar una etapa futura. No ampliar aquí. Con 32 bytes y Hamming una trama ocuparía 54,2 s, superior a la captura actual. |
| Frecuencias iniciales modificables | Cuatro frecuencias fijas; perfiles de amplitud explícitos | Describir parámetros elegidos/validados, no una configuración dinámica existente. |
| Comparación un canal activo/dos simultáneos | BFSK técnico individual y FDM de dos textos obligatorios; no hay un modo de texto FDM que silencie un canal | Aprobar posteriormente cómo hacer esa comparación. Un texto de ceros no apaga un canal BFSK. |
| Selección de entrada/salida | Dispositivos predeterminados | Documentar selección mediante Windows y estado actual; selector en aplicación queda pendiente. |
| ScottPlot.WPF y vista Configuración | Controles WPF propios y navegación de cuatro secciones | Ajustar la descripción; no añadir una biblioteca para coincidir con el documento antiguo. |
| Registro de ruido/distancia, éxito y tiempos | BER/cobertura/Hamming/CRC/CSV, origen y perfil declarado; no medición calibrada automática de SNR/distancia | Registrar condiciones físicas manualmente y evitar afirmar mediciones no implementadas. |

No se editó el DOCX ni se cambió el alcance comprometido del TFI sin revisión.

## Evidencia y plan físico

**Automática:** línea base de 38 checks; check nuevo `VerifyCoreStability.cs` y ampliación de `VerifyManualCaptureWorkflow.cs` para los defectos/riesgos concretos. Las pruebas no usan referencias para reconstruir el mensaje.

**WPF ejecutado:** ventanas fuera de pantalla, Dispatcher real, buffers sintéticos y fallo de parada inyectado; captura manual/incremental, cambio de fuente, históricos, cancelación y cierre. Esto no es una prueba acústica.

**Física ejecutada en esta pasada:** WPF fuera de pantalla, 1366×768, modo manual, aproximadamente 8 segundos, sin emisión ni archivo de audio. Entrada predeterminada Console y Multimedia: «Micrófono de los auriculares con micrófono (DualSense Wireless Controller)». Salida predeterminada de ambos roles: «Altavoces (USB Audio CODEC )», únicamente enumerada; no se probó reproducción acústica. Captura a 48 kHz: 394.560 muestras; monitor 390.240 al tomar la lectura; cero procesadas por el receptor de mensajes. Heartbeat Dispatcher: 255 pulsos, demora máxima observada 236 ms incluyendo inicialización. Parada Manual, IsRecording=false y ningún error. El desfase entre conteos refleja publicaciones asíncronas, no una prueba de pérdida de muestras. No se guardó audio del micrófono.

Esto comprueba inicio, recepción de buffers, monitor y parada con ese dispositivo, no recepción física de Hola/Mundo, desconexión, comportamiento de todas las salas ni latencia acústica. No se han inyectado desconexiones reales ni ausencia de dispositivo. Los resultados físicos del informe anterior no se presentan como nuevos.

Plan manual reproducible:

1. Anotar nombres de entrada/salida predeterminadas de Windows, formato efectivo, volumen, perfil, distancia, ubicación y opciones Hamming/borrados; desactivar o registrar AGC/cancelación de ruido. No asignar SNR sin calibración.
2. Modo manual, 30 s: comenzar, comprobar onda/RMS/pico, detener a 8 s, guardar/reabrir WAV y comparar recepción explícita. Repetir hasta límite y con reinicio inmediato.
3. Hola/Mundo, captura antes de emitir, perfiles Original/Priorizar A, con/sin Hamming. Comprobar ambos mensajes y CRC; medir después sobre el mismo audio, exportar CSV y verificar origen/opciones.
4. Repetir en modo incremental; registrar aparición de cabecera/longitud/símbolos antes de finalizar. Pausar seguimiento, inspeccionar ambos canales y volver al vivo. Al detener, comparar con recepción manual del WAV idéntico.
5. Ensayos con volumen/distancia reducidos: observar A incompleto/B válido y viceversa sin forzar umbrales; conservar WAV fallidos, cobertura y CRC no comprobado. No confundir BER cero parcial con éxito.
6. Detener durante cabecera, longitud, datos y CRC; cancelar solo recepción; reiniciar y cerrar capturando/reproduciendo/analizando. Confirmar ausencia de resultados de otra sesión.
7. Con supervisión, desconectar entrada y salida, cambiar predeterminado entre sesiones y probar sin dispositivo. Confirmar diagnóstico, audio preservado, controles recuperados y posibilidad de nueva sesión. No desconectar equipos de terceros ni alterar ajustes sin registrar/restaurar.

## Archivos cambiados

- `AcousticLink.App/Audio/CapturedAudio.cs`
- `AcousticLink.App/Experiments/FdmExperimentAnalyzer.cs`
- `AcousticLink.App/MainWindow.xaml.cs`
- `checks/VerifyCoreStability.cs` (nuevo)
- `checks/VerifyManualCaptureWorkflow.cs`
- `Docs/CoreStabilityAudit.md` (este informe)

## Resultado final

`dotnet build`: cero errores y cero advertencias. Las 39 verificaciones finalizaron correctamente (38 previas y un check nuevo), incluidas las WPF y el fallo de parada inyectado. `git diff --check` sin incidencias. Solo tres archivos de producción modificados, dos de pruebas y este informe. La rama se mantuvo y no se hicieron commits.

Veredicto: base apta para continuar los ensayos reproducibles con las limitaciones explicitadas; no equivale a certificar el enlace acústico completo ni a cerrar las decisiones de alcance de 32 bytes/un canal/dispositivos. Los pasos físicos restantes deben revisarse antes de la demostración académica.
