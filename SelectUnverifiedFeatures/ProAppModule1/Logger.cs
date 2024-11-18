using Serilog;

using System;

namespace Geodatastyrelsen.ArcGIS.Modules
{
    internal static class Logger
    {
        public static ILogger Current => _logger;

        private static Serilog.Core.Logger _logger;

        private const string outputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff}| [{Level:u3}] {Message:lj} {NewLine}{Exception}";

        static Logger() {
            var configuration = new LoggerConfiguration()
                 .MinimumLevel.Verbose()
                 .Enrich.WithExceptionData()
                 .Enrich.WithProperty("MachineName", Environment.MachineName);

            if (System.Diagnostics.Debugger.IsAttached) {
                configuration = configuration.WriteTo.File(
                     System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Geodatastyrelsen", "module1", "module1-developer.log"),
                     rollingInterval: RollingInterval.Infinite,
                     retainedFileCountLimit: 1,
                     shared: true,
                     outputTemplate: outputTemplate);
            }
            else {
                configuration = configuration.WriteTo.File(
                    System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Geodatastyrelsen", "module1", "module1-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 31,
                    shared: true,
                    outputTemplate: outputTemplate);
            }

            _logger = configuration.CreateLogger();
        }
    }
}
