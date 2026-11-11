using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using RaidPlanner.Core;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>
/// Sending a pull to the Raid Planner, saving it as a file, and linking your account (Settings →
/// Account), as the mock-ups' "Sending" draws it: not linked, Link your account is the telegraph;
/// linked, Send to Raid Planner in the same place; sending, the button holds still and dims over a
/// cast bar, with a quiet Stop; sent, a ready tick and Open in Review; failed, a red box that says
/// what to do and the one button that does it. A send reads the pull's file and posts it from a
/// worker. The token is never logged.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>A send under way (no result yet) or done, by the pull's file: its stop, its size, when it began.</summary>
    private sealed record SendState(SendResult? Result, CancellationTokenSource? Stop = null, long Bytes = 0);

    private readonly ConcurrentDictionary<string, SendState> sends = new(StringComparer.OrdinalIgnoreCase);

    private string tokenDraft = string.Empty;
    private string? tokenProblem;

    private bool Linked => SiteLink.LooksRight(config.SiteToken);

    private bool Sending(RecordingInfo info) => sends.TryGetValue(info.File, out var s) && s.Result == null;

    /// <summary>
    /// Stops a send under way. Its worker disposes the stop when it's done, which can be on the very
    /// frame Stop is pressed (the answer came meanwhile): then there's nothing left to stop.
    /// </summary>
    private static void StopSend(SendState s)
    {
        try
        {
            s.Stop?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // It finished as Stop was pressed: how it went is said as usual.
        }
    }

    /// <summary>
    /// Sends a kept pull to your Raid Planner (Send each pull, /raidplan send last); <paramref name="done"/>
    /// hears how it went, on the framework thread. False when it can't go now (not linked, already going).
    /// </summary>
    public bool SendPull(RecordingInfo info, Action<SendResult>? done = null)
    {
        if (!Linked || Sending(info)) return false;
        Send(info, done);
        return true;
    }

    /// <summary>Sends a kept pull to your Raid Planner (it waits there under From the game).</summary>
    private void Send(RecordingInfo info, Action<SendResult>? done = null)
    {
        if (!Linked || Sending(info)) return;
        var token = config.SiteToken;
        var file = info.File;
        var http = site.Http;
        var stop = CancellationTokenSource.CreateLinkedTokenSource(site.Closing);
        var cancel = stop.Token;
        long bytes = 0;
        try
        {
            bytes = new FileInfo(store.PathOf(info)).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The size is only said while it goes.
        }
        sends[file] = new SendState(null, stop, bytes);
        _ = Task.Run(async () =>
        {
            SendResult result;
            try
            {
                var json = await File.ReadAllTextAsync(store.PathOf(info), cancel).ConfigureAwait(false);
                result = await SiteLink.SendAsync(http, token, info.Name, json, SiteLink.ClientId(info.File, info.RecordedAt), cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                result = new SendResult(null, "Stopped.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result = new SendResult(null, "The pull's file couldn't be read.");
            }
            catch (Exception ex)
            {
                // SendAsync says what went wrong in words; anything else is a fault here (it holds no token).
                Services.Log.Error(ex, "Send to Raid Planner failed");
                result = new SendResult(null, "Something went wrong sending it (see /xllog).");
            }
            finally
            {
                stop.Dispose();
            }
            // Unloaded meanwhile: the plugin loaded again keeps the list now, so this one leaves it alone.
            if (site.Closing.IsCancellationRequested) return;
            if (result.Sent) store.MarkSent(info, result.Id!, DateTimeOffset.UtcNow);
            // Stopped by hand: as if it hadn't been sent at all.
            if (result.Why == "Stopped.") sends.TryRemove(file, out _);
            else sends[file] = new SendState(result);
            try
            {
                _ = Services.Framework.RunOnFrameworkThread(() =>
                {
                    if (result.Sent)
                    {
                        var id = result.Id!;
                        Notify("Sent to your Raid Planner", ToastKind.Ok, action: "Open", onAction: () => Dalamud.Utility.Util.OpenLink(ReviewUrl(info, id)));
                    }
                    else if (result.Why != "Stopped.")
                    {
                        Notify("Couldn't send it", ToastKind.Bad, action: "Why", onAction: () => ShowPull(info));
                    }
                    try
                    {
                        done?.Invoke(result);
                    }
                    catch (Exception ex)
                    {
                        Services.Log.Error(ex, "Raid Planner couldn't say how the send went");
                    }
                });
            }
            catch (Exception)
            {
                // The plugin was unloaded while it went.
            }
        });
    }

    /// <summary>
    /// Where Open in Review goes: the plan for the pull's duty in Review on that pull, or From the game
    /// (the site's dialog for a sent pull) when the plugin has no plan for that duty.
    /// </summary>
    private string ReviewUrl(RecordingInfo info, string sentId)
    {
        if (plans.Plan is { Game: { } g } plan && string.Equals(g.Duty, info.Duty, StringComparison.OrdinalIgnoreCase))
            return $"{SiteLink.PlannerUrl}/plan/{Uri.EscapeDataString(plan.Id)}/review?pull={Uri.EscapeDataString(sentId)}";
        return SiteLink.OpenUrl(sentId);
    }

    /// <summary>Saves a copy of a pull's file wherever you like (to import on the site, or to share).</summary>
    private void SaveAs(RecordingInfo info)
    {
        var start = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var name = Path.GetFileName(info.File);
        if (name.EndsWith(Words.Extension, StringComparison.OrdinalIgnoreCase)) name = name[..^".json".Length];
        dialogs.SaveFileDialog("Save the pull as a file", "Raid Planner recording{.json}", name, ".json", (ok, path) =>
        {
            if (!ok) return;
            try
            {
                if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) path += ".json";
                File.Copy(store.PathOf(info), path, true);
                Notify($"Saved {Path.GetFileName(path)}", ToastKind.News, Icon.Download);
            }
            catch (Exception ex)
            {
                // Run by the file dialog, outside the window's own catch: nothing may escape.
                if (ex is not (IOException or UnauthorizedAccessException)) Services.Log.Error(ex, "Raid Planner couldn't save the file");
                Notify("Couldn't save it there", ToastKind.Bad);
            }
        }, start, true);
    }

    /// <summary>A sent line (`.sentline`): a 16px ready tick and the words; returns its height.</summary>
    private static float SentLine(Vector2 at, float width, string text, string? linkId = null, string? link = null, Action? onLink = null)
    {
        var dl = Kit.Dl;
        var h = S(19.5f);
        Kit.Tick(dl, new Vector2(at.X + S(8), at.Y + h / 2), 16);
        var lw = link != null ? Kit.LinkWidth(link, Icon.ArrowUpRight) : 0;
        var x = at.X + S(16 + 8.8f);
        Txt.Line(dl, new Vector2(x, at.Y), Txt.Fit(text, Face.Sans, 13, at.X + width - x - (link != null ? lw + S(12) : 0)), Face.Sans, 13, Palette.Text, 19.5f);
        if (link != null && Kit.Link(linkId!, new Vector2(at.X + width - lw, at.Y + (h - Txt.Natural(Face.SansMedium, 13)) / 2), link, Icon.ArrowUpRight)) onLink?.Invoke();
        return h;
    }

    /// <summary>Two buttons on a row (the first growing to fill it when asked), stacked when the window is narrow. Returns which was pressed.</summary>
    private static (bool First, bool Second) ButtonPair(string id, string first, Btn firstStyle, Icon? firstIcon, string? second, Btn secondStyle, Icon? secondIcon,
        bool grow = false, float dim = 1f, bool firstDisabled = false, string? firstTip = null, string? secondTip = null)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var h = Kit.ButtonHeight();
        var fw = Kit.ButtonWidth(first, BtnSize.Md, firstIcon, firstStyle);
        var sw = second != null ? Kit.ButtonWidth(second, BtnSize.Md, secondIcon, secondStyle) : 0;
        var gap = S(6.4f);
        var stacked = second != null && fw + gap + sw > w;
        if (grow && second != null && !stacked) fw = w - gap - sw;
        var a = Kit.Button(id + "1", at, first, firstStyle, BtnSize.Md, firstIcon, grow && !stacked && second != null ? fw : 0, firstDisabled, firstTip, dim);
        var b = false;
        if (second != null)
        {
            var p = stacked ? new Vector2(at.X, at.Y + h + gap) : new Vector2(at.X + fw + gap, at.Y);
            b = Kit.Button(id + "2", p, second, secondStyle, BtnSize.Md, secondIcon, 0, false, secondTip);
        }
        Kit.Reserve(at, w, stacked ? h * 2 + gap : h);
        return (a, b);
    }

    /// <summary>
    /// A pull's send, in full (the latest pull's card, a pull's footer): the buttons for where it is
    /// (link, send, sending, sent, failed) and the line or box under them. <paramref name="readyLine"/>
    /// is what's said when it's ready to go.
    /// </summary>
    private void DrawSend(string id, RecordingInfo info, string readyLine, bool grow = false)
    {
        const string save = "Save as file";
        const string saveTip = "A file to import in the Raid Planner yourself (Import on its front page), or to share.";
        sends.TryGetValue(info.File, out var s);
        var sentId = store.Find(info.File)?.SentId ?? info.SentId;

        // Sending: the telegraph holds still and dims; a quiet Stop; a cast bar runs under, because a send is time.
        if (s is { Result: null })
        {
            var (_, stop) = ButtonPair(id, "Sending…", Btn.Tele, Icon.Send, "Stop", Btn.Quiet, null, false, 0.55f, true);
            if (stop) StopSend(s);
            Kit.Gap(10);
            var at = Kit.At;
            var h = Kit.Cast(Kit.Dl, at, Kit.Width, $"Sending pull {Math.Max(1, info.Pull)}", s.Bytes > 0 ? Format.Megabytes(s.Bytes) : null, Running(1.6f));
            Kit.Reserve(at, Kit.Width, h);
            return;
        }

        // Failed: red, and the one thing to do.
        if (s?.Result is { Sent: false } failed && SendFailure.Of(failed) is { Fix: not SendFix.Stopped } f)
        {
            Kit.NoteBox(f.Text);
            Kit.Gap(10);
            switch (f.Fix)
            {
                case SendFix.LinkAgain:
                {
                    var (a, b) = ButtonPair(id, "Link again", Btn.Tele, Icon.Link, save, Btn.Outline, Icon.Download, secondTip: saveTip);
                    if (a) OpenSettings(SettingsTab.Account);
                    if (b) SaveAs(info);
                    break;
                }
                case SendFix.Look:
                {
                    var (a, b) = ButtonPair(id, "Open the Raid Planner", Btn.Tele, Icon.ExternalLink, save, Btn.Outline, Icon.Download, secondTip: saveTip);
                    if (a) Dalamud.Utility.Util.OpenLink(SiteLink.PlannerUrl);
                    if (b) SaveAs(info);
                    break;
                }
                case SendFix.SaveFile:
                case SendFix.Update:
                {
                    var (a, _) = ButtonPair(id, save, Btn.Tele, Icon.Download, null, Btn.Outline, null, firstTip: saveTip);
                    if (a) SaveAs(info);
                    break;
                }
                default:
                {
                    var (a, b) = ButtonPair(id, "Try again", Btn.Tele, Icon.RefreshCw, save, Btn.Outline, Icon.Download, secondTip: saveTip);
                    if (a) Send(info);
                    if (b) SaveAs(info);
                    break;
                }
            }
            return;
        }

        // Sent this session: a ready tick, Open in Review; Send again goes quiet (Save as file when it was made smaller).
        if (s?.Result is { Sent: true } sent && sent.Id is { } sid)
        {
            var at = Kit.At;
            Kit.Reserve(at, Kit.Width, SentLine(at, Kit.Width, sent.Thinned ? "Sent, with fewer of the party's places: it was too big whole." : "Sent. It's waiting in your Raid Planner."));
            Kit.Gap(10);
            var (a, b) = ButtonPair(id, "Open in Review", Btn.Outline, Icon.ExternalLink, sent.Thinned ? save : "Send again", Btn.Quiet, sent.Thinned ? Icon.Download : Icon.Send,
                secondTip: sent.Thinned ? saveTip : "It's in your Raid Planner already: this sends another copy.");
            if (a) Dalamud.Utility.Util.OpenLink(ReviewUrl(info, sid));
            if (b)
            {
                if (sent.Thinned) SaveAs(info);
                else Send(info);
            }
            return;
        }

        // Sent in an earlier session: one line, and the quiet buttons.
        if (sentId != null)
        {
            var at = Kit.At;
            var when = Format.SentLine(store.Find(info.File)?.SentAt ?? info.SentAt ?? info.RecordedAt, DateTimeOffset.Now);
            Kit.Reserve(at, Kit.Width, SentLine(at, Kit.Width, when, id + "review", "Open in Review", () => Dalamud.Utility.Util.OpenLink(ReviewUrl(info, sentId))));
            Kit.Gap(10);
            var (a, b) = ButtonPair(id, "Send again", Btn.Quiet, Icon.Send, save, Btn.Quiet, Icon.Download,
                firstTip: "It's in your Raid Planner already: this sends another copy.", secondTip: saveTip);
            if (a) Send(info);
            if (b) SaveAs(info);
            return;
        }

        // Not linked: linking is the one thing to do. Linked: the same place, the same size.
        if (!Linked)
        {
            var (a, b) = ButtonPair(id, "Link your account", Btn.Tele, Icon.Link, save, Btn.Outline, Icon.Download, grow,
                firstTip: "Link the plugin to the site once, and pulls go straight to your Raid Planner.", secondTip: saveTip);
            if (a) OpenSettings(SettingsTab.Account);
            if (b) SaveAs(info);
            Kit.Gap(10);
            Txt.Note("Link once, and each pull is a button away from being a plan.");
            return;
        }
        var (send, saveIt) = ButtonPair(id, "Send to Raid Planner", Btn.Tele, Icon.Send, save, Btn.Outline, Icon.Download, grow,
            firstTip: "It waits in the Raid Planner under From the game, ready to make into a plan.", secondTip: saveTip);
        if (send) Send(info);
        if (saveIt) SaveAs(info);
        Kit.Gap(10);
        Txt.Note(readyLine);
    }

    /// <summary>
    /// The latest pull's send while a pull is recorded: no telegraph (nothing on Home is the one thing
    /// to do then). A sent line with Open in Review, or how the send went; What's in it and Save as file, dense.
    /// </summary>
    private void DrawSendCompact(RecordingInfo info)
    {
        sends.TryGetValue(info.File, out var s);
        var sentId = store.Find(info.File)?.SentId ?? info.SentId;
        var w = Kit.Width;
        if (s is { Result: null })
        {
            var at = Kit.At;
            var h = Kit.Cast(Kit.Dl, at, w, $"Sending pull {Math.Max(1, info.Pull)}", s.Bytes > 0 ? Format.Megabytes(s.Bytes) : null, Running(1.6f));
            Kit.Reserve(at, w, h);
            Kit.Gap(12);
        }
        else if (s?.Result is { Sent: false } failed && SendFailure.Of(failed) is { Fix: not SendFix.Stopped } f)
        {
            Kit.NoteBox(f.Text);
            Kit.Gap(12);
        }
        else if (sentId != null)
        {
            var at = Kit.At;
            var text = s?.Result is { Sent: true } ? "Sent. It's waiting in your Raid Planner." : Format.SentLine(store.Find(info.File)?.SentAt ?? info.SentAt ?? info.RecordedAt, DateTimeOffset.Now);
            Kit.Reserve(at, w, SentLine(at, w, text, "##latest-review", "Open in Review", () => Dalamud.Utility.Util.OpenLink(ReviewUrl(info, sentId))));
            Kit.Gap(12);
        }
        var row = Kit.At;
        var x = row.X;
        const string what = "What's in it";
        if (Kit.Button("##latest-what", row, what, Btn.Outline, BtnSize.Sm, Icon.List, 0, false, "Its mechanics, in order")) Later(() => ShowPull(info));
        x += Kit.ButtonWidth(what, BtnSize.Sm, Icon.List) + S(6.4f);
        const string save = "Save as file";
        if (Kit.Button("##latest-save", new Vector2(x, row.Y), save, Btn.Quiet, BtnSize.Sm, Icon.Download)) SaveAs(info);
        x += Kit.ButtonWidth(save, BtnSize.Sm, Icon.Download, Btn.Quiet) + S(6.4f);
        if (Linked && sentId == null && s is not { Result: null } && Kit.Button("##latest-send", new Vector2(x, row.Y), "Send", Btn.Quiet, BtnSize.Sm, Icon.Send, 0, false,
                "It waits in the Raid Planner under From the game."))
            Send(info);
        Kit.Reserve(row, w, S(HSm));
    }

    // ---------------------------------------------------------------------------------------------
    // Settings → Account: the token.

    /// <summary>
    /// The account's row: linked, a ready tick, its name, a green Linked tag and the token masked in
    /// mono, Unlink dense; not linked, its words, the 40px well field lined up with every other row's
    /// control and Link as the telegraph beside it, a bad token said in red under it, and Get a token.
    /// </summary>
    private void DrawAccountRow(int index)
    {
        const string name = "Raid Planner account";
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        if (index > 0) Kit.Rule(dl, at.X, at.X + w, at.Y - 0.5f);
        var x = at.X + S(16);
        var inner = w - S(32);
        if (Linked)
        {
            const string unlink = "Unlink";
            var uw = Kit.ButtonWidth(unlink, BtnSize.Sm);
            var nameH = S(14 * 1.3f);
            var lineH = S(18);
            var h = S(11) * 2 + MathF.Max(nameH + S(2) + lineH, S(HSm));
            var top = at.Y + (h - nameH - S(2) - lineH) / 2;
            Kit.Tick(dl, new Vector2(x + S(9), at.Y + h / 2));
            var tx = x + S(18) + S(12);
            Txt.Line(dl, new Vector2(tx, top), name, Face.SansMedium, 14, Palette.White, 14 * 1.3f);
            var ly = top + nameH + S(2);
            var tw = Kit.Tag(dl, new Vector2(tx, ly), "Linked", TagKind.Live, 18);
            Txt.Line(dl, new Vector2(tx + tw + S(6.4f), ly), SiteLink.Masked(config.SiteToken), Face.Mono, 12, Palette.Muted, 18);
            if (Kit.Pointed(new Vector2(x, top), new Vector2(x + inner - uw, top + nameH + lineH)))
                Kit.Tooltip("Send to Raid Planner puts a pull straight into your account on the site, under From the game.");
            if (Kit.Button("##site-unlink", new Vector2(at.X + w - S(16) - uw, at.Y + (h - S(HSm)) / 2), unlink, Btn.Outline, BtnSize.Sm, null, 0, false,
                    "Forget the token here. Revoke it on the site too if you won't use it again."))
            {
                config.SiteToken = string.Empty;
                config.Save();
                // A send on its way with the token just forgotten stops too.
                foreach (var under in sends.Values)
                    if (under.Result == null) StopSend(under);
                sends.Clear();
                Notify("Unlinked from the Raid Planner", ToastKind.News, Icon.Link);
            }
            Kit.Reserve(at, w, h);
            return;
        }

        // Not linked: the words, then the field and Link on a row, the reason under them in red, then Get a token.
        var y = at.Y + S(11);
        Txt.Line(dl, new Vector2(x, y), name, Face.SansMedium, 14, Palette.White, 14 * 1.3f);
        y += S(14 * 1.3f) + S(2);
        var note = new[] { new Run("Not linked yet. Make a token on the site (Settings, Raid Planner, In-game plugin) and paste it here.", Face.Sans, 12, Palette.Muted) };
        y += Txt.Flow(new Vector2(x, y), inner, note, 17.4f, out _);
        y += S(10);
        const string link = "Link";
        var linkW = Kit.ButtonWidth(link, BtnSize.Md, Icon.Link, Btn.Tele);
        var fieldW = MathF.Max(S(120), inner - linkW - S(6.4f));
        var text = tokenDraft;
        if (Kit.Field("##site-token", new Vector2(x, y), fieldW, ref text, "rpp_…", Icon.Link, 128, ImGuiInputTextFlags.Password, tokenProblem != null, Face.Mono, 13))
        {
            tokenDraft = text;
            tokenProblem = null;
        }
        if (Kit.Button("##site-link-btn", new Vector2(x + fieldW + S(6.4f), y), link, Btn.Tele, BtnSize.Md, Icon.Link, 0, tokenDraft.Trim().Length == 0))
            LinkToken();
        y += S(H) + S(10);
        if (tokenProblem != null)
        {
            Icons.Draw(dl, Icon.CircleX, new Vector2(x, y + (S(18) - S(Kit.IconPx)) / 2), S(Kit.IconPx), Palette.Danger);
            Txt.Line(dl, new Vector2(x + S(Kit.IconPx + 6.4f), y), Txt.Fit(tokenProblem, Face.Sans, 13, inner - S(Kit.IconPx + 6.4f)), Face.Sans, 13, Palette.DangerSoft, 18);
            y += S(18) + S(10);
        }
        if (Kit.Button("##site-get", new Vector2(x, y), "Get a token", Btn.Outline, BtnSize.Sm, Icon.ExternalLink, 0, false,
                "Opens the site's Settings, Raid Planner, In-game plugin, where a token is made (shown once, revoked there)."))
            Dalamud.Utility.Util.OpenLink(SiteLink.TokensUrl);
        y += S(HSm) + S(11);
        Kit.Reserve(at, w, y - at.Y);
    }

    private void LinkToken()
    {
        var token = SiteLink.Clean(tokenDraft);
        if (!SiteLink.LooksRight(token))
        {
            tokenProblem = "That isn't a token: it starts rpp_ and is 47 letters long.";
            return;
        }
        config.SiteToken = token;
        config.Save();
        tokenDraft = string.Empty;
        tokenProblem = null;
        sends.Clear();
        Notify("Linked to the Raid Planner", ToastKind.Ok);
    }
}
