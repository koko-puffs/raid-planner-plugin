using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace RaidPlanner.UI.Model;

/// <summary>
/// A settings profile (0.9.0; Settings → Profiles): a name, the kinds of duty it's used in, and the settings it changes,
/// each by its field's path in the configuration ("MeterCompact", "CalloutOverlay.Show") with its value as text
/// (<see cref="ProfileFields"/>). A setting it doesn't list follows your usual settings; a new profile lists none.
/// </summary>
[Serializable]
public sealed class SettingsProfile
{
    /// <summary>Its own id, kept as it's renamed.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "New profile";

    /// <summary>The kinds of duty it's used in (one profile a kind: the editor stops a second claiming one).</summary>
    public List<DutyKind> UsedIn { get; set; } = new();

    /// <summary>The settings it changes: a field's path to its value (<see cref="ProfileFields.Text"/>).</summary>
    public Dictionary<string, string> Values { get; set; } = new();
}

/// <summary>
/// Where the profiles live and which is in effect (the configuration): the profiles; the one in effect (null: your usual
/// settings); and, while one is, your usual values of the fields it changes, kept with the settings so a crash or a reload
/// while it's in effect gives them back (<see cref="ProfileLayer.Recover"/>).
/// </summary>
public interface IProfileHost
{
    List<SettingsProfile> Profiles { get; }

    string? ProfileActive { get; set; }

    Dictionary<string, string> ProfileBackup { get; }
}

/// <summary>
/// Reads and writes a configuration's settings by their path ("ShowMeter", "Cooldowns.Show": a property, or a property of
/// a property), as invariant text: true/false, whole numbers, floats round-tripped, enums by name. Public properties with
/// a getter and a setter only; a path that isn't one reads null and writes nothing.
/// </summary>
public static class ProfileFields
{
    private static readonly Dictionary<(Type, string), PropertyInfo[]?> Paths = new();

    private static PropertyInfo[]? Resolve(Type type, string path)
    {
        lock (Paths)
        {
            if (Paths.TryGetValue((type, path), out var known)) return known;
            var parts = path.Split('.');
            var props = new PropertyInfo[parts.Length];
            var t = type;
            for (var i = 0; i < parts.Length; i++)
            {
                var p = t.GetProperty(parts[i], BindingFlags.Public | BindingFlags.Instance);
                var last = i == parts.Length - 1;
                if (p == null || p.GetMethod == null || p.GetIndexParameters().Length > 0 || (last && p.SetMethod is not { IsPublic: true }) || (!last && p.PropertyType.IsValueType))
                {
                    props = null;
                    break;
                }
                props[i] = p;
                t = p.PropertyType;
            }
            Paths[(type, path)] = props;
            return props;
        }
    }

    /// <summary>The property a path ends at, and the object it's on; null when it isn't one.</summary>
    private static (object Owner, PropertyInfo Prop)? Find(object root, string path)
    {
        var props = Resolve(root.GetType(), path);
        if (props == null) return null;
        var owner = root;
        for (var i = 0; i < props.Length - 1; i++)
        {
            owner = props[i].GetValue(owner)!;
            if (owner == null) return null;
        }
        return (owner, props[^1]);
    }

    /// <summary>Whether a path is a setting this can read and write.</summary>
    public static bool Exists(object root, string path) => Find(root, path) != null;

    /// <summary>The type a path's value is (null when it isn't one).</summary>
    public static Type? TypeOf(Type rootType, string path) => Resolve(rootType, path) is { } p ? p[^1].PropertyType : null;

    /// <summary>A setting's value as text; null when the path isn't one.</summary>
    public static string? Get(object root, string path) => Find(root, path) is { } f ? Text(f.Prop.GetValue(f.Owner)) : null;

    /// <summary>Sets a setting from its text; false (nothing changed) when the path isn't one or the text isn't its kind of value.</summary>
    public static bool Set(object root, string path, string text)
    {
        if (Find(root, path) is not { } f || !TryParse(f.Prop.PropertyType, text, out var value)) return false;
        f.Prop.SetValue(f.Owner, value);
        return true;
    }

    /// <summary>A value as invariant text.</summary>
    public static string Text(object? value) => value switch
    {
        null => string.Empty,
        bool b => b ? "true" : "false",
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        Enum e => e.ToString(),
        IFormattable x => x.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>Reads text as a value of a type (bool, int, float, double, an enum by name or number, string).</summary>
    public static bool TryParse(Type type, string text, out object? value)
    {
        value = null;
        text = text?.Trim() ?? string.Empty;
        if (type == typeof(string))
        {
            value = text;
            return true;
        }
        if (type == typeof(bool) && bool.TryParse(text, out var b))
        {
            value = b;
            return true;
        }
        if (type == typeof(int) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
        {
            value = i;
            return true;
        }
        if (type == typeof(float) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && float.IsFinite(f))
        {
            value = f;
            return true;
        }
        if (type == typeof(double) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d))
        {
            value = d;
            return true;
        }
        if (type.IsEnum && Enum.TryParse(type, text, false, out var e) && e != null && Enum.IsDefined(type, e))
        {
            value = e;
            return true;
        }
        return false;
    }
}

/// <summary>How a profile's row edits its value (the view draws each).</summary>
public enum ProfileControl
{
    /// <summary>Off / On (a bool).</summary>
    Switch,

    /// <summary>One of a few (<see cref="ProfileItem.Choices"/>): segmented, or stepped through when there are many.</summary>
    Choice,

    /// <summary>A number from <see cref="ProfileItem.Min"/> to <see cref="ProfileItem.Max"/>, stepped.</summary>
    Count,

    /// <summary>The windows' size (<see cref="WindowSize"/>): fit the screen, or 80–200%.</summary>
    Size,

    /// <summary>Every window's place: no value to edit, Arrange windows does it.</summary>
    Places,
}

/// <summary>A choice's words and the value it sets.</summary>
public readonly record struct ProfileChoice(string Label, string Value);

/// <summary>
/// A row of a profile's What it changes: its id, name and line, its control, the configuration's fields it changes
/// (<see cref="ProfileFields"/> paths; Window places has many), the Settings rows it shadows (they say the profile sets
/// them while it's in effect), and the words its chip says (<see cref="ProfileItems.Chip"/>).
/// </summary>
public sealed record ProfileItem(string Id, string Name, string Line, ProfileControl Control, IReadOnlyList<string> Fields, IReadOnlyList<SettingId> Rows)
{
    /// <summary>A switch's chip, on and off ("Callouts on", "Callouts off"); a choice's chip lead ("Meter"); Places' chip.</summary>
    public string On { get; init; } = string.Empty;

    public string Off { get; init; } = string.Empty;

    /// <summary>A choice's items.</summary>
    public IReadOnlyList<ProfileChoice> Choices { get; init; } = Array.Empty<ProfileChoice>();

    /// <summary>A count's least and most (its chip is <see cref="On"/> with the number for {0}: "Callouts {0} at once").</summary>
    public int Min { get; init; }

    public int Max { get; init; }
}

/// <summary>
/// The settings a profile can change (Settings → Profiles' What it changes, in this order). Adding one is a line here
/// (a field of the configuration and the Settings rows it shadows); a new overlay's place is a line in <see cref="PlaceFields"/>.
/// </summary>
public static class ProfileItems
{
    private static ProfileItem Switch(string id, string name, string line, string field, string on, string off, params SettingId[] rows) =>
        new(id, name, line, ProfileControl.Switch, new[] { field }, rows) { On = on, Off = off };

    private static ProfileItem Choice(string id, string name, string line, string field, string lead, ProfileChoice[] choices, params SettingId[] rows) =>
        new(id, name, line, ProfileControl.Choice, new[] { field }, rows) { On = lead, Choices = choices };

    private static ProfileItem Count(string id, string name, string line, string field, int min, int max, string chip, params SettingId[] rows) =>
        new(id, name, line, ProfileControl.Count, new[] { field }, rows) { Min = min, Max = max, On = chip };

    /// <summary>An overlay's place (<see cref="OverlaySettings"/>): its anchor, which corner that is, and its width.</summary>
    private static string[] Overlay(string field) =>
        new[] { field + ".AnchorX", field + ".AnchorY", field + ".Anchored", field + ".AnchorFor", field + ".Width" };

    /// <summary>
    /// Window places' fields: the meter's anchor and the height you gave it, each overlay's anchor and width, the plan
    /// window's place and size. Arrange windows (and moving a window) while the profile's in effect saves into it.
    /// </summary>
    public static readonly IReadOnlyList<string> PlaceFields = new[]
        {
            new[] { "MeterAnchorX", "MeterAnchorY", "MeterAnchored", "MeterAnchorFor", "MeterHeight" },
            Overlay("CalloutOverlay"),
            Overlay("Cooldowns"),
            Overlay("ReadyCheck"),
            Overlay("DeathRecap"),
            new[] { "PlanPlaceX", "PlanPlaceY", "PlanPlaceW", "PlanPlaceH", "PlanPlaced" },
        }.SelectMany(f => f).ToArray();

    public const string PlacesId = "places";

    /// <summary>Every setting a profile can change, in What it changes' order.</summary>
    public static readonly IReadOnlyList<ProfileItem> All = new[]
    {
        Switch("meter", "Damage meter", "Shown in these duties.", "ShowMeter", "Meter on", "Meter off", SettingId.ShowMeter),
        Choice("meter-size", "Meter size", "Compact or Detailed.", "MeterCompact", "Meter",
            new[] { new ProfileChoice("Compact", "true"), new ProfileChoice("Detailed", "false") }, SettingId.MeterSize),
        Choice("meter-theme", "Meter theme", "Both meters' look.", "MeterTheme", "Theme",
            MeterThemes.All.Select(t => new ProfileChoice(t.Name, t.Id.ToString())).ToArray(), SettingId.MeterTheme),
        Switch("meter-hide", "Meter out of combat", "Hidden where it isn't kept open.", "MeterHideOutOfCombat", "Meter hidden out of combat",
            "Meter shown out of combat", SettingId.MeterHide),
        Switch("meter-open", "Meter between pulls", "Kept open in duties, on the last pull.", "MeterOpenInDuties", "Meter kept open", "Meter folds between pulls",
            SettingId.MeterOpenInDuties),
        Switch("callouts", "Callouts", "On screen, while your plan follows a pull.", "CalloutOverlay.Show", "Callouts on", "Callouts off", SettingId.CalloutShow),
        Count("callout-count", "Callouts at once", "The soonest first.", "CalloutCount", 1, 5, "Callouts {0} at once", SettingId.CalloutCount),
        Switch("callout-small", "Callouts' size", "Smaller words and rows.", "CalloutOverlay.Compact", "Callouts smaller", "Callouts big", SettingId.CalloutCompact),
        Switch("plan", "Plan window", "Opens in these duties when you have a plan.", "ShowPlanInDuties", "Plan window on", "Plan window off", SettingId.ShowPlan),
        Switch("death-recap", "Death recap", "Whose deaths get a card.", "DeathRecapEveryone", "Recap everyone's deaths", "Recap your deaths",
            SettingId.DeathRecapWho),
        Switch("cooldowns", "Party cooldowns", "Shown in these duties.", "Cooldowns.Show", "Party cooldowns on", "Party cooldowns off", SettingId.CdShow),
        new ProfileItem(PlacesId, "Window places", "Its own places for every window: Arrange windows in these duties moves these.", ProfileControl.Places,
            PlaceFields, new[] { SettingId.Arrange }) { On = "Own window places" },
        new ProfileItem("size", "Window size", "Every window's, beside Dalamud's scale.", ProfileControl.Size, new[] { "WindowSize" },
            new[] { SettingId.WindowFit, SettingId.WindowScale }),
    };

    /// <summary>A row by its id (null when there's none).</summary>
    public static ProfileItem? ById(string id) => All.FirstOrDefault(i => i.Id == id);

    /// <summary>The row that changes a Settings row, if one does (the first).</summary>
    public static IEnumerable<ProfileItem> ForRow(SettingId row) => All.Where(i => i.Rows.Contains(row));

    /// <summary>Whether a profile changes a row (any of its fields; the layer fills in the rest).</summary>
    public static bool Changes(SettingsProfile p, ProfileItem item) => item.Fields.Any(p.Values.ContainsKey);

    /// <summary>The words a row's value says on a profile's card and in its notice: "Meter Detailed", "3 at once", "Callouts big".</summary>
    public static string Chip(ProfileItem item, string? value)
    {
        switch (item.Control)
        {
            case ProfileControl.Switch:
                return value == "true" ? item.On : item.Off;
            case ProfileControl.Choice:
                var label = item.Choices.FirstOrDefault(c => c.Value == value).Label ?? value ?? string.Empty;
                return string.IsNullOrEmpty(item.On) ? label : item.On + " " + label;
            case ProfileControl.Count:
                return string.Format(CultureInfo.InvariantCulture, item.On, value);
            case ProfileControl.Size:
                return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) && size != WindowSize.Fit
                    ? $"Windows at {WindowSize.Clamp(size).ToString(CultureInfo.InvariantCulture)}%"
                    : "Windows fit the screen";
            default:
                return item.On;
        }
    }

    /// <summary>The chips of what a profile changes, in What it changes' order.</summary>
    public static List<string> Chips(SettingsProfile p)
    {
        var chips = new List<string>();
        foreach (var item in All)
            if (Changes(p, item))
                chips.Add(Chip(item, p.Values.TryGetValue(item.Fields[0], out var v) ? v : null));
        return chips;
    }

    /// <summary>
    /// What the notice says as a profile comes into effect: "Ultimate profile: meter Detailed, callouts big" (the first
    /// three things it changes, "and 2 more" after); just "Ultimate profile" when it changes nothing.
    /// </summary>
    public static string Summary(SettingsProfile p)
    {
        var head = Title(p);
        var chips = Chips(p);
        if (chips.Count == 0) return head;
        static string Lower(string s) => s.Length > 1 && char.IsUpper(s[0]) && !char.IsUpper(s[1]) ? char.ToLowerInvariant(s[0]) + s[1..] : s;
        var shown = chips.Take(3).Select(Lower).ToList();
        var more = chips.Count - shown.Count;
        return $"{head}: {string.Join(", ", shown)}{(more > 0 ? $" and {more.ToString(CultureInfo.InvariantCulture)} more" : string.Empty)}";
    }

    /// <summary>"Ultimate profile" (a name that ends in "profile" as it is).</summary>
    public static string Title(SettingsProfile p)
    {
        var name = string.IsNullOrWhiteSpace(p.Name) ? "Unnamed" : p.Name.Trim();
        return name.EndsWith("profile", StringComparison.OrdinalIgnoreCase) ? name : name + " profile";
    }

    /// <summary>What a Settings row says while the profile in effect sets it: "The Ultimate profile sets this here; changes go to it."</summary>
    public static string SetsNote(SettingsProfile p) => $"The {Title(p)} sets this here; changes go to it.";

    /// <summary>The window size a Size row steps to: fit, then 80% to 200% in 10s (a step down from fit is 200%, up from 200% is fit).</summary>
    public static int StepSize(int value, int by)
    {
        var steps = new List<int> { WindowSize.Fit };
        for (var s = WindowSize.Smallest; s <= WindowSize.Largest; s += WindowSize.Step) steps.Add(s);
        var at = steps.IndexOf(value == WindowSize.Fit ? WindowSize.Fit : WindowSize.Clamp(value));
        if (at < 0) at = 0;
        return steps[((at + by) % steps.Count + steps.Count) % steps.Count];
    }

    /// <summary>A choice stepped by one, round (for a choice of many, drawn as a stepper).</summary>
    public static string StepChoice(ProfileItem item, string? value, int by)
    {
        if (item.Choices.Count == 0) return value ?? string.Empty;
        var at = 0;
        for (var i = 0; i < item.Choices.Count; i++)
            if (item.Choices[i].Value == value)
                at = i;
        return item.Choices[((at + by) % item.Choices.Count + item.Choices.Count) % item.Choices.Count].Value;
    }

    /// <summary>A new profile's name: "New profile", or "New profile 2" and on when that's taken.</summary>
    public static string NewName(IEnumerable<SettingsProfile> profiles)
    {
        var names = new HashSet<string>(profiles.Select(p => p.Name.Trim()), StringComparer.OrdinalIgnoreCase);
        if (!names.Contains("New profile")) return "New profile";
        for (var n = 2; ; n++)
            if (!names.Contains($"New profile {n.ToString(CultureInfo.InvariantCulture)}"))
                return $"New profile {n.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>The profile (other than <paramref name="except"/>) already used in a kind of duty, if one is.</summary>
    public static SettingsProfile? ClaimedBy(IEnumerable<SettingsProfile> profiles, DutyKind kind, SettingsProfile? except = null) =>
        profiles.FirstOrDefault(p => !ReferenceEquals(p, except) && p.UsedIn.Contains(kind));
}

/// <summary>
/// Profiles as a layer over the configuration, which the plugin reads directly everywhere. In effect, a profile's values
/// are on the live configuration and your usual values of those fields are kept aside (<see cref="IProfileHost.ProfileBackup"/>,
/// saved with the settings). A change made then to a field the profile changes (from Settings, the meter's menu, Arrange
/// windows) is the profile's: it's copied back into the profile (<see cref="Capture"/>, as the settings are saved and as it
/// goes out of effect); a change to any other field is your usual settings'. Going out of effect puts your usual values back.
/// </summary>
public static class ProfileLayer
{
    /// <summary>The profile in effect (null: your usual settings, or one that's gone).</summary>
    public static SettingsProfile? Active(IProfileHost host) =>
        host.ProfileActive is { } id ? host.Profiles.FirstOrDefault(p => p.Id == id) : null;

    /// <summary>A profile by id.</summary>
    public static SettingsProfile? ById(IProfileHost host, string? id) => id == null ? null : host.Profiles.FirstOrDefault(p => p.Id == id);

    /// <summary>Your usual value of a field: kept aside while a profile changes it, else the live one.</summary>
    public static string? Usual(IProfileHost host, object config, string field) =>
        host.ProfileBackup.TryGetValue(field, out var kept) ? kept : ProfileFields.Get(config, field);

    /// <summary>Whether a field is the profile in effect's now.</summary>
    public static bool Overridden(IProfileHost host, string field) => host.ProfileActive != null && host.ProfileBackup.ContainsKey(field);

    /// <summary>The profile in effect, if it changes a Settings row (for its note there).</summary>
    public static SettingsProfile? Sets(IProfileHost host, SettingId row)
    {
        if (host.ProfileActive == null || host.ProfileBackup.Count == 0) return null;
        foreach (var item in ProfileItems.ForRow(row))
            if (item.Fields.Any(host.ProfileBackup.ContainsKey))
                return Active(host);
        return null;
    }

    /// <summary>
    /// The profile in effect's fields copied from the live configuration into it: what was changed while it was in
    /// effect is its own. The plugin does it as it saves the settings. Nothing in effect: nothing.
    /// </summary>
    public static void Capture(IProfileHost host, object config)
    {
        if (Active(host) is not { } p) return;
        foreach (var field in host.ProfileBackup.Keys)
            if (ProfileFields.Get(config, field) is { } live)
                p.Values[field] = live;
    }

    /// <summary>
    /// Puts a profile in effect (the one in effect goes out first): each field it changes kept aside as your usual
    /// value and set to the profile's. A row it changes only some fields of (a window added since) is filled in from
    /// your usual settings first. False when it's in effect already or there's none by that id.
    /// </summary>
    public static bool Activate(IProfileHost host, object config, string id)
    {
        if (host.ProfileActive == id && Active(host) != null) return false;
        if (ById(host, id) is not { } p) return false;
        Deactivate(host, config);
        Fill(p, config);
        host.ProfileBackup.Clear();
        foreach (var (field, value) in p.Values.ToList())
        {
            if (ProfileFields.Get(config, field) is not { } usual) continue;
            if (!ProfileFields.Set(config, field, value)) continue;
            host.ProfileBackup[field] = usual;
        }
        host.ProfileActive = p.Id;
        return true;
    }

    /// <summary>
    /// Takes the profile in effect out (what was changed meanwhile kept in it), putting your usual values back. Also
    /// what's done with a backup left by a crash, or one whose profile was deleted. False when nothing was in effect.
    /// </summary>
    public static bool Deactivate(IProfileHost host, object config)
    {
        if (host.ProfileActive == null && host.ProfileBackup.Count == 0) return false;
        Capture(host, config);
        foreach (var (field, usual) in host.ProfileBackup) ProfileFields.Set(config, field, usual);
        host.ProfileBackup.Clear();
        host.ProfileActive = null;
        return true;
    }

    /// <summary>
    /// As the plugin loads: a profile left in effect (the game closed or the plugin reloaded in a duty) is taken out,
    /// your usual values put back, before anything reads them; the duty you're in puts it in again. True when it did.
    /// </summary>
    public static bool Recover(IProfileHost host, object config) => Deactivate(host, config);

    /// <summary>A row a profile changes some fields of gets the rest from the configuration (your usual settings).</summary>
    private static void Fill(SettingsProfile p, object config)
    {
        foreach (var item in ProfileItems.All)
        {
            if (!ProfileItems.Changes(p, item)) continue;
            foreach (var field in item.Fields)
                if (!p.Values.ContainsKey(field) && ProfileFields.Get(config, field) is { } v)
                    p.Values[field] = v;
        }
    }

    /// <summary>
    /// Turns a row on in a profile: it starts at your usual values, and the profile's own from then on. In effect,
    /// nothing visible changes (the live value is your usual one); it's kept aside so it comes back as the profile goes.
    /// </summary>
    public static void Override(IProfileHost host, object config, SettingsProfile p, ProfileItem item)
    {
        var live = host.ProfileActive == p.Id;
        foreach (var field in item.Fields)
        {
            if (Usual(host, config, field) is not { } usual) continue;
            if (live && !host.ProfileBackup.ContainsKey(field)) host.ProfileBackup[field] = usual;
            p.Values[field] = live ? ProfileFields.Get(config, field) ?? usual : usual;
        }
    }

