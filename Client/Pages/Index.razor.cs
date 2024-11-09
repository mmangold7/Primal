using BlazorPanzoom;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Utilities;
using Primal.Client.Models;
using SkiaSharp;
using SkiaSharp.Views.Blazor;

namespace Primal.Client.Pages;

public partial class Index
{
    private bool _isDebugModeEnabled;
    private bool _isLoading;
    private int _squareSize;
    private int _maxPrimeInput = 10000;
    private string _debugText = "";

    //private Panzoom _panzoom = new Panzoom();
    private SKCanvasView? _drawingCanvas;
    private SKBitmap? _spiralBitmap;
    private SKImageInfo _spiralBitmapInfo;
    private CancellationTokenSource _generationCancel = new();

    private MudColor _primeColor = "#000000";
    private MudColor _compositeBackgroundColor = "#FFFFFF00";
    private MudColor _spiralLineColor = "#00FF00";
    private MudColor _gridLinesColor = "#000000";
    private MudColor _numbersTextColor = "#00CC00";

    private bool ToolsOpen { get; set; } = true;
    public bool ShowPrimesEnabled { get; set; } = true;
    public bool ShowSpiralLineEnabled { get; set; }
    public bool ShowTextLabelsEnabled { get; set; }
    public bool ShowSquareLatticeEnabled { get; set; } = true;
    public double SpiralGenerationPercentComplete { get; set; }
    private string? ImageDownloadBlobLocation { get; set; }
    private string? DownloadFileName { get; set; }
    private MudDialog? SaveDialog { get; set; }
    public float AngularScale { get; set; } = 1;

    private float _canvasWidth;
    private float _canvasHeight;
    private float _zoomScale = 1f;
    private SKPoint _lastMousePosition;
    private SKPoint _lastTouchPosition;
    private SKPoint _currentPanOffset = new(0, 0);
    private bool _usePolar = true;

    private void CenterSpiralInCanvas(float canvasWidth, float canvasHeight)
    {
        _currentPanOffset = new SKPoint(
            (canvasWidth - _spiralBitmapInfo.Width * _zoomScale) / 2,
            (canvasHeight - _spiralBitmapInfo.Height * _zoomScale) / 2
        );
    }

    private void SetInitialCanvasZoom(float canvasHeight)
    {
        //var desiredBlocksVisible = 50;
        //float totalHeightOfBlocks = desiredBlocksVisible * _squareSize;
        //_zoomScale = canvasHeight / totalHeightOfBlocks;
        _zoomScale = 10f / _squareSize;
    }

    private void OnMouseWheel(WheelEventArgs e)
    {
        var amount = e.DeltaY < 0 ? 1.2f : 0.8f;
        Zoom(amount);
    }

    private void Zoom(float amount)
    {
        var oldZoomScale = _zoomScale;
        _zoomScale *= amount;

        var scaleChange = _zoomScale / oldZoomScale;

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

    private void OnTouchStart(TouchEventArgs e)
    {
        if (e.Touches.Length > 0)
        {
            var touch = e.Touches[0];
            _lastTouchPosition = new SKPoint((float)touch.ClientX, (float)touch.ClientY);
        }
    }

    private void OnTouchMove(TouchEventArgs e)
    {
        if (e.Touches.Length > 0)
        {
            var touch = e.Touches[0];
            var currentTouchPosition = new SKPoint((float)touch.ClientX, (float)touch.ClientY);
            var delta = currentTouchPosition - _lastTouchPosition;
            _lastTouchPosition = currentTouchPosition;
            _currentPanOffset += delta;

            UpdateDebugText();
            TriggerUiCanvasRedraw().ConfigureAwait(false);
        }
    }

    private async Task TriggerUiCanvasRedraw()
    {
        _drawingCanvas?.Invalidate();
        await InvokeAsync(StateHasChanged);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            var size = await JSRuntime.InvokeAsync<HtmlElementSize>("getElementSize", "canvasElementId");
            _canvasWidth = size.Width;
            _canvasHeight = size.Height;
            await ReGenerate();
        }
    }

    private async Task ReGenerate()
    {
        CancelSpiralGeneration();
        _generationCancel = new CancellationTokenSource();
        var cancellationToken = _generationCancel.Token;

        _isLoading = true;
        await Task.Run(() => GenerateSpiral(cancellationToken), cancellationToken);
        SetInitialCanvasZoom(_canvasHeight);
        CenterSpiralInCanvas(_canvasWidth, _canvasHeight);
        _isLoading = false;

        await TriggerUiCanvasRedraw();
    }

