using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Primal.Client.Services;
using SkiaSharp;
using SkiaSharp.Views.Blazor;
using System.Numerics;

namespace Primal.Client.Pages;

public partial class Index
{
    private SKCanvasView? _drawingCanvas;
    public BigInteger SelectedNumber { get; set; }
    private float _zoomScale = 1f;
    private List<int> primes = new List<int>();
    private int maxNumber = 10000;
    private SKPoint _lastMousePosition;
    private SKPoint _currentPanOffset = new SKPoint(0, 0);
    private SKBitmap _spiralBitmap;
    private bool _isDebugModeEnabled = true;
    private const float squareSize = 1; // Adjust this value based on your preference
    private string _debugText = "";
    private float _canvasWidth;
    private float _canvasHeight;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            var size = await JSRuntime.InvokeAsync<Size>("getElementSize", "canvasElementId");
            _canvasWidth = size.Width;
            _canvasHeight = size.Height;

            SetInitialZoomLevel(size.Height);
            CreateSpiralBitmap(maxNumber, _zoomScale);
            CenterSpiralOnCanvas(size.Width, size.Height);

            StateHasChanged();
        }
    }

    private class Size
    {
        public float Width { get; set; }
        public float Height { get; set; }
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        GeneratePrimesUpTo(maxNumber);
        CreateSpiralBitmap(maxNumber, _zoomScale);

        ThemeService.OnDarkModeChanged += HandleDarkModeChanged;
        ThemeService.OnDebugModeChanged += HandleDebugModeChanged;
    }
    public void Dispose()
    {
        ThemeService.OnDarkModeChanged -= HandleDarkModeChanged;
        ThemeService.OnDebugModeChanged -= HandleDebugModeChanged;
    }

    private void HandleDarkModeChanged(object? sender, EventArgs e)
    {
        //CreateSpiralBitmap(maxNumber, _zoomScale);
        //StateHasChanged(); // Update the UI
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

    private void CenterSpiralOnCanvas(float canvasWidth, float canvasHeight)
    {
        int spiralSize = CalculateRequiredBitmapSize(maxNumber, _zoomScale);
        _currentPanOffset = new SKPoint(
            (canvasWidth - spiralSize * _zoomScale) / 2,
            (canvasHeight - spiralSize * _zoomScale) / 2
        );
    }

    private void SetInitialZoomLevel(float canvasHeight)
    {
        int desiredBlocksVisible = 20; // Number of blocks to show
        float totalHeightOfBlocks = desiredBlocksVisible * squareSize; // Total height of the blocks
        _zoomScale = canvasHeight / totalHeightOfBlocks;
    }

    private int CalculateRequiredBitmapSize(int maxNumber, float scale)
    {
        // Rough estimation of the spiral size
        var maxLayer = (int)Math.Ceiling((Math.Sqrt(maxNumber) - 1) / 2);
        var size = 2 * maxLayer + 1; // Number of squares per side
        return (int)(size * scale);
    }

    private void CreateSpiralBitmap(int maxNumber, float scale)
    {
        int bitmapSize = CalculateRequiredBitmapSize(maxNumber, scale);
        _spiralBitmap = new SKBitmap(bitmapSize, bitmapSize);

        using (var canvas = new SKCanvas(_spiralBitmap))
        {
            //var backgroundColor = ThemeService.IsDarkMode 
            //    ? new SKColor(50, 51, 61, 1) 
            //    : SKColors.White;

            canvas.Clear(SKColors.Transparent);
            var primePaint = new SKPaint { Color = SKColors.Black, IsAntialias = true, Style = SKPaintStyle.Fill };
            var nonPrimePaint = new SKPaint { Color = SKColors.Transparent, IsAntialias = true, Style = SKPaintStyle.Fill };

            for (int i = 1; i <= maxNumber; i++)
            {
                var (x, y) = GetSpiralPosition(i, scale);
                var rect = new SKRect(x + bitmapSize / 2, y + bitmapSize / 2,
                    x + scale + bitmapSize / 2, y + scale + bitmapSize / 2);

                var paint = primes.Contains(i) ? primePaint : nonPrimePaint;
                canvas.DrawRect(rect, paint);
            }
        }
    }

    private async void DrawNextCanvasFrame(SKPaintSurfaceEventArgs args)
    {
        var canvas = args.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        // Create a matrix for scaling and translating
        var matrix = SKMatrix.CreateIdentity();
        SKMatrix.PostConcat(ref matrix, SKMatrix.CreateScale(_zoomScale, _zoomScale));
        SKMatrix.PostConcat(ref matrix, SKMatrix.CreateTranslation(_currentPanOffset.X, _currentPanOffset.Y));

        // Set the matrix on the canvas
        canvas.SetMatrix(matrix);

        // Draw the bitmap
        canvas.DrawBitmap(_spiralBitmap, 0, 0);

        // Reset the matrix after drawing
        canvas.ResetMatrix();

        await TriggerUiCanvasRedraw();
    }

    private async Task TriggerUiCanvasRedraw()
    {
        _drawingCanvas?.Invalidate();
        await InvokeAsync(StateHasChanged);
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

    private void OnMouseUp(MouseEventArgs e)
    {
        // You might want to handle this event if needed
    }

    private void GeneratePrimesUpTo(int max)
    {
        bool[] isPrime = new bool[max + 1];
        Array.Fill(isPrime, true);
        isPrime[0] = isPrime[1] = false;

        for (int p = 2; p <= max; p++)
        {
            if (isPrime[p])
            {
                primes.Add(p);
                for (long i = (long)p * p; i <= max; i += p)
                    isPrime[i] = false;
            }
        }
    }

    private (float x, float y) GetSpiralPosition(int number, float scale)
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

        return (x * scale, y * scale);
    }
}