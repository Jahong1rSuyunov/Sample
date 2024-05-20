﻿using BruTile;
using BruTile.Predefined;
using BruTile.Web;
using System;
using System.Threading;


namespace Sample
{
    public class tileWriter
    {

        public async Task Run()
        {
            // Chilonzor district bounding box coordinates
            double minLat = 41.248; // Minimum latitude of the bounding box
            double minLon = 69.194; // Minimum longitude of the bounding box
            double maxLat = 41.319; // Maximum latitude of the bounding box
            double maxLon = 69.273; // Maximum longitude of the bounding box

            var osmTileSource = new HttpTileSource(new GlobalSphericalMercator(), "https://tile.openstreetmap.org/{z}/{x}/{y}.png");

            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("YourAppName/1.0 (your-email@example.com)");

            for (int zoom = 0; zoom <= 17; zoom++)
            {
                int minTileX = LonToTileX(minLon, zoom);
                int maxTileX = LonToTileX(maxLon, zoom);
                int minTileY = LatToTileY(maxLat, zoom); // Note: maxLat here
                int maxTileY = LatToTileY(minLat, zoom); // Note: minLat here

                for (int x = minTileX; x <= maxTileX; x = x + 2)
                {
                    for (int y = minTileY; y <= maxTileY; y = y + 2)
                    {
                        var tileIndex1 = new TileIndex(x, y, zoom);
                        var tileIndex2 = new TileIndex(x, y, zoom);
                        Uri uri1 = osmTileSource.GetUri(new TileInfo { Index = tileIndex1 });
                        Uri uri2 = osmTileSource.GetUri(new TileInfo { Index = tileIndex2 });

                        if (uri1 != null && uri2 is not null)
                        {
                            Thread first = new Thread(() =>
                            {
                                DownloadTile(uri1, zoom, x, y);
                            });
                            Thread second = new Thread(() =>
                            {
                                DownloadTile(uri2, zoom, x + 1, y + 1);
                            });
                            first.Start();
                            second.Start();

                            first.Join();
                            second.Join();
                        }
                    }
                }
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

        private async void DownloadTile(Uri uri, int zoom, int x, int y)
        {
            string url = uri.ToString();

            using (HttpClient client = new HttpClient())
            {

                client.DefaultRequestHeaders.UserAgent.ParseAdd("YourAppName/1.0 (your-email@example.com)");
                try
                {
                    byte[] tileData = await client.GetByteArrayAsync(url);

                    string directoryPath = Path.Combine("tiles", zoom.ToString(), x.ToString());
                    Directory.CreateDirectory(directoryPath);

                    string fileName = Path.Combine(directoryPath, $"{y}.png");
                    await File.WriteAllBytesAsync(fileName, tileData);

                    Console.WriteLine($"Downloaded {fileName}");
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    Console.WriteLine($"Access forbidden for tile {zoom}/{x}/{y}: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to download tile {zoom}/{x}/{y}: {ex.Message}");
                }
            }
        }
    }
}
