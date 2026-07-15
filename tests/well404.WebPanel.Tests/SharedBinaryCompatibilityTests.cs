using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnturnedMods.Shared.WebPanel;
using Xunit;

namespace well404.WebPanel.Tests
{
    public class SharedBinaryCompatibilityTests
    {
        [Theory]
        [MemberData(nameof(CompatibleConstructors))]
        public void Shared_RetainsCompatibleConstructorSignatures(Type type, Type[] parameterTypes)
            => Assert.NotNull(type.GetConstructor(parameterTypes));

        public static IEnumerable<object[]> CompatibleConstructors()
        {
            yield return Signature(typeof(PlayerButton), typeof(string), typeof(string),
                typeof(string), typeof(string), typeof(string));
            yield return Signature(typeof(PlayerCard), typeof(string), typeof(string),
                typeof(IReadOnlyList<string>), typeof(IReadOnlyList<string>),
                typeof(IReadOnlyList<PlayerButton>), typeof(string), typeof(string),
                typeof(IReadOnlyList<PlayerCard>));
            yield return Signature(typeof(PlayerCard), typeof(string), typeof(string),
                typeof(IReadOnlyList<string>), typeof(IReadOnlyList<string>),
                typeof(IReadOnlyList<PlayerButton>), typeof(string), typeof(string),
                typeof(IReadOnlyList<PlayerCard>), typeof(string), typeof(string));
            yield return Signature(typeof(PlayerCard), typeof(string), typeof(string),
                typeof(IReadOnlyList<string>), typeof(IReadOnlyList<string>),
                typeof(IReadOnlyList<PlayerButton>), typeof(string), typeof(string),
                typeof(IReadOnlyList<PlayerCard>), typeof(string), typeof(string),
                typeof(IReadOnlyDictionary<string, string>));
            yield return Signature(typeof(WebRecord), typeof(string), typeof(string),
                typeof(IReadOnlyDictionary<string, string>), typeof(IReadOnlyList<string>));
            yield return Signature(typeof(WebPanelModule), typeof(string), typeof(string),
                typeof(IReadOnlyList<WebPanelAction>), typeof(string));
            yield return Signature(typeof(WebPanelAction), typeof(string), typeof(string),
                typeof(WebActionKind), typeof(Func<WebActionRequest, Task<WebActionResult>>),
                typeof(IReadOnlyList<WebField>), typeof(string),
                typeof(Func<Task<IReadOnlyDictionary<string, string>>>),
                typeof(Func<Task<IReadOnlyList<WebRecord>>>),
                typeof(Func<WebActionRequest, Task<WebActionResult>>), typeof(string),
                typeof(string), typeof(bool), typeof(IReadOnlyList<string>));
        }

        [Fact]
        public void PlayerCards_SerializeStableMetadataForCustomUi()
        {
            var card = new PlayerCard(
                "6", "Military Magazine", null, null, null, "Vault", "#6", null, null, null,
                new Dictionary<string, string>
                {
                    ["rarity"] = "rare",
                    ["count"] = "3"
                });
            var builder = new StringBuilder();
            var serializer = typeof(WebPanelHttpServer).GetMethod(
                "AppendPlayerCards", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(serializer);
            serializer!.Invoke(null, new object[] { builder, new[] { card } });

            Assert.Contains("\"metadata\":{\"count\":\"3\",\"rarity\":\"rare\"}", builder.ToString());
        }

        private static object[] Signature(Type type, params Type[] parameterTypes)
            => new object[] { type, parameterTypes };
    }
}
