using AngleSharp.Html.Parser;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace bakawatch.BakaSync
{
    public class BakaAPI
    {
        HttpClient client;
        HttpClientHandler clientHandler;

        HtmlParser htmlParser = new();
        LoginDetails user;

        public BakaAPI(string baseUri, LoginDetails loginDetails)
        {
            clientHandler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
            };
            clientHandler.UseCookies = true;
            client = new HttpClient(clientHandler)
            {
                BaseAddress = new Uri(baseUri),
            };
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:122.0) Gecko/20100101 Firefox/122.0");
            client.DefaultRequestHeaders.AcceptLanguage.Add(new("cs-CZ", 1));

            user = loginDetails;
        }

        public async Task Login()
        {
            var req = new HttpRequestMessage()
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri(client.BaseAddress!, "/login"),
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["username"] = user.Username,
                    ["password"] = user.Password,
                    ["returnUrl"] = ""
                }),
                Headers = {
                    AcceptLanguage = {new("en-US",1)}
                }
            };

            var res = await client.SendAsync(req);

            if (res.StatusCode != System.Net.HttpStatusCode.Redirect)
            {
                var body = await res.Content.ReadAsStringAsync();
                var doc = htmlParser.ParseDocument(body);

                var err = doc.QuerySelector(".bk-infoPanel.error")?
                             .QuerySelector(".bk-infoPanel-text label")
                            ?? throw new BakaLoginFailure();

                throw new BakaLoginFailure(err.TextContent);
            }
        }

        private async Task<HttpResponseMessage> RequestInternal(DRequestGenerator requestGenerator)
        {
            for (; ; )
            {
                var request = requestGenerator();
                var res = await client.SendAsync(request);

                switch (res.StatusCode)
                {
                    case System.Net.HttpStatusCode.Redirect:
                        if (res.Headers.Location?.OriginalString.StartsWith("/next/errinfo.aspx") ?? false)
                        {
                            var query = res.Headers.Location.OriginalString
                                .Split("?")[1]
                                .Split("&")
                                .Select(x =>
                                {
                                    var s = x.Split("=");
                                    return (Uri.UnescapeDataString(s[0]), Uri.UnescapeDataString(s[1]));
                                });
                            var error = query
                                .Where(x => x.Item1 == "e")
                                .Select(x => x.Item2)
                                .FirstOrDefault(res.Headers.Location.OriginalString);
                            throw new BakaError(error);
                        }
                        goto case System.Net.HttpStatusCode.Unauthorized;
                    case System.Net.HttpStatusCode.Unauthorized:
                        await Login();
                        break;
                    default:
                        return res;
                }
            }
        }

        public async Task<HttpResponseMessage> Request(DRequestGenerator requestGenerator) {
            try {
                return await RequestInternal(requestGenerator);
            } catch (BakaError ex) {
                throw new BakaHttpError("Bakalari error", ex);
            } catch (HttpRequestException ex) {
                throw new BakaHttpError("Bakalari are down, probably", ex);
            } catch (TaskCanceledException ex) {
                throw new BakaHttpError("Bakalari are down, probably, probably", ex);
            }
        }

        public delegate HttpRequestMessage DRequestGenerator();

        public record LoginDetails(string Username, string Password);

        public class BakaLoginFailure : Exception
        {
            public BakaLoginFailure() : base() { }
            public BakaLoginFailure(string message) : base(message) { }
        }

        public class BakaError : Exception
        {
            public BakaError() : base() { }
            public BakaError(string message) : base(message) { }
        }

        public class BakaHttpError(string message, Exception innerException) : Exception(message, innerException) { }
    }
}
