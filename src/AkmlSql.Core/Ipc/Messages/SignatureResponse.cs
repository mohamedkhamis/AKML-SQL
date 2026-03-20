using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class SignatureResponse
    {
        [Key(0)]
        public string FunctionName { get; set; } = string.Empty;

        [Key(1)]
        public SignatureOverload[] Overloads { get; set; } = [];

        [Key(2)]
        public int ActiveOverload { get; set; }

        [Key(3)]
        public int ActiveParameter { get; set; }
    }

    [MessagePackObject]
    public class SignatureOverload
    {
        [Key(0)]
        public string Label { get; set; } = string.Empty;

        [Key(1)]
        public string Documentation { get; set; } = string.Empty;

        [Key(2)]
        public ParameterInfo[] Parameters { get; set; } = [];
    }

    [MessagePackObject]
    public class ParameterInfo
    {
        [Key(0)]
        public string Name { get; set; } = string.Empty;

        [Key(1)]
        public string Type { get; set; } = string.Empty;

        [Key(2)]
        public string Documentation { get; set; } = string.Empty;

        [Key(3)]
        public bool IsOptional { get; set; }
    }
}
