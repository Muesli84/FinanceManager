using Microsoft.Extensions.Localization;
using System.Collections;
using System.Globalization;
using System.Resources;

namespace FinanceManager.Web.Services
{
    /// <summary>
    /// String localizer that reads localization values from the compiled resource set for the <c>Pages</c> resource class.
    /// This implementation implements <see cref="IStringLocalizer{TResource}"/> and resolves the resource set on every
    /// lookup against <see cref="CultureInfo.CurrentUICulture"/>, which is set per-request by the localization middleware.
    /// </summary>
    /// <remarks>
    /// The <see cref="ResourceManager"/> is shared as a singleton field because it is thread-safe and caches the
    /// individual <see cref="ResourceSet"/> instances internally. The resource set is intentionally NOT cached at
    /// construction time so that the correct culture is honoured on every request.
    /// </remarks>
    public class PagesStringLocalizer : IStringLocalizer<Pages>
    {
        // ResourceManager is thread-safe and caches ResourceSets internally — safe to keep as a static field.
        private static readonly ResourceManager _rm = new ResourceManager(typeof(FinanceManager.Web.Pages));

        /// <summary>
        /// Resolves the <see cref="ResourceSet"/> for the current request's UI culture.
        /// Must be called per-lookup (not cached) so that culture changes between requests are respected.
        /// </summary>
        /// <returns>The result.</returns>
        private static ResourceSet? GetCurrentResourceSet() =>
            _rm.GetResourceSet(CultureInfo.CurrentUICulture, createIfNotExists: true, tryParents: true);

        /// <summary>
        /// Gets the localized string for the specified name. When no resource is found the returned <see cref="LocalizedString"/>
        /// will have <see cref="LocalizedString.ResourceNotFound"/> set to <c>true</c> and the <see cref="LocalizedString.Value"/> will equal the key.
        /// </summary>
        /// <param name="name">Resource key.</param>
        /// <returns>A <see cref="LocalizedString"/> containing the resolved value or the key when not found.</returns>
        public LocalizedString this[string name]
        {
            get => GetCurrentResourceSet()?.GetObject(name) is string value
                ? new LocalizedString(name, value, false)
                : new LocalizedString(name, name, true);
        }

        /// <summary>
        /// Gets the localized and formatted string for the specified name and format arguments.
        /// When no resource format is found the returned <see cref="LocalizedString"/> will have
        /// <see cref="LocalizedString.ResourceNotFound"/> set to <c>true</c> and the <see cref="LocalizedString.Value"/> will equal the key.
        /// </summary>
        /// <param name="name">Resource key.</param>
        /// <param name="arguments">Format arguments to apply to the resource value when present.</param>
        /// <returns>A <see cref="LocalizedString"/> containing the formatted value or the key when not found.</returns>
        public LocalizedString this[string name, params object[] arguments]
        {
            get => GetCurrentResourceSet()?.GetObject(name) is string format
                ? new LocalizedString(name, string.Format(format, arguments), false)
                : new LocalizedString(name, name, true);
        }

        /// <summary>
        /// Returns all localized strings from the resource set for the current UI culture.
        /// </summary>
        /// <param name="includeParentCultures">When <c>true</c>, parent cultures are included via <see cref="ResourceManager"/> fallback.</param>
        /// <returns>An enumerable of <see cref="LocalizedString"/> instances representing available localized values.</returns>
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        {
            return GetCurrentResourceSet()?
                .Cast<DictionaryEntry>()
                .Select(de => new LocalizedString((string)de.Key, (string)de.Value!, false))
                ?? Enumerable.Empty<LocalizedString>();
        }
    }
}
