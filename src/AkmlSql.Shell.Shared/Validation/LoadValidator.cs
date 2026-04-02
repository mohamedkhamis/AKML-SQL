using System;
using System.IO;
using Constants = AkmlSql.Core.Constants;
using Serilog;

namespace AkmlSql.Shell.Shared.Validation
{
    internal static class LoadValidator
    {
        public static bool Validate(string extensionDirectory)
        {
            var allValid = true;

            // Check that core DLL exists alongside the extension
            var coreDll = Path.Combine(extensionDirectory, "AkmlSql.Core.dll");
            if (!File.Exists(coreDll))
            {
                Log.Error("Missing required file: {Path}", coreDll);
                allValid = false;
            }

            // Check config directory is writable
            try
            {
                var configDir = Constants.AppDataPath;
                Directory.CreateDirectory(configDir);
                var testFile = Path.Combine(configDir, ".writetest");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
                Log.Debug("Config directory writable: {Path}", configDir);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Config directory is not writable");
                allValid = false;
            }

            // Check logs directory is writable
            try
            {
                Directory.CreateDirectory(Constants.LogsPath);
                Log.Debug("Logs directory accessible: {Path}", Constants.LogsPath);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Logs directory is not writable");
            }

            if (allValid)
            {
                Log.Information("Load validation passed for extension at {Path}", extensionDirectory);
            }
            else
            {
                Log.Error("Load validation FAILED for extension at {Path}", extensionDirectory);
            }

            return allValid;
        }
    }
}
