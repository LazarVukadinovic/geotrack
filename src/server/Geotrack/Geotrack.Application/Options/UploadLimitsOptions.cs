namespace Geotrack.Application.Options;

public static class UploadLimitDefaults
{
    public const long ImageMaxFileSizeBytes = 8L * 1024L * 1024L;
    public const int ContributionMaxImageFilesPerRequest = 10;
    public const long ContributionMaxTotalRequestBytes = 90L * 1024L * 1024L;

    public const int RegionalMaxImageFilesPerRequest = 12;
    public const long RegionalMaxTotalRequestBytes = 96L * 1024L * 1024L;

    public const long ProofDocumentMaxFileSizeBytes = 10L * 1024L * 1024L;
    public const int ProofDocumentMaxFilesPerRequest = 1;

    public const long UserAvatarMaxFileSizeBytes = 5L * 1024L * 1024L;
    public const long AdminAvatarMaxFileSizeBytes = 2L * 1024L * 1024L;

    public const long GlobalMaxRequestBodyBytes = RegionalMaxTotalRequestBytes;
    public const long GlobalMultipartBodyLengthLimitBytes = ProofDocumentMaxFileSizeBytes;
}

public sealed class UploadLimitsOptions
{
    public UploadImageLimitsOptions Images { get; set; } = new();
    public UploadProofDocumentLimitsOptions ProofDocuments { get; set; } = new();
    public UploadAvatarLimitsOptions UserAvatars { get; set; } = new()
    {
        MaxFileSizeBytes = UploadLimitDefaults.UserAvatarMaxFileSizeBytes
    };
    public UploadAvatarLimitsOptions AdminAvatars { get; set; } = new()
    {
        MaxFileSizeBytes = UploadLimitDefaults.AdminAvatarMaxFileSizeBytes
    };

    public long GlobalMaxRequestBodyBytes { get; set; } = UploadLimitDefaults.GlobalMaxRequestBodyBytes;
    public long GlobalMultipartBodyLengthLimitBytes { get; set; } = UploadLimitDefaults.GlobalMultipartBodyLengthLimitBytes;
}

public sealed class UploadImageLimitsOptions
{
    public long MaxFileSizeBytes { get; set; } = UploadLimitDefaults.ImageMaxFileSizeBytes;
    public int MaxContributionFilesPerRequest { get; set; } = UploadLimitDefaults.ContributionMaxImageFilesPerRequest;
    public int MaxRegionalFilesPerRequest { get; set; } = UploadLimitDefaults.RegionalMaxImageFilesPerRequest;
    public long MaxContributionRequestBytes { get; set; } = UploadLimitDefaults.ContributionMaxTotalRequestBytes;
    public long MaxRegionalRequestBytes { get; set; } = UploadLimitDefaults.RegionalMaxTotalRequestBytes;

    public string[] ContributionAllowedContentTypes { get; set; } =
    [
        "image/png",
        "image/jpeg",
        "image/jpg",
        "image/gif",
        "image/webp"
    ];

    public string[] ContributionAllowedExtensions { get; set; } =
    [
        ".png",
        ".jpg",
        ".jpeg",
        ".gif",
        ".webp"
    ];

    public string[] RegionalAllowedContentTypes { get; set; } =
    [
        "image/png",
        "image/jpeg",
        "image/jpg",
        "image/webp"
    ];

    public string[] RegionalAllowedExtensions { get; set; } =
    [
        ".png",
        ".jpg",
        ".jpeg",
        ".webp"
    ];
}

public sealed class UploadProofDocumentLimitsOptions
{
    public long MaxFileSizeBytes { get; set; } = UploadLimitDefaults.ProofDocumentMaxFileSizeBytes;
    public int MaxFilesPerRequest { get; set; } = UploadLimitDefaults.ProofDocumentMaxFilesPerRequest;

    public string[] AllowedContentTypes { get; set; } =
    [
        "application/pdf",
        "image/png",
        "image/jpeg",
        "image/jpg",
        "image/webp"
    ];

    public string[] AllowedExtensions { get; set; } =
    [
        ".pdf",
        ".png",
        ".jpg",
        ".jpeg",
        ".webp"
    ];
}

public sealed class UploadAvatarLimitsOptions
{
    public long MaxFileSizeBytes { get; set; }

    public string[] AllowedContentTypes { get; set; } =
    [
        "image/png",
        "image/jpeg",
        "image/jpg",
        "image/webp"
    ];

    public string[] AllowedExtensions { get; set; } =
    [
        ".png",
        ".jpg",
        ".jpeg",
        ".webp"
    ];
}
