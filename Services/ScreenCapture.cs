using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PC_Optimizer.Services;

/// <summary>
/// Herramienta de revisión visual, solo con PCOPT_SHOTS=&lt;carpeta&gt; (y PCOPT_SCALE=1|1.25|1.5).
/// Simula un portátil de 1366x768 con esa escala (tamaño lógico = 1366/escala x 768/escala),
/// recorre las secciones sin aplicar nada y guarda un PNG por sección. Después cierra la app.
/// </summary>
public static class ScreenCapture
{
    public static string? Folder { get; } = Environment.GetEnvironmentVariable("PCOPT_SHOTS");
    public static bool Requested => !string.IsNullOrWhiteSpace(Folder);

    public static async Task RunAsync(Window window, Action<string> navigate, Action? sampleToast = null)
    {
        try
        {
            double scale = 1;
            double.TryParse(Environment.GetEnvironmentVariable("PCOPT_SCALE"),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out scale);
            if (!double.IsFinite(scale) || scale < 1) scale = 1;
            scale = Math.Min(scale, 3); // tope: evita imágenes enormes
            Directory.CreateDirectory(Folder!);

            window.WindowState = WindowState.Normal;
            window.Width = 1366 / scale;
            window.Height = 768 / scale;
            window.Left = 0; window.Top = 0;
            await Task.Delay(4000);

            string[] sections = { "Panel", "Home", "Diagnostics", "Guided", "Cleanup", "Measurement", "Profile", "BackupHistory" };
            string tag = scale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture).Replace('.', '_');
            foreach (var s in sections)
            {
                navigate(s);
                await Task.Delay(s == "Guided" ? 6000 : 3000);
                Save(window, Path.Combine(Folder!, $"{s}_{tag}.png"), scale);
                if (s == "Panel" && sampleToast != null)
                {
                    // Aviso de muestra (SIMULADO, texto «Simulado: …»): solo para la captura.
                    sampleToast();
                    await Task.Delay(900);
                    Save(window, Path.Combine(Folder!, $"Aviso_{tag}.png"), scale);
                }
            }
        }
        catch (Exception ex) { App.WriteLog($"[SHOT] {ex.GetType().Name}"); }
        finally { System.Windows.Application.Current?.Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown()); }
    }

    private static void Save(Window window, string path, double scale)
    {
        if (window.Content is not FrameworkElement root) return;
        window.UpdateLayout();
        int w = (int)Math.Ceiling(root.ActualWidth * scale), h = (int)Math.Ceiling(root.ActualHeight * scale);
        var bmp = new RenderTargetBitmap(w, h, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        // Fondo de la ventana debajo: los paneles sin fondo propio se verían blancos en la imagen.
        var back = new DrawingVisual();
        using (var dc = back.RenderOpen())
            dc.DrawRectangle((System.Windows.Application.Current.TryFindResource("BrushBackground") as System.Windows.Media.Brush) ?? System.Windows.Media.Brushes.Black, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        bmp.Render(back);
        bmp.Render(root);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
