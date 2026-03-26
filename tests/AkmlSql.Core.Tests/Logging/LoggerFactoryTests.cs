using System.IO;
using Xunit;
using AkmlSql.Core.Logging;

namespace AkmlSql.Core.Tests.Logging
{
    public class LoggerFactoryTests
    {
        public LoggerFactoryTests()
        {
            // Reset state before each test so Initialize() always starts fresh
            LoggerFactory.Shutdown();
        }

        [Fact]
        public void Initialize_CreatesLogsDirectory()
        {
            LoggerFactory.Initialize();

            Assert.True(Directory.Exists(Constants.LogsPath));

            LoggerFactory.Shutdown();
        }

        [Fact]
        public void Initialize_IsIdempotent_DoesNotThrow()
        {
            // Multiple Initialize calls on a fresh instance must not throw
            LoggerFactory.Initialize();
            LoggerFactory.Initialize();
            LoggerFactory.Initialize();

            LoggerFactory.Shutdown();
        }

        [Fact]
        public void Shutdown_ResetsState_AllowsReinitialize()
        {
            LoggerFactory.Initialize();
            LoggerFactory.Shutdown();

            // After shutdown, Initialize must work again
            LoggerFactory.Initialize();
            LoggerFactory.Shutdown();
        }

        [Fact]
        public void Shutdown_WithoutPriorInitialize_DoesNotThrow()
        {
            // Shutdown on a never-initialized (or already-shut-down) factory
            LoggerFactory.Shutdown();
        }
    }
}
