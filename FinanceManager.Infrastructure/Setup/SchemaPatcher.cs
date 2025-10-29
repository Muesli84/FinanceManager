using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore; // for Database facade
using Microsoft.EntityFrameworkCore.Infrastructure; // extension metadata

namespace FinanceManager.Infrastructure.Setup;

/// <summary>
/// Runtime safety patcher that ensures the new user preference columns for import split settings exist.
/// This is a fallback in case earlier migrations were partially applied or a database was created before the migration existed.
/// Only executed for SQLite provider.
/// </summary>
public static class SchemaPatcher
{
    
}
