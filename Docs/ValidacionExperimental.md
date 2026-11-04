# Validación experimental de AcousticLink — campaña inicial

## Estado y alcance

Protocolo preparado para ejecutar; **no contiene resultados físicos nuevos**. Consultar `CoreStabilityAudit.md` para defectos corregidos y limitaciones abiertas. La captura/parada física comprobada allí no demuestra todavía recepción acústica de mensajes.

No se cambia el protocolo: A=1200/1900 Hz, B=3000/3700 Hz, símbolo 0,10 s, salida digital 48 kHz. El receptor utiliza la tasa efectiva de la captura. Hola ocupa 4 bytes UTF-8 y Mundo 5, dentro del límite acústico de 1–8 bytes. Original: A/B=0,075/0,075; Priorizar A: 0,10/0,05. El perfil no se infiere del audio.

La campaña evalúa dos canales simultáneos, sin escuchar indefinidamente ni enviar varias tramas en una misma captura. No demuestra SNR calibrada ni sirve para ampliar el límite documental a 32 bytes.

## Condiciones que deben fijarse antes de empezar

- Identificar la sesión experimental, versión/commit si existe y modificaciones locales. No cambiar software, frecuencias, umbrales ni equipos dentro de la campaña.
- Registrar nombres exactos de entrada/salida predeterminadas de Windows, PC(s), dispositivo acústico emisor, micrófono, conexión y formato efectivo. La aplicación usa los dispositivos predeterminados; no tiene selector propio. Registrar aparte los roles de Windows si difieren.
- Volumen de salida Windows **80 % en el endpoint utilizado**. Registrar además volumen por aplicación, ganancia de micrófono y mejoras/AGC/cancelación de ruido/sonido espacial; mantenerlos constantes. El 80 % no es una presión sonora calibrada.
- Marcar posición fija de emisor y micrófono con cinta o soporte. Medir distancia entre los puntos acústicos de referencia elegidos y describir orientación; anotar cómo se midió. No asignar una distancia inventada.
- Misma sala, ubicación, puertas/ventanas, ruido de fondo y fuente de ruido. Describir cambios audibles sin convertirlos en dB/SNR no medidos. Una foto/esquema ayuda a repetir la posición.
- Captura **30 s**. Seleccionar entrada de micrófono, no mezcla estéreo/loopback. Si una PC emite y recibe, la fuente debe seguir identificada como micrófono; el buffer digital del emisor no es evidencia acústica.
- Textos exactos y sin espacios adicionales: A=`Hola`, B=`Mundo`. Receptor y emisor deben seleccionar la misma modalidad Hamming.

## Matriz de 12 transmisiones físicas

Ejecutar en el orden indicado, por rondas intercaladas, para no concentrar un perfil en un único momento. Cada fila requiere una captura y transmisión nuevas. Las columnas son **configuración planificada**, no evidencia de ejecución.

| Intento | Ronda | Perfil emisor | Hamming emisor/receptor |
|---|---:|---|---|
| E01 | 1 | Original | No |
| E02 | 1 | Priorizar A | No |
| E03 | 1 | Original | Sí |
| E04 | 1 | Priorizar A | Sí |
| E05 | 2 | Original | No |
| E06 | 2 | Priorizar A | No |
| E07 | 2 | Original | Sí |
| E08 | 2 | Priorizar A | Sí |
| E09 | 3 | Original | No |
| E10 | 3 | Priorizar A | No |
| E11 | 3 | Original | Sí |
| E12 | 3 | Priorizar A | Sí |

Son tres intentos por cada una de las cuatro condiciones, todos con volumen 80 % y geometría/hardware constantes. Para comparar manual/incremental con el mismo audio, activar **Decodificar durante la captura en los 12 intentos**. Mantener recuperación de borrados **apagada durante esa captura**; su comparación posterior usa los mismos WAV con Hamming. Sin Hamming, recuperación se registra **no aplicable**.

No repetir silenciosamente un fallo buscando un éxito. Conservar todos los intentos. Si una ejecución se invalida por interrupción, equipo movido o formato incorrecto, registrarla y asignar un ID adicional, por ejemplo E03-R1, enlazado al original y con motivo. No sobrescribir archivos. Un intento acústico fallido con condiciones correctas sí cuenta; un error operativo se informa separado.

## Procedimiento de cada intento