    private void GenerateSpiral(CancellationToken cancellationToken)
    {
        var primes = GeneratePrimesUpTo(_maxPrimeInput, cancellationToken);

        _spiralBitmap = _usePolar 
            ? CreateLabeledPolarSpiralBitmap(primes, cancellationToken) 
            : CreateLabeledSquareLatticeSpiralBitmap(primes, cancellationToken);

        _spiralBitmapInfo = _spiralBitmap.Info;
    }

    private void CancelSpiralGeneration()
    {
        if (_generationCancel != null)
        {
            _generationCancel.Cancel();
            _generationCancel.Dispose();
        }
    }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();

        ThemeService.ToolsToggled += ToggleSpiralTools;
        ThemeService.ZoomInPressed += ZoomIn;
        ThemeService.ZoomOutPressed += ZoomOut;
    }

    private void ZoomOut(object? sender, EventArgs e)
    {
        Zoom(.8f);
    }

    private void ZoomIn(object? sender, EventArgs e)
    {
        Zoom(1.2f);
    }

    private HashSet<int> GeneratePrimesUpTo(int max, CancellationToken cancellationToken)
    {
        //Sieve of Eratosthenes
        var primes = new HashSet<int>();
        var isPrime = new bool[max + 1];
        Array.Fill(isPrime, true);
        isPrime[0] = isPrime[1] = false;

        for (var p = 2; p * p <= max; p++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (isPrime[p])
            {
                for (int i = p * p; i <= max; i += p)
                {
                    isPrime[i] = false;
                }
            }
        }

        for (var p = 2; p <= max; p++)
        {
            if (isPrime[p])
            {
                primes.Add(p);
            }
        }

        return primes;
    }

    private SKBitmap CreateLabeledPolarSpiralBitmap(HashSet<int> primes, CancellationToken cancellationToken)
    {
        _squareSize = 1;
        if (ShowSquareLatticeEnabled || ShowSpiralLineEnabled)
        {
            _squareSize = 10;
        }
        if (ShowTextLabelsEnabled)
        {
            _squareSize = 100;
        }

        var filteredPrimes = primes.ToList();
        var biggestPrime = filteredPrimes.Max();
        var bitmapSideLength = CalculateBitmapSideLengthForPolarSpiral(biggestPrime);
        var center = bitmapSideLength / 2.0f;

        var spiralBitmap = new SKBitmap(bitmapSideLength, bitmapSideLength);
        using var canvas = new SKCanvas(spiralBitmap);
        canvas.Clear(FromMud(_compositeBackgroundColor));

        var primePaint = CreateSpiralPaint(FromMud(_primeColor));
        var linePaint = new SKPaint { Color = FromMud(_spiralLineColor), StrokeWidth = _squareSize / 5.0f, IsAntialias = false, StrokeCap = SKStrokeCap.Square };
        var gridPaint = new SKPaint { Color = FromMud(_gridLinesColor), IsStroke = false, StrokeWidth = _squareSize / 10.0f, IsAntialias = false };
        var textPaint = new SKPaint { Color = FromMud(_numbersTextColor), TextSize = _squareSize / 3.0f, TextAlign = SKTextAlign.Center, IsAntialias = true };

        SKPoint? lastPoint = null;

        //grid lines
        if (ShowSquareLatticeEnabled)
        {
            //// Draw each rectangle with rotation
            //for (int i = 1; i <= biggestPrime; i++)
            //{
            //    cancellationToken.ThrowIfCancellationRequested();

            //    var (x, y, angle) = GetPolarSpiralPositionWithAngle(i, center);
            //    var rect = new SKRect(x - _squareSize / 2, y - _squareSize / 2, x + _squareSize / 2, y + _squareSize / 2);

            //    // Save the current state of the canvas
            //    canvas.Save();

            //    // Rotate the canvas around the center of the rectangle
            //    canvas.RotateDegrees((float)angle, x, y);

            //    // Draw the rectangle (now rotated)
            //    lock (canvas)
            //    {
            //        canvas.DrawRect(rect, gridPaint);
            //    }

            //    // Restore the canvas to its original state
            //    canvas.Restore();
            //}
            for (int i = 1; i <= biggestPrime; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var (x, y, angle) = GetPolarSpiralPositionWithAngle(i, center);

                // Draw the rectangle (now rotated)
                lock (canvas)
                {
                    canvas.DrawCircle(x, y, _squareSize / 2.0f, gridPaint);
                }
            }
        }

        //spiral line
        if (ShowSpiralLineEnabled)
        {
            for (int i = 1; i <= biggestPrime; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var (x, y, angle) = GetPolarSpiralPositionWithAngle(i, center);
                var rect = new SKRect(x, y, x + _squareSize, y + _squareSize);
                var centerPoint = new SKPoint(rect.MidX, rect.MidY);

                if (lastPoint.HasValue)
                {
                    lock (canvas)
                    {
                        canvas.DrawLine(lastPoint.Value, centerPoint, linePaint);
                    }
                }
                lastPoint = centerPoint;
            }
        }

        //prime squares
        if (ShowPrimesEnabled)
        {
            //foreach (var prime in filteredPrimes)
            //{
            //    cancellationToken.ThrowIfCancellationRequested();
            //    var (x, y, angle) = GetPolarSpiralPositionWithAngle(prime, center);
            //    var rect = new SKRect(x - _squareSize / 2, y - _squareSize / 2, x + _squareSize / 2, y + _squareSize / 2);

            //    // Save the current state of the canvas
            //    canvas.Save();

            //    // Rotate the canvas around the center of the rectangle
            //    canvas.RotateDegrees(angle, x, y);

            //    // Draw the rectangle (now rotated)
            //    lock (canvas)
            //    {
            //        canvas.DrawRect(rect, primePaint);
            //    }

            //    // Restore the canvas to its original state
            //    canvas.Restore();
            //}
            foreach (var prime in filteredPrimes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (x, y, angle) = GetPolarSpiralPositionWithAngle(prime, center);

                lock (canvas)
                {
                    canvas.DrawCircle(x, y, _squareSize / 2.0f, primePaint);
                }
            }
        }

        //text labels
        if (ShowTextLabelsEnabled)
        {
            for (int i = 1; i <= biggestPrime; i++)
            {
                lock (canvas)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var (x, y, angle) = GetPolarSpiralPositionWithAngle(i, center);
                    var rect = new SKRect(x, y, x + _squareSize, y + _squareSize);
                    var text = i.ToString();
                    var textBounds = new SKRect();
                    textPaint.MeasureText(text, ref textBounds);
                    canvas.DrawText(text, rect.MidX, rect.MidY - textBounds.MidY, textPaint);
                }
            }
        }

        return spiralBitmap;
    }

    private (float x, float y, float angle) GetPolarSpiralPositionWithAngle(int number, float center)
    {
        //double radius = Math.Sqrt(number / Math.PI) * _squareSize;
        //double angle = number * _squareSize / radius * 2;
        double radius = Math.Sqrt(number) * _squareSize;
        double angle = 2 * Math.PI * radius;

        // Convert polar coordinates (r, θ) to Cartesian coordinates (x, y)
        float x = (float)(center + radius * Math.Cos(angle));
        float y = (float)(center + radius * Math.Sin(angle));

        // Calculate the angle of rotation for the rectangle
        float rotationAngle = (float)(angle * 180 / Math.PI) - 90;

        return (x, y, rotationAngle);
    }

    private SKBitmap CreateLabeledSquareLatticeSpiralBitmap(HashSet<int> primes, CancellationToken cancellationToken)
    {
        _squareSize = 1;
        if (ShowSquareLatticeEnabled || ShowSpiralLineEnabled)
        {
            _squareSize = 10;
        }
        if (ShowTextLabelsEnabled)
        {
            _squareSize = 100;
        }

        var filteredPrimes = primes.ToList();
        var biggestPrime = filteredPrimes.Max();
        var bitmapSideLength = CalculateBitmapSideLength(biggestPrime);
        var center = bitmapSideLength / 2.0f;

        var spiralBitmap = new SKBitmap(bitmapSideLength, bitmapSideLength);
        using var canvas = new SKCanvas(spiralBitmap);
        canvas.Clear(FromMud(_compositeBackgroundColor));

        var primePaint = CreateSpiralPaint(FromMud(_primeColor));
        var linePaint = new SKPaint { Color = FromMud(_spiralLineColor), StrokeWidth = _squareSize / 5.0f, IsAntialias = false, StrokeCap = SKStrokeCap.Square};
        var gridPaint = new SKPaint { Color = FromMud(_gridLinesColor), IsStroke = true, StrokeWidth = _squareSize / 10.0f, IsAntialias = false };
        var textPaint = new SKPaint { Color = FromMud(_numbersTextColor), TextSize = _squareSize / 3.0f, TextAlign = SKTextAlign.Center, IsAntialias = true};

        SKPoint? lastPoint = null;

        //grid lines
        if (ShowSquareLatticeEnabled)
        {
            for (int i = 1; i <= biggestPrime; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (x, y) = GetSpiralPosition(i, center);
                var rect = new SKRect(x, y, x + _squareSize, y + _squareSize);
                lock (canvas)
                {
                    canvas.DrawRect(rect, gridPaint);
                }
            }
        }

        //spiral line
        if (ShowSpiralLineEnabled)
        {
            for (int i = 1; i <= biggestPrime; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var (x, y) = GetSpiralPosition(i, center);
                var rect = new SKRect(x, y, x + _squareSize, y + _squareSize);
                var centerPoint = new SKPoint(rect.MidX, rect.MidY);

                if (lastPoint.HasValue)
                {
                    lock (canvas)
                    {
                        canvas.DrawLine(lastPoint.Value, centerPoint, linePaint);
                    }
                }
                lastPoint = centerPoint;
            }
        }

        //prime squares
        if (ShowPrimesEnabled)
        {
            foreach (var prime in filteredPrimes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (x, y) = GetSpiralPosition(prime, center);
                var rect = new SKRect(x, y, x + _squareSize, y + _squareSize);

                lock (canvas)
                {
                    canvas.DrawRect(rect, primePaint);
                }
            }
        }

        //text labels
        if (ShowTextLabelsEnabled)
        {
            for (int i = 1; i <= biggestPrime; i++)
            {
                lock (canvas)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var (x, y) = GetSpiralPosition(i, center);
                    var rect = new SKRect(x, y, x + _squareSize, y + _squareSize);
                    var text = i.ToString();
                    var textBounds = new SKRect();
                    textPaint.MeasureText(text, ref textBounds);
                    canvas.DrawText(text, rect.MidX, rect.MidY - textBounds.MidY, textPaint);
                }
            }
        }

        return spiralBitmap;
    }

    private int CalculateBitmapSideLength(int biggestPrime)
    {
        var maxLayer = (int)Math.Ceiling((Math.Sqrt(biggestPrime) - 1) / 2);
        return (2 * maxLayer) * _squareSize;
    }

    private int CalculateBitmapSideLengthForPolarSpiral(int biggestPrime)
    {
        //return (int)Math.Ceiling(Math.Sqrt(biggestPrime / Math.PI) * _squareSize * 2);
        return (int)Math.Ceiling(Math.Sqrt(biggestPrime) * _squareSize * 2);
    }

    private static SKPaint CreateSpiralPaint(SKColor color) =>
        new() { Color = color, IsAntialias = false, Style = SKPaintStyle.Fill };

    private (float x, float y) GetSpiralPosition(int number, float center)
    {
        var (x, y) = GetSpiralCoordinates(number);
        return (x * _squareSize + center, y * _squareSize + center);
    }

    private (float x, float y) GetSpiralCoordinates(int number)
    {
        if (number == 1) return (0, 0);

        var layer = (int)Math.Ceiling((Math.Sqrt(number) - 1) / 2);
        var maxLayerNumber = (2 * layer + 1) * (2 * layer + 1);
        var sideLength = 2 * layer;
        var stepsFromMax = maxLayerNumber - number;

        var side = stepsFromMax / sideLength;
        var positionOnSide = stepsFromMax % sideLength;

        return side switch
        {
            0 => (layer - positionOnSide, layer),         // Top
            1 => (-layer, layer - positionOnSide),        // Left
            2 => (-layer + positionOnSide, -layer),       // Bottom
            3 => (layer, -layer + positionOnSide),        // Right
            _ => throw new InvalidOperationException()
        };
    }

    private async Task GenerateBlobUrl(byte[]? imageData)
    {
        if (imageData != null)
        {
            DownloadFileName = "ulamSpiral.png";

            ImageDownloadBlobLocation = await JSRuntime.InvokeAsync<string>(
                "createBlobUrl", imageData, "image/png", DownloadFileName);
        }
    }

    private async Task CloseDialog()
    {
        SaveDialog.Close(DialogResult.Ok(true));
        if (!string.IsNullOrEmpty(ImageDownloadBlobLocation))
        {
            await JSRuntime.InvokeVoidAsync("revokeBlobUrl", ImageDownloadBlobLocation);
            ImageDownloadBlobLocation = "";
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

        if (_spiralBitmap != null)
            canvas.DrawBitmap(_spiralBitmap, 0, 0);

        canvas.ResetMatrix();

        await TriggerUiCanvasRedraw();
    }

    private async void SaveImage()
    {
        if (_spiralBitmap == null) return;

        using var image = SKImage.FromBitmap(_spiralBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        var imageData = data.ToArray();
        await GenerateBlobUrl(imageData);

        SaveDialog.Show();
    }

    private void ToggleSpiralTools(object? sender, EventArgs e)
    {
        ToolsOpen = !ToolsOpen;
        StateHasChanged();
    }

    private void UpdatePrimeColor(string colorValue)
    {
        _primeColor = new MudColor(colorValue);
        StateHasChanged();
    }

    private void UpdateBackgroundColor(string colorValue)
    {
        _compositeBackgroundColor = new MudColor(colorValue);
        StateHasChanged();
    }

    private void UpdateSpiralLineColor(string colorValue)
    {
        _spiralLineColor = new MudColor(colorValue);
        StateHasChanged();
    }

    private void UpdateGridLinesColor(string colorValue)
    {
        _gridLinesColor = new MudColor(colorValue);
        StateHasChanged();
    }

    private void UpdateNumbersTextColor(string colorValue)
    {
        _numbersTextColor = new MudColor(colorValue);
        StateHasChanged();
    }

    private async void ToggleDebug()
    {
        _isDebugModeEnabled = !_isDebugModeEnabled;
        UpdateDebugText();
        await TriggerUiCanvasRedraw();
    }

    private void UpdateDebugText()
    {
        if (_isDebugModeEnabled)
        {
            _debugText = $"Pan: {_currentPanOffset}, Zoom: {_zoomScale}";
            StateHasChanged();
        }
    }

    public void Dispose()
    {
        ThemeService.ToolsToggled -= ToggleSpiralTools;
        ThemeService.ZoomInPressed -= ZoomIn;
        ThemeService.ZoomOutPressed -= ZoomOut;
    }

    private static SKColor FromMud(MudColor color) => new(color.R, color.G, color.B, color.A);
}

//private int upscaleFactor = 1;
//public SKBitmap ScaleBitmap(SKBitmap originalBitmap, int scaleFactor)
//{
//    int newWidth = originalBitmap.Width * scaleFactor;
//    int newHeight = originalBitmap.Height * scaleFactor;

//    var scaledBitmap = new SKBitmap(newWidth, newHeight);

//    using (var canvas = new SKCanvas(scaledBitmap))
//    {
//        canvas.Clear(SKColors.Transparent);
//        var paint = new SKPaint
//        {
//            FilterQuality = SKFilterQuality.High
//        };

//        canvas.DrawBitmap(originalBitmap, new SKRect(0, 0, newWidth, newHeight), paint);
//    }

//    return scaledBitmap;
//}

//private SKBitmap CreateSpiralBitmap(HashSet<int> primes, CancellationToken cancellationToken)
//{
//    var filteredPrimes = primes.ToList();
//    var biggestPrime = filteredPrimes.Max();
//    var bitmapSideLength = CalculateBitmapSideLength(biggestPrime);
//    var center = bitmapSideLength / 2;

//    var spiralBitmap = new SKBitmap(bitmapSideLength, bitmapSideLength);
//    using var canvas = new SKCanvas(spiralBitmap);
//    canvas.Clear(SKColors.Transparent);

//    var primeColor = FromMud(_primeColor);
//    var primePaint = CreateSpiralPaint(primeColor);

//    Parallel.ForEach(filteredPrimes, new ParallelOptions { CancellationToken = cancellationToken }, prime =>
//    {
//        var (x, y) = GetSpiralPosition(prime, center);
//        var rect = new SKRect(x, y, x + SquareSize, y + SquareSize);
//        lock (canvas)
//        {
//            canvas.DrawRect(rect, primePaint);
//        }
//    });

//    return spiralBitmap;
//}