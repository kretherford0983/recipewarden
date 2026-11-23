using System.Text;
using System.Text.Json;
using RecipeWarden.Core.AiParser;
using RecipeWarden.Importers.Ai;
using RecipeWarden.Importers.Import;

namespace RecipeWarden.Importers.Tests;

[Trait("Req", "FR-IMP-09")]
public sealed class FR_IMP_09_AiProtocolTests
{
    [Fact]
    public void Claude_request_has_system_user_model_and_no_tools()
    {
        using var doc = JsonDocument.Parse(AiProtocol.Request(AiProvider.Claude, "claude-haiku-5-5", "SYS", "USER"));
        var root = doc.RootElement;
        root.GetProperty("model").GetString().ShouldBe("claude-haiku-5-5");
        root.GetProperty("system").GetString().ShouldBe("SYS");
        root.GetProperty("messages")[0].GetProperty("content").GetString().ShouldBe("USER");
        root.TryGetProperty("tools", out _).ShouldBeFalse("SEC-47: no tools");
        AiProtocol.Headers(AiProvider.Claude, "k")["x-api-key"].ShouldBe("k");
        AiProtocol.Headers(AiProvider.Claude, "k")["anthropic-version"].ShouldBe(AiParserRules.ClaudeVersion);
    }

    [Fact]
    public void OpenAI_request_uses_system_and_user_messages_and_a_bearer_key_when_set()
    {
        using var doc = JsonDocument.Parse(AiProtocol.Request(AiProvider.OpenAiCompatible, "llama3.1", "SYS", "USER"));
        var messages = doc.RootElement.GetProperty("messages");
        messages[0].GetProperty("role").GetString().ShouldBe("system");
        messages[1].GetProperty("content").GetString().ShouldBe("USER");
        doc.RootElement.GetProperty("stream").GetBoolean().ShouldBeFalse();
        AiProtocol.Headers(AiProvider.OpenAiCompatible, "k")["Authorization"].ShouldBe("Bearer k");
        AiProtocol.Headers(AiProvider.OpenAiCompatible, null).ContainsKey("Authorization").ShouldBeFalse();
    }

    [Fact]
    [Trait("Req", "FR-IMP-10")]
    public void Replies_and_token_counts_are_read()
    {
        var claude = AiProtocol.ReadResponse(AiProvider.Claude, Encoding.UTF8.GetBytes(
            """{"content":[{"type":"text","text":"Hello "},{"type":"text","text":"there"}],"usage":{"input_tokens":12,"output_tokens":3}}"""));
        claude.ShouldBe(new AiReply("Hello there", 12, 3, null));

        var openai = AiProtocol.ReadResponse(AiProvider.OpenAiCompatible, Encoding.UTF8.GetBytes(
            """{"choices":[{"message":{"role":"assistant","content":"Hi"}}],"usage":{"prompt_tokens":5,"completion_tokens":1}}"""));
        openai.ShouldBe(new AiReply("Hi", 5, 1, null));
    }

    [Theory]
    [InlineData("""{"error":{"type":"authentication_error","message":"invalid x-api-key"}}""", "refused: invalid x-api-key")]
    [InlineData("not json", "not understood")]
    [InlineData("[1,2]", "not understood")]
    [InlineData("""{"content":[]}""", "no text")]
    [InlineData("", "empty")]
    [InlineData("""{"content":[{"type":"text","text":"x"}],"usage":{"input_tokens":"lots"}}""", null)]
    [Trait("Req", "SEC-47")]
    public void Odd_answers_never_throw(string body, string? error)
    {
        var reply = AiProtocol.ReadResponse(AiProvider.Claude, Encoding.UTF8.GetBytes(body));
        if (error is null)
        {
            reply.Error.ShouldBeNull();
            reply.InputTokens.ShouldBe(0);
        }
        else
        {
            reply.Error.ShouldNotBeNull().ShouldContain(error);
        }
    }

    [Fact]
    public void Plain_text_for_the_parser_comes_from_html_text_and_files_never_markup()
    {
        var html = "<html><body><nav>Menu</nav><h2>Stew</h2><p>Brown <b>beef</b>.</p><script>evil()</script></body></html>";
        var text = ImportPlainText.From(Encoding.UTF8.GetBytes(html), null)!;
        text.ShouldContain("Stew");
        text.ShouldContain("Brown beef.");
        text.ShouldNotContain("<");
        text.ShouldNotContain("evil");
        text.ShouldNotContain("Menu");
        ImportPlainText.From(Encoding.UTF8.GetBytes("Just text"), "notes.txt").ShouldBe("Just text");
    }
}
