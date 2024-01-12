using MudBlazor;

namespace Primal.Client.Services;

public class ThemeService
{
    public static MudTheme CurrentTheme { get; } = new();
    private bool _isDarkMode = false;
    private bool _isDebugMode = false;
    public bool IsDarkMode
    {
        get => _isDarkMode;
        set
        {
            if (_isDarkMode != value)
            {
                _isDarkMode = value;
                OnDarkModeChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool IsDebugMode
    {
        get => _isDebugMode;
        set
        {
            if (_isDebugMode != value)
            {
                _isDebugMode = value;
                OnDebugModeChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public event EventHandler OnDarkModeChanged;
    public event EventHandler OnDebugModeChanged;
    public event EventHandler SaveImageClicked;
    public event EventHandler? ToolsToggled;

    public void SaveImage()
    {
        SaveImageClicked?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleTools()
    {
        var handler = ToolsToggled;
        handler?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleDebug()
    {
        IsDebugMode = !IsDebugMode;
        var handler = OnDebugModeChanged;
        handler?.Invoke(this, EventArgs.Empty);
    }
}