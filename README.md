# Sys.ProxyLib
http/https Socks5 Socks4 Socks4a Proxy

---

var urls = new[]
{
	"http://www.baidu.com/a",
	"https://www.baidu.com",
	"http://www.hao123.com"
};

using (var proxyClientHandler = new Sys.ProxyLib.Http.ProxyMessageHandler(opts =>
{
	opts.PoolSizePerHost = 10;
	opts.ProxyType = ProxyType.Http;
	opts.ProxyHost = "127.0.0.1";
	opts.ProxyPort = 8080;
	opts.UseCookies = true;
	opts.AllowAutoRedirect = true;
	opts.CookieContainer = new System.Net.CookieContainer();
}))
{
	using (var client = new HttpClient(proxyClientHandler))
	{
		client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip");
		var list = new List<string>();

		foreach (var url in urls)
		{
			using (var response = await client.GetAsync(url))
			{
				var ss = await response.Content.ReadAsStringAsync();
				list.Add(ss);
			}
		}

		var arr = await Task.WhenAll(urls.Select(async url => await client.GetStringAsync(url)));
	}
}
