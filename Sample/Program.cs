using MaxRev.Gdal.Core;
using OSGeo.OGR;




// Initialize GDAL
GdalBase.ConfigureAll();
Ogr.RegisterAll();



var tileWriter = new TileWriter();
await tileWriter.Run();


//// Your GeoJSON data
//string geoJson = @"{
//                      ""type"": ""FeatureCollection"",
//                      ""features"": [
//                        {
//                          ""type"": ""Feature"",
//                          ""geometry"": {
//                            ""type"": ""Point"",
//                            ""coordinates"": [
//                              125.6,
//                              10.1
//                            ]
//                          },
//                          ""properties"": {
//                            ""name"": ""Dinagat Islands""
//                          }
//                        }
//                      ]
//                    }";

//// Parse GeoJSON
//var json = JObject.Parse(geoJson);
//var features = json["features"];

//// Correct the file path for the GeoPackage
//string geoPackagePath = "your_geopackage.gpkg";
//var driver = Ogr.GetDriverByName("GPKG");
//var ds = driver.Open(geoPackagePath, 1);

//// Define spatial reference
//SpatialReference sr = new SpatialReference("");
//sr.ImportFromEPSG(4326);  // WGS 84

//// Create a layer
//Layer layer = ds.CreateLayer("geojson_layer23", sr, wkbGeometryType.wkbPoint, null);

//// Loop through each feature
//foreach (var featureJson in features)
//{
//    // Create feature
//    FeatureDefn defn = layer.GetLayerDefn();
//    Feature feature = new Feature(defn);

//    // Manually create geometry from JSON
//    var geomJson = featureJson["geometry"];
//    Geometry geom = new Geometry(wkbGeometryType.wkbPoint);
//    double x = (double)geomJson["coordinates"][0];
//    double y = (double)geomJson["coordinates"][1];
//    geom.AddPoint(x, y, 0); // Provide 0 for z-coordinate

//    // Set geometry to feature
//    feature.SetGeometry(geom);

//    // Set properties (attributes)
//    //var properties = featureJson["properties"];
//    //if (properties["name"] != null)
//    //{
//    //    feature.SetField("name", properties["name"].ToString());
//    //}
//    feature.SetField("name", "Dinagat Islands");

//    // Add feature to layer
//    layer.CreateFeature(feature);

//    feature.Dispose();
//    geom.Dispose();
//}

//// Clean up
//ds.Dispose();