1. Completar los metadatos previos de `PlantillaRegistroExperimental.md`. Confirmar ID, perfil, Hamming, volumen 80 %, distancia/orientación, hardware y referencias Hola/Mundo.
2. En Emisor seleccionar el perfil y Hamming de la fila. Preparar vista previa es opcional y no constituye transmisión. Para Hola/Mundo, la duración digital esperada es 10,4 s sin Hamming y 16,4 s con Hamming. No modificar símbolos para obtener otra duración.
3. En Receptor seleccionar 30 s, Hamming coincidente, recuperación apagada y decodificación durante captura activada. Abrir Monitor de recepción y comprobar fuente micrófono. Las opciones quedan fijadas al iniciar.
4. Iniciar captura; comprobar crecimiento real de muestras/tiempo/onda. Emitir **una sola vez** aproximadamente a los 3 s de captura (registrar la demora real o aproximada y método; procurar ±0,5 s). No hablar ni producir sonidos deliberados durante el intento. El mensaje más largo con Hamming terminaría alrededor de 19,4 s, dejando margen hasta 30 s.
5. Observar y registrar estados incrementales A/B **antes del final**: cabecera, símbolos, longitud, Hamming y CRC cuando existan. Anotar tiempo de aparición observado o guardar captura de pantalla; esos tiempos de UI son aproximados, no latencia acústica calibrada. Pausar seguimiento visual no pausa recepción. No usar Recorrer WAV como prueba de procesamiento en vivo.
6. Dejar finalizar por límite. Si se detiene antes, registrar duración y motivo; no tratar automáticamente una trama truncada como ensayo comparable completo. Guardar inmediatamente `SESION_E01.wav` (adaptar ID) mediante Guardar WAV. Comprobar nombre, duración y tasa real. No normalizar, editar ni remuestrear el archivo.
7. **Antes de importar o pulsar Recibir mensajes**, registrar el resultado incremental retenido A/B: sincronización, inicio si disponible, longitud, símbolos/diagnóstico, CRC y texto. La nueva acción puede reemplazar esa presentación. Si no existe detalle accesible, registrar «no disponible» y no afirmar igualdad de ese dato.
8. Importar el WAV recién guardado. Comprobar su ruta y que se invalidaron los resultados anteriores. Ejecutar Recibir mensajes con Hamming correcto y recuperación apagada. Comparar con el resultado incremental registrado: cabecera, inicio, símbolos, longitud, Hamming, CRC y texto disponibles. Una divergencia debe conservarse e investigarse; no ajustar umbrales ni descartarla.
9. En Análisis introducir referencias exactas Hola/Mundo, Hamming coincidente y recuperación apagada. Seleccionar **perfil FDM declarado de ese intento**, porque vuelve a desconocido tras una nueva captura/importación. Las opciones de Análisis son independientes de las de Receptor: revisar ambas, sin confiar solo en la advertencia de coincidencia.
10. Ejecutar Medir y esperar finalización. Exportar `SESION_E01_base.csv`. Comprobar que contiene dos filas A/B, origen del WAV seleccionado, modalidad Hamming, recuperación, perfil y tasa esperados. El CSV es de la medición posterior, **no un registro temporal del incremental**.
11. Para un WAV con Hamming, sin cambiar archivo, referencia ni perfil: activar recuperación en Receptor, volver a recibir; activarla también en Análisis, volver a Medir y exportar `SESION_E03_borrados_on.csv`. Mantener `..._base.csv` como recuperación apagada. No basta cambiar un toggle y exportar: hay que recalcular. Sin Hamming, omitir esta variante.
12. Completar resultado/observaciones y enlaces de registro. Confirmar que los archivos se abren, no se sobrescribieron y pertenecen al ID correcto. Preparar la siguiente fila sin mover equipos. Restaurar recuperación apagada antes de la siguiente captura.

La primera matriz produce 12 WAV y **18 CSV de mediciones** si se completa: seis sin Hamming y doce correspondientes a los seis WAV Hamming con dos configuraciones. Son 12 transmisiones, no 18; cada CSV contiene ambos canales. Los registros incrementales/screenhots son evidencia adicional de esas mismas capturas.

## Comparaciones permitidas

| Comparación | ¿Nueva transmisión? | Condiciones |
|---|---|---|
| Manual vs incremental | No, si se registró incremental durante esa captura | Importar exactamente su WAV; mismas opciones. No se puede obtener retrospectivamente evidencia incremental física a partir de un WAV. |
| Borrados apagados/encendidos | No | Mismo WAV con Hamming, referencias idénticas, modo de análisis correcto y dos CSV distintos. |
| Hamming apagado/encendido | **Sí** | Son tramas/duraciones diferentes; cada WAV se recibe en el modo con que fue emitido. Elegir el modo opuesto no es una comparación válida de corrección. |
| Original/Priorizar A | **Sí** | Cambia la señal emitida. Cambiar el metadato del WAV no cambia sus ganancias ni genera otro ensayo. |
| Canal A/B | No | Resultados independientes dentro del mismo intento; frecuencias y textos son distintos, por lo que no se atribuye toda diferencia únicamente a amplitud. |
| Recorrer WAV/inspección estática | No | Solo navegación; no es otra recepción física ni una repetición independiente. |

