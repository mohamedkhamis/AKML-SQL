using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using AkmlSql.Core.Config;
using Xunit;

namespace AkmlSql.Core.Tests
{
    /// <summary>
    /// Spec 036 (US2, FR-008, T024) — <see cref="ApiKeyProtector"/> is the promoted Core home of
    /// the engine's DPAPI wrap/unwrap for AI API keys. The entropy MUST stay
    /// SHA-256(UTF8("AkmlSql-ApiKey-v1")) byte-for-byte: changing it makes every key stored by an
    /// earlier build unreadable. Reads accept legacy plaintext (no migration step).
    /// </summary>
    public class ApiKeyProtectorTests
    {
        [Fact]
        public void Protect_ThenUnprotect_RoundTrips()
        {
            const string key = "sk-kimi-Test.Key_1234567890";

            var wrapped = ApiKeyProtector.Protect(key);

            Assert.NotEqual(key, wrapped);
            Assert.True(ApiKeyProtector.IsProtected(wrapped));
            Assert.Equal(key, ApiKeyProtector.Unprotect(wrapped));
        }

        [Fact]
        public void Protect_PrefixedWithDpapiMarker()
        {
            var wrapped = ApiKeyProtector.Protect("sk-test");

            Assert.StartsWith("dpapi:", wrapped);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Protect_NullOrEmpty_ReturnsEmpty(string? value)
            => Assert.Equal(string.Empty, ApiKeyProtector.Protect(value));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Unprotect_NullOrEmpty_ReturnsEmpty(string? value)
            => Assert.Equal(string.Empty, ApiKeyProtector.Unprotect(value));

        [Fact]
        public void Unprotect_PlaintextPassesThroughUnchanged()
        {
            // Backward compatibility: configs written before keys were wrapped hold plaintext.
            const string legacy = "sk-legacy-plaintext-key";

            Assert.False(ApiKeyProtector.IsProtected(legacy));
            Assert.Equal(legacy, ApiKeyProtector.Unprotect(legacy));
        }

        [Fact]
        public void IsProtected_OnlyForPrefixedValues()
        {
            Assert.True(ApiKeyProtector.IsProtected("dpapi:AAAA"));
            Assert.False(ApiKeyProtector.IsProtected("sk-plain"));
            Assert.False(ApiKeyProtector.IsProtected(null));
        }

        /// <summary>
        /// Guards the entropy against accidental edits: the protector's private entropy bytes must
        /// equal SHA-256 of EXACTLY this literal. If someone changes the source string in
        /// <c>ApiKeyProtector.cs</c>, this test fails before any user loses their stored keys.
        /// </summary>
        [Fact]
        public void Entropy_IsSha256Of_AkmlSqlApiKeyV1_ByteForByte()
        {
            var field = typeof(ApiKeyProtector).GetField("AppEntropy",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(field);

            var actual = (byte[])field!.GetValue(null)!;
            byte[] expected;
            using (var sha = SHA256.Create())
                expected = sha.ComputeHash(Encoding.UTF8.GetBytes("AkmlSql-ApiKey-v1"));

            Assert.Equal(expected, actual);
        }
    }
}
