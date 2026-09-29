#nullable enable
using AkmlSql.Shell.Shared.History;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (HIS) — SQL History records a run only for Query.Execute itself. Matching the command
    /// group GUID alone recorded every command in SSMS's SQL editor group as an execution (found by
    /// the quickstart run on 2026-09-28: opening menus added history rows).
    /// </summary>
    public sealed class ExecutionCommandMatchTests
    {
        private const string SqlEditorGroup = "{52692960-56BC-4989-B5D3-94C47A513E8D}";

        [Fact]
        public void The_same_group_and_id_is_the_command()
            => Assert.True(ExecutionCapture.IsSameCommand(SqlEditorGroup, 0x0100, SqlEditorGroup, 0x0100));

        [Fact]
        public void Another_command_in_the_same_group_is_not()
            => Assert.False(ExecutionCapture.IsSameCommand(SqlEditorGroup, 0x0101, SqlEditorGroup, 0x0100));

        [Fact]
        public void The_group_guid_compares_without_case()
            => Assert.True(ExecutionCapture.IsSameCommand(SqlEditorGroup.ToLowerInvariant(), 7, SqlEditorGroup, 7));

        [Fact]
        public void Another_group_is_not()
            => Assert.False(ExecutionCapture.IsSameCommand("{00000000-0000-0000-0000-000000000000}", 7, SqlEditorGroup, 7));
    }
}
