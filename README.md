# Phantasm Optimizer

Aplicación gratuita para Windows 10 y 11 que te deja ver y cambiar ajustes de Windows con el riesgo de cada uno indicado (Seguro / Moderado / Avanzado). Antes de aplicar nada puedes usar el **modo simulación**, que muestra lo que haría sin cambiar nada. Guarda un historial local para **deshacer** los cambios y compara tu PC antes y después con datos medidos. Funciona sin internet.

- No promete mejoras de rendimiento: solo muestra lo que se mide en tu equipo.
- Los ajustes se pueden deshacer. La limpieza de archivos temporales es la única excepción, y la app lo avisa.
- Nunca toca Defender, el firewall, UAC, Windows Update ni las mitigaciones de CPU.

## Requisitos
- Windows 10 o Windows 11, 64 bits.
- No necesitas instalar nada más (.NET viene incluido).
- No hace falta ser administrador para instalarla. Algunos ajustes piden permisos de administrador solo cuando los aplicas.

## Instalación paso a paso

### Opción A: instalador (recomendada)
1. Abre la página de descargas: https://github.com/carlosatencia27/phantasm-optimizer/releases/latest
2. Baja hasta la sección **Assets** y haz clic en **Phantasm-Optimizer-Setup.exe**. Se descargará en tu carpeta *Descargas*.
3. Abre la carpeta *Descargas* y haz doble clic en `Phantasm-Optimizer-Setup.exe`.
4. Si Windows muestra «Windows protegió su PC», haz clic en **Más información** y luego en **Ejecutar de todas formas**. Esto pasa porque la app no tiene firma de código de pago; no significa que tenga virus.
5. Sigue el asistente del instalador (**Siguiente** → **Instalar** → **Finalizar**). La casilla de acceso directo en el escritorio es opcional. Se instala solo para tu usuario.
6. Abre **Phantasm Optimizer** desde el menú Inicio (escribe «Phantasm» y pulsa Enter).

### Opción B: versión portátil (sin instalar)
1. En la misma página de descargas, haz clic en **Phantasm-Optimizer-portable.zip**.
2. En *Descargas*, haz clic derecho sobre el zip → **Extraer todo…** → **Extraer**.
3. Abre la carpeta extraída y haz doble clic en `PC Optimizer.exe`.
4. Si aparece «Windows protegió su PC», usa **Más información** → **Ejecutar de todas formas**.

## Cómo usarla
1. Al abrirla, entra en **Inicio** y pulsa **Analizar mi PC**: solo lee datos, no cambia nada.
2. Si quieres probar sin riesgo, enciende **Modo simulación** (arriba a la derecha).
3. Para cambiar un ajuste, entra en **Ajustes uno por uno**, elige el ajuste y confírmalo.
4. Para volver atrás, entra en **Deshacer cambios**.

## Desinstalar
- Instalada: *Configuración* de Windows → *Aplicaciones* → *Aplicaciones instaladas* → Phantasm Optimizer → **Desinstalar**.
- Portátil: borra la carpeta extraída.

## Para desarrolladores
Requiere el SDK de .NET 8.
```
dotnet build
dotnet run --project "PC Optimizer.csproj"
```
El nombre visible es «Phantasm Optimizer». El ejecutable (`PC Optimizer.exe`), el espacio de nombres y la carpeta de datos `%LOCALAPPDATA%\PC Optimizer` conservan el nombre anterior a propósito, para no romper respaldos ni historial.

## Derechos
Todos los derechos reservados. Puedes descargar y usar la app gratis, pero no copiar ni redistribuir el código (ver `LICENSE`).

---

**Created by: Carlos Atencia**
