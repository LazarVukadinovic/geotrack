namespace Geotrack.Application.Options;

public class BoundaryImportOptions
{
    public string Adm0GeoJsonPath { get; set; } = "Data/Boundaries/geoBoundariesCGAZ_ADM0.geojson";
    public string Adm1GeoJsonPath { get; set; } = "Data/Boundaries/geoBoundariesCGAZ_ADM1.geojson";
    public string Adm2GeoJsonPath { get; set; } = "Data/Boundaries/geoBoundariesCGAZ_ADM2.geojson";
    public int MaxSummaryItems { get; set; } = 100;
    public int SaveBatchSize { get; set; } = 10;
    public int ProgressLogInterval { get; set; } = 100;
}
