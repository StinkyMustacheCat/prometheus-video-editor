namespace Prometheus.Core.Timeline;

public enum TimelineTool
{
    Selection,       // V - Selección, Trim dinámico y Roll
    Razor,           // C - Cuchilla de corte preciso
    Slip,            // Deslizamiento horizontal de source_in
    SelectForward,   // A - Selección direccional hacia adelante
    SelectBackward,  // Shift+A - Selección direccional hacia atrás
    Hand,            // H - Mano / Desplazamiento de lienzo
    Zoom,            // Z - Zoom centrado en cursor
    Sam,             // G - Elegir clip y fotograma para máscara SAM
    Text             // T - Dibujar texto en el monitor
}
