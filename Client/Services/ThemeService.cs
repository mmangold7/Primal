using MudBlazor;

namespace Primal.Client.Services;

public class ThemeService
{
    public static MudTheme CurrentTheme { get; } = new();

    private bool _isDarkMode;
    public bool IsDarkMode
    {
        get => _isDarkMode;
        set => _isDarkMode = value;
    }

    public event EventHandler? ToolsToggled;
    public void ToggleTools()
    {
        var handler = ToolsToggled;
        handler?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? ZoomInPressed;
    public void ZoomIn()
    {
        var handler = ZoomInPressed;
        handler?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? ZoomOutPressed;
    public void ZoomOut()
    {
        var handler = ZoomOutPressed;
        handler?.Invoke(this, EventArgs.Empty);
    }
}