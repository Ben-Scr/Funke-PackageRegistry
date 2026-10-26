using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Funke.InputActions;

public sealed partial class InputActionMap
{
    private const int k_OverridesSchemaVersion = 1;
    private const int k_MaxOverridesJsonLength = 1024 * 1024;
    private const int k_MaxOverrideEntries = 4096;

    private static readonly JsonSerializerOptions s_OverridesJsonOptions = CreateOverridesJsonOptions();

    /// <summary>Exports only changed bindings; code-defined defaults remain the source of truth.</summary>
    public string SaveBindingOverridesToJson()
    {
        ThrowIfDisposed();
        var entries = new List<BindingOverrideEntry>();
        foreach (InputAction action in Actions)
        {
            for (int index = 0; index < action.BindingCount; index++)
            {
                if (!action.HasBindingOverride(index)) continue;
                entries.Add(new BindingOverrideEntry(action.Name, index, action.Bindings[index]));
            }
        }

        if (entries.Count > k_MaxOverrideEntries)
            throw new InvalidOperationException($"A profile supports at most {k_MaxOverrideEntries} binding overrides.");

        string json = JsonSerializer.Serialize(
            new BindingOverridesDocument(k_OverridesSchemaVersion, Name, entries), s_OverridesJsonOptions);
        if (json.Length > k_MaxOverridesJsonLength)
            throw new InvalidOperationException("The binding profile is too large to save.");
        return json;
    }

    /// <summary>Replaces overrides atomically; removed actions and binding slots are ignored when loading older profiles.</summary>
    public bool TryLoadBindingOverridesFromJson(string json, out string? error)
    {
        ThrowIfDisposed();
        error = null;
        if (string.IsNullOrWhiteSpace(json) || json.Length > k_MaxOverridesJsonLength)
        {
            error = "The binding profile is empty or exceeds the 1 MiB character limit.";
            return false;
        }

        var pending = new List<(InputAction Action, int Index, InputBinding Binding)>();
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            JsonElement root = document.RootElement;
            RequireObject(root);
            if (!root.TryGetProperty("schemaVersion", out JsonElement version)
                || !version.TryGetInt32(out int schemaVersion) || schemaVersion != k_OverridesSchemaVersion)
                throw new JsonException("Unsupported or missing binding profile schemaVersion.");
            if (!root.TryGetProperty("map", out JsonElement map) || map.ValueKind != JsonValueKind.String
                || !string.Equals(map.GetString(), Name, StringComparison.Ordinal))
                throw new JsonException("The binding profile belongs to a different action map.");
            if (!root.TryGetProperty("overrides", out JsonElement entries) || entries.ValueKind != JsonValueKind.Array)
                throw new JsonException("The binding profile must contain an overrides array.");
            if (entries.GetArrayLength() > k_MaxOverrideEntries)
                throw new JsonException($"A profile supports at most {k_MaxOverrideEntries} binding overrides.");

            var seen = new HashSet<(string Action, int Index)>();
            foreach (JsonElement entry in entries.EnumerateArray())
            {
                RequireObject(entry);
                if (!entry.TryGetProperty("action", out JsonElement actionName) || actionName.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(actionName.GetString()))
                    throw new JsonException("Each override needs an action name.");
                if (!entry.TryGetProperty("index", out JsonElement slot) || !slot.TryGetInt32(out int index) || index < 0)
                    throw new JsonException("Each override needs a non-negative binding index.");
                string name = actionName.GetString()!;
                if (!seen.Add((name, index)))
                    throw new JsonException($"Duplicate binding override for '{name}' at index {index}.");

                InputAction? action = FindAction(name);
                if (action == null || index >= action.BindingCount) continue;
                if (!entry.TryGetProperty("binding", out JsonElement bindingJson))
                    throw new JsonException($"Override for '{name}' is missing its binding.");
                RequireObject(bindingJson);
                if (!bindingJson.TryGetProperty("kind", out JsonElement kind) || kind.ValueKind != JsonValueKind.String)
                    throw new JsonException("Each binding needs a string kind.");
                InputBinding binding = bindingJson.Deserialize<InputBinding>(s_OverridesJsonOptions)
                    ?? throw new JsonException("A binding cannot be null.");
                binding.Validate(action.Type);
                pending.Add((action, index, binding));
            }
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            error = exception.Message;
            return false;
        }

        CancelRebinding();
        foreach (InputAction action in Actions) action.ResetBindings();
        foreach (var entry in pending) entry.Action.OverrideBinding(entry.Index, entry.Binding);
        return true;
    }

    /// <summary>Writes this map's overrides to PlayerPrefs immediately; storage errors are propagated.</summary>
    public void SaveBindingOverrides(string profile = "default")
    {
        string key = BindingProfileKey(profile);
        PlayerPrefs.SetString(key, SaveBindingOverridesToJson());
        PlayerPrefs.Save();
    }

    /// <summary>Returns false for a missing or invalid profile and keeps current bindings unchanged.</summary>
    public bool LoadBindingOverrides(string profile = "default")
    {
        ThrowIfDisposed();
        string json = PlayerPrefs.GetString(BindingProfileKey(profile));
        return !string.IsNullOrEmpty(json) && TryLoadBindingOverridesFromJson(json, out _);
    }

    /// <summary>Deletes the saved profile without changing the current bindings.</summary>
    public void DeleteSavedBindingOverrides(string profile = "default")
    {
        ThrowIfDisposed();
        PlayerPrefs.DeleteKey(BindingProfileKey(profile));
        PlayerPrefs.Save();
    }

    private string BindingProfileKey(string profile)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(profile))
            throw new ArgumentException("A binding profile name must not be empty.", nameof(profile));
        return "Funke.Input:" + Name.Length.ToString(CultureInfo.InvariantCulture) + ":" + Name
            + ":" + profile.Length.ToString(CultureInfo.InvariantCulture) + ":" + profile;
    }

    private static void RequireObject(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new JsonException("Expected a JSON object.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
            if (!names.Add(property.Name)) throw new JsonException($"Duplicate JSON property '{property.Name}'.");
    }

    private static JsonSerializerOptions CreateOverridesJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            MaxDepth = 16,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }

    private sealed record BindingOverrideEntry(string Action, int Index, InputBinding Binding);
    private sealed record BindingOverridesDocument(int SchemaVersion, string Map, List<BindingOverrideEntry> Overrides);
}
