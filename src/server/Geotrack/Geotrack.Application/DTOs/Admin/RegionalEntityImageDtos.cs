namespace Geotrack.Application.DTOs.Admin;

public class RegionalEntityImageDto
{
    public int Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? AltText { get; set; }
    public string? Caption { get; set; }
    public bool IsMain { get; set; }
    public int SortOrder { get; set; }
}

public class RegionalEntityImageInputDto
{
    public string? Url { get; set; }
    public string? AltText { get; set; }
    public string? Caption { get; set; }
    public bool IsMain { get; set; }
    public int SortOrder { get; set; }
}

public class RegionalImageUploadFileDto
{
    public string FileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public byte[] Content { get; set; } = Array.Empty<byte>();
}

public class RegionalImageUploadResultDto
{
    public string FileName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}

public class RegionalImageUploadResponseDto
{
    public List<RegionalImageUploadResultDto> Images { get; set; } = new();
}
