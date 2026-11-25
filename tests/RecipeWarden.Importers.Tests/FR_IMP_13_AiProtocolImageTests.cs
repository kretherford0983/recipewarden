using System.Text.Json;
using RecipeWarden.Core.AiParser;
using RecipeWarden.Importers.Ai;

namespace RecipeWarden.Importers.Tests;

/// <summary>FR-IMP-13, AC-128, ADR 0058: photos go to the model as image parts in both wire formats.</summary>
[Trait("Req", "FR-IMP-13")]
[Trait("Req", "AC-128")]
public sealed class FR_IMP_13_AiProtocolImageTests
{
    private static readonly byte[] First = [0xFF, 0xD8, 0xFF, 0x01];
    private static readonly byte[] Second = [0xFF, 0xD8, 0xFF, 0x02];

    [Fact]
    public void Claude_gets_base64_image_blocks_in_order_then_the_text()
    {
        using var doc = JsonDocument.Parse(AiProtocol.Request(AiProvider.Claude, "claude-haiku-5-5", "SYS", "USER", [First, Second]));
        var root = doc.RootElement;
        root.GetProperty("system").GetString().ShouldBe("SYS");
        var content = root.GetProperty("messages")[0].GetProperty("content");
        content.GetArrayLength().ShouldBe(3);
        for (var i = 0; i < 2; i++)
        {
            content[i].GetProperty("type").GetString().ShouldBe("image");
            var source = content[i].GetProperty("source");
            source.GetProperty("type").GetString().ShouldBe("base64");
            source.GetProperty("media_type").GetString().ShouldBe("image/jpeg");
            Convert.FromBase64String(source.GetProperty("data").GetString()!).ShouldBe(i == 0 ? First : Second);
        }

        content[2].GetProperty("type").GetString().ShouldBe("text");
        content[2].GetProperty("text").GetString().ShouldBe("USER");
        root.TryGetProperty("tools", out _).ShouldBeFalse("SEC-47: no tools");
    }

    [Fact]
    public void OpenAI_compatible_gets_image_url_data_parts_in_order_then_the_text()
    {
        using var doc = JsonDocument.Parse(AiProtocol.Request(AiProvider.OpenAiCompatible, "qwen2.5vl", "SYS", "USER", [First, Second]));
        var messages = doc.RootElement.GetProperty("messages");
        messages[0].GetProperty("role").GetString().ShouldBe("system");
        var parts = messages[1].GetProperty("content");
        parts.GetArrayLength().ShouldBe(3);
        parts[0].GetProperty("type").GetString().ShouldBe("image_url");
        parts[0].GetProperty("image_url").GetProperty("url").GetString().ShouldBe("data:image/jpeg;base64," + Convert.ToBase64String(First));
        parts[1].GetProperty("image_url").GetProperty("url").GetString().ShouldBe("data:image/jpeg;base64," + Convert.ToBase64String(Second));
        parts[2].GetProperty("type").GetString().ShouldBe("text");
        parts[2].GetProperty("text").GetString().ShouldBe("USER");
    }

    [Fact]
    public void Without_images_the_request_is_the_plain_text_one()
    {
        AiProtocol.Request(AiProvider.Claude, "m", "S", "U", []).ShouldBe(AiProtocol.Request(AiProvider.Claude, "m", "S", "U"));
        AiProtocol.Request(AiProvider.OpenAiCompatible, "m", "S", "U", []).ShouldBe(AiProtocol.Request(AiProvider.OpenAiCompatible, "m", "S", "U"));
    }
}
