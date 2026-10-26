using System;
using System.Text.Json;
using Funke;
using Funke.InputActions;
using Xunit;

namespace FunkeInput.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public void DefaultBindingsAreNotWrittenIntoTheProfile()
    {
        var map = CreateMap();
        using JsonDocument document = JsonDocument.Parse(map.SaveBindingOverridesToJson());
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("Gameplay", document.RootElement.GetProperty("map").GetString());
        Assert.Equal(0, document.RootElement.GetProperty("overrides").GetArrayLength());
    }

    [Fact]
    public void RoundTripRestoresOverridesAndPreservesCodeDefaults()
    {
        var source = CreateMap();
        source.FindAction("Jump")!.OverrideBinding(0, InputBinding.Key(KeyCode.J));
        source.FindAction("Move")!.OverrideBinding(0,
            InputBinding.Keys2D(KeyCode.I, KeyCode.K, KeyCode.J, KeyCode.L));

        var target = CreateMap();
        Assert.True(target.TryLoadBindingOverridesFromJson(source.SaveBindingOverridesToJson(), out string? error), error);
        Assert.Equal(InputBinding.Key(KeyCode.J), target.FindAction("Jump")!.Bindings[0]);
        Assert.Equal(InputBinding.Key(KeyCode.Space), target.FindAction("Jump")!.GetDefaultBinding(0));
        Assert.Equal(source.FindAction("Move")!.Bindings[0], target.FindAction("Move")!.Bindings[0]);
        target.FindAction("Jump")!.ResetBinding(0);
        Assert.Equal(InputBinding.Key(KeyCode.Space), target.FindAction("Jump")!.Bindings[0]);
    }

    [Fact]
    public void LoadingReplacesExistingOverridesRatherThanMergingProfiles()
    {
        var source = CreateMap();
        source.FindAction("Jump")!.OverrideBinding(0, InputBinding.Key(KeyCode.J));
        var target = CreateMap();
        target.FindAction("Move")!.OverrideBinding(0, InputBinding.Arrows());
        Assert.True(target.TryLoadBindingOverridesFromJson(source.SaveBindingOverridesToJson(), out _));
        Assert.False(target.FindAction("Move")!.HasBindingOverride(0));
        Assert.True(target.FindAction("Jump")!.HasBindingOverride(0));
    }

    [Fact]
    public void EmptyProfileRestoresEveryDefault()
    {
        var map = CreateMap();
        map.FindAction("Jump")!.OverrideBinding(0, InputBinding.Key(KeyCode.J));
        Assert.True(map.TryLoadBindingOverridesFromJson(Profile(""), out _));
        Assert.False(map.FindAction("Jump")!.HasBindingOverride(0));
        Assert.Equal(InputBinding.Key(KeyCode.Space), map.FindAction("Jump")!.Bindings[0]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"schemaVersion\":2,\"map\":\"Gameplay\",\"overrides\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"map\":\"Menu\",\"overrides\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"map\":\"Gameplay\"}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1,\"map\":\"Gameplay\",\"overrides\":[]}")]
    public void InvalidEnvelopeDoesNotChangeBindings(string json)
    {
        AssertRejectedWithoutMutation(json);
    }

    [Theory]
    [InlineData("{\"kind\":\"Key\",\"code\":99999}")]
    [InlineData("{\"kind\":\"DoesNotExist\"}")]
    [InlineData("{\"kind\":0,\"code\":32}")]
    [InlineData("{\"kind\":\"Key\",\"code\":32,\"scale\":\"NaN\"}")]
    [InlineData("{\"kind\":\"Key\",\"code\":32,\"scale\":1e100}")]
    [InlineData("{\"kind\":\"GamepadStick\",\"code\":0}")]
    [InlineData("{\"kind\":\"Key\",\"code\":32,\"code\":74}")]
    [InlineData("{\"code\":32}")]
    [InlineData("{\"kind\":\"Key\",\"code\":32,\"unexpected\":true}")]
    public void InvalidKnownBindingDoesNotApplyEarlierValidEntries(string binding)
    {
        string valid = "{\"action\":\"Jump\",\"index\":1,\"binding\":{\"kind\":\"Key\",\"code\":75}}";
        string invalid = "{\"action\":\"Jump\",\"index\":0,\"binding\":" + binding + "}";
        AssertRejectedWithoutMutation(Profile(valid + "," + invalid));
    }

    [Fact]
    public void RemovedActionsAndRemovedBindingSlotsAreIgnored()
    {
        var map = CreateMap();
        string entries = "{\"action\":\"OldAction\",\"index\":0,\"binding\":{\"kind\":\"OldKind\"}},"
            + "{\"action\":\"Jump\",\"index\":99},"
            + "{\"action\":\"Jump\",\"index\":0,\"binding\":{\"kind\":\"Key\",\"code\":74}}";
        Assert.True(map.TryLoadBindingOverridesFromJson(Profile(entries), out string? error), error);
        Assert.Equal(InputBinding.Key(KeyCode.J), map.FindAction("Jump")!.Bindings[0]);
    }

    [Fact]
    public void DuplicateSlotsAreRejectedWithoutChangingBindings()
    {
        string entry = "{\"action\":\"Jump\",\"index\":0,\"binding\":{\"kind\":\"Key\",\"code\":74}}";
        AssertRejectedWithoutMutation(Profile(entry + "," + entry));
    }

    [Fact]
    public void NegativeBindingIndexIsRejected()
    {
        AssertRejectedWithoutMutation(Profile("{\"action\":\"Jump\",\"index\":-1,\"binding\":{\"kind\":\"Key\",\"code\":74}}"));
    }

    [Fact]
    public void OversizedProfileIsRejectedWithoutParsingOrMutation()
    {
        AssertRejectedWithoutMutation(new string(' ', 1024 * 1024 + 1));
    }

    [Fact]
    public void ExcessiveEntryCountIsRejected()
    {
        string[] entries = new string[4097];
        for (int index = 0; index < entries.Length; index++)
            entries[index] = "{\"action\":\"Removed\",\"index\":" + index + "}";
        AssertRejectedWithoutMutation(Profile(string.Join(",", entries)));
    }

    [Fact]
    public void ArbitraryMapNamesRoundTripWithoutLosingIdentity()
    {
        var source = new InputActionMap("Menü: \"操作\"", new FakeInputSource());
        source.AddButton("Accept").Bind(KeyCode.Enter).OverrideBinding(0, InputBinding.Key(KeyCode.Space));
        var target = new InputActionMap(source.Name, new FakeInputSource());
        target.AddButton("Accept").Bind(KeyCode.Enter);
        Assert.True(target.TryLoadBindingOverridesFromJson(source.SaveBindingOverridesToJson(), out _));
        Assert.Equal(InputBinding.Key(KeyCode.Space), target.FindAction("Accept")!.Bindings[0]);
    }

    [Fact]
    public void SuccessfulProfileLoadCancelsAnOlderRebind()
    {
        var source = new FakeInputSource();
        var map = new InputActionMap("Gameplay", source);
        InputAction jump = map.AddButton("Jump").Bind(KeyCode.Space);
        RebindOperation operation = jump.Rebind();

        Assert.True(map.TryLoadBindingOverridesFromJson(Profile(""), out _));
        source.SetKey(KeyCode.J);
        source.NextFrame();
        Assert.Equal(RebindStatus.Canceled, operation.Status);
        Assert.Equal(InputBinding.Key(KeyCode.Space), jump.Bindings[0]);
    }

    [Fact]
    public void RejectedProfileDoesNotCancelAnActiveRebind()
    {
        var map = CreateMap();
        RebindOperation operation = map.FindAction("Jump")!.Rebind();
        Assert.False(map.TryLoadBindingOverridesFromJson("invalid", out _));
        Assert.Equal(RebindStatus.Waiting, operation.Status);
    }

    [Fact]
    public void DisposedMapCannotImportOrExportProfiles()
    {
        var map = CreateMap();
        map.Dispose();
        Assert.Throws<ObjectDisposedException>(() => map.SaveBindingOverridesToJson());
        Assert.Throws<ObjectDisposedException>(() => map.TryLoadBindingOverridesFromJson(Profile(""), out _));
    }

    private static void AssertRejectedWithoutMutation(string json)
    {
        var map = CreateMap();
        map.FindAction("Jump")!.OverrideBinding(0, InputBinding.Key(KeyCode.J));
        string before = map.SaveBindingOverridesToJson();
        Assert.False(map.TryLoadBindingOverridesFromJson(json, out string? error));
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal(before, map.SaveBindingOverridesToJson());
    }

    private static string Profile(string entries)
        => "{\"schemaVersion\":1,\"map\":\"Gameplay\",\"overrides\":[" + entries + "]}";

    private static InputActionMap CreateMap()
    {
        var map = new InputActionMap("Gameplay", new FakeInputSource());
        map.AddButton("Jump").Bind(KeyCode.Space).Bind(GamepadButton.A);
        map.AddVector2("Move").BindWASD();
        return map;
    }
}
