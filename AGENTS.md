# Reglas de desarrollo de AcousticLink

- Desarrollar incrementalmente, una funcionalidad por tarea y dentro del alcance solicitado. Escribir C# claro y mantenible, apropiado para un proyecto universitario de Ingeniería en Sistemas de Información. Mantener rigor técnico sin sobreingeniería, abstracciones innecesarias ni refactorizaciones masivas injustificadas.
- Separar generación y procesamiento de señales, protocolo y decodificación, captura y reproducción de audio, métricas experimentales y presentación/visualización WPF. Al cambiar comportamiento, analizar su impacto en los componentes que lo consumen.
- En cambios exclusivamente visuales, no alterar algoritmos de comunicaciones, frecuencias, umbrales, Hamming, CRC ni fórmulas de métricas.
- Distinguir explícitamente datos generados digitalmente, audio capturado, observaciones o mediciones, decisiones estimadas del detector, información reconstruida y resultados validados por CRC. No presentar valores inventados como mediciones reales.
- No afirmar que la comunicación acústica funciona basándose solo en pruebas sintéticas. Diferenciar compilación, checks, revisión WPF y ensayos físicos.
- Usar `verify-acousticlink` al modificar código funcional; `design-wpf-desktop` al modificar presentación; `debug-acousticlink` al investigar un defecto. Aplicar las verificaciones según el alcance real.
- Para tareas significativas, cerrar con: qué cambió, archivos afectados, qué se comprobó realmente, pendientes y contexto útil para el siguiente chat. Actualizar `Docs/DevelopmentStatus.md` solo ante cambios materiales del estado del proyecto.
- No cambiar de rama, crear commits ni descartar modificaciones existentes automáticamente.
