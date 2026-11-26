using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Translate
{
    internal sealed class ServerClient : IDisposable
    {
        private readonly HttpClient http = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false
        })
        {
            Timeout = TimeSpan.FromSeconds(14),
            MaxResponseContentBufferSize = 256 * 1024
        };

        internal static bool TryParseAddress(string value, out Uri origin)
        {
            origin = null;
            var address = value.Trim();
            if (address.Length == 0 || address.IndexOfAny(new[] { '\\', '?', '#', '@' }) >= 0)
                return false;

            var hasScheme = address.Contains("://");
            IPAddress ip;
            if (!hasScheme && IPAddress.TryParse(address, out ip) && ip.AddressFamily == AddressFamily.InterNetworkV6)
                address = "[" + ip + "]";

            Uri parsed;
            if (!Uri.TryCreate(hasScheme ? address : "http://" + address, UriKind.Absolute, out parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) ||
                parsed.Host.Length == 0 || parsed.AbsolutePath != "/" || parsed.Port <= 0)
                return false;

            var hasPort = address[0] == '[' ? address.Contains("]:") : address.Contains(":");
            origin = hasScheme || hasPort ? parsed : new UriBuilder(parsed) { Port = 3000 }.Uri;
            return true;
        }

        internal async Task<bool> IsOnlineAsync(Uri origin, CancellationToken cancellation)
        {
            using (var response = await http.GetAsync(new Uri(origin, "health"), cancellation).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode) return false;
                var health = ReadJson<HealthPayload>(await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false));
                return health != null && health.Status == "ok";
            }
        }

        internal async Task<TranslationResult> TranslateAsync(
            Uri origin, string text, string source, string target, CancellationToken cancellation)
        {
            var payload = new TranslationPayload { Text = text, Source = source, Target = target };
            using (var content = new ByteArrayContent(WriteJson(payload)))
            {
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
                using (var response = await http.PostAsync(new Uri(origin, "v1/translate"), content, cancellation).ConfigureAwait(false))
                {
                    var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new TranslationException(DescribeError(
                            (int)response.StatusCode, ReadErrorCode(bytes), response.Headers.RetryAfter));
                    }

                    var result = ReadJson<TranslationResult>(bytes);
                    if (result == null || string.IsNullOrWhiteSpace(result.Text))
                        throw new TranslationException("服务器返回的译文为空。");
                    return result;
                }
            }
        }

        private static byte[] WriteJson<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                return stream.ToArray();
            }
        }

        private static T ReadJson<T>(byte[] data)
        {
            using (var stream = new MemoryStream(data, false))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }

        private static string ReadErrorCode(byte[] data)
        {
            try
            {
                var envelope = ReadJson<ErrorEnvelope>(data);
                return envelope != null && envelope.Error != null ? envelope.Error.Code : null;
            }
            catch (SerializationException)
            {
                return null;
            }
        }

        private static string DescribeError(int status, string code, RetryConditionHeaderValue retryAfter)
        {
            switch (status)
            {
                case 400:
                    return "服务器无法翻译这段文字。请检查内容和语言选项。";
                case 401:
                    return "此服务器需要访问令牌。请使用支持 IP 直连的部署地址。";
                case 404:
                    return "此地址没有翻译接口。请检查服务器地址和端口。";
                case 413:
                    return "文字太长，请分段翻译。";
                case 429:
                    var service = code == "UPSTREAM_RATE_LIMITED" ? "Google" : "服务器";
                    if (retryAfter != null && retryAfter.Delta.HasValue)
                    {
                        var seconds = Math.Max(1, Math.Ceiling(retryAfter.Delta.Value.TotalSeconds));
                        return service + "正在限流，请等待 " + seconds + " 秒。";
                    }
                    return service + "正在限流，请稍后再试。";
                case 502:
                    return "服务器暂时无法从 Google 获取译文。";
                case 504:
                    return "服务器连接 Google 超时，请稍后再试。";
                default:
                    return "翻译失败（HTTP " + status + "），请稍后再试。";
            }
        }

        public void Dispose()
        {
            http.Dispose();
        }

        [DataContract]
        private sealed class HealthPayload
        {
            [DataMember(Name = "status")]
            public string Status { get; set; }
        }

        [DataContract]
        private sealed class TranslationPayload
        {
            [DataMember(Name = "text")]
            public string Text { get; set; }
            [DataMember(Name = "source")]
            public string Source { get; set; }
            [DataMember(Name = "target")]
            public string Target { get; set; }
        }

        [DataContract]
        private sealed class ErrorEnvelope
        {
            [DataMember(Name = "error")]
            public ApiFailure Error { get; set; }
        }

        [DataContract]
        private sealed class ApiFailure
        {
            [DataMember(Name = "code")]
            public string Code { get; set; }
        }
    }

    [DataContract]
    internal sealed class TranslationResult
    {
        [DataMember(Name = "translatedText")]
        public string Text { get; set; }
        [DataMember(Name = "source")]
        public string Source { get; set; }
    }

    internal sealed class TranslationException : Exception
    {
        internal TranslationException(string message) : base(message) { }
    }
}
