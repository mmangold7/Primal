using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MudBlazor.Components.Chart;
using SkiaSharp;
using SkiaSharp.Views.Blazor;
using System.Numerics;
using System.Text.Json;

namespace Primal.Client.Pages;

public partial class Index
{
    private readonly int _maxNumber = 10000;
    private List<int> _primes = new();
    private bool _isDebugModeEnabled = true;
    private string _debugText = "";
    private float _zoomScale = 1f;
    private SKPoint _lastMousePosition;
    private SKPoint _currentPanOffset = new(0, 0);
    private const int SquareSize = 1;
    private float _canvasWidth;
    private float _canvasHeight;
    private SKCanvasView? _drawingCanvas;
    private SKBitmap? _spiralBitmap;
    private SKImageInfo _spiralBitmapInfo;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            var size = await JSRuntime.InvokeAsync<Size>("getElementSize", "canvasElementId");
            _canvasWidth = size.Width;
            _canvasHeight = size.Height;

            SetInitialZoomLevel(size.Height);
            CreateSpiralBitmap(_maxNumber);
            CenterSpiralOnCanvas(size.Width, size.Height);

            StateHasChanged();
        }
    }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();

        var loaded = await LoadPrimesFromLocal();
        if (!loaded)
        {
            GeneratePrimesUpTo(_maxNumber);
            SavePrimesToLocal();
        }

        ThemeService.OnDarkModeChanged += HandleDarkModeChanged;
        ThemeService.OnDebugModeChanged += HandleDebugModeChanged;
    }

    private void GeneratePrimesUpTo(int max)
    {
        bool[] isPrime = new bool[max + 1];
        Array.Fill(isPrime, true);
        isPrime[0] = isPrime[1] = false;

        for (int p = 2; p * p <= max; p++)
        {
            if (isPrime[p])
            {
                for (int i = p * p; i <= max; i += p)
                    isPrime[i] = false;
            }
        }

        for (int p = 2; p <= max; p++)
        {
            if (isPrime[p])
            {
                _primes.Add(p);
            }
        }
    }

    private (float x, float y) GetSpiralPosition(int number)
    {
        if (number == 1) return (0, 0);

        int layer = (int)Math.Ceiling((Math.Sqrt(number) - 1) / 2);
        int legLength = 2 * layer;
        int maxLayerValue = (2 * layer + 1) * (2 * layer + 1);
        int offset = maxLayerValue - number;

        int x = 0, y = 0;
        if (offset < legLength) // Top
        {
            x = layer - offset;
            y = layer;
        }
        else if (offset < 2 * legLength) // Left
        {
            x = -layer;
            y = layer - (offset - legLength);
        }
        else if (offset < 3 * legLength) // Bottom
        {
            x = -layer + (offset - 2 * legLength);
            y = -layer;
        }
        else // Right
        {
            x = layer;
            y = -layer + (offset - 3 * legLength);
        }

        return (x, y);
    }

    private void CenterSpiralOnCanvas(float canvasWidth, float canvasHeight)
    {
        _currentPanOffset = new SKPoint(
            (canvasWidth - _spiralBitmapInfo.Width * _zoomScale) / 2,
            (canvasHeight - _spiralBitmapInfo.Height * _zoomScale) / 2
        );
    }

    private void SetInitialZoomLevel(float canvasHeight)
    {
        int desiredBlocksVisible = 20; // Number of blocks to show
        float totalHeightOfBlocks = desiredBlocksVisible * SquareSize; // Total height of the blocks
        _zoomScale = canvasHeight / totalHeightOfBlocks;
    }

    private int CalculateRequiredBitmapSideLength(int maxNumber)
    {
        // Rough estimation of the spiral size
        var maxLayer = (int)Math.Ceiling((Math.Sqrt(maxNumber) - 1) / 2);
        var size = 2 * maxLayer + 1; // Number of squares per side
        return size * SquareSize;
    }

    private void CreateSpiralBitmap(int maxNumber)
    {
        int bitmapSideLength = CalculateRequiredBitmapSideLength(maxNumber);
        _spiralBitmap = new SKBitmap(bitmapSideLength, bitmapSideLength);
        _spiralBitmapInfo = _spiralBitmap.Info;
        var center = bitmapSideLength / 2;

        using (var canvas = new SKCanvas(_spiralBitmap))
        {
            canvas.Clear(SKColors.Transparent);
            var primePaint = new SKPaint { Color = SKColors.Black, IsAntialias = true, Style = SKPaintStyle.Fill };
            var nonPrimePaint = new SKPaint { Color = SKColors.Transparent, IsAntialias = true, Style = SKPaintStyle.Fill };

            for (int i = 1; i <= maxNumber; i++)
            {
                var (x, y) = GetSpiralPosition(i);
                var rect = new SKRect(x + center, y + center,
                    x + SquareSize + center, y + SquareSize + center);

                var paint = _primes.Contains(i) ? primePaint : nonPrimePaint;
                canvas.DrawRect(rect, paint);
            }
        }
    }

    private async void DrawNextCanvasFrame(SKPaintSurfaceEventArgs args)
    {
        var canvas = args.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        var matrix = SKMatrix.CreateIdentity();
        matrix = matrix.PostConcat(SKMatrix.CreateScale(_zoomScale, _zoomScale));
        matrix = matrix.PostConcat(SKMatrix.CreateTranslation(_currentPanOffset.X, _currentPanOffset.Y));
        canvas.SetMatrix(matrix);
        canvas.DrawBitmap(_spiralBitmap, 0, 0);
        canvas.ResetMatrix();
        await TriggerUiCanvasRedraw();
    }

    private async Task TriggerUiCanvasRedraw()
    {
        _drawingCanvas?.Invalidate();
        await InvokeAsync(StateHasChanged);
    }

    private void HandleDarkModeChanged(object? sender, EventArgs e)
    {
        //might need to update a local variable related to the canvas?
    }

    private void UpdateDebugText()
    {
        if (_isDebugModeEnabled)
        {
            _debugText = $"Pan: {_currentPanOffset}, Zoom: {_zoomScale}";
            StateHasChanged();
        }
    }

    private async void HandleDebugModeChanged(object? sender, EventArgs e)
    {
        _isDebugModeEnabled = ThemeService.IsDebugMode;
        UpdateDebugText();
        await TriggerUiCanvasRedraw();
    }

    private void OnMouseWheel(WheelEventArgs e)
    {
        var oldZoomScale = _zoomScale;
        _zoomScale *= e.DeltaY < 0 ? 1.2f : 0.8f;

        // Calculate the change in scale
        float scaleChange = _zoomScale / oldZoomScale;

        // Adjust the pan offset to maintain the center of the view
        _currentPanOffset.X = (_currentPanOffset.X - _canvasWidth / 2) * scaleChange + _canvasWidth / 2;
        _currentPanOffset.Y = (_currentPanOffset.Y - _canvasHeight / 2) * scaleChange + _canvasHeight / 2;

        UpdateDebugText();
        TriggerUiCanvasRedraw().ConfigureAwait(false);
    }

    private void OnMouseDown(MouseEventArgs e)
    {
        _lastMousePosition = new SKPoint((float)e.ClientX, (float)e.ClientY);
    }

    private void OnMouseMove(MouseEventArgs e)
    {
        if (e.Buttons == 1) // Left mouse button
        {
            var currentMousePosition = new SKPoint((float)e.ClientX, (float)e.ClientY);
            var delta = currentMousePosition - _lastMousePosition;
            _lastMousePosition = currentMousePosition;
            _currentPanOffset += delta;

            UpdateDebugText();
            TriggerUiCanvasRedraw().ConfigureAwait(false);
        }
    }

    private async Task<bool> LoadPrimesFromLocal()
    {
        var jsonPrimes = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "primes");
        if (!string.IsNullOrEmpty(jsonPrimes))
        {
            _primes = JsonSerializer.Deserialize<List<int>>(jsonPrimes);
            return true;
        }
        return false;
    }

    private void SavePrimesToLocal()
    {
        var jsonPrimes = JsonSerializer.Serialize(_primes);
        JSRuntime.InvokeVoidAsync("localStorage.setItem", "primes", jsonPrimes);
    }

    private class Size
    {
        public float Width { get; set; }
        public float Height { get; set; }
    }

    public void Dispose()
    {
        ThemeService.OnDarkModeChanged -= HandleDarkModeChanged;
        ThemeService.OnDebugModeChanged -= HandleDebugModeChanged;
    }
}