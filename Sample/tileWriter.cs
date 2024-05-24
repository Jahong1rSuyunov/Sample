using System;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Threading;
using OSGeo.GDAL;
using OSGeo.OGR;
using MaxRev.Gdal.Core;

public class TileWriter
{
    private static readonly HttpClient client = new HttpClient();
    private static SemaphoreSlim semaphore = new SemaphoreSlim(20); // 20 parallel downloads
    private const int TileSize = 256;

    private readonly Dataset _geoPackage;
    private readonly double _minLon;
    private readonly double _minLat;
    private readonly double _maxLon;
    private readonly double _maxLat;

    public TileWriter()
    {
        client.DefaultRequestHeaders.UserAgent.ParseAdd("YourAppName/1.0 (your-email@example.com)");
        GdalBase.ConfigureAll(); // Initialize GDAL
        Gdal.AllRegister(); // Register all GDAL drivers

        _minLon = 41.248;
        _minLat = 69.194;
        _maxLon = 41.319;
        _maxLat = 69.273;
        var gpkgDriver = Gdal.GetDriverByName("GPKG");
        int maxTiles = 1 << 18; // 2^18 tile count
        int fullSize = TileSize * maxTiles;
        _geoPackage = gpkgDriver.Create("tile.gpkg", fullSize, fullSize, 4, DataType.GDT_Byte, null);
    }

    public async Task DownloadTiles()
    {
        string gpkgFile = "tiles.gpkg";
        if (File.Exists(gpkgFile))
        {
            File.Delete(gpkgFile);
        }

        CreateGeoPackage(gpkgFile);

        double minLat = 41.248; // Minimum latitude of the bounding box
        double minLon = 69.194; // Minimum longitude of the bounding box
        double maxLat = 41.319; // Maximum latitude of the bounding box
        double maxLon = 69.273; // Maximum longitude of the bounding box

        var tasks = new ConcurrentBag<Task>();

        int maxTiles = 1 << 18; // 2^18 tile count
        int fullSize = TileSize * maxTiles;
        double[] adfGeoTransform = new double[6];
        adfGeoTransform[0] = minLon;
        adfGeoTransform[1] = (maxLon - minLon) / fullSize;
        adfGeoTransform[2] = 0;
        adfGeoTransform[3] = maxLat;
        adfGeoTransform[4] = 0;
        adfGeoTransform[5] = -(maxLat - minLat) / fullSize;

        _geoPackage.SetGeoTransform(adfGeoTransform);

        for (int zoom = 0; zoom <= 17; zoom++)
        {
            int minTileX = LonToTileX(minLon, zoom);
            int maxTileX = LonToTileX(maxLon, zoom);
            int minTileY = LatToTileY(maxLat, zoom);
            int maxTileY = LatToTileY(minLat, zoom);

            for (int x = minTileX; x <= maxTileX; x++)
            {
                for (int y = minTileY; y <= maxTileY; y++)
                {
                    Uri uri = new Uri($"https://tile.openstreetmap.org/{zoom}/{x}/{y}.png");
                    if (uri != null)
                    {
                        tasks.Add(DownloadAndSaveTileAsync(uri, zoom, x, y, gpkgFile));
                    }
                }
            }
        }

        await Task.WhenAll(tasks);
        Console.WriteLine("All tiles downloaded and saved to GeoPackage");
    }

    private async Task DownloadAndSaveTileAsync(Uri uri, int zoom, int x, int y, string gpkgFile)
    {
        await semaphore.WaitAsync();
        string url = uri.ToString();
        try
        {
            byte[] tileData = await client.GetByteArrayAsync(url);
            using (var ms = new MemoryStream(tileData))
            {
                Bitmap bitmap = new Bitmap(ms);
                SaveTileToGeoPackage(bitmap, zoom, x, y, gpkgFile);
            }

            Console.WriteLine($"Downloaded and saved tile {zoom}/{x}/{y} to GeoPackage");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            Console.WriteLine($"Access forbidden for tile {zoom}/{x}/{y}: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to download or save tile {zoom}/{x}/{y}: {ex.Message}");
        }
        finally
        {
            semaphore.Release();
        }
    }

    private void SaveTileToGeoPackage(Bitmap bitmap, int zoom, int tileX, int tileY, string gpkgFile)
    {
        int _tileSize = 256;
        int pixelX = tileX * _tileSize;
        int pixelY = tileY * _tileSize;

        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                Color pixelColor = bitmap.GetPixel(x, y);
                int[] pixelValues = new int[] { pixelColor.R, pixelColor.G, pixelColor.B, pixelColor.A };

                for (int band = 1; band <= 4; band++)
                {
                    Band dstBand = _geoPackage.GetRasterBand(band);
                    dstBand.WriteRaster(pixelX + x, pixelY + y, 1, 1, new int[] { pixelValues[band - 1] }, 1, 1, 0, 0);
                    Console.WriteLine("succses");
                }
            }
        }
    }

    private void CreateGeoPackage(string gpkgFile)
    {
        var driver = Gdal.GetDriverByName("GPKG");
        if (driver == null)
        {
            throw new Exception("GPKG driver is not available.");
        }

        using (Dataset dataset = driver.Create(gpkgFile, 0, 0, 0, DataType.GDT_Unknown, null))
        {
            if (dataset == null)
            {
                throw new Exception("Failed to create GeoPackage.");
            }

            // Create the necessary tables and metadata for GeoPackage tiles
            //Layer layer = dataset.CreateLayer("tiles", null, wkbGeometryType.wkbUnknown, null);
            //if (layer == null)
            //{
            //    throw new Exception("Layer creation failed.");
            //}
            
        }
    }

    public int LonToTileX(double lon, int zoom)
    {
        return (int)((lon + 180.0) / 360.0 * Math.Pow(2.0, zoom));
    }

    public int LatToTileY(double lat, int zoom)
    {
        return (int)((1.0 - Math.Log(Math.Tan(lat * Math.PI / 180.0) + 1.0 / Math.Cos(lat * Math.PI / 180.0)) / Math.PI) / 2.0 * Math.Pow(2.0, zoom));
    }
}

public class TileIndex
{
    public int X { get; }
    public int Y { get; }
    public int Zoom { get; }

    public TileIndex(int x, int y, int zoom)
    {
        X = x;
        Y = y;
        Zoom = zoom;
    }
}
