namespace FinanceManager.Web.Infrastructure.Logging;

public static class FileLoggingBuilderExtensions
{
    public static ILoggingBuilder AddFile(this ILoggingBuilder builder)
    {
        builder.Services.AddSingleton<ILoggerProvider, FileLoggerProvider>();
        return builder;
    }
}