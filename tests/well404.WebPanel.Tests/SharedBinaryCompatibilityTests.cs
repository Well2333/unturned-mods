using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnturnedMods.Shared.WebPanel;
using Xunit;

namespace well404.WebPanel.Tests
{
    public class SharedBinaryCompatibilityTests
    {
        [Theory]
        [MemberData(nameof(Shared10Constructors))]
        public void Shared11_RetainsShared10ConstructorSignatures(Type type, Type[] parameterTypes)
            => Assert.NotNull(type.GetConstructor(parameterTypes));

        public static IEnumerable<object[]> Shared10Constructors()
        {
            yield return Signature(typeof(PlayerButton), typeof(string), typeof(string),
                typeof(string), typeof(string), typeof(string));
            yield return Signature(typeof(PlayerCard), typeof(string), typeof(string),
                typeof(IReadOnlyList<string>), typeof(IReadOnlyList<string>),
                typeof(IReadOnlyList<PlayerButton>), typeof(string), typeof(string),
                typeof(IReadOnlyList<PlayerCard>));
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

        private static object[] Signature(Type type, params Type[] parameterTypes)
            => new object[] { type, parameterTypes };
    }
}
