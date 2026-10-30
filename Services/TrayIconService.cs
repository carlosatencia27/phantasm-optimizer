namespace PC_Optimizer.Services;

public interface ITrayIconService : IDisposable
{
    void Show();
    void Hide();
}

public class TrayIconService : ITrayIconService, IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
    private readonly Action _onShow;
    private readonly Action _onExit;
    private bool _disposed;

    public TrayIconService(Action onShow, Action onExit)
    {
        _onShow = onShow;
        _onExit = onExit;

        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Visible = false,
            Text = "Phantasm Optimizer"
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        var showItem = new System.Windows.Forms.ToolStripMenuItem("Mostrar Phantasm Optimizer");
        showItem.Click += (_, _) => _onShow();
        var exitItem = new System.Windows.Forms.ToolStripMenuItem("Salir");
        exitItem.Click += (_, _) => _onExit();
        menu.Items.Add(showItem);
        menu.Items.Add(exitItem);
        _notifyIcon.ContextMenuStrip = menu;

        _notifyIcon.DoubleClick += (_, _) => _onShow();
    }

    public void Show()
    {
        _notifyIcon.Visible = true;
    }

    public void Hide()
    {
        _notifyIcon.Visible = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
