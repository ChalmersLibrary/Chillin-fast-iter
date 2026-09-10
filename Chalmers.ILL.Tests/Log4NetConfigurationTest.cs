using System;
using System.IO;
using log4net;
using log4net.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chalmers.ILL.Tests
{
    // log4net.config used to reference Umbraco.Core.Logging.AsynchronousRollingFileAppender (a type
    // in a package that's since been removed), and nothing called XmlConfigurator.Configure() after
    // Umbraco's boot sequence stopped doing it. Both meant every log4net.LogManager.GetLogger(...)
    // call in the app silently wrote nowhere. This test configures log4net from the real config file
    // into an isolated repository (so it can't interfere with other tests) and proves a log line
    // actually reaches disk.
    [TestClass]
    public class Log4NetConfigurationTest
    {
        [TestMethod]
        public void XmlConfigurator_ConfiguresFromLog4NetConfig_WritesLogFile()
        {
            var configFile = new FileInfo(FindLog4NetConfigFile());
            Assert.IsTrue(configFile.Exists, "Config/log4net.config not found at " + configFile.FullName);

            var logFilePath = Path.Combine(Environment.CurrentDirectory, "App_Data", "Logs", "ChillinTraceLog.txt");
            if (File.Exists(logFilePath))
                File.Delete(logFilePath);

            var repository = LogManager.CreateRepository(Guid.NewGuid().ToString());
            XmlConfigurator.Configure(repository, configFile);

            var message = "Log4NetConfigurationTest characterization message " + Guid.NewGuid();
            var log = LogManager.GetLogger(repository.Name, typeof(Log4NetConfigurationTest));
            log.Info(message);

            Assert.IsTrue(File.Exists(logFilePath), "Expected log4net to create a log file at " + logFilePath);
            var contents = File.ReadAllText(logFilePath);
            Assert.IsTrue(contents.Contains(message), "Expected the log file to contain the logged message.");

            File.Delete(logFilePath);
        }

        private static string FindLog4NetConfigFile()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Chalmers.ILL", "Config", "log4net.config")))
            {
                dir = dir.Parent;
            }
            if (dir == null)
                throw new FileNotFoundException("Could not locate Chalmers.ILL/Config/log4net.config by walking up from " + AppDomain.CurrentDomain.BaseDirectory);
            return Path.Combine(dir.FullName, "Chalmers.ILL", "Config", "log4net.config");
        }
    }
}
