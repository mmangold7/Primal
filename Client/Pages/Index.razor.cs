using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MudBlazor;
using SkiaSharp;
using SkiaSharp.Views.Blazor;

namespace Primal.Client.Pages;

public partial class Index
{
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
    private int _maxPrimeInput = 10000;
    private bool _isLoading;

    private bool ToolsOpen { get; set; } = true;
    private string? PixelizedImageBlobLocation { get; set; }
    private string? DownloadFileName { get; set; }
    private MudDialog SaveDialog { get; set; }
    public double PixelizationPercentComplete { get; set; }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            var size = await JSRuntime.InvokeAsync<Size>("getElementSize", "canvasElementId");
            _canvasWidth = size.Width;
            _canvasHeight = size.Height;

            await DrawSpiralFromMaxValue();
        }
    }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();

        ThemeService.ToolsToggled += ToggleTools;
        ThemeService.OnDarkModeChanged += HandleDarkModeChanged;
        ThemeService.OnDebugModeChanged += HandleDebugModeChanged;
        ThemeService.SaveImageClicked += HandleSaveImage;
    }

    private async Task DrawSpiralFromMaxValue()
    {
        _isLoading = true;
        StateHasChanged();
        _spiralBitmap = await GenerateSpiralFromScratch(_maxPrimeInput);
        _spiralBitmapInfo = _spiralBitmap.Info;
        _isLoading = false;

        SetInitialZoomLevel(_canvasHeight);
        CenterSpiralOnCanvas(_canvasWidth, _canvasHeight);
        await TriggerUiCanvasRedraw();
    }

    private void ToggleTools(object? sender, EventArgs e)
    {
        ToolsOpen = !ToolsOpen;
        StateHasChanged();
    }

    private async Task<SKBitmap> GenerateSpiralFromScratch(int? maxPrimeInput = null)
    {
        var primes = GeneratePrimesUpTo(maxPrimeInput ?? _maxPrimeInput);
        return CreateSpiralBitmap(primes);
    }

    private HashSet<int> GeneratePrimesUpTo(int max)
    {
        var primes = new HashSet<int>();
        var isPrime = new bool[max + 1];
        Array.Fill(isPrime, true);
        isPrime[0] = isPrime[1] = false;

        int totalNumbers = max - 1; // Exclude 0 and 1
        int numbersProcessed = 0;

        for (var p = 2; p * p <= max; p++)
        {
            if (isPrime[p])
            {
                for (int i = p * p; i <= max; i += p)
                {
                    if (isPrime[i])
                    {
                        isPrime[i] = false;
                        numbersProcessed++;
                    }
                }

                // Update the completion percentage
                PixelizationPercentComplete = (double)numbersProcessed / totalNumbers * 100;
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

    private void CenterSpiralOnCanvas(float canvasWidth, float canvasHeight)
    {
        _currentPanOffset = new SKPoint(
            (canvasWidth - _spiralBitmapInfo.Width * _zoomScale) / 2,
            (canvasHeight - _spiralBitmapInfo.Height * _zoomScale) / 2
        );
    }

    private void SetInitialZoomLevel(float canvasHeight)
    {
        var desiredBlocksVisible = 100;
        float totalHeightOfBlocks = desiredBlocksVisible * SquareSize;
        _zoomScale = canvasHeight / totalHeightOfBlocks;
    }

    private SKBitmap CreateSpiralBitmap(HashSet<int> primes)
    {
        var filteredPrimes = primes.ToList();
        var biggestPrime = filteredPrimes.Max();
        var bitmapSideLength = CalculateBitmapSideLength(biggestPrime);
        var center = bitmapSideLength / 2;

        var spiralBitmap = new SKBitmap(bitmapSideLength, bitmapSideLength);
        using var canvas = new SKCanvas(spiralBitmap);
        canvas.Clear(SKColors.Transparent);

        var primePaint = CreateSpiralPaint(SKColors.Black);

        Parallel.ForEach(filteredPrimes, prime =>
        {
            var (x, y) = GetSpiralPosition(prime, center);
            var rect = new SKRect(x, y, x + SquareSize, y + SquareSize);
            lock (canvas) { canvas.DrawRect(rect, primePaint); }
        });

        return spiralBitmap;
    }

    private int CalculateBitmapSideLength(int biggestPrime)
    {
        var maxLayer = (int)Math.Ceiling((Math.Sqrt(biggestPrime) - 1) / 2);
        return (2 * maxLayer + 1) * SquareSize;
    }

    private static SKPaint CreateSpiralPaint(SKColor color) =>
        new() { Color = color, IsAntialias = false, Style = SKPaintStyle.Fill };

    private (float x, float y) GetSpiralPosition(int number, int center)
    {
        var (x, y) = GetSpiralCoordinates(number);
        return (x * SquareSize + center, y * SquareSize + center);
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

            PixelizedImageBlobLocation = await JSRuntime.InvokeAsync<string>(
                "createBlobUrl", imageData, "image/png", DownloadFileName);
        }
    }

    private async Task CloseDialog()
    {
        SaveDialog.Close(DialogResult.Ok(true));
        if (!string.IsNullOrEmpty(PixelizedImageBlobLocation))
        {
            await JSRuntime.InvokeVoidAsync("revokeBlobUrl", PixelizedImageBlobLocation);
            PixelizedImageBlobLocation = "";
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

    private async void HandleSaveImage(object? sender, EventArgs e)
    {
        if (_spiralBitmap == null) return;
        //var upScaledBitmap = ScaleBitmap(_spiralBitmap, upscaleFactor);
        using var image = SKImage.FromBitmap(_spiralBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var imageData = data.ToArray();
        await GenerateBlobUrl(imageData);
        SaveDialog.Show();
    }

    private void OnMouseWheel(WheelEventArgs e)
    {
        var oldZoomScale = _zoomScale;
        _zoomScale *= e.DeltaY < 0 ? 1.2f : 0.8f;

        float scaleChange = _zoomScale / oldZoomScale;

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

    private class Size
    {
        public float Width { get; set; }
        public float Height { get; set; }
    }

    public void Dispose()
    {
        ThemeService.OnDarkModeChanged -= HandleDarkModeChanged;
        ThemeService.OnDebugModeChanged -= HandleDebugModeChanged;
        ThemeService.SaveImageClicked -= HandleSaveImage;
    }


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

//private async Task<string> LoadMillionPrimesCsv()
//{
//    try
//    {
//        var filePath = "data/P-1000000.txt";
//        var csvContent = await JSRuntime.InvokeAsync<string>("loadEmbeddedCSV", filePath);
//        return csvContent;
//    }
//    catch (Exception ex)
//    {
//        Console.WriteLine($"Error loading CSV: {ex.Message}");
//        return "";
//    }
//}

//private async Task<string> Load10MillionPrimesCsv()
//{
//    try
//    {
//        var filePath = "data/50_million_primes_0.csv";
//        var csvContent = await JSRuntime.InvokeAsync<string>("loadEmbeddedCSV", filePath);
//        return csvContent;
//    }
//    catch (Exception ex)
//    {
//        Console.WriteLine($"Error loading CSV: {ex.Message}");
//        return "";
//    }
//}

//private async Task<SKBitmap> CreateNewSpiralBitmapFromBinaryFile()
//{
//    var csvContent = await JSRuntime.InvokeAsync<byte[]>("loadBinaryFile", "data/50_million_primes_try3_0.bin");

//    using (var memoryStream = new MemoryStream(csvContent))
//    using (var binaryReader = new BinaryReader(memoryStream))
//    {
//        while (memoryStream.Position < memoryStream.Length)
//        {
//            var prime = binaryReader.ReadInt32();
//            _primes.Add(prime);
//        }
//    }

//    return CreateSpiralBitmap();
//}

//private async Task<SKBitmap> LoadPreGeneratedSpiral()
//{
//    var base64String = await JSRuntime.InvokeAsync<string>("loadBitmapFile", "data/ulamSpiral-179424673.png");
//    var bitmapData = Convert.FromBase64String(base64String);
//    return SKBitmap.Decode(bitmapData);
//}

//private async Task LoadSelectedPreGeneratedSpiral()
//{
//    if (!string.IsNullOrWhiteSpace(selectedPreGeneratedSpiral))
//    {
//        _isLoading = true;
//        var base64String = await JSRuntime.InvokeAsync<string>("loadBitmapFile", selectedPreGeneratedSpiral);
//        var bitmapData = Convert.FromBase64String(base64String);
//        _spiralBitmap = SKBitmap.Decode(bitmapData);
//        _spiralBitmapInfo = _spiralBitmap.Info;

//        SetInitialZoomLevel(_canvasHeight);
//        CenterSpiralOnCanvas(_canvasWidth, _canvasHeight);

//        _isLoading = false;
//        await TriggerUiCanvasRedraw();
//    }
//}