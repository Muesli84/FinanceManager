using System;
using System.Reflection;

namespace FinanceManager.Tests.TestHelpers;

internal static class TestEntityHelper
{
    public static void SetEntityId(object entity, Guid id)
    {
        if (entity == null) throw new ArgumentNullException(nameof(entity));
        var prop = entity.GetType().GetProperty("Id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop == null) throw new InvalidOperationException($"Type {entity.GetType().FullName} does not have an 'Id' property.");
        prop.SetValue(entity, id);
    }
}
