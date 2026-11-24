using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>
/// Settings → Profiles (0.9.0; the mock-ups' "Settings → Profiles" and "A profile: where it's used, what it changes"): a
/// setup for each kind of duty. The page lists the profiles as cards (its name, Now on the one in effect, the duties it's
/// used in, a chip for each thing it changes, Edit) with Usual (your settings as they are) last, and New profile. A
/// profile's page: its name, the kinds of duty it's used in (one profile a kind: one another has is dimmed and says
/// whose), and What it changes, a row a setting with a switch at the right: off, it follows your usual setting; on, it
/// starts at your usual value and its control edits the profile's own. Delete asks first. The layer itself (what's in
/// effect, kept aside, put back) is <see cref="ProfileLayer"/>'s; the switching, the plugin's.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The kind of duty you're in (set by the plugin), for the profile cards' "Now".</summary>
    public Func<DutyKind>? DutyKindHere { get; set; }

    /// <summary>Puts the plan window where the settings say (after a profile's window places come out of effect here).</summary>
    public Action? PlanPlaceBack { get; set; }

    /// <summary>The profile being edited (its id), or null for the list.</summary>
    private string? profileEditing;

    /// <summary>The name field's words while a profile is edited.</summary>
    private string profileName = string.Empty;

    private static readonly string[] OffOnItems = { "Off", "On" };

    /// <summary>Each choice row's words, made once.</summary>
    private static readonly Dictionary<string, string[]> ChoiceItems = ProfileItems.All.ToDictionary(i => i.Id, i => i.Choices.Select(c => c.Label).ToArray());

    private static readonly Dictionary<string, string> ProfileRowIds = ProfileItems.All.ToDictionary(i => i.Id, i => "##prof-" + i.Id);

    private static readonly Dictionary<DutyKind, string> KindIds = DutyKinds.All.ToDictionary(k => k, k => "##prof-kind-" + k);

    /// <summary>The page: the list, or the profile being edited.</summary>
    private void DrawProfiles()
    {
        if (ProfileLayer.ById(config, profileEditing) is { } editing)
        {
            DrawProfileEditor(editing);
            return;
        }
        profileEditing = null;
        DrawProfilesHead();
        Kit.Gap(18);
        var active = ProfileLayer.Active(config);
        for (var i = 0; i < config.Profiles.Count; i++)
        {
            ProfileCard(config.Profiles[i], i, ReferenceEquals(config.Profiles[i], active));
            Kit.Gap(10);
        }
        UsualCard(active == null);
        Kit.Gap(18);
        Txt.Note(config.Profiles.Count == 0
            ? "No profiles yet. A new one starts as a copy of your usual settings, changing nothing until you turn its rows on."
            : "Going into a duty puts its kind's profile in effect, with a short notice (Undo keeps your usual settings until you leave). Changes you make there to what it "
              + "changes, from Settings, the meter's menu or Arrange windows, go into the profile; the rest are your usual settings.");
    }

    private static readonly Run[] ProfileLead = { new(SettingsIndex.Lead(SettingsTab.Profiles), Face.Sans, 13, Palette.Muted) };

    /// <summary>The page's head: its title, its line, New profile at the right.</summary>
    private void DrawProfilesHead()
    {
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        const string add = "New profile";
        var bw = Kit.ButtonWidth(add, BtnSize.Md, Icon.Plus);
        var textW = w - bw - S(16);
        var th = Kit.Callout(dl, at, SettingsIndex.Title(SettingsTab.Profiles), 17, textW, 2, 1.1f);
        var y = at.Y + th + S(6);
        var lh = Txt.Flow(new Vector2(at.X, y), textW, ProfileLead, 19.5f, out _);
        if (Kit.Button("##prof-new", new Vector2(at.X + w - bw, at.Y), add, Btn.Outline, BtnSize.Md, Icon.Plus, 0, false,
                "A new profile changes nothing until you turn its rows on: it starts as your usual settings."))
            Later(NewProfile);
        Kit.Reserve(at, w, MathF.Max(y + lh - at.Y, Kit.ButtonHeight()));
    }

    private void NewProfile()
    {
        var p = new SettingsProfile { Name = ProfileItems.NewName(config.Profiles) };
        config.Profiles.Add(p);
        config.Save();
        EditProfile(p);
    }

    private void EditProfile(SettingsProfile p)
    {
        profileEditing = p.Id;
        profileName = p.Name;
    }

    /// <summary>A profile's card: its name (and Now), where it's used, a chip for each thing it changes, Edit.</summary>
    private void ProfileCard(SettingsProfile p, int index, bool now)
    {
        var fill = now ? Palette.Over(Palette.EmberWash, Palette.Slab) : Palette.Slab;
        Kit.BeginPanel("##prof-card" + p.Id, 14, 16, 14, fill, now ? Palette.EmberLine : Palette.Line, Palette.RadiusLg);
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        const string edit = "Edit";
        var ew = Kit.ButtonWidth(edit, BtnSize.Sm);
        var nameRoom = w - ew - S(12);
        var tagW = now ? Kit.TagWidth("Now", TagKind.Ember) + S(10) : 0;
        var name = Txt.Fit(p.Name, Face.SansSemibold, 14, MathF.Max(S(40), nameRoom - tagW));
        var nw = Txt.Line(dl, at, name, Face.SansSemibold, 14, Palette.White, 22);
        if (now) Kit.Tag(dl, new Vector2(at.X + nw + S(10), at.Y + S(1)), "Now", TagKind.Ember, 20);
        if (Kit.Button("##prof-edit" + index, new Vector2(at.X + w - ew, at.Y - S(3)), edit, Btn.Outline, BtnSize.Sm))
            Later(() => EditProfile(p));
        Kit.Reserve(at, w, S(22));
        Kit.Gap(6);
        var used = DutyKinds.Words(p.UsedIn);
        var lp = Kit.At;
        Txt.Line(dl, lp, Txt.Fit(used.Length > 0 ? "Used in: " + used : "Not used in any duty yet: Edit picks where.", Face.Sans, 13, nameRoom), Face.Sans, 13,
            used.Length > 0 ? Palette.Muted : Palette.Faint, 18.5f);
        Kit.Reserve(lp, w, S(18.5f));
        Kit.Gap(10);
        var chips = ProfileItems.Chips(p);
        var wrap = new Wrap(6);
        if (chips.Count == 0) ChangeChip(wrap, "Changes nothing yet", true);
        foreach (var chip in chips) ChangeChip(wrap, chip, false);
        wrap.End();
        Kit.EndPanel();
    }

    /// <summary>Usual: your settings as they are, everywhere no profile is used and for what a profile doesn't change. Always last; not edited here.</summary>
    private void UsualCard(bool now)
    {
        Kit.BeginPanel("##prof-usual", 14, 16, 14, Palette.Slab, Palette.Line, Palette.RadiusLg);
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        var nw = Txt.Line(dl, at, "Usual", Face.SansSemibold, 14, Palette.White, 22);
        if (now) Kit.Tag(dl, new Vector2(at.X + nw + S(10), at.Y + S(1)), "Now", TagKind.Ember, 20);
        Kit.Reserve(at, w, S(22));
        Kit.Gap(6);
        var lp = Kit.At;
        Txt.Line(dl, lp, Txt.Fit("Everywhere else, and anything a profile doesn’t change", Face.Sans, 13, w), Face.Sans, 13, Palette.Muted, 18.5f);
        Kit.Reserve(lp, w, S(18.5f));
        Kit.Gap(10);
        var wrap = new Wrap(6);
        ChangeChip(wrap, "Your Settings as they are", false);
        wrap.End();
        Kit.EndPanel();
    }

    /// <summary>A chip naming something a profile changes (a small pill on the well).</summary>
    private static void ChangeChip(Wrap wrap, string text, bool faint)
    {
        var label = Txt.Fit(text, Face.SansMedium, 12, Kit.Width - S(20));
        var w = Txt.Width(label, Face.SansMedium, 12) + S(10) * 2;
        var h = S(24);
        var p = wrap.Next(w, h);
        var dl = Kit.Dl;
        dl.AddRectFilled(p, p + new Vector2(w, h), Palette.U32(Palette.Well), h / 2);
        dl.AddRect(p + new Vector2(0.5f), p + new Vector2(w, h) - new Vector2(0.5f), Palette.U32(Palette.Line), h / 2, ImDrawFlags.None, 1f);
        Txt.Line(dl, new Vector2(p.X + S(10), p.Y), label, Face.SansMedium, 12, faint ? Palette.Faint : Palette.Text, 24);
    }

    // ---------------------------------------------------------------------------------------------
    // A profile's page.

    private void DrawProfileEditor(SettingsProfile p)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        // The head: back, the name big, Delete at the right.
        var back = S(HSm);
        if (Kit.IconButton("##prof-back", at, Icon.ChevronLeft, "Back to the profiles", Btn.Quiet, BtnSize.Sm))
            Later(() => profileEditing = null);
        const string delete = "Delete";
        var dw = Kit.ButtonWidth(delete, BtnSize.Sm, Icon.Trash, Btn.Danger);
        var tx = at.X + back + S(10);
        var title = string.IsNullOrWhiteSpace(p.Name) ? "Unnamed" : p.Name;
        var th = Kit.CalloutHeight(title, 22, w - back - S(10) - dw - S(12), 1, 1.1f);
        Kit.Callout(dl, new Vector2(tx, at.Y + (back - th) / 2), title, 22, w - back - S(10) - dw - S(12), 1, 1.1f);
        if (Kit.Button("##prof-delete", new Vector2(at.X + w - dw, at.Y), delete, Btn.Danger, BtnSize.Sm, Icon.Trash, 0, false, "Deletes this profile. It asks first."))
            Ask($"Delete {ProfileItems.Title(p)}?",
                "Its settings go. Where it was used, your usual settings are, from now on.",
                "Delete it", "Keep it", () => DeleteProfile(p), danger: true, yesIcon: Icon.Trash);
        Kit.Reserve(at, w, MathF.Max(back, th));
        Kit.Gap(18);

        // Its name.
        SettingsParts.SectionHead("##prof-name-head", "Name");
        var np = Kit.At;
        if (Kit.Field("##prof-name", np, MathF.Min(w, S(360)), ref profileName, "Ultimate, Savage, Farming…", null, 40))
        {
            var typed = profileName.Trim();
            if (typed.Length > 0 && typed != p.Name)
            {
                p.Name = typed;
                config.Save();
            }
        }
        Kit.Reserve(np, w, S(H));
        Kit.Gap(18);

        // Where it's used.
        SettingsParts.SectionHead("##prof-used-head", "Used in", "One profile a kind of duty.");
        var wrap = new Wrap(6);
        foreach (var kind in DutyKinds.All) KindChip(wrap, p, kind);
        wrap.End();
        Kit.Gap(18);

        // What it changes.
        SettingsParts.SectionHead("##prof-changes-head", "What it changes", "Off: it follows your usual settings.");
        Kit.BeginPanel("##prof-changes" + p.Id, 0, 0, 0);
        for (var i = 0; i < ProfileItems.All.Count; i++) ProfileRow(p, ProfileItems.All[i], i);
        Kit.EndPanel();
        Kit.Gap(18);
        Txt.Note(ReferenceEquals(ProfileLayer.Active(config), p)
            ? "It's in effect now: what you change here, and in Settings for what it changes, is the profile's."
            : "Turned on, a row starts at your usual setting; then it's the profile's own. Window places: Arrange windows in one of its duties puts its windows.");
    }

    private void DeleteProfile(SettingsProfile p)
    {
        var title = ProfileItems.Title(p);
        var wasActive = ReferenceEquals(ProfileLayer.Active(config), p);
        ProfileLayer.Delete(config, config, p);
        profileEditing = null;
        config.Save();
        if (wasActive) PlanPlaceBack?.Invoke();
        Notify($"{title} deleted", ToastKind.Ok, Icon.Trash);
    }

    /// <summary>A kind of duty's chip: picked (ember, a tick), free, or another profile's (dimmed, saying whose).</summary>
    private void KindChip(Wrap wrap, SettingsProfile p, DutyKind kind)
    {
        var picked = p.UsedIn.Contains(kind);
        var other = picked ? null : ProfileItems.ClaimedBy(config.Profiles, kind, p);
        var label = DutyKinds.Name(kind);
        var icon = S(14);
        var tw = Txt.Width(label, Face.SansMedium, 13);
        var w = S(12) + (picked ? icon + S(6) : 0) + tw + S(12);
        var h = S(HSm);
        var pos = wrap.Next(w, h);
        var (clicked, hovered, _) = Kit.Hit(KindIds[kind], pos, new Vector2(w, h), other != null);
        var dl = Kit.Dl;
        var r = S(Palette.RadiusMd);
        var fill = picked ? Palette.Over(Palette.EmberWash, Palette.Well) : hovered && other == null ? Palette.Over(Palette.HoverButton, Palette.Well) : Palette.Well;
        var edge = picked ? Palette.EmberLine : hovered && other == null ? Palette.Line3 : Palette.Line2;
        dl.AddRectFilled(pos, pos + new Vector2(w, h), Palette.U32(fill), r);
        dl.AddRect(pos + new Vector2(0.5f), pos + new Vector2(w, h) - new Vector2(0.5f), Palette.U32(edge), r, ImDrawFlags.None, 1f);
        var x = pos.X + S(12);
        if (picked)
        {
            Icons.Draw(dl, Icon.Check, new Vector2(x, pos.Y + (h - icon) / 2), icon, Palette.Ember);
            x += icon + S(6);
        }
        Txt.Line(dl, new Vector2(x, pos.Y), label, Face.SansMedium, 13, picked ? Palette.White : other != null ? Palette.Disabled : Palette.Muted, HSm);
        Kit.FocusRing(pos, pos + new Vector2(w, h), r);
        if (Kit.Pointed(pos, pos + new Vector2(w, h)))
            Kit.Tooltip(other != null ? $"{DutyKinds.Line(kind)} {ProfileItems.Title(other)} has it: a kind of duty has one profile." : DutyKinds.Line(kind));
        if (!clicked || other != null) return;
        if (picked) p.UsedIn.Remove(kind);
        else p.UsedIn.Add(kind);
        config.Save();
    }

    /// <summary>
    /// A row of What it changes: its name (and line, while it's on), its control and its switch at the right. Off, the
    /// row is dimmed and says "usual"; turned on, it starts at your usual value.
    /// </summary>
    private void ProfileRow(SettingsProfile p, ProfileItem item, int index)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        if (index > 0) Kit.Rule(dl, at.X, at.X + w, at.Y - 0.5f);
        var on = ProfileItems.Changes(p, item);
        var field = item.Fields[0];
        var value = on ? ProfileLayer.Value(config, config, p, field) : null;
        var sw = Kit.SwitchSize;
        var x = at.X + S(16);
        var right = at.X + w - S(16);
        var controlW = on ? ControlWidth(item, value) : Txt.Width("usual", Face.Sans, 12);
        var wordsW = right - sw.X - S(14) - controlW - S(14) - x;
        var nameH = S(14 * 1.3f);
        var lineH = on ? S(2) + S(17.4f) : 0;
        var h = S(10) * 2 + MathF.Max(nameH + lineH, S(HSm));
        var top = at.Y + (h - nameH - lineH) / 2;
        Txt.Line(dl, new Vector2(x, top), Txt.Fit(item.Name, Face.SansMedium, 14, wordsW), Face.SansMedium, 14, on ? Palette.White : Palette.Faint, 14 * 1.3f);
        if (on) Txt.Line(dl, new Vector2(x, top + nameH + S(2)), Txt.Fit(item.Line, Face.Sans, 12, wordsW), Face.Sans, 12, Palette.Muted, 17.4f);
        if (Kit.Pointed(new Vector2(x, top), new Vector2(x + wordsW, top + nameH + lineH))) Kit.Tooltip(item.Line);
        var sx = right - sw.X;
        var cx = sx - S(14) - controlW;
        if (on) DrawControl(p, item, value, new Vector2(cx, at.Y + (h - S(HSm)) / 2), at.Y + h / 2);
        else Txt.Line(dl, new Vector2(cx, at.Y + (h - S(17.4f)) / 2), "usual", Face.Sans, 12, Palette.Faint, 17.4f);
        if (Kit.Switch(ProfileRowIds[item.Id], new Vector2(sx, at.Y + (h - sw.Y) / 2), on))
        {
            if (on) ProfileLayer.Follow(config, config, p, item);
            else ProfileLayer.Override(config, config, p, item);
            config.Save();
            // Window places changed for the profile in effect: the plan window goes where they now say (the others follow by themselves).
            if (item.Id == ProfileItems.PlacesId && ReferenceEquals(ProfileLayer.Active(config), p)) PlanPlaceBack?.Invoke();
        }
        Kit.Reserve(at, w, h);
    }

    private static float ControlWidth(ProfileItem item, string? value) => item.Control switch
    {
        ProfileControl.Switch => Kit.SegmentedWidth(OffOnItems),
        ProfileControl.Choice when item.Choices.Count <= 3 => Kit.SegmentedWidth(ChoiceItems[item.Id]),
        ProfileControl.Choice => Kit.StepperWidth(ChoiceLabel(item, value)),
        ProfileControl.Count => Kit.StepperWidth(value ?? "0"),
        ProfileControl.Size => Kit.StepperWidth(SizeLabel(value)),
        _ => Kit.TagWidth("Own places", TagKind.Ember),
    };

    private static string ChoiceLabel(ProfileItem item, string? value) => item.Choices.FirstOrDefault(c => c.Value == value).Label ?? value ?? string.Empty;

    private static string SizeLabel(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v != WindowSize.Fit
            ? WindowSize.Clamp(v).ToString(CultureInfo.InvariantCulture) + "%"
            : "Fit";

    /// <summary>A row's control, editing the profile's own value (the live one too while it's in effect).</summary>
    private void DrawControl(SettingsProfile p, ProfileItem item, string? value, Vector2 pos, float middle)
    {
        var id = ProfileRowIds[item.Id] + "-value";
        var field = item.Fields[0];
        string? to = null;
        switch (item.Control)
        {
            case ProfileControl.Switch:
            {
                var picked = Kit.Segmented(id, pos, OffOnItems, value == "true" ? 1 : 0);
                if (picked != (value == "true" ? 1 : 0)) to = picked == 1 ? "true" : "false";
                break;
            }
            case ProfileControl.Choice when item.Choices.Count <= 3:
            {
                var at = 0;
                for (var i = 0; i < item.Choices.Count; i++)
                    if (item.Choices[i].Value == value)
                        at = i;
                var picked = Kit.Segmented(id, pos, ChoiceItems[item.Id], at);
                if (picked != at) to = item.Choices[picked].Value;
                break;
            }
            case ProfileControl.Choice:
            {
                var step = Kit.Stepper(id, new Vector2(pos.X, middle - S(H) / 2), ChoiceLabel(item, value), true, true);
                if (step != 0) to = ProfileItems.StepChoice(item, value, step);
                break;
            }
            case ProfileControl.Count:
            {
                var n = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : item.Min;
                var step = Kit.Stepper(id, new Vector2(pos.X, middle - S(H) / 2), n.ToString(CultureInfo.InvariantCulture), n > item.Min, n < item.Max);
                if (step != 0) to = Math.Clamp(n + step, item.Min, item.Max).ToString(CultureInfo.InvariantCulture);
                break;
            }
            case ProfileControl.Size:
            {
                var n = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : WindowSize.Fit;
                var step = Kit.Stepper(id, new Vector2(pos.X, middle - S(H) / 2), SizeLabel(value), true, true);
                if (step != 0) to = ProfileItems.StepSize(n, step).ToString(CultureInfo.InvariantCulture);
                break;
            }
            default:
                Kit.Tag(Kit.Dl, new Vector2(pos.X, middle - S(10)), "Own places", TagKind.Ember, 20);
                break;
        }
        if (to == null || to == value) return;
        ProfileLayer.SetValue(config, config, p, field, to);
        config.Save();
    }

    // ---------------------------------------------------------------------------------------------
    // The note on a Settings row the profile in effect sets.

    /// <summary>The words a Settings row says under it while the profile in effect sets it (<see cref="SetRow"/>); null otherwise.</summary>
    private string? rowProfileNote;

    private string? ProfileNoteFor(SettingId id) => ProfileLayer.Sets(config, id) is { } p ? ProfileItems.SetsNote(p) : null;
}
