using MudBlazor;

namespace Primal.Client.Services;

public class ThemeService
{
    public MudTheme CurrentTheme { get; } = new();
    private bool _isDarkMode = true;
    private bool _isDebugMode = true;
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
}