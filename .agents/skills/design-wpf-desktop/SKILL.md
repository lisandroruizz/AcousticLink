---
name: design-wpf-desktop
description: Diseñar, implementar y revisar pantallas y visualizaciones WPF de AcousticLink; usar para cambios de presentación o interacción, no para modificar algoritmos del módem.
---

# Diseño WPF de AcousticLink

## Criterio visual

Mantener y mejorar gradualmente la identidad actual de herramienta técnica de escritorio. Inspeccionar `MainWindow.xaml`, `Styles/Dashboard.xaml`, vistas en `Visualization/` y la pantalla renderizada antes de proponer cambios. La interfaz debe permitir estudiar señales y resultados, con navegación, controles e inspectores coherentes; evitar una sucesión de formularios, tarjetas iguales y gráficos de relleno. Respetar la arquitectura existente: no imponer MVVM ni migrar a tecnologías web.

Cada sección debe responder de inmediato: **Panel general**, qué ocurre; **Emisor**, qué se transmite y cómo se vuelve señal; **Receptor**, qué llega del micrófono y cómo se interpreta; **Análisis**, qué calidad y qué errores/recuperaciones hubo. Dar jerarquía a indicadores, valores y unidades; dejar detalles secundarios disponibles sin dominar la pantalla. Usar títulos, estados, texto auxiliar y estilos compartidos de forma consistente. No depender solo del color.

## Distribución e interacción

Aprovechar ancho y alto con paneles de escritorio e inspectores estables. Evitar desplazamiento innecesario, expansiones que muevan bruscamente toda la pantalla y controles fuera de vista. Revisar 1366 × 768 y escalado de 125 %; comprobar recortes, superposiciones, contraste y proporciones. Modernizar controles sin perder acciones ni estados existentes.

Usar movimiento discreto para transiciones de sección, selección de símbolo, apertura de inspector, nueva información y estados de captura/recepción. Evitar animaciones continuas del layout o trabajo pesado en Dispatcher. Priorizar respuesta de WPF y fidelidad de los datos.

## Visualización técnica

- **Emisor:** mostrar formación BFSK y combinación FDM con perspectivas técnicas distintas.
- **Receptor:** diferenciar muestras capturadas, energía de portadoras, decisiones estimadas y datos reconstruidos.
- **Hamming:** mostrar posiciones recibidas, corregidas o recuperadas y resultado final.
- **Análisis:** representar BER, cobertura, errores, borrados, correcciones y comparaciones con denominadores claros.

Todo gráfico debe identificar fuente de datos, ejes, unidades, escalas y leyenda o explicación contextual; permitir selección si ayuda al análisis. No repetir una onda sin aportar otra lectura ni crear muestras ficticias para llenar espacio. Separar observación, estimación, reconstrucción y validación CRC.

## Proceso para cambios importantes

Inspeccionar pantalla y controles; registrar problemas concretos; definir brevemente distribución y jerarquía; implementar un cambio acotado; observar el render y la interacción cuando sea posible; corregir recortes, espacio, contraste, movimiento y lectura técnica; comprobar que las funciones anteriores sigan accesibles. Aplicar `verify-acousticlink` si se modificó código funcional. Si no se puede observar WPF, reportar esa limitación: compilar XAML no valida calidad visual.
