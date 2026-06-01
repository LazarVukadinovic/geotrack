using Geotrack.Application.Options;

namespace Geotrack.Server.Services;

public sealed class UploadValidationException : InvalidOperationException
{
    public UploadValidationException(int statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

public static class UploadValidation
{
    private const int MaxSignatureBytes = 12;
    private const string ContributionImageTypeMessage = "Only PNG, JPG, GIF, or WEBP images are supported.";
    private const string RegionalImageTypeMessage = "Only PNG, JPG/JPEG, or WEBP images are supported.";
    private const string AvatarImageTypeMessage = "Only PNG, JPG, JPEG, or WEBP images are supported.";
    private const string ProofDocumentTypeMessage = "Ownership proof document must be a PDF, PNG, JPG/JPEG, or WEBP file.";

    public static async Task ValidateContributionUploadAsync(
        IFormFile? cardImage,
        IFormFile? coverImage,
        IEnumerable<IFormFile>? galleryImages,
        IFormFile? ownershipProofDocument,
        UploadLimitsOptions limits,
        CancellationToken cancellationToken = default)
    {
        var images = CollectFiles(cardImage, coverImage);
        images.AddRange(CollectFiles(galleryImages));

        EnsureFileCount(
            images,
            limits.Images.MaxContributionFilesPerRequest,
            $"A maximum of {limits.Images.MaxContributionFilesPerRequest} contribution images can be uploaded at once.");

        var allFiles = new List<IFormFile>(images);
        if (IsPresent(ownershipProofDocument))
        {
            allFiles.Add(ownershipProofDocument!);
        }

        EnsureTotalLength(
            allFiles,
            limits.Images.MaxContributionRequestBytes,
            "Contribution upload request is too large.");

        foreach (var image in images)
        {
            await ValidateImageFileAsync(
                image,
                limits.Images.MaxFileSizeBytes,
                limits.Images.ContributionAllowedContentTypes,
                limits.Images.ContributionAllowedExtensions,
                allowGif: true,
                $"Contribution image must be {FormatBytes(limits.Images.MaxFileSizeBytes)} or smaller.",
                ContributionImageTypeMessage,
                cancellationToken);
        }

        if (IsPresent(ownershipProofDocument))
        {
            await ValidateOwnershipProofAsync(ownershipProofDocument!, limits.ProofDocuments, cancellationToken);
        }
    }

    public static async Task ValidateRegionalImageUploadAsync(
        IEnumerable<IFormFile>? files,
        UploadLimitsOptions limits,
        CancellationToken cancellationToken = default)
    {
        var images = CollectFiles(files);

        EnsureFileCount(
            images,
            limits.Images.MaxRegionalFilesPerRequest,
            $"A maximum of {limits.Images.MaxRegionalFilesPerRequest} images can be uploaded at once.");

        EnsureTotalLength(
            images,
            limits.Images.MaxRegionalRequestBytes,
            "Regional image upload request is too large.");

        foreach (var image in images)
        {
            await ValidateImageFileAsync(
                image,
                limits.Images.MaxFileSizeBytes,
                limits.Images.RegionalAllowedContentTypes,
                limits.Images.RegionalAllowedExtensions,
                allowGif: false,
                $"Regional image must be {FormatBytes(limits.Images.MaxFileSizeBytes)} or smaller.",
                RegionalImageTypeMessage,
                cancellationToken);
        }
    }

    public static Task ValidateUserAvatarAsync(
        IFormFile avatar,
        UploadLimitsOptions limits,
        CancellationToken cancellationToken = default) =>
        ValidateAvatarAsync(
            avatar,
            limits.UserAvatars,
            $"Profile image must be {FormatBytes(limits.UserAvatars.MaxFileSizeBytes)} or smaller.",
            cancellationToken);

    public static Task ValidateAdminAvatarAsync(
        IFormFile avatar,
        UploadLimitsOptions limits,
        CancellationToken cancellationToken = default) =>
        ValidateAvatarAsync(
            avatar,
            limits.AdminAvatars,
            $"Profile image must be {FormatBytes(limits.AdminAvatars.MaxFileSizeBytes)} or smaller.",
            cancellationToken);

    public static async Task<byte[]?> ReadFileBytesAsync(
        IFormFile? file,
        CancellationToken cancellationToken = default)
    {
        if (!IsPresent(file))
        {
            return null;
        }

        await using var stream = file!.OpenReadStream();
        using var memory = new MemoryStream(file.Length <= int.MaxValue ? (int)file.Length : 0);
        await stream.CopyToAsync(memory, cancellationToken);
        return memory.ToArray();
    }

    public static async Task<List<byte[]>> ReadFileBytesAsync(
        IEnumerable<IFormFile>? files,
        CancellationToken cancellationToken = default)
    {
        var result = new List<byte[]>();
        foreach (var file in CollectFiles(files))
        {
            var bytes = await ReadFileBytesAsync(file, cancellationToken);
            if (bytes is { Length: > 0 })
            {
                result.Add(bytes);
            }
        }

        return result;
    }

    private static async Task ValidateAvatarAsync(
        IFormFile avatar,
        UploadAvatarLimitsOptions limits,
        string sizeMessage,
        CancellationToken cancellationToken)
    {
        await ValidateImageFileAsync(
            avatar,
            limits.MaxFileSizeBytes,
            limits.AllowedContentTypes,
            limits.AllowedExtensions,
            allowGif: false,
            sizeMessage,
            AvatarImageTypeMessage,
            cancellationToken);
    }

    private static async Task ValidateOwnershipProofAsync(
        IFormFile file,
        UploadProofDocumentLimitsOptions limits,
        CancellationToken cancellationToken)
    {
        if (file.Length > limits.MaxFileSizeBytes)
        {
            throw PayloadTooLarge($"Ownership proof document must be {FormatBytes(limits.MaxFileSizeBytes)} or smaller.");
        }

        EnsureAllowedDeclaredType(
            file,
            limits.AllowedContentTypes,
            limits.AllowedExtensions,
            ProofDocumentTypeMessage);

        var detected = await DetectUploadFileAsync(file, allowPdf: true, allowGif: false, cancellationToken);
        if (!MatchesDeclaredFile(file, detected) ||
            !IsAllowedDetectedType(detected, limits.AllowedContentTypes, limits.AllowedExtensions))
        {
            throw BadRequest(ProofDocumentTypeMessage);
        }
    }

    private static async Task ValidateImageFileAsync(
        IFormFile file,
        long maxFileSizeBytes,
        IEnumerable<string> allowedContentTypes,
        IEnumerable<string> allowedExtensions,
        bool allowGif,
        string sizeMessage,
        string typeMessage,
        CancellationToken cancellationToken)
    {
        if (file.Length > maxFileSizeBytes)
        {
            throw PayloadTooLarge(sizeMessage);
        }

        EnsureAllowedDeclaredType(file, allowedContentTypes, allowedExtensions, typeMessage);

        var detected = await DetectUploadFileAsync(file, allowPdf: false, allowGif, cancellationToken);
        if (!MatchesDeclaredFile(file, detected) ||
            !IsAllowedDetectedType(detected, allowedContentTypes, allowedExtensions))
        {
            throw BadRequest(typeMessage);
        }
    }

    private static void EnsureFileCount(
        IReadOnlyCollection<IFormFile> files,
        int maxFiles,
        string message)
    {
        if (files.Count > maxFiles)
        {
            throw BadRequest(message);
        }
    }

    private static void EnsureTotalLength(
        IReadOnlyCollection<IFormFile> files,
        long maxTotalBytes,
        string message)
    {
        var total = files.Sum(file => file.Length);
        if (total > maxTotalBytes)
        {
            throw PayloadTooLarge(message);
        }
    }

    private static void EnsureAllowedDeclaredType(
        IFormFile file,
        IEnumerable<string> allowedContentTypes,
        IEnumerable<string> allowedExtensions,
        string message)
    {
        var extension = NormalizeExtension(Path.GetExtension(file.FileName));
        var contentType = NormalizeContentType(file.ContentType);

        if (string.IsNullOrWhiteSpace(extension) ||
            string.IsNullOrWhiteSpace(contentType) ||
            !ContainsOrdinalIgnoreCase(allowedExtensions, extension) ||
            !ContainsOrdinalIgnoreCase(allowedContentTypes, contentType))
        {
            throw BadRequest(message);
        }
    }

    private static async Task<DetectedUploadFile?> DetectUploadFileAsync(
        IFormFile file,
        bool allowPdf,
        bool allowGif,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxSignatureBytes];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
        return DetectUploadFile(buffer.AsSpan(0, read), allowPdf, allowGif);
    }

    private static DetectedUploadFile? DetectUploadFile(
        ReadOnlySpan<byte> fileBytes,
        bool allowPdf,
        bool allowGif)
    {
        if (allowPdf &&
            fileBytes.Length >= 5 &&
            fileBytes[0] == 0x25 &&
            fileBytes[1] == 0x50 &&
            fileBytes[2] == 0x44 &&
            fileBytes[3] == 0x46 &&
            fileBytes[4] == 0x2D)
        {
            return DetectedUploadFile.Pdf;
        }

        if (fileBytes.Length >= 8 &&
            fileBytes[0] == 0x89 &&
            fileBytes[1] == 0x50 &&
            fileBytes[2] == 0x4E &&
            fileBytes[3] == 0x47 &&
            fileBytes[4] == 0x0D &&
            fileBytes[5] == 0x0A &&
            fileBytes[6] == 0x1A &&
            fileBytes[7] == 0x0A)
        {
            return DetectedUploadFile.Png;
        }

        if (fileBytes.Length >= 3 &&
            fileBytes[0] == 0xFF &&
            fileBytes[1] == 0xD8 &&
            fileBytes[2] == 0xFF)
        {
            return DetectedUploadFile.Jpeg;
        }

        if (allowGif &&
            fileBytes.Length >= 6 &&
            fileBytes[0] == 0x47 &&
            fileBytes[1] == 0x49 &&
            fileBytes[2] == 0x46 &&
            fileBytes[3] == 0x38 &&
            (fileBytes[4] == 0x37 || fileBytes[4] == 0x39) &&
            fileBytes[5] == 0x61)
        {
            return DetectedUploadFile.Gif;
        }

        if (fileBytes.Length >= 12 &&
            fileBytes[0] == 0x52 &&
            fileBytes[1] == 0x49 &&
            fileBytes[2] == 0x46 &&
            fileBytes[3] == 0x46 &&
            fileBytes[8] == 0x57 &&
            fileBytes[9] == 0x45 &&
            fileBytes[10] == 0x42 &&
            fileBytes[11] == 0x50)
        {
            return DetectedUploadFile.Webp;
        }

        return null;
    }

    private static bool MatchesDeclaredFile(IFormFile file, DetectedUploadFile? detected)
    {
        if (detected is null)
        {
            return false;
        }

        var extension = NormalizeExtension(Path.GetExtension(file.FileName));
        var contentType = NormalizeContentType(file.ContentType);

        return ContainsOrdinalIgnoreCase(detected.Extensions, extension) &&
            ContainsOrdinalIgnoreCase(detected.ContentTypes, contentType);
    }

    private static bool IsAllowedDetectedType(
        DetectedUploadFile? detected,
        IEnumerable<string> allowedContentTypes,
        IEnumerable<string> allowedExtensions) =>
        detected is not null &&
        detected.Extensions.Any(extension => ContainsOrdinalIgnoreCase(allowedExtensions, extension)) &&
        detected.ContentTypes.Any(contentType => ContainsOrdinalIgnoreCase(allowedContentTypes, contentType));

    private static List<IFormFile> CollectFiles(params IFormFile?[] files) =>
        files
            .Where(IsPresent)
            .Select(file => file!)
            .ToList();

    private static List<IFormFile> CollectFiles(IEnumerable<IFormFile>? files) =>
        files?
            .Where(IsPresent)
            .ToList()
        ?? new List<IFormFile>();

    private static bool IsPresent(IFormFile? file) => file is { Length: > 0 };

    private static string NormalizeExtension(string? extension) =>
        extension?.Trim().ToLowerInvariant() ?? string.Empty;

    private static string NormalizeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return string.Empty;
        }

