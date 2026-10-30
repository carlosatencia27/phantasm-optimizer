namespace PC_Optimizer.Core;

/// <summary>
/// Formato de tamaños con unidad automática, en un solo lugar (B, KB, MB, GB, TB).
/// </summary>
public static class ByteSize
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

    public static string Format(long bytes)
    {
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < Units.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {Units[order]}";
    }
}
