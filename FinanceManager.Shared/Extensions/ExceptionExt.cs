using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FinanceManager.Shared.Extensions
{
    /// <summary>
    /// Extension methods for <see cref="Exception"/>.
    /// </summary>
    public static class ExceptionExt
    {
        /// <summary>
        /// Builds a human-readable message including inner exception messages recursively.
        /// </summary>
        /// <param name="ex">The exception to render.</param>
        /// <returns>A combined message string including inner exceptions, if present.</returns>
        public static string ToMessageWithInner(this Exception ex)
        {
            if (ex.InnerException != null)
            {
                return $"{ex.Message} --> {ex.InnerException.ToMessageWithInner()}";
            }
            return ex.Message;
        }
    }
}
