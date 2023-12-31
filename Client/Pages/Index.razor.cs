using Microsoft.AspNetCore.Components.Web;
using SkiaSharp;
using SkiaSharp.Views.Blazor;
using System.Numerics;

namespace Primal.Client.Pages;

public partial class Index
{
    private SKCanvasView? _drawingCanvas;
    public BigInteger SelectedNumber { get; set; }
    private float _zoomScale = 1.0f;
    private List<int> primes = new List<int>();
    private int maxNumber = 10000;
    private SKPoint _lastMousePosition;
    private SKPoint _currentPanOffset = new SKPoint(0, 0);

    protected override void OnInitialized()
    {
        base.OnInitialized();
        GeneratePrimesUpTo(10000);
    }

    private async void DrawNextCanvasFrame(SKPaintSurfaceEventArgs args)
    {
        var info = args.Info;
        var surface = args.Surface;
        var canvas = surface.Canvas;

        canvas.Clear(SKColors.White);

        float scale = _zoomScale * 20; // Adjust this value as needed
        int maxDrawNumber = maxNumber;

        for (int i = 1; i <= maxDrawNumber; i++)
        {
            var (x, y) = GetSpiralPosition(i, scale);
            var rect = new SKRect(x + info.Width / 2 + _currentPanOffset.X,
                y + info.Height / 2 + _currentPanOffset.Y,
                x + scale + info.Width / 2 + _currentPanOffset.X,
                y + scale + info.Height / 2 + _currentPanOffset.Y);

            var paint = new SKPaint
            {
                Color = primes.Contains(i) ? SKColors.Blue : SKColors.Gray,
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };

            canvas.DrawRect(rect, paint);

            // Draw numbers for smaller squares
            if (i < 100)
            {
                var textPaint = new SKPaint
                {
                    Color = SKColors.Black,
                    IsAntialias = true,
                    TextSize = scale / 2, // Adjust text size as needed
                    TextAlign = SKTextAlign.Center
                };

                var textBounds = new SKRect();
                textPaint.MeasureText(i.ToString(), ref textBounds);

                canvas.DrawText(i.ToString(), rect.MidX, rect.MidY - textBounds.MidY, textPaint);
            }
        }

        await TriggerUiCanvasRedraw();
    }

    private async Task TriggerUiCanvasRedraw()
    {
        _drawingCanvas?.Invalidate();
        await InvokeAsync(StateHasChanged);
    }

    private void OnMouseWheel(WheelEventArgs e)
    {
        _zoomScale *= e.DeltaY < 0 ? 1.2f : 0.8f; // Inverted zoom direction
        TriggerUiCanvasRedraw().ConfigureAwait(false);
    }

    private void OnMouseDown(MouseEventArgs e)
    {
        _lastMousePosition = new SKPoint((float)e.ClientX, (float)e.ClientY);
    }

    private void OnMouseMove(MouseEventArgs e)
    {
        if (e.Buttons == 1) // Check if the left mouse button is pressed
        {
            var currentMousePosition = new SKPoint((float)e.ClientX, (float)e.ClientY);
            var delta = currentMousePosition - _lastMousePosition;
            _lastMousePosition = currentMousePosition;
            _currentPanOffset += delta;

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