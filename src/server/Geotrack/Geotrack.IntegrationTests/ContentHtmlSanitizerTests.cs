using Geotrack.Infrastructure.Services;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class ContentHtmlSanitizerTests
{
    [Fact]
    public void Rich_text_sanitizer_removes_script_handlers_and_unsafe_links()
    {
        var sanitizer = new ContentHtmlSanitizer();

        var sanitized = sanitizer.SanitizeRichText(
            """
            <p onclick="alert(1)">Hello <strong>safe</strong></p>
            <script>alert(1)</script>
            <img src=x onerror="alert(1)">
            <a href="javascript:alert(1)" onmouseover="alert(1)">bad link</a>
            <ul><li>Item</li></ul>
            """);

        Assert.Contains("<strong>safe</strong>", sanitized);
        Assert.Contains("<ul><li>Item</li></ul>", sanitized);
        Assert.DoesNotContain("<script", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onmouseover", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", sanitized, StringComparison.OrdinalIgnoreCase);
    }
}
