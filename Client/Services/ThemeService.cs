using MudBlazor;

namespace Primal.Client.Services;

public class ThemeService
{
    public MudTheme CurrentTheme { get; } = new();
}