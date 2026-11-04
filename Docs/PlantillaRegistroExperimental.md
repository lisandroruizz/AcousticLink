# Registro de campaña — plantilla sin resultados

Duplicar este documento para cada campaña. Vacío significa **pendiente**, no cero ni fallo. La matriz planificada está en `ValidacionExperimental.md`; esta plantilla no afirma que se ejecutó ningún intento.

## Condiciones comunes de la sesión

| Dato | Valor registrado |
|---|---|
| ID de sesión / fecha / zona horaria / operador | |
| Versión, commit si existe y cambios locales | |
| PC emisora / PC receptora / Windows | |
| Salida predeterminada y emisor acústico exactos | |
| Entrada predeterminada y micrófono exactos | |
| Conexiones / roles predeterminados si difieren | |
| Volumen Windows / volumen aplicación / ganancia micrófono | |
| AGC, cancelación de ruido, mejoras, sonido espacial | |
| Distancia medida / puntos de medición / instrumento | |
| Orientación y posiciones / foto o esquema | |
| Sala / ruido de fondo / puertas y ventanas | |
| Referencias A/B y duración configurada | |
| Ubicación del archivo de campaña | |

Condiciones planificadas: volumen 80 %, Hola/Mundo, captura 30 s, hardware y geometría constantes. Escribir los valores efectivamente usados; no completar automáticamente como cumplidos.

## Una ficha por transmisión física

| Dato | Valor registrado |
|---|---|
| ID de intento / ronda / condición / reemplaza a otro ID | |
| Fecha y hora de captura con zona horaria | |
| Perfil emitido (Original / Priorizar A) | |
| Hamming emitido y seleccionado en Receptor | |
| Decodificar durante captura / recuperación durante captura | |
| Volumen / distancia / hardware (igual a sesión o cambio exacto) | |
| Orientación / ruido / interrupciones respecto de la sesión | |
| Inicio de emisión aproximado dentro de captura / método | |
| Duración y tasa efectivas / parada por límite, manual o error | |
| Ruta WAV original / SHA-256 opcional | |
| Evidencia incremental antes del final (nota, captura de pantalla) | |
| Incremental A: cabecera / longitud / CRC / texto validado | |
| Incremental B: cabecera / longitud / CRC / texto validado | |
| Evolución observada antes del final / símbolos o tiempos disponibles | |
| Coincidencia manual/incremental / diferencias precisas | |
| Resultado base: ambos / solo A / solo B / ninguno válido | |
| Validez operativa: comparable / interrumpido / otro, con motivo | |
| Observaciones / recuperación del dispositivo / incidencias | |

## Una fila por análisis posterior

| ID de análisis | ID intento/WAV | Configuración real: Hamming / borrados | Perfil declarado | Ruta CSV | Resultado A/B resumido | Observaciones |
|---|---|---|---|---|---|---|
| | | | | | | |

Ejemplo de **nomenclatura**, sin afirmar ejecución: E03-base y E03-borrados-on apuntan al mismo WAV E03 y a CSV distintos. Sin Hamming, borrados se marca N/A; el CSV puede escribir `no` por estar deshabilitado. Registrar cualquier discrepancia entre configuración planificada y exportada.

## Fuente única de las métricas: CSV de AcousticLink

No duplicar a mano las métricas en la ficha. Cada CSV tiene una fila por canal, fecha UTC de **análisis** (no de captura), origen y configuración. Consultar estas columnas para cubrir el registro técnico:

| Dato solicitado | Columnas del CSV / interpretación |
|---|---|
| Perfil, Hamming y recuperación | `perfil_amplitud_declarado`, `amplitud_a_declarada`, `amplitud_b_declarada`, `hamming`, `recuperacion_borrados` |
| Fecha de medición, origen, tasa y duración | `fecha_utc`, `origen`, `tasa_hz`, `duracion_s`; fecha/hardware/distancia de captura quedan en la ficha |
| Sincronización A/B | Filas `canal`, `cabecera_valida`, `cabecera_bits`, `inicio_s`, `sincronizacion`; el estado global no reemplaza la cabecera individual |
| CRC y resultado | `crc`, `estado_recepcion`, `diagnostico`; el texto recuperado exacto se confirma en Receptor y se anota/evidencia aparte, no se supone incluido en CSV |
| BER previa/posterior | `antes_ber`, `despues_ber`; fracciones entre 0 y 1, multiplicar por 100 solo para presentación porcentual |
| Cobertura previa/posterior | `antes_cobertura`, `despues_cobertura`; mismas unidades fraccionales |
| Denominadores y errores | `antes_esperados`, `antes_comparados`, `antes_incorrectos`, `antes_faltantes`; equivalentes `despues_*` cuando correspondan |
| Símbolos no concluyentes | `antes_no_concluyentes`; `despues_no_concluyentes` son bits originales todavía indeterminados, no nuevos símbolos del micrófono |
| Inversiones Hamming intentadas | `hamming_sindromes_no_nulos`, `hamming_inversiones`, `hamming_posiciones`; no son correcciones reales garantizadas |
| Borrados recuperados | `borrados_recuperados`, `palabras_recuperadas_por_borrados`, `palabras_irrecuperables`; separados de inversiones |
| Referencias y observaciones binarias | `referencia_original`, `referencia_transmitida`, `antes_observados`, `despues_observados`; referencias no alimentan la recepción |

CSV: UTF-8 con BOM, separador `;`, coma decimal y campos entre comillas. Campos métricos vacíos pueden indicar no definido/no calculable/no aplicable: revisar sincronización y Hamming antes de interpretarlos. No rellenarlos con ceros. No confundir el mensaje esperado del CSV con un mensaje efectivamente recuperado.

## Resumen al terminar la campaña

| Condición | Ejecutados | Comparables | Ambos válidos | Solo A | Solo B | Ninguno | Incidencias/excluidos y motivo |
|---|---|---|---|---|---|---|---|
| Original sin Hamming | | | | | | | |
| Priorizar A sin Hamming | | | | | | | |
| Original con Hamming | | | | | | | |
| Priorizar A con Hamming | | | | | | | |

Para Hamming, separar resumen base (borrados apagados) y variante encendida; no contar ambos análisis del mismo WAV como intentos independientes. Acompañar cualquier porcentaje con su numerador/denominador y conservar todos los archivos fallidos.
