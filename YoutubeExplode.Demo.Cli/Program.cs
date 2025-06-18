using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace YoutubeExplode.Demo.Cli;

// This demo prompts for video ID and downloads one media stream.
// It's intended to be very simple and straight to the point.
// For a more involved example - check out the WPF demo.
public static class Program
{
    private const string ContinuationPrefix =
        "https://www.youtube.com/live_chat_replay?continuation=";

    public static async Task Main()
    {
        Console.Title = "YoutubeExplode Demo";

        var youtube = new YoutubeClient();
        string testUrl = "https://www.youtube.com/watch?v=RZ2h1FM7YKE&t=1032s";

        // 定义请求的URL
        //string url = "https://www.youtube.com/youtubei/v1/live_chat/get_live_chat_replay";

        // 创建HttpClient实例
        using (HttpClient client = new HttpClient())
        {
            // 设置请求头（可选）
            client.DefaultRequestHeaders.Add(
                "User-Agent",
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_5) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/83.0.4103.116 Safari/537.36"
            );

            // 发送Get请求
            HttpResponseMessage response = await client.GetAsync(testUrl);

            // 检查响应状态码
            if (response.IsSuccessStatusCode)
            {
                // 读取响应内容
                string responseBody = await response.Content.ReadAsStringAsync();

                var d = GetYtInitialData(responseBody);
                var contiuation = GetContinueUrl(d as JObject);

                // 获取Chats
                var lst = await GetChatReplayFromContinuation("RZ2h1FM7YKE", contiuation);

                Console.WriteLine($"{response.StatusCode}");
            }
            else
            {
                Console.WriteLine($"Error: {response.StatusCode}");
                Console.WriteLine($"Reason: {response.ReasonPhrase}");
            }
        }

        return;
        //// Get the video ID
        //Console.Write("Enter YouTube video ID or URL: ");
        //var videoId = VideoId.Parse(testUrl ?? "");

        //// Get available streams and choose the best muxed (audio + video) stream
        //var streamManifest = await youtube.Videos.Streams.GetManifestAsync(videoId);
        //var streamInfo = streamManifest.GetMuxedStreams().TryGetWithHighestVideoQuality();
        //if (streamInfo is null)
        //{
        //    // Available streams vary depending on the video and it's possible
        //    // there may not be any muxed streams at all.
        //    // See the readme to learn how to handle adaptive streams.
        //    Console.Error.WriteLine("This video has no muxed streams.");
        //    return;
        //}

        //// Download the stream
        //var fileName = $"{videoId}.{streamInfo.Container.Name}";

        //Console.Write(
        //    $"Downloading stream: {streamInfo.VideoQuality.Label} / {streamInfo.Container.Name}... "
        //);

        //using (var progress = new ConsoleProgress())
        //    await youtube.Videos.Streams.DownloadAsync(streamInfo, fileName, progress);

        //Console.WriteLine("Done");
        //Console.WriteLine($"Video saved to '{fileName}'");
    }

    public class RestrictedFromYoutubeException : Exception { }

    public static object GetYtInitialData(string htmlContent)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(htmlContent);

        // 检查是否被限制
        if (
            htmlContent.Contains(
                "Sorry for the interruption. We have been receiving a large volume of requests from your network."
            )
        )
        {
            throw new RestrictedFromYoutubeException();
        }

        var scriptNodes = doc.DocumentNode.SelectNodes("//script");

        if (scriptNodes == null)
            return null;

        foreach (var script in scriptNodes)
        {
            var scriptText = script.InnerText;

            if (scriptText.Contains("ytInitialData"))
            {
                // 尝试匹配 'var ytInitialData = ...'
                var varMatch = Regex.Match(
                    scriptText,
                    @"var ytInitialData\s*=\s*(\{.*?\});",
                    RegexOptions.Singleline
                );
                if (varMatch.Success)
                {
                    string jsonData = varMatch.Groups[1].Value;
                    try
                    {
                        return JsonConvert.DeserializeObject(jsonData);
                    }
                    catch
                    {
                        // 可选：处理反序列化错误
                    }
                }

                // 尝试匹配 'window["ytInitialData"] = ...'
                //var windowMatch = Regex.Match(scriptText, @"window[$"ytInitialData"$]\s*=\s*(\{.*?\});", RegexOptions.Singleline);
                var windowMatch = Regex.Match(
                    scriptText,
                    @"window[$""]ytInitialData[$""]\s*=\s*(\{.*?\});",
                    RegexOptions.Singleline
                );
                if (windowMatch.Success)
                {
                    string jsonData = windowMatch.Groups[1].Value;
                    try
                    {
                        return JsonConvert.DeserializeObject(jsonData);
                    }
                    catch
                    {
                        // 可选：处理反序列化错误
                    }
                }
            }
        }

        return null;
    }

    public class ContinuationURLNotFound : Exception
    {
        public ContinuationURLNotFound()
            : base("Continuation URL not found") { }
    }

    public static string GetContinueUrl(JObject ytInitialData)
    {
        var continueDict = new Dictionary<string, string>();

        try
        {
            var continuations =
                ytInitialData["contents"]
                    ?["twoColumnWatchNextResults"]
                    ?["conversationBar"]
                    ?["liveChatRenderer"]
                    ?["header"]
                    ?["liveChatHeaderRenderer"]
                    ?["viewSelector"]
                    ?["sortFilterSubMenuRenderer"]
                    ?["subMenuItems"] as JArray;

            if (continuations != null)
            {
                foreach (JToken continuation in continuations)
                {
                    var titleToken = continuation.SelectToken("title")?.ToString();
                    var continuationToken = continuation
                        .SelectToken("continuation.reloadContinuationData.continuation")
                        ?.ToString();

                    if (
                        !string.IsNullOrEmpty(titleToken)
                        && !string.IsNullOrEmpty(continuationToken)
                    )
                    {
                        continueDict[titleToken] = continuationToken;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error parsing continuations: " + ex.Message);
        }

        string continueUrl = null;

        // 尝试匹配日文键名
        if (continueDict.TryGetValue("上位のチャットのリプレイ", out var topJa))
        {
            continueUrl = topJa;
        }
        else if (continueDict.TryGetValue("Top chat replay", out var topEn))
        {
            continueUrl = topEn;
        }

        // 如果没找到，尝试普通聊天回放
        if (string.IsNullOrEmpty(continueUrl))
        {
            if (continueDict.TryGetValue("チャットのリプレイ", out var chatJa))
            {
                continueUrl = chatJa;
            }
            else if (continueDict.TryGetValue("Live chat replay", out var chatEn))
            {
                continueUrl = chatEn;
            }
        }

        // 最后尝试 fallback 到默认路径
        if (string.IsNullOrEmpty(continueUrl))
        {
            var defaultContinuation = ytInitialData["contents"]
                ?["twoColumnWatchNextResults"]?["conversationBar"]?["liveChatRenderer"]?[
                    "continuations"
                ]?[0]?["reloadContinuationData"]?["continuation"]?.ToString();
            continueUrl = defaultContinuation;
        }

        if (string.IsNullOrEmpty(continueUrl))
        {
            throw new ContinuationURLNotFound();
        }

        return continueUrl;
    }

    public class ChatLog
    {
        public string Author { get; set; }
        public string Message { get; set; }
        public string Timestamp { get; set; }
        public string Video_id { get; set; }
        public string Chat_No { get; set; }
    }

    public static async Task<(
        List<ChatLog> result,
        string continuation
    )> GetChatReplayFromContinuation(
        string videoId,
        string continuation,
        int pageCountLimit = 800,
        bool isLocallyRun = false
    )
    {
        var result = new List<ChatLog>();
        int count = 1;
        int pageCount = 1;
        HttpClient client = new HttpClient();

        while (pageCount < pageCountLimit)
        {
            if (string.IsNullOrEmpty(continuation))
            {
                Console.WriteLine("continuation is null. Maybe hit the last chat segment.");
                break;
            }

            try
            {
                string url = ContinuationPrefix + continuation;

                var ytTemp = await GetYtInitialDataAsync(url);
                JObject ytInitialData = ytTemp as JObject;
                if (ytInitialData == null)
                {
                    Console.WriteLine($"video_id: {videoId}, continuation: {continuation}");
                    continuation = null;
                    break;
                }

                var liveChatCont = ytInitialData["continuationContents"]?["liveChatContinuation"];
                if (liveChatCont == null || liveChatCont["actions"] == null)
                {
                    continuation = null;
                    break;
                }

                JArray actions = (JArray)liveChatCont["actions"];

                foreach (var action in actions)
                {
                    var replayAction = action["replayChatItemAction"];
                    if (
                        replayAction == null
                        || replayAction["actions"] == null
                        || ((JArray)replayAction["actions"]).Count == 0
                    )
                        continue;

                    var item = replayAction["actions"][0]["addChatItemAction"]?["item"];
                    if (item == null)
                        continue;

                    ChatLog chatlog = null;

                    if (item["liveChatTextMessageRenderer"] != null)
                    {
                        chatlog = ConvertChatReplay(item["liveChatTextMessageRenderer"]);
                    }
                    else if (item["liveChatPaidMessageRenderer"] != null)
                    {
                        chatlog = ConvertChatReplay(item["liveChatPaidMessageRenderer"]);
                    }

                    if (chatlog != null)
                    {
                        chatlog.Video_id = videoId;
                        chatlog.Chat_No = count.ToString("D5");
                        result.Add(chatlog);
                        count++;
                    }
                }

                continuation = GetContinuation(ytInitialData);

                if (isLocallyRun)
                {
                    Console.Write($"\rPage {pageCount} ");
                }

                pageCount++;
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"HTTP Error: {ex.Message}");
                continue;
            }
            catch (RestrictedFromYoutubeException)
            {
                Console.WriteLine("Restricted from Youtube (Rate limit)");
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error: {ex.GetType().Name}");
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                break;
            }
        }

        Console.WriteLine($"{videoId} found {pageCount:D3} pages");
        return (result, continuation);
    }

    public static string GetContinuation(JObject ytInitialData)
    {
        var continuation = ytInitialData["continuationContents"]
            ?["liveChatContinuation"]?["continuations"]?[0]?["liveChatReplayContinuationData"]?[
                "continuation"
            ]?.ToString();

        return continuation;
    }

    public static async Task<JObject> GetYtInitialDataAsync(string targetUrl)
    {
        try
        {
            // 设置请求头
            var request = new HttpRequestMessage(HttpMethod.Get, targetUrl);
            request.Headers.UserAgent.ParseAdd(
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_5) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/83.0.4103.116 Safari/537.36"
            );

            HttpClient client = new HttpClient();
            // 发送请求
            var response = await client.SendAsync(request);
            var html = await response.Content.ReadAsStringAsync();

            // 使用 HtmlAgilityPack 解析 HTML
            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(html);

            // 遍历所有 <script> 标签
            var nodes = htmlDoc.DocumentNode.SelectNodes("//script");
            foreach (var scriptNode in nodes)
            {
                string scriptText = scriptNode.InnerHtml;

                if (scriptText.Contains("ytInitialData"))
                {
                    // 尝试匹配：var ytInitialData = ...
                    int startIndex = scriptText.IndexOf(
                        "var ytInitialData =",
                        StringComparison.Ordinal
                    );
                    if (startIndex >= 0)
                    {
                        try
                        {
                            string jsonStr = scriptText.Substring(
                                startIndex,
                                scriptText.Length - startIndex - 10
                            );
                            return JObject.Parse(jsonStr);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("JSON parse error (var): " + ex.Message);
                        }
                    }

                    // 尝试匹配：window["ytInitialData"] = ...
                    startIndex = scriptText.IndexOf(
                        "window[\"ytInitialData\"] = ",
                        StringComparison.Ordinal
                    );
                    if (startIndex >= 0)
                    {
                        try
                        {
                            // 去掉结尾的分号和 </script>
                            string jsonStr = scriptText
                                .Substring(startIndex + "window[\"ytInitialData\"] = ".Length)
                                .TrimEnd(';')
                                .Trim();
                            return JObject.Parse(jsonStr);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("JSON parse error (window): " + ex.Message);
                        }
                    }
                }
            }

            // 检查是否被限制访问
            if (
                html.Contains(
                    "Sorry for the interruption. We have been receiving a large volume of requests from your network."
                )
            )
            {
                Console.WriteLine("Restricted from Youtube (Rate limit)");
                throw new RestrictedFromYoutubeException();
            }

            Console.WriteLine("Cannot get ytInitialData");
            return null;
        }
        catch (JsonException je)
        {
            Console.WriteLine("JSON parse error: " + je.Message);
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error fetching/parsing ytInitialData: " + ex.Message);
            return null;
        }
    }

    public static ChatLog ConvertChatReplay(JToken renderer)
    {
        var chatlog = new ChatLog();

        // 作者名：authorName.simpleText
        chatlog.Author = renderer.SelectToken("authorName.simpleText")?.ToString() ?? "";

        // 消息内容：message.simpleText 或 message.runs.text/emoji
        chatlog.Message = ExtractMessage(renderer["message"]);

        // 时间戳：timestampText.simpleText
        chatlog.Timestamp = renderer.SelectToken("timestampText.simpleText")?.ToString() ?? "";

        // Video_id 和 Chat_No 暂时无法从 renderer 获取，设为空或传入参数补充
        chatlog.Video_id = ""; // 需要外部提供
        chatlog.Chat_No = ""; // 需要外部提供或生成唯一 ID

        return chatlog;
    }

    private static string ExtractMessage(JToken messageToken)
    {
        if (messageToken == null)
            return "";

        // 简单文本直接提取
        if (messageToken["simpleText"] != null)
        {
            return messageToken["simpleText"].ToString();
        }

        // runs 分段提取
        if (messageToken["runs"] is JArray runs)
        {
            var content = "";
            foreach (var run in runs)
            {
                // 文本部分
                if (run["text"] != null)
                {
                    content += run["text"].ToString();
                }

                // 表情符号部分
                if (run["emoji"] is JObject emoji)
                {
                    bool isCustomEmoji = (bool?)emoji["isCustomEmoji"] ?? false;

                    if (isCustomEmoji)
                    {
                        var shortcuts = emoji["shortcuts"] as JArray;
                        if (shortcuts != null && shortcuts.Count > 0)
                        {
                            content += shortcuts[0].ToString(); // 取第一个快捷方式
                        }
                    }
                    else
                    {
                        content += emoji["emojiId"]?.ToString() ?? "";
                    }
                }
            }

            return content;
        }

        return "";
    }
}