    /// <summary>Turns a row off in a profile: it follows your usual settings again (put back now when the profile's in effect).</summary>
    public static void Follow(IProfileHost host, object config, SettingsProfile p, ProfileItem item)
    {
        var live = host.ProfileActive == p.Id;
        foreach (var field in item.Fields)
        {
            p.Values.Remove(field);
            if (live && host.ProfileBackup.Remove(field, out var usual)) ProfileFields.Set(config, field, usual);
        }
    }

    /// <summary>A profile's value of a field (the live one while it's in effect: changed since, it's newer); null when it doesn't change it.</summary>
    public static string? Value(IProfileHost host, object config, SettingsProfile p, string field)
    {
        if (!p.Values.TryGetValue(field, out var v)) return null;
        return host.ProfileActive == p.Id && host.ProfileBackup.ContainsKey(field) ? ProfileFields.Get(config, field) ?? v : v;
    }

    /// <summary>Sets a profile's value of a field it changes (the live one too while it's in effect).</summary>
    public static void SetValue(IProfileHost host, object config, SettingsProfile p, string field, string value)
    {
        p.Values[field] = value;
        if (host.ProfileActive == p.Id && host.ProfileBackup.ContainsKey(field)) ProfileFields.Set(config, field, value);
    }

    /// <summary>Deletes a profile (taken out of effect first: your usual settings come back).</summary>
    public static void Delete(IProfileHost host, object config, SettingsProfile p)
    {
        if (host.ProfileActive == p.Id) Deactivate(host, config);
        host.Profiles.Remove(p);
    }

    /// <summary>The profile a kind of duty wants: the first used in it (null: your usual settings).</summary>
    public static SettingsProfile? For(IProfileHost host, DutyKind kind) => host.Profiles.FirstOrDefault(p => p.UsedIn.Contains(kind));
}

/// <summary>What a switch did: the profile that came into effect (null: back to your usual settings), the one that went, and whether windows' places moved.</summary>
public sealed record ProfileChange(SettingsProfile? Entered, SettingsProfile? Left, bool Places);

/// <summary>
/// Puts the profile for the duty you're in in effect, and your usual settings back as you leave (asked each frame with
/// where you are). Undo (the notice's) takes it out until you leave that place. While <c>wait</c> (Arrange windows is on)
/// nothing switches.
/// </summary>
public sealed class ProfileSwitcher
{
    private readonly IProfileHost host;
    private readonly object config;
    private uint? place;
    private bool held;

    public ProfileSwitcher(IProfileHost host, object config)
    {
        this.host = host;
        this.config = config;
    }

    /// <summary>The kind of duty last asked about.</summary>
    public DutyKind Kind { get; private set; } = DutyKind.OpenWorld;

    /// <summary>Undo was pressed here: no profile until you leave.</summary>
    public bool Held => held;

    /// <summary>Switches when the place's kind wants another profile than the one in effect; null when nothing changed.</summary>
    public ProfileChange? Update(uint territory, DutyKind kind, bool wait)
    {
        if (place != territory)
        {
            place = territory;
            held = false;
        }
        Kind = kind;
        if (wait) return null;
        var want = held ? null : ProfileLayer.For(host, kind);
        var now = ProfileLayer.Active(host);
        var stale = now == null && (host.ProfileActive != null || host.ProfileBackup.Count > 0);
        if (want == now && !stale) return null;
        var places = Moves(now) || Moves(want);
        if (want == null)
        {
            ProfileLayer.Deactivate(host, config);
            return new ProfileChange(null, now, places);
        }
        ProfileLayer.Activate(host, config, want.Id);
        return new ProfileChange(want, now, places);
    }

    /// <summary>The notice's Undo: the profile out until you leave this place.</summary>
    public ProfileChange? Undo()
    {
        held = true;
        var now = ProfileLayer.Active(host);
        if (!ProfileLayer.Deactivate(host, config)) return null;
        return new ProfileChange(null, now, Moves(now));
    }

    private static bool Moves(SettingsProfile? p) => p != null && ProfileItems.ById(ProfileItems.PlacesId) is { } item && ProfileItems.Changes(p, item);
}