        return contentType.Split(';', 2)[0].Trim().ToLowerInvariant();
    }

    private static bool ContainsOrdinalIgnoreCase(IEnumerable<string> values, string value) =>
        values.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));

    private static string FormatBytes(long bytes)
    {
        const long oneMegabyte = 1024L * 1024L;
        if (bytes % oneMegabyte == 0)
        {
            return $"{bytes / oneMegabyte} MB";
        }

        return $"{bytes} bytes";
    }

    private static UploadValidationException BadRequest(string message) =>
        new(StatusCodes.Status400BadRequest, message);

    private static UploadValidationException PayloadTooLarge(string message) =>
        new(StatusCodes.Status413PayloadTooLarge, message);

    private sealed record DetectedUploadFile(
        string[] Extensions,
        string[] ContentTypes)
    {
        public static readonly DetectedUploadFile Pdf = new(
            [".pdf"],
            ["application/pdf"]);

        public static readonly DetectedUploadFile Png = new(
            [".png"],
            ["image/png"]);

        public static readonly DetectedUploadFile Jpeg = new(
            [".jpg", ".jpeg"],
            ["image/jpeg", "image/jpg"]);

        public static readonly DetectedUploadFile Gif = new(
            [".gif"],
            ["image/gif"]);

        public static readonly DetectedUploadFile Webp = new(
            [".webp"],
            ["image/webp"]);
    }
}
