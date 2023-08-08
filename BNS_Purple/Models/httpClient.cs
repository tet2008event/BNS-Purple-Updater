using BNS_Purple.Functions;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace BNS_Purple.Models
{
    public class httpClient
    {
        private readonly HttpClient _httpClient;

        public httpClient()
        {
            _httpClient = new HttpClient()
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            // Should probably change this request header in the future to match what NCLauncher / Purple is using incase they ever start restricting user agents
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/108.0.0.0 Safari/537.36");
        }

        public bool RemoteFileExists(string url)
        {
            try
            {
                var response = _httpClient.Send(new HttpRequestMessage(HttpMethod.Head, url));
                if (response.IsSuccessStatusCode)
                    return true;
                else
                    return false;
            }
            catch
            {
                return false;
            }
        }

        public bool Download(string url, string path, bool retry = true, string hash = "")
        {
            if (!Directory.Exists(Path.GetDirectoryName(path)))
                Directory.CreateDirectory(Path.GetDirectoryName(path));

            if (File.Exists(path) && Crypto.SHA1_File(path) == hash)
                return true;
            else
                File.Delete(path);

            int retries = 0;
            while (true)
            {
                try
                {
                    using HttpResponseMessage response = _httpClient.GetAsync(new Uri(url)).Result;
                    response.EnsureSuccessStatusCode();
                    using (var fs = new FileStream(path, FileMode.Create))
                        response.Content.ReadAsStream().CopyTo(fs);

                    return true;
                }
                catch (Exception ex)
                {
                    //Debug.WriteLine(ex);
                    if (!retry || retries >= 4) return false;
                    retries++;
                }
            }
        }
    }
}
