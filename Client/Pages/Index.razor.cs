using CsvHelper.Configuration.Attributes;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MudBlazor;
using SkiaSharp;
using SkiaSharp.Views.Blazor;

namespace Primal.Client.Pages;

public partial class Index
{
    private readonly int _maxPrimeForBitmap = 4000000;
    private HashSet<int> _primes = new();
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

    private string? PixelizedImageBlobLocation { get; set; }
    private string? DownloadFileName { get; set; }
    private MudDialog SaveDialog { get; set; }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            var size = await JSRuntime.InvokeAsync<Size>("getElementSize", "canvasElementId");
            _canvasWidth = size.Width;
            _canvasHeight = size.Height;

            StateHasChanged();
        }
    }

    private async Task<string> LoadMillionPrimesCsv()
    {
        try
        {
            var filePath = "data/P-1000000.txt";
            var csvContent = await JSRuntime.InvokeAsync<string>("loadEmbeddedCSV", filePath);
            return csvContent;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading CSV: {ex.Message}");
            return "";
        }
    }

    private async Task<string> Load10MillionPrimesCsv()
    {
        try
        {
            var filePath = "data/50_million_primes_0.csv";
            var csvContent = await JSRuntime.InvokeAsync<string>("loadEmbeddedCSV", filePath);
            return csvContent;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading CSV: {ex.Message}");
            return "";
        }
    }

    private async Task<byte[]> Load10MillionPrimesBinary()
    {
        try
        {
            var filePath = "data/50_million_primes_try3_0.bin";
            return await JSRuntime.InvokeAsync<byte[]>("loadBinaryFile", filePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading binary file: {ex.Message}");
            return Array.Empty<byte>();
        }
    }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();

        //var csvContent = await LoadMillionPrimesCsv();
        //using (var reader = new StringReader(csvContent))
        //using (var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        //{
        //    HasHeaderRecord = false
        //}))
        //{
        //    await foreach (var record in csv.GetRecordsAsync<PrimeCsvRecord>())
        //    {
        //        _primes.Add(record.Value);
        //    }
        //}

        //var csvContent = await Load10MillionPrimesCsv();
        //var primeStrings = csvContent.Split(',');

        _csvContent = await Load10MillionPrimesBinary();

        using (var memoryStream = new MemoryStream(_csvContent))
        using (var binaryReader = new BinaryReader(memoryStream))
        {
            while (memoryStream.Position < memoryStream.Length)
            {
                var prime = binaryReader.ReadInt32();
                _primes.Add(prime);
            }
        }

        // Assuming that each integer is 4 bytes (32 bits)

        // Copy bytes into integers
        //MemoryMarshal.Cast<byte, int>(new ReadOnlySpan<byte>(csvContent)).CopyTo(primes);


        //GeneratePrimesUpTo(_maxNumber);

        //var loaded = await LoadPrimesFromLocal();
        //if (!loaded)
        //{
        //    GeneratePrimesUpTo(_maxNumber);
        //    SavePrimesToLocal();
        //}

        ThemeService.OnDarkModeChanged += HandleDarkModeChanged;
        ThemeService.OnDebugModeChanged += HandleDebugModeChanged;
        ThemeService.SaveImageClicked += HandleSaveImage;

        CreateSpiralBitmap();
        SetInitialZoomLevel(_canvasHeight);
        CenterSpiralOnCanvas(_canvasWidth, _canvasHeight);
        StateHasChanged();
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

    private void CreateSpiralBitmap()
    {
        //var integers = MemoryMarshal.Cast<byte, int>(_csvContent);
        //var array = integers.ToArray();
        //_primes = new HashSet<int>(array);

        var biggestPrime = _primes.Max();
        if (biggestPrime > _maxPrimeForBitmap)
            biggestPrime = _maxPrimeForBitmap;
        var maxLayer = (int)Math.Ceiling((Math.Sqrt(biggestPrime) - 1) / 2);
        var squaresPerSide = 2 * maxLayer + 1;
        var bitmapSideLength = squaresPerSide * SquareSize;
        var center = bitmapSideLength / 2;

        _spiralBitmap = new SKBitmap(bitmapSideLength, bitmapSideLength);
        _spiralBitmapInfo = _spiralBitmap.Info;
        using var canvas = new SKCanvas(_spiralBitmap);
        canvas.Clear(SKColors.Transparent);
        var primePaint = new SKPaint { Color = SKColors.Black, IsAntialias = false, Style = SKPaintStyle.Fill };
        var nonPrimePaint = new SKPaint { Color = SKColors.Transparent, IsAntialias = false, Style = SKPaintStyle.Fill };

        Parallel.For(1, biggestPrime + 1, i =>
        {
            var (x, y) = GetSpiralPosition(i);
            var rect = new SKRect(x + center, y + center, x + SquareSize + center, y + SquareSize + center);
            var rectPaint = _primes.Contains(i) ? primePaint : nonPrimePaint;

            lock (canvas)
            {
                canvas.DrawRect(rect, rectPaint);
            }
        });

        //for (var i = 1; i <= biggestPrime; i++)
        //{
        //    var (x, y) = GetSpiralPosition(i);
        //    var rect = new SKRect(x + center, y + center, x + SquareSize + center, y + SquareSize + center);
        //    var rectPaint = _primes.Contains(i) ? primePaint : nonPrimePaint; // should be fast since _primes is a HashSet, I think?
        //    canvas.DrawRect(rect, rectPaint);
        //}
    }

    //var smallerMax = _maxPrimeForBitmap < biggestPrime ? _maxPrimeForBitmap : biggestPrime;

    private int upscaleFactor = 1;
    private byte[] _csvContent;

    private async Task GenerateBlobUrl(byte[]? imageData)
    {
        if (imageData != null)
        {
            DownloadFileName = "ulamSpiral-" + _primes.Last() + ".png";

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

    public SKBitmap ScaleBitmap(SKBitmap originalBitmap, int scaleFactor)
    {
        // Calculate the new dimensions
        int newWidth = originalBitmap.Width * scaleFactor;
        int newHeight = originalBitmap.Height * scaleFactor;

        // Create a new bitmap with the larger size
        var scaledBitmap = new SKBitmap(newWidth, newHeight);

        // Create a canvas for the new bitmap and set the nearest-neighbor scaling
        using (var canvas = new SKCanvas(scaledBitmap))
        {
            canvas.Clear(SKColors.Transparent); // Or any background color you want
            var paint = new SKPaint
            {
                FilterQuality = SKFilterQuality.High // Nearest-neighbor scaling
            };

            // Draw the original bitmap onto the canvas with scaling
            canvas.DrawBitmap(originalBitmap, new SKRect(0, 0, newWidth, newHeight), paint);
        }

        return scaledBitmap;
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

    //private async Task<bool> LoadPrimesFromLocal()
    //{
    //    var jsonPrimes = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "primes");
    //    if (!string.IsNullOrEmpty(jsonPrimes))
    //    {
    //        _primes = JsonSerializer.Deserialize<List<int>>(jsonPrimes);
    //        return (_primes.Last() >= _maxNumber);
    //    }
    //    return false;
    //}

    //private void SavePrimesToLocal()
    //{
    //    var jsonPrimes = JsonSerializer.Serialize(_primes);
    //    JSRuntime.InvokeVoidAsync("localStorage.setItem", "primes", jsonPrimes);
    //}

    private class Size
    {
        public float Width { get; set; }
        public float Height { get; set; }
    }

    public class PrimeCsvRecord
    {
        [Index(0)]
        public int Ordinality { get; set; }

        [Index(1)]
        public int Value { get; set; }

        [Index(2)]
        public int Difference { get; set; }
    }

    public void Dispose()
    {
        ThemeService.OnDarkModeChanged -= HandleDarkModeChanged;
        ThemeService.OnDebugModeChanged -= HandleDebugModeChanged;
        ThemeService.SaveImageClicked -= HandleSaveImage;
    }
}