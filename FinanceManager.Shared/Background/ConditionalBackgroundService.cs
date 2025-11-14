using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FinanceManager.Shared.Background
{
    /// <summary>
    /// Base class for background workers that can be disabled via configuration.
    /// Configuration key defaults to "BackgroundTasks:Enabled" and defaults to true when missing.
    /// </summary>
    public abstract class ConditionalBackgroundService : BackgroundService
    {
        private readonly IConfiguration _config;
        private readonly ILogger _logger;
        private readonly string _configKey;

        protected bool Enabled { get; }

        protected ConditionalBackgroundService(IConfiguration config, ILogger<ConditionalBackgroundService> logger, string configKey = "BackgroundTasks:Enabled")
        {
            _config = config;
            _logger = logger;
            _configKey = configKey;
            Enabled = bool.TryParse(_config[_configKey], out var v) ? v : true;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!Enabled)
            {
                _logger.LogInformation("Background worker '{Worker}' is disabled via configuration key '{Key}'.", GetType().Name, _configKey);
                return;
            }

            await RunAsync(stoppingToken);
        }

        /// <summary>
        /// Implement worker logic here instead of overriding ExecuteAsync.
        /// </summary>
        protected abstract Task RunAsync(CancellationToken stoppingToken);
    }
}
