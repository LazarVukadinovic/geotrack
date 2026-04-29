using Geotrack.Application.DTOs.Moderation;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Reviews;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Geotrack.Infrastructure.Services;

public class ModerationTermService : IModerationTermService
{
    private readonly AppDbContext _db;
    private readonly IModerationTermCache _termCache;

    public ModerationTermService(AppDbContext db, IModerationTermCache termCache)
    {
        _db = db;
        _termCache = termCache;
    }

    public async Task<GetModerationTermsResponseDto> GetTermsAsync(
        string? languageCode,
        string? category,
        bool includeInactive)
    {
        var query = _db.ModerationTerms.AsNoTracking();

        var normalizedLanguageCode = NormalizeLanguageCode(languageCode);
        if (!string.IsNullOrWhiteSpace(normalizedLanguageCode))
            query = query.Where(term => term.LanguageCode == normalizedLanguageCode);

        if (!string.IsNullOrWhiteSpace(category))
        {
            var parsedCategory = ParseCategory(category);
            query = query.Where(term => term.Category == parsedCategory);
        }

        if (!includeInactive)
            query = query.Where(term => term.IsActive);

        var terms = await query
            .OrderBy(term => term.LanguageCode)
            .ThenBy(term => term.Category)
            .ThenBy(term => term.Term)
            .ToListAsync();

        return new GetModerationTermsResponseDto
        {
            Terms = terms.Select(MapTerm).ToList()
        };
    }

    public async Task<ModerationTermDto> CreateTermAsync(int adminUserId, CreateModerationTermRequestDto request)
    {
        var normalized = BuildValidatedTermInput(
            request.Term,
            request.LanguageCode,
            request.Category,
            request.MatchType,
            request.Severity);

        await EnsureNoDuplicateAsync(null, normalized);

        var now = DateTime.UtcNow;
        var term = new ModerationTerm
        {
            Term = request.Term.Trim(),
            NormalizedTerm = normalized.NormalizedTerm,
            LanguageCode = normalized.LanguageCode,
            Category = normalized.Category,
            MatchType = normalized.MatchType,
            IsActive = request.IsActive,
            Severity = normalized.Severity,
            CreatedAt = now,
            CreatedByUserId = adminUserId
        };

        await _db.ModerationTerms.AddAsync(term);
        await SaveAndInvalidateAsync();

        return MapTerm(term);
    }

    public async Task<ModerationTermDto> UpdateTermAsync(
        int adminUserId,
        int termId,
        UpdateModerationTermRequestDto request)
    {
        var term = await _db.ModerationTerms.FindAsync(termId)
            ?? throw new KeyNotFoundException("Moderation term not found.");

        var normalized = BuildValidatedTermInput(
            request.Term,
            request.LanguageCode,
            request.Category,
            request.MatchType,
            request.Severity);

        await EnsureNoDuplicateAsync(term.Id, normalized);

        term.Term = request.Term.Trim();
        term.NormalizedTerm = normalized.NormalizedTerm;
        term.LanguageCode = normalized.LanguageCode;
        term.Category = normalized.Category;
        term.MatchType = normalized.MatchType;
        term.IsActive = request.IsActive;
        term.Severity = normalized.Severity;
        term.UpdatedAt = DateTime.UtcNow;
        term.UpdatedByUserId = adminUserId;

        await SaveAndInvalidateAsync();

        return MapTerm(term);
    }

    public async Task<ModerationTermDto> SetActiveAsync(int adminUserId, int termId, bool isActive)
    {
        var term = await _db.ModerationTerms.FindAsync(termId)
            ?? throw new KeyNotFoundException("Moderation term not found.");

        term.IsActive = isActive;
        term.UpdatedAt = DateTime.UtcNow;
        term.UpdatedByUserId = adminUserId;

        await SaveAndInvalidateAsync();

        return MapTerm(term);
    }

    public async Task DeleteTermAsync(int termId)
    {
        var term = await _db.ModerationTerms.FindAsync(termId)
            ?? throw new KeyNotFoundException("Moderation term not found.");

        _db.ModerationTerms.Remove(term);
        await SaveAndInvalidateAsync();
    }

    private async Task SaveAndInvalidateAsync()
    {
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            throw new InvalidOperationException(
                "A moderation term with the same language, category, match type, and normalized value already exists.",
                ex);
        }

        _termCache.Invalidate();
    }

    private async Task EnsureNoDuplicateAsync(int? existingTermId, ValidatedTermInput input)
    {
        var duplicateExists = await _db.ModerationTerms.AnyAsync(term =>
            (!existingTermId.HasValue || term.Id != existingTermId.Value) &&
            term.LanguageCode == input.LanguageCode &&
            term.NormalizedTerm == input.NormalizedTerm &&
            term.Category == input.Category &&
            term.MatchType == input.MatchType);

        if (duplicateExists)
        {
            throw new InvalidOperationException(
                "A moderation term with the same language, category, match type, and normalized value already exists.");
        }
    }

    private static ValidatedTermInput BuildValidatedTermInput(
        string term,
        string languageCode,
        string category,
        string matchType,
        int severity)
    {
        if (string.IsNullOrWhiteSpace(term))
            throw new InvalidOperationException("Term is required.");

        var normalizedTerm = ModerationTextNormalizer.NormalizeForModeration(term);
        if (string.IsNullOrWhiteSpace(normalizedTerm))
            throw new InvalidOperationException("Term must contain letters or numbers.");

        if (term.Trim().Length > 160 || normalizedTerm.Length > 160)
            throw new InvalidOperationException("Term is too long.");

        var normalizedLanguageCode = NormalizeLanguageCode(languageCode);
        if (string.IsNullOrWhiteSpace(normalizedLanguageCode))
            throw new InvalidOperationException("Language code is required.");

        if (normalizedLanguageCode.Length > 10)
            throw new InvalidOperationException("Language code is too long.");

        if (severity is < 1 or > 5)
            throw new InvalidOperationException("Severity must be between 1 and 5.");

        return new ValidatedTermInput(
            normalizedTerm,
            normalizedLanguageCode,
            ParseCategory(category),
            ParseMatchType(matchType),
            severity);
    }

    private static string NormalizeLanguageCode(string? languageCode) =>
        (languageCode ?? string.Empty).Trim().ToLowerInvariant();

    private static ModerationTermCategory ParseCategory(string? value)
    {
        var normalized = NormalizeEnumValue(value);
        return normalized switch
        {
            "" or "profanity" => ModerationTermCategory.Profanity,
            "harassment" or "abuse" => ModerationTermCategory.Harassment,
            "slur" or "slurs" => ModerationTermCategory.Slur,
            "offensive" or "offensivecontent" => ModerationTermCategory.Offensive,
            _ when Enum.TryParse<ModerationTermCategory>(value, true, out var parsed) => parsed,
            _ => throw new InvalidOperationException("Invalid moderation term category.")
        };
    }

    private static ModerationTermMatchType ParseMatchType(string? value)
    {
        var normalized = NormalizeEnumValue(value);
        return normalized switch
        {
            "" or "exact" or "normalized" => ModerationTermMatchType.Exact,
            "prefix" or "stem" => ModerationTermMatchType.Prefix,
            _ when Enum.TryParse<ModerationTermMatchType>(value, true, out var parsed) => parsed,
            _ => throw new InvalidOperationException("Invalid moderation term match type.")
        };
    }

    private static string NormalizeEnumValue(string? value) =>
        new string((value ?? string.Empty)
            .Trim()
            .Where(char.IsLetterOrDigit)
            .ToArray())
            .ToLowerInvariant();

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { ConstraintName: "IX_ModerationTerms_UQ_NormalizedRule" };

    private static ModerationTermDto MapTerm(ModerationTerm term) => new()
    {
        Id = term.Id,
        Term = term.Term,
        NormalizedTerm = term.NormalizedTerm,
        LanguageCode = term.LanguageCode,
        Category = term.Category.ToString(),
        MatchType = term.MatchType.ToString(),
        IsActive = term.IsActive,
        Severity = term.Severity,
        CreatedAt = term.CreatedAt,
        UpdatedAt = term.UpdatedAt,
        CreatedByUserId = term.CreatedByUserId,
        UpdatedByUserId = term.UpdatedByUserId
    };

    private sealed record ValidatedTermInput(
        string NormalizedTerm,
        string LanguageCode,
        ModerationTermCategory Category,
        ModerationTermMatchType MatchType,
        int Severity);
}
