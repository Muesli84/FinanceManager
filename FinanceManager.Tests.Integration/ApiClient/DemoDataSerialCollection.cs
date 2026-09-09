using Xunit;

namespace FinanceManager.Tests.Integration.ApiClient;

/// <summary>
/// xUnit collection used to run demo-data integration tests without parallel execution.
/// </summary>
[CollectionDefinition("DemoDataSerial", DisableParallelization = true)]
public sealed class DemoDataSerialCollection
{
}
