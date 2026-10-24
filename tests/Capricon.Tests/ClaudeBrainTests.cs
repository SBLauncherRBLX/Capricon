using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Anthropic;
using Capricon.Core;

namespace Capricon.Tests;

/// <summary>Drives the tool loop against a local fake of the Messages API.</summary>
public class ClaudeBrainTests
{
    static string Message(string content, string stopReason) =>
        "{\"id\":\"msg_1\",\"type\":\"message\",\"role\":\"assistant\",\"model\":\"claude-opus-5-5\"," +
        $"\"content\":{content},\"stop_reason\":\"{stopReason}\",\"stop_sequence\":null," +
        "\"usage\":{\"input_tokens\":10,\"output_tokens\":5}}";

    [Fact]
    public async Task ExecutesToolCallsAndSpeaksTheFinalAnswer()
    {
        var replies = new Queue<string>(
        [
            Message("""[{"type":"thinking","thinking":"","signature":"sig1"},{"type":"tool_use","id":"toolu_1","name":"open_url","input":{"url":"https://www.youtube.com"}}]""", "tool_use"),
            Message("""[{"type":"text","text":"[hack] Ютуб открыт, чумба!"}]""", "end_turn"),
        ]);
        var bodies = new List<JsonDocument>();

        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var server = Task.Run(async () =>
        {
            while (replies.Count > 0)
            {
                var ctx = await listener.GetContextAsync();
                using var reader = new StreamReader(ctx.Request.InputStream);
                bodies.Add(JsonDocument.Parse(await reader.ReadToEndAsync()));
                var bytes = Encoding.UTF8.GetBytes(replies.Dequeue());
                ctx.Response.ContentType = "application/json";
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
        });

        var pc = new FakePc();
        var client = new AnthropicClient { ApiKey = "test", BaseUrl = $"http://127.0.0.1:{port}", MaxRetries = 0 };
        var brain = new ClaudeBrain(client, pc);

        var reply = await brain.Ask("включи ютуб", new DateTime(2026, 10, 24, 12, 0, 0));
        await server;

        Assert.Equal(["url:https://www.youtube.com"], pc.Calls);
        Assert.Equal(Mood.Hack, reply.Mood);
        Assert.Equal("Ютуб открыт, чумба!", reply.Text);

        var first = bodies[0].RootElement;
        Assert.Equal("claude-opus-5-5", first.GetProperty("model").GetString());
        Assert.Equal("default", first.GetProperty("fallbacks").GetString());
        Assert.Equal("low", first.GetProperty("output_config").GetProperty("effort").GetString());
        Assert.Contains(first.GetProperty("tools").EnumerateArray(), t => t.GetProperty("name").GetString() == "launch_app");

        // Second request replays the assistant turn unchanged (thinking signature included)
        // and answers the tool call.
        var messages = bodies[1].RootElement.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(3, messages.Count);
        var assistant = messages[1].GetProperty("content").EnumerateArray().ToList();
        Assert.Equal("sig1", assistant[0].GetProperty("signature").GetString());
        var result = messages[2].GetProperty("content")[0];
        Assert.Equal("tool_result", result.GetProperty("type").GetString());
        Assert.Equal("toolu_1", result.GetProperty("tool_use_id").GetString());
    }

    static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
