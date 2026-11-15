---
name: verify-acousticlink
description: Verificar cambios funcionales de AcousticLink con checks pertinentes, compilación y evidencia diferenciada; usar antes de declarar completada una modificación de código, no para una edición documental menor.
---

# Verificación de AcousticLink

## Delimitar antes de ejecutar

1. Inspeccionar `git status --short`, `git diff --name-only`, `git diff` y los archivos nuevos pertinentes. Distinguir cambios propios de los preexistentes. Identificar consumidores de los archivos modificados y los comportamientos que pueden variar.
2. Ejecutar `git diff --check`. Para archivos nuevos aún no incluidos en el diff, revisar también sus espacios y sintaxis. Elegir verificaciones proporcionales al riesgo; documentación y configuración no requieren la suite del módem.
3. Si el entorno admite WPF, compilar con `dotnet build AcousticLink.slnx` desde la raíz. Si no, registrar por qué no se pudo. La compilación no demuestra comportamiento.
4. Los checks reales son programas C# independientes en `checks/`, con directivas `#:property TargetFramework=net10.0-windows` y `#:project ../AcousticLink.App/AcousticLink.App.csproj`. Con SDK .NET 10 se ejecuta cada check pertinente con `dotnet run --file checks/VerifyNombre.cs`; comprobar su salida y código de salida. No sustituirlos por `dotnet test` ni ejecutar todos por rutina. Revisar si el check WPF usa audio simulado o hardware real antes de lanzarlo.
5. Inspeccionar efectos indirectos, casos límite y regresiones en áreas conectadas. Comparar la evidencia con el comportamiento solicitado, no solo con el nombre del check. Tras corregir un fallo, repetir las verificaciones afectadas sobre el contenido final.

## Focos según el área modificada

- **Transmisión:** símbolos, duración, frecuencias, amplitudes, trama y compatibilidad con el protocolo actual.
- **Recepción:** detección, sincronización, bits, cabeceras, longitud, CRC y entrega independiente de canales.
- **Hamming:** codificación, síndrome, corrección, recuperación opcional de borrados, rechazos y contadores.
- **Audio:** captura, reproducción, cancelación, recursos, errores, concurrencia y cierre.
- **Recepción continua:** límites de buffers/cola, sesiones consecutivas, rearme, publicaciones tardías, trabajo costoso fuera del hilo UI y ausencia de bloqueo del Dispatcher.
- **Métricas:** denominadores, BER, cobertura, bits inconclusos, antes/después y CSV.
- **WPF:** controles operativos, cambios de estado y visualizaciones coherentes con los datos. Si el cambio es visual, aplicar también `design-wpf-desktop`.

## Informe de evidencia

Separar **compilación**, **checks automatizados**, **inspección/interacción WPF** y **ensayo acústico físico**. Para cada comprobación importante indicar `APROBADO`, `FALLIDO` o `NO COMPROBADO`, comando o pasos, resultado observable y límite de la evidencia. No informar una prueba como aprobada si no se ejecutó. Una simulación o WAV no prueba transmisión acústica física.