## Clasificación de resultados

1. **Captura realizada:** hay muestras reales, formato/duración y archivo guardado. No demuestra sincronización ni recepción.
2. **Cabecera sincronizada por canal:** el receptor valida preámbulo y marcador. La sincronización global parcial puede significar que solo A o solo B dispone de cabecera válida.
3. **Bloque reconstruido:** se reunieron los bits necesarios, o Hamming produjo los originales; aún debe respetarse la validación final. Un síndrome cero o una inversión intentada no demuestra integridad.
4. **Mensaje válido por canal:** estado válido, CRC válido, UTF-8 válido y texto recuperado exacto respecto de lo emitido. CRC válido con UTF-8 inválido no es éxito de mensaje. Si un texto con CRC válido difiere de Hola/Mundo, registrar discrepancia; no corregirlo desde la referencia.
5. **Resultado del intento:** ambos válidos, solo A válido, solo B válido o ninguno válido. Describir cada fallo como no sincronizado, incompleto/truncado, longitud inválida, bits no concluyentes, CRC inválido o UTF-8 inválido. «CRC no comprobado» no equivale a válido ni a CRC incorrecto.

BER = incorrectos/comparados, solo cuando hay bits comparados. Cobertura = comparados/esperados. BER indefinida se registra «no definida»; no se convierte en 0. Sin cabecera validada, BER/cobertura no disponibles no se sustituyen por cero. **BER 0 con cobertura parcial no demuestra recuperación completa.** El 100 % de cobertura tampoco garantiza ausencia de errores.

Antes de Hamming se comparan bits codificados realmente recibidos; después, bits originales reconstruidos. Los denominadores son distintos. Los `?` originales reducen cobertura previa; no son automáticamente errores binarios ni inversiones. Borrados recuperados e inversiones por síndrome se registran por separado. Sin Hamming, las métricas posteriores específicas son no aplicables.

## Registro y trazabilidad

Usar la plantilla Markdown adjunta para condiciones físicas, archivos y evidencia incremental. Las métricas exactas se conservan en el CSV de la aplicación, sin volver a transcribirlas manualmente. El esquema de columnas y las unidades se explican en la plantilla. Archivar copia del software/configuración y los originales WAV; una suma SHA-256 opcional ayuda a comprobar que las dos variantes usan el mismo archivo, sin modificarlo.

Al cambiar de audio se invalidan mediciones/CSV anteriores; una importación fallida conserva la selección anterior. Tras un error de importación, **no exportar pensando que se seleccionó el archivo nuevo**: comprobar ruta y repetir la importación. Tras cambiar opciones o referencias, exportar solo después de Medir otra vez. El programa conserva el resultado de la medición previa hasta recalcular: el nombre del archivo no altera sus parámetros.

## Cierre y revisión

Informar número de intentos ejecutados/válidos operativamente, ambos válidos, solo A, solo B y ninguno, por condición. Presentar fracciones como 2/3 además del porcentaje. Tres repeticiones son una exploración inicial pequeña; no acreditan fiabilidad general ni diferencias estadísticamente concluyentes. Mantener los fallos en el conjunto.

Si aparece una divergencia manual/incremental, resultado obsoleto o bloqueo, conservar WAV, CSV, opciones y pasos exactos. Suspender la interpretación comparativa de los ensayos afectados y proponer la corrección mínima antes de cambiar código. No declarar resueltas desconexiones, captura de 30 s, condiciones de sala o recepción acústica completa solo porque pasan checks sintéticos.

## Verificación de preparación

En esta fase se ejecutó `dotnet build` (sin advertencias ni errores), las 39 verificaciones existentes (todas aprobadas, incluidas las correcciones de estabilidad) y `git diff --check` sin incidencias. Se revisaron controles de captura de 30 s, opciones independientes Receptor/Análisis, declaración de perfil, invalidación y columnas CSV. No se detectó un defecto nuevo que requiriera modificar el programa.

Estos resultados respaldan preparar los ensayos, no cerrarlos como exitosos. Siguen pendientes recepción acústica de los mensajes, comparación de ganancias a 80 %, captura física de 30 s en las condiciones fijadas, estabilidad ante desconexiones y repetibilidad en la sala. Las limitaciones de la auditoría anterior no se declaran resueltas por esta revisión. No se ejecutaron los 12 intentos ni se generaron resultados físicos o CSV de mediciones ficticias.
