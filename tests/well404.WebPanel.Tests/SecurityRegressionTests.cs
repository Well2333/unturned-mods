using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using well404.WebPanel;
using Xunit;

namespace well404.WebPanel.Tests
{
    public sealed class SecurityRegressionTests
    {
        [Fact]
        public void PlayerMarkdownLinks_ValidateProtocolAndEscapeAttributes()
        {
            var html = ReadPlayerHtml();

            Assert.Contains("const url = new URL(", html);
            Assert.Contains("url.protocol !== \"http:\" && url.protocol !== \"https:\"", html);
            Assert.Contains("rel=\"noopener noreferrer\"", html);
            Assert.Contains("/[&<>\"']/g", html);
            Assert.DoesNotContain("rel=\"noopener\">$1</a>", html);
        }

        [Fact]
        public void PlayerLanguageStore_QuotesNewlinesAndQuotesWithoutYamlInjection()
        {
            WithTemporaryDirectory(directory =>
            {
                const string language = "zh\"\nforged: true";
                var store = new PlayerLanguageStore(directory);
                store.Set("76561198000000000", language);

                var yaml = File.ReadAllText(Path.Combine(directory, "player-languages.yaml"));
                Assert.Contains("zh\\\"\\nforged: true", yaml);
                Assert.DoesNotContain("\nforged: true\n", yaml);
                Assert.Equal(language, new PlayerLanguageStore(directory).Get("76561198000000000"));
            });
        }

        [Fact]
        public void AdminLanguageStore_QuotesNewlinesAndQuotesWithoutYamlInjection()
        {
            WithTemporaryDirectory(directory =>
            {
                const string language = "en\"\nadmin: true";
                var store = new AdminLanguageStore(directory);
                store.Set(language);

                var yaml = File.ReadAllText(Path.Combine(directory, "admin-language.yaml"));
                Assert.Contains("en\\\"\\nadmin: true", yaml);
                Assert.DoesNotContain("\nadmin: true\n", yaml);
                Assert.Equal(language, new AdminLanguageStore(directory).Get());
            });
        }

        [Fact]
        public async Task CloudflaredAutoDownload_RefusesUnverifiedExecutableDownloads()
        {
            await WithTemporaryDirectoryAsync(async directory =>
            {
                var missing = Path.Combine(directory, "missing-cloudflared");
                var result = await CloudflaredDownloader.EnsureAsync(
                    missing, true, new List<string> { "https://example.invalid/{asset}" }, 3,
                    directory, NullLogger.Instance, CancellationToken.None);

                Assert.Null(result);
                Assert.False(File.Exists(missing));
                Assert.Empty(Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories));
            });
        }

        [Fact]
        public void Sessions_RejectOfflineAndDeveloperTokensAndCanBeRevokedAsAGeneration()
        {
            var manager = new PlayerWebSessionManager(
                null!, _ => false, () => new WebServerSettings { DevPlayer = new DevPlayerSettings() });
            var field = typeof(PlayerWebSessionManager).GetField("m_Sessions",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            var sessions = Assert.IsType<ConcurrentDictionary<string, PlayerSession>>(field!.GetValue(manager));

            sessions["player"] = new PlayerSession("76561198000000000", "Player",
                DateTime.UtcNow.AddMinutes(15), PlayerSessionKind.Player, 0);
            Assert.Null(manager.Validate("player"));

            sessions["developer"] = new PlayerSession("76561198000000000", "Developer",
                DateTime.UtcNow.AddDays(1), PlayerSessionKind.Developer, 0);
            Assert.Null(manager.Validate("developer"));

            sessions["old-generation"] = new PlayerSession("76561198000000000", "Player",
                DateTime.UtcNow.AddMinutes(15), PlayerSessionKind.Player, 0);
            manager.RevokeAllSessions();
            Assert.Empty(sessions);
            Assert.Null(manager.Validate("old-generation"));
        }

        [Fact]
        public async Task HttpResponses_SetNoStoreAndBrowserSecurityHeaders()
        {
            await WithTemporaryDirectoryAsync(async directory =>
            {
                using var server = CreateServer(directory, out var baseAddress, out _);
                server.Start();
                using var client = new HttpClient { BaseAddress = baseAddress };

                using var response = await client.GetAsync("p");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
                Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
                Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
                Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
                Assert.Equal("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
                Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));

                using var preflight = new HttpRequestMessage(HttpMethod.Options, "api/p/view");
                using var preflightResponse = await client.SendAsync(preflight);
                Assert.NotEqual(HttpStatusCode.NoContent, preflightResponse.StatusCode);
                Assert.False(preflightResponse.Headers.Contains("Access-Control-Allow-Origin"));
            });
        }

        [Fact]
        public async Task HttpRequestBody_RejectsPayloadOverHardLimit()
        {
            await WithTemporaryDirectoryAsync(async directory =>
            {
                using var server = CreateServer(directory, out var baseAddress, out _);
                server.Start();
                using var client = new HttpClient { BaseAddress = baseAddress };
                using var content = new StringContent("lang=" + new string('x', 300 * 1024), Encoding.UTF8,
                    "application/x-www-form-urlencoded");

                using var response = await client.PostAsync("secret/api/lang", content);
                Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
            });
        }

        [Fact]
        public async Task AdminLanguageEndpoint_AcceptsOnlyRegisteredCanonicalLanguage()
        {
            await WithTemporaryDirectoryAsync(async directory =>
            {
                using var server = CreateServer(directory, out var baseAddress, out var languageStore);
                server.Start();
                using var client = new HttpClient { BaseAddress = baseAddress };

                using (var invalid = await client.PostAsync("secret/api/lang",
                    new StringContent("lang=evil%0Aforged%3Atrue", Encoding.UTF8,
                        "application/x-www-form-urlencoded")))
                {
                    Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
                    Assert.Null(languageStore.Get());
                }

                using (var valid = await client.PostAsync("secret/api/lang",
                    new StringContent("lang=ZH", Encoding.UTF8, "application/x-www-form-urlencoded")))
                {
                    Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
                    Assert.Equal("zh", languageStore.Get());
                }
            });
        }

        private static WebPanelHttpServer CreateServer(
            string directory, out Uri baseAddress, out AdminLanguageStore languageStore)
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            baseAddress = new Uri("http://127.0.0.1:" + port + "/");
            var translations = new WebTranslationRegistry();
            translations.AddBundle("zh", new Dictionary<string, string> { ["test"] = "测试" });
            languageStore = new AdminLanguageStore(directory);
            return new WebPanelHttpServer(
                new WebPanelRegistry(), new PlayerMenuRegistry(), translations,
                new PlayerWebSessionManager(null!), new PlayerLanguageStore(directory), languageStore,
                NullLogger.Instance, baseAddress.ToString(), "secret", "<html></html>", "<html></html>",
                new byte[] { 1 }, new DevPlayerSettings(), 5);
        }

        private static string ReadPlayerHtml()
        {
            var assembly = typeof(WebPanelPlugin).Assembly;
            var resource = assembly.GetManifestResourceNames().Single(name => name.EndsWith(".player.html"));
            using var stream = assembly.GetManifestResourceStream(resource);
            Assert.NotNull(stream);
            using var reader = new StreamReader(stream!);
            return reader.ReadToEnd();
        }

        private static void WithTemporaryDirectory(Action<string> action)
        {
            var directory = Path.Combine(Path.GetTempPath(), "webpanel-security-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                action(directory);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static async Task WithTemporaryDirectoryAsync(Func<string, Task> action)
        {
            var directory = Path.Combine(Path.GetTempPath(), "webpanel-security-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                await action(directory);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
