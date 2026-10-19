using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Yatra.Contracts.Components;
using Yatra.Contracts.Messages;
namespace Yatra.Contracts.Tests;

public sealed class ComponentExtensionContractTests
{
    private static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public static IEnumerable<object[]> LimitCases()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Yatra.slnx"))) root = root.Parent;
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root!.FullName, "tests/fixtures/component-limits.json")));
        return fixture.RootElement.EnumerateArray().Select(test => new object[] { test.GetProperty("name").GetString()!, test.GetProperty("kind").GetString()!, test.GetProperty("count").GetInt32(), test.GetProperty("valid").GetBoolean() }).ToArray();
    }
    [Theory, MemberData(nameof(LimitCases))]
    public void SharedPayloadBoundaries(string name, string kind, int count, bool valid)
    {
        object value = Build(kind, count);
        var payload = JsonSerializer.SerializeToElement(value, Compact);
        if (kind is "bytes" or "unicode") Assert.Equal(count, Encoding.UTF8.GetByteCount(payload.GetRawText()));
        Assert.True(ComponentPayloadLimits.IsWithinLimits(payload) == valid, name);
    }
    private static object Build(string kind, int count)
    {
        if (kind == "array") return Enumerable.Range(0, count).ToArray();
        if (kind == "properties") return Enumerable.Range(0, count).ToDictionary(index => $"p{index}", _ => 0);
        if (kind == "key") return new Dictionary<string, int> { [new string('x', count)] = 0 };
        if (kind == "string") return new string('x', count);
        if (kind == "depth") { object value = "leaf"; for (var index = 0; index < count; index++) value = new Dictionary<string, object> { ["child"] = value }; return value; }
        if (kind == "nodes") return Enumerable.Range(0, 21).Select(index => Enumerable.Repeat(0, index < 20 ? 100 : count - 2022).ToArray()).ToArray();
        var remaining = count - 52;
        return Enumerable.Range(0, 17).Select(_ => {
            var size = Math.Min(remaining, 4096); remaining -= size;
            return kind == "unicode" ? new string('é', size / 2) + (size % 2 == 1 ? "x" : "") : new string('x', size);
        }).ToArray();
    }
    [Fact] public void DuplicateJsonPropertiesFailClosed() => Assert.False(ComponentPayloadLimits.IsWithinLimits(JsonDocument.Parse("{\"a\":1,\"a\":2}").RootElement));
    [Fact] public void BothComponentPayloadKindsEnforceLimits()
    {
        var common = new { componentId = "test-card", componentVersion = "1.0", instanceId = YatraUlid.NewUlid() };
        var request = JsonSerializer.SerializeToElement(new { common.componentId, common.componentVersion, common.instanceId, title = "Test", props = new { value = new string('x', 4097) }, allowedActions = new[] { "acknowledge" } });
        var response = JsonSerializer.SerializeToElement(new { common.componentId, common.componentVersion, common.instanceId, action = "acknowledge", data = new { value = new string('x', 4097) } });
        Assert.False(ComponentPayloadValidator.Validate(request).IsValid); Assert.False(ComponentPayloadValidator.Validate(response, true).IsValid);
    }
    [Fact] public void CapabilitiesRoundTripOnlyStableDeclarativeFields()
    {
        ComponentCapability[] expected = [new("customer-comparison", "1.0", ["select", "cancel"]), new("status-summary", "1.0", ["acknowledge"])];
        var json = JsonSerializer.SerializeToElement(expected, YatraJson.SerializerOptions);
        Assert.True(ComponentCapabilities.TryRead(json, out var actual)); Assert.Equal(expected.Select(c => (c.ComponentId, c.ComponentVersion)), actual.Select(c => (c.ComponentId, c.ComponentVersion)));
        Assert.All(json.EnumerateArray(), item => Assert.Equal(new[] { "componentId", "componentVersion", "supportedActions" }, item.EnumerateObject().Select(property => property.Name)));
    }
    [Theory]
    [InlineData("null")][InlineData("{}")][InlineData("[{\"componentId\":\"status-summary\",\"componentVersion\":\"latest\",\"supportedActions\":[\"acknowledge\"]}]")]
    [InlineData("[{\"componentId\":\"Status-Summary\",\"componentVersion\":\"1.0\",\"supportedActions\":[\"acknowledge\"]}]")]
    [InlineData("[{\"componentId\":\"status-summary\",\"componentVersion\":\"1.0\",\"supportedActions\":[]}]")]
    [InlineData("[{\"componentId\":\"status-summary\",\"componentVersion\":\"1.0\",\"supportedActions\":[\"acknowledge\",\"acknowledge\"]}]")]
    [InlineData("[{\"componentId\":\"status-summary\",\"componentVersion\":\"1.0\",\"supportedActions\":[\"acknowledge\"],\"source\":\"https://invalid.example/module.js\"}]")]
    public void MalformedCapabilityListsRejected(string json) => Assert.False(ComponentCapabilities.TryRead(JsonDocument.Parse(json).RootElement, out _));
    [Fact] public void DuplicateOrExcessiveCapabilityListsRejected()
    {
        var entry = new ComponentCapability("status-summary", "1.0", ["acknowledge"]);
        Assert.False(ComponentCapabilities.TryRead(JsonSerializer.SerializeToElement(new[] { entry, entry }, YatraJson.SerializerOptions), out _));
        var tooMany = Enumerable.Range(0, 33).Select(index => entry with { ComponentId = $"card-{index}" }).ToArray();
        Assert.False(ComponentCapabilities.TryRead(JsonSerializer.SerializeToElement(tooMany, YatraJson.SerializerOptions), out _));
        Assert.True(ComponentCapabilities.TryRead(JsonSerializer.SerializeToElement(Array.Empty<ComponentCapability>()), out var empty)); Assert.Empty(empty);
    }
}
