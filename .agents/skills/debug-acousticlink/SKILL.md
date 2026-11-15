---
name: debug-acousticlink
description: Diagnosticar defectos funcionales, bloqueos WPF o fallos de sincronización en AcousticLink antes de corregirlos; usar ante síntomas observados, no para implementar una función nueva.
---

# Depuración de AcousticLink

1. **Reproducir.** Precisar resultado esperado y observado; anotar configuración, origen de audio, modalidad, duración, acciones y condiciones de repetición. Conservar muestras o un WAV pertinente cuando sea posible, sin presentarlos como prueba física si son sintéticos.
2. **Investigar.** Seguir las llamadas y los datos desde la entrada hasta la salida. Separar síntomas, hipótesis y hechos. Usar checks existentes, logs, trazas o instrumentación temporal acotada cuando falte evidencia. Para congelamientos, identificar operación, hilo, volumen de trabajo, espera/bloqueo y relación con Dispatcher; no atribuir causa solo por proximidad al código UI.
3. **Identificar la causa.** Explicar el mecanismo con evidencia reproducible. Si varias hipótesis siguen abiertas, indicarlo y buscar una observación que las distinga; no declarar una causa única sin demostrarla.
4. **Corregir.** Hacer un cambio localizado que preserve el comportamiento existente. No cambiar algoritmos, frecuencias, umbrales ni arquitectura para ocultar un síntoma.
5. **Verificar.** Repetir el caso original y aplicar `verify-acousticlink` a las regresiones pertinentes. Si solo hay evidencia sintética, mantener pendiente la validación física.

Informar síntoma, causa confirmada o hipótesis pendiente, evidencia, corrección, comprobaciones ejecutadas y riesgos restantes. El mismo proceso se aplica a defectos de protocolo, audio, concurrencia, recepción continua y presentación.
