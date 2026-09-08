using System.Text.Json;
using System.Text.RegularExpressions;
using FinanceManager.Web.Services.Help;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers;

/// <summary>
/// Controller for serving help documentation pages and content.
/// Provides static HTML files and markdown content for help documentation.
/// </summary>
[ApiController]
[Route("api/help")]
public partial class HelpController : ControllerBase
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<HelpController> _logger;
    private readonly IHelpContentRenderer _renderer;
    private readonly IHelpAssetIntegrityValidator _assetIntegrityValidator;

    /// <summary>
    /// Initializes a new instance of HelpController.
    /// </summary>
    /// <param name="env">The env.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="renderer">The renderer.</param>
    /// <param name="assetIntegrityValidator">The asset integrity validator.</param>
    public HelpController(
        IWebHostEnvironment env,
        ILogger<HelpController> logger,
        IHelpContentRenderer renderer,
        IHelpAssetIntegrityValidator assetIntegrityValidator)
    {
        _env = env;
        _logger = logger;
        _renderer = renderer;
        _assetIntegrityValidator = assetIntegrityValidator;
    }

    /// <summary>
    /// Gets a help documentation page by language and feature ID (legacy HTML endpoint).
    /// </summary>
    /// <param name="language">The language.</param>
    /// <param name="featureId">The feature id.</param>
    /// <returns>The result.</returns>
    [HttpGet("{language}/{featureId}.html")]
    [Produces("text/html")]
    public async Task<IActionResult> GetHelpPage(string language, string featureId)
    {
        try
        {
            if (!TryNormalizeLanguage(language, out var normalizedLanguage)
                || !TryNormalizeFeatureId(featureId, out var normalizedFeatureId))
            {
                return BadRequest("Invalid help request");
            }

            var filePath = Path.Combine(_env.WebRootPath, "help", normalizedLanguage, $"{normalizedFeatureId}.html");

            if (!System.IO.File.Exists(filePath))
            {
                _logger.LogWarning("Help page not found: {FilePath}", filePath);
                return NotFound("Help page not found");
            }

            if (!_assetIntegrityValidator.IsTrustedHelpFile(filePath))
            {
                _logger.LogWarning("Blocked untrusted help page: {FilePath}", filePath);
                return NotFound("Help page not found");
            }

            var content = await System.IO.File.ReadAllTextAsync(filePath);
            return Content(_renderer.SanitizeHtml(content), "text/html; charset=utf-8");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error serving help page: {Language}/{FeatureId}", language, featureId);
            return StatusCode(500, "Error retrieving help page");
        }
    }

    /// <summary>
    /// Gets the markdown content for a feature by language and feature ID.
    /// Used by the Blazor help page view component.
    /// </summary>
    /// <param name="language">The language.</param>
    /// <param name="helpPath">The help path.</param>
    /// <returns>The result.</returns>
    [HttpGet("markdown/{language}/{**helpPath}")]
    [Produces("text/html")]
    public async Task<IActionResult> GetMarkdown(string language, string helpPath)
    {
        try
        {
            if (!TryNormalizeLanguage(language, out var normalizedLanguage)
                || !HelpDocumentPathResolver.TryNormalizeHelpPath(helpPath, out var normalizedHelpPath))
            {
                return BadRequest("Invalid help request");
            }

            var docsPath = HelpDocumentPathResolver.GetHelpSourcePath(_env);

            _logger.LogInformation("Looking for markdown in: {DocsPath}", docsPath);
            _logger.LogInformation("Searching for help path: {HelpPath}, Language: {Language}", normalizedHelpPath, normalizedLanguage);

            if (!Directory.Exists(docsPath))
            {
                _logger.LogError("Docs directory not found: {DocsPath}", docsPath);
                return StatusCode(500, "Docs directory not found");
            }

            var selectedFile = HelpDocumentPathResolver.FindMarkdownFile(docsPath, normalizedLanguage, normalizedHelpPath);
            if (selectedFile is null
                || !HelpContentCatalog.TryResolveDocument(docsPath, normalizedLanguage, normalizedHelpPath, out _, out _, out _))
            {
                _logger.LogWarning("No markdown files found for: {HelpPath}", normalizedHelpPath);
                return NotFound("Documentation not found");
            }

            _logger.LogInformation("Selected file: {FileName}", Path.GetFileName(selectedFile));

            if (!System.IO.File.Exists(selectedFile))
            {
                _logger.LogError("Selected file does not exist: {FilePath}", selectedFile);
                return StatusCode(500, "Selected documentation not found");
            }

            if (!_assetIntegrityValidator.IsTrustedHelpFile(selectedFile))
            {
                _logger.LogWarning("Blocked untrusted markdown help file: {FilePath}", selectedFile);
                return NotFound("Documentation not found");
            }

            var content = await System.IO.File.ReadAllTextAsync(selectedFile, System.Text.Encoding.UTF8);
            var relativeDocumentPath = Path.GetRelativePath(docsPath, selectedFile).Replace('\\', '/');
            var html = _renderer.RenderMarkdownToHtml(content, relativeDocumentPath);

            _logger.LogInformation("Successfully loaded markdown content ({Size} bytes)", content.Length);

            return Content(html, "text/html; charset=utf-8");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error serving markdown: {Language}/{HelpPath}", language, helpPath);
            return StatusCode(500, "Error retrieving markdown");
        }
    }

    /// <summary>
    /// Gets the search index for help pages by language.
    /// </summary>
    /// <param name="language">The language.</param>
    /// <returns>The result.</returns>
    [HttpGet("search-index/{language}.json")]
    [Produces("application/json")]
    public async Task<IActionResult> GetSearchIndex(string language)
    {
        try
        {
            if (!TryNormalizeLanguage(language, out var normalizedLanguage))
            {
                return BadRequest("Invalid language parameter");
            }

            var filePath = ResolveWebRootFile("help", normalizedLanguage, "search-index.json");

            if (!System.IO.File.Exists(filePath))
            {
                _logger.LogWarning("Search index not found: {FilePath}", filePath);
                return NotFound("Search index not found");
            }

            if (!_assetIntegrityValidator.IsTrustedHelpFile(filePath))
            {
                _logger.LogWarning("Blocked untrusted search index: {FilePath}", filePath);
                return NotFound("Search index not found");
            }

            var content = await System.IO.File.ReadAllTextAsync(filePath);
            var searchIndex = ParseSearchIndex(content);
            return Ok(searchIndex);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid search index for language: {Language}", language);
            return BadRequest("Invalid search index");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error serving search index for language: {Language}", language);
            return StatusCode(500, "Error retrieving search index");
        }
    }

    private static HelpSearchIndexDto ParseSearchIndex(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("documents", out var documentsElement)
            || documentsElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Help search index must contain a documents array.");
        }

        var documents = new List<HelpSearchDocumentDto>();
        foreach (var item in documentsElement.EnumerateArray())
        {
            if (TryReadSearchDocument(item, out var searchDocument))
            {
                documents.Add(searchDocument);
            }
        }

        return new HelpSearchIndexDto(documents);
    }

    private static bool TryReadSearchDocument(JsonElement item, out HelpSearchDocumentDto document)
    {
        document = default!;

        if (!TryGetSafeString(item, "id", 64, out var id)
            || !HelpContentCatalog.TryGetTopic(id, out var topic)
            || !TryGetSafeString(item, "title", 200, out var title)
            || !TryGetSafeString(item, "excerpt", 1000, out var excerpt))
        {
            return false;
        }

        var keywords = new List<string>();
        if (item.TryGetProperty("keywords", out var keywordsElement))
        {
            if (keywordsElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var keywordElement in keywordsElement.EnumerateArray().Take(20))
            {
                if (keywordElement.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                var keyword = keywordElement.GetString();
                if (!IsSafeText(keyword, 80))
                {
                    return false;
                }

                keywords.Add(keyword!.Trim());
            }
        }

        document = new HelpSearchDocumentDto(topic.Id, title.Trim(), excerpt.Trim(), keywords);
        return true;
    }

    private static bool TryGetSafeString(JsonElement item, string propertyName, int maxLength, out string value)
    {
        value = string.Empty;
        if (!item.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return IsSafeText(value, maxLength);
    }

    private static bool IsSafeText(string? value, int maxLength)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Length <= maxLength
            && !value.Any(char.IsControl)
            && !value.Contains('<', StringComparison.Ordinal)
            && !value.Contains('>', StringComparison.Ordinal);
    }

    private static bool TryNormalizeLanguage(string? language, out string normalizedLanguage)
    {
        return HelpLanguages.TryNormalize(language, out normalizedLanguage);
    }

    private string ResolveWebRootFile(params string[] pathSegments)
    {
        var webRootPath = Path.Combine(new[] { _env.WebRootPath }.Concat(pathSegments).ToArray());
        if (System.IO.File.Exists(webRootPath))
        {
            return webRootPath;
        }

        return Path.Combine(new[] { Path.Combine(AppContext.BaseDirectory, "wwwroot") }.Concat(pathSegments).ToArray());
    }

    private static bool TryNormalizeFeatureId(string? featureId, out string normalizedFeatureId)
    {
        normalizedFeatureId = (featureId ?? string.Empty).Trim().ToLowerInvariant();
        return FeatureIdRegex().IsMatch(normalizedFeatureId);
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{0,63}$", RegexOptions.Compiled)]
    private static partial Regex FeatureIdRegex();

}
