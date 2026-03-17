using System.Text.Json;

namespace AkmlSql.Shell.Shared
{
    internal static class JsonOptions
    {
        public static readonly JsonSerializerOptions CamelCase = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }
}
