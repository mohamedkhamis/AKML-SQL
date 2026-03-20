using System;
using System.IO;
using System.Text.Json;
using Constants = AkmlSql.Core.Constants;
using AkmlSql.Core.Update;
using Serilog;

namespace AkmlSql.Shell.Shared.Update
{
    internal static class UpdateNotifier
    {
        private static readonly UpdateResult NoUpdate = new UpdateResult { Available = false };

        public static UpdateResult CheckForPendingUpdate()
        {
            try
            {
                var resultPath = Constants.UpdateResultFilePath;
                if (!File.Exists(resultPath))
                    return NoUpdate;

                var json = File.ReadAllText(resultPath);
                var result = JsonSerializer.Deserialize<UpdateResult>(json, JsonOptions.CamelCase);

                if (result != null && result.Available)
                {
                    Log.Information("Update available: v{Version}", result.Version);
                    return result;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to read update result");
            }

            return NoUpdate;
        }
    }
}
