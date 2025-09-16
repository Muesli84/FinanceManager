using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FinanceManager.Shared.Extensions
{
    public static class ExceptionExt
    {
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
