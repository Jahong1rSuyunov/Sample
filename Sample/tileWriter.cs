using System;
using System.Data.SQLite;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Threading;

public class TileWriter
{
    private static readonly HttpClient client = new HttpClient();
    private static SemaphoreSlim semaphore = new SemaphoreSlim(20); // 20 parallel yuklash
    private const int TileSize = 256;

    public TileWriter()
    {
        client.DefaultRequestHeaders.UserAgent.ParseAdd("YourAppName/1.0 (your-email@example.com)");
    }
    public async Task Run()
    {
        // GeoPackage yaratish
        string gpkgFile = "tiles.gpkg";
        if (File.Exists(gpkgFile))
        {
            File.Delete(gpkgFile);
        }

        CreateGeoPackage(gpkgFile);

        // Chilonzor district bounding box coordinates
        double minLat = 41.248; // Minimum latitude of the bounding box
        double minLon = 69.194; // Minimum longitude of the bounding box
        double maxLat = 41.319; // Maximum latitude of the bounding box
        double maxLon = 69.273; // Maximum longitude of the bounding box

        var tasks = new ConcurrentBag<Task>();

        for (int zoom = 0; zoom <= 17; zoom++)
        {
            int minTileX = LonToTileX(minLon, zoom);
            int maxTileX = LonToTileX(maxLon, zoom);
            int minTileY = LatToTileY(maxLat, zoom); // Note: maxLat here
            int maxTileY = LatToTileY(minLat, zoom); // Note: minLat here

            for (int x = minTileX; x <= maxTileX; x++)
            {
                for (int y = minTileY; y <= maxTileY; y++)
                {
                    var tileIndex = new TileIndex(x, y, zoom);
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

    private void CreateGeoPackage(string gpkgFile)
    {
        using (var connection = new SQLiteConnection($"Data Source={gpkgFile}; Version=3;"))
        {
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE gpkg_spatial_ref_sys (
                    srs_name TEXT NOT NULL,
                    srs_id INTEGER NOT NULL PRIMARY KEY,
                    organization TEXT NOT NULL,
                    organization_coordsys_id INTEGER NOT NULL,
                    definition TEXT NOT NULL,
                    description TEXT
                );

                INSERT INTO gpkg_spatial_ref_sys (srs_name, srs_id, organization, organization_coordsys_id, definition, description)
                VALUES ('WGS 84', 4326, 'EPSG', 4326, 'GEOGCS[""WGS 84"",DATUM[""WGS_1984"",SPHEROID[""WGS 84"",6378137,298.257223563]],PRIMEM[""Greenwich"",0],UNIT[""degree"",0.0174532925199433]]', 'WGS 84');

                CREATE TABLE gpkg_contents (
                    table_name TEXT NOT NULL PRIMARY KEY,
                    data_type TEXT NOT NULL,
                    identifier TEXT UNIQUE,
                    description TEXT DEFAULT '',
                    last_change DATETIME NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
                    min_x DOUBLE,
                    min_y DOUBLE,
                    max_x DOUBLE,
                    max_y DOUBLE,
                    srs_id INTEGER,
                    CONSTRAINT fk_gc_r_srs_id FOREIGN KEY (srs_id) REFERENCES gpkg_spatial_ref_sys(srs_id)
                );

                CREATE TABLE gpkg_tile_matrix_set (
                    table_name TEXT NOT NULL PRIMARY KEY,
                    srs_id INTEGER NOT NULL,
                    min_x DOUBLE NOT NULL,
                    min_y DOUBLE NOT NULL,
                    max_x DOUBLE NOT NULL,
                    max_y DOUBLE NOT NULL,
                    CONSTRAINT fk_gtms_table_name FOREIGN KEY (table_name) REFERENCES gpkg_contents(table_name),
                    CONSTRAINT fk_gtms_srs FOREIGN KEY (srs_id) REFERENCES gpkg_spatial_ref_sys(srs_id)
                );

                CREATE TABLE gpkg_tile_matrix (
                    table_name TEXT NOT NULL,
                    zoom_level INTEGER NOT NULL,
                    matrix_width INTEGER NOT NULL,
                    matrix_height INTEGER NOT NULL,
                    tile_width INTEGER NOT NULL,
                    tile_height INTEGER NOT NULL,
                    pixel_x_size DOUBLE NOT NULL,
                    pixel_y_size DOUBLE NOT NULL,
                    CONSTRAINT pk_ttm PRIMARY KEY (table_name, zoom_level),
                    CONSTRAINT fk_tmm_table_name FOREIGN KEY (table_name) REFERENCES gpkg_contents(table_name)
                );

                CREATE TABLE tiles (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    zoom_level INTEGER NOT NULL,
                    tile_column INTEGER NOT NULL,
                    tile_row INTEGER NOT NULL,
                    tile_data BLOB NOT NULL,
                    UNIQUE (zoom_level, tile_column, tile_row)
                );

                INSERT INTO gpkg_contents (table_name, data_type, identifier, description, min_x, min_y, max_x, max_y, srs_id)
                VALUES ('tiles', 'tiles', 'tiles', '', -180.0, -85.0511287798066, 180.0, 85.0511287798066, 4326);

                INSERT INTO gpkg_tile_matrix_set (table_name, srs_id, min_x, min_y, max_x, max_y)
                VALUES ('tiles', 4326, -20037508.342789244, -20037508.342789244, 20037508.342789244, 20037508.342789244);

                INSERT INTO gpkg_tile_matrix (table_name, zoom_level, matrix_width, matrix_height, tile_width, tile_height, pixel_x_size, pixel_y_size)
                VALUES ('tiles', 0, 1, 1, 256, 256, 156543.03392804097, 156543.03392804097);
            ";
            command.ExecuteNonQuery();
        }
    }

    private async Task DownloadAndSaveTileAsync(Uri uri, int zoom, int x, int y, string gpkgFile)
    {
        await semaphore.WaitAsync();
        string url = uri.ToString();
        try
        {
            byte[] tileData = await client.GetByteArrayAsync(url);

            // Tile ni GeoPackage ga yozish
            SaveTileToGeoPackage(tileData, zoom, x, y, gpkgFile);

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

    private void SaveTileToGeoPackage(byte[] tileData, int zoom, int x, int y, string gpkgFile)
    {
        using (var connection = new SQLiteConnection($"Data Source={gpkgFile}; Version=3;"))
        {
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO tiles (zoom_level, tile_column, tile_row, tile_data)
                VALUES (@zoom_level, @tile_column, @tile_row, @tile_data);
            ";

            command.Parameters.AddWithValue("@zoom_level", zoom);
            command.Parameters.AddWithValue("@tile_column", x);
            command.Parameters.AddWithValue("@tile_row", y);
            command.Parameters.AddWithValue("@tile_data", tileData);

            command.ExecuteNonQuery();
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
