using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using RaidPlanner.Core;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>
/// Sending a pull to the Raid Planner, saving it as a file, and linking your account (Settings →
/// Account), as Housing Studio's plugin links and sends: a token made on the site, pasted once,
/// kept masked, Unlink. A send reads the pull's file and posts it from a worker; how it went shows
/// under the buttons, with Open it once it's there. The token is never logged.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>A send under way (no result yet) or done, by the pull's file.</summary>
    private sealed record SendState(SendResult? Result);

    private readonly ConcurrentDictionary<string, SendState> sends = new(StringComparer.OrdinalIgnoreCase);

    private string tokenDraft = string.Empty;
    private string? tokenProblem;

    private bool Linked => SiteLink.LooksRight(config.SiteToken);

    private bool Sending(RecordingInfo info) => sends.TryGetValue(info.File, out var s) && s.Result == null;

    /// <summary>Sends a kept pull to your Raid Planner (it waits there under From the game).</summary>
    private void Send(RecordingInfo info)
    {
        if (!Linked || Sending(info)) return;
        var token = config.SiteToken;
        var file = info.File;
        var cancel = closing.Token;
        sends[file] = new SendState(null);
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
                return;
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
            // Unloaded meanwhile: the plugin loaded again keeps the list now, so this one leaves it alone.
            if (cancel.IsCancellationRequested) return;
            if (result.Sent) store.MarkSent(info, result.Id!, DateTimeOffset.UtcNow);
            sends[file] = new SendState(result);
            try
            {
                _ = Services.Framework.RunOnFrameworkThread(() => Notify(result.Sent ? "Sent to your Raid Planner" : "Couldn't send it"));
            }
            catch (Exception)
            {
                // The plugin was unloaded while it went.
            }
        });
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
                Notify($"Saved {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                // Run by the file dialog, outside the window's own catch: nothing may escape.
                if (ex is not (IOException or UnauthorizedAccessException)) Services.Log.Error(ex, "Raid Planner couldn't save the file");
                Notify("Couldn't save it there");
            }
        }, start, true);
    }

    /// <summary>
    /// The pull's two buttons from <paramref name="pos"/>: the white Send to Raid Planner (or Link your
    /// account, not linked yet) and Save as file. Returns their height.
    /// </summary>
    private float SendButtons(string id, Vector2 pos, RecordingInfo info, float width)
    {
        string label;
        Icon icon;
        string? tip = null;
        var disabled = false;
        if (!Linked)
        {
            label = "Link your account";
            icon = Icon.Link;
            tip = "Link the plugin to the site once, and pulls go straight to your Raid Planner.";
        }
        else if (Sending(info))
        {
            label = "Sending…";
            icon = Icon.Send;
            disabled = true;
        }
        else if (info.SentId != null)
        {
            label = "Send again";
            icon = Icon.Send;
            tip = "It's in your Raid Planner already: this sends another copy.";
        }
        else
        {
            label = "Send to Raid Planner";
            icon = Icon.Send;
            tip = "It waits in the Raid Planner under From the game, ready to make into a plan.";
        }
        var style = info.SentId != null && Linked ? Btn.Glass : Btn.Primary;
        var sendW = Kit.ButtonWidth(label, BtnSize.Md, icon, style);
        const string save = "Save as file";
        var saveW = Kit.ButtonWidth(save, BtnSize.Md, Icon.FileDown);
        var h = Kit.ButtonHeight(BtnSize.Md);
        if (Kit.Button(id + "send", pos, label, style, BtnSize.Md, icon, 0, disabled, tip))
        {
            if (!Linked) OpenSettings(SettingsTab.Account);
            else Send(info);
        }
        var stacked = sendW + S(8) + saveW > width;
        var savePos = stacked ? new Vector2(pos.X, pos.Y + h + S(8)) : new Vector2(pos.X + sendW + S(8), pos.Y);
        if (Kit.Button(id + "save", savePos, save, Btn.Glass, BtnSize.Md, Icon.FileDown, 0, false,
                "A file to import in the Raid Planner yourself (Import on its front page), or to share."))
            SaveAs(info);
        return stacked ? h * 2 + S(8) : h;
    }

    /// <summary>Under a pull's buttons: sending, sent (with Open it), or why it couldn't; and, sent before, where it went.</summary>
    private void DrawSendState(RecordingInfo info, string? idleLine = null)
    {
        Run[]? runs = null;
        if (sends.TryGetValue(info.File, out var s))
        {
            runs = s.Result switch
            {
                null => new[] { Run.Note("Sending it to your Raid Planner…") },
                { Sent: true, Thinned: true } => new[]
                {
                    Run.Note("Sent, with the places kept less often (it was too big whole). It's waiting in your Raid Planner. ", Palette.Success),
                    new Run("Open it", Face.SansMedium, 13, Palette.Ink, Link: "open"),
                },
                { Sent: true } => new[]
                {
                    Run.Note("Sent. It's waiting in your Raid Planner. ", Palette.Success),
                    new Run("Open it", Face.SansMedium, 13, Palette.Ink, Link: "open"),
                },
                { TokenRefused: true } => new[]
                {
                    Run.Note($"Couldn't send it. {s.Result.Why} ", Palette.Danger),
                    new Run("Settings", Face.SansMedium, 13, Palette.Ink, Link: "settings"),
                },
                _ => new[] { Run.Note($"Couldn't send it. {s.Result.Why}", Palette.Danger) },
            };
        }
        else if (info.SentId != null)
        {
            runs = new[]
            {
                Run.Note($"Sent {Words.When(info.SentAt ?? info.RecordedAt, DateTimeOffset.Now)}. "),
                new Run("Open it", Face.SansMedium, 13, Palette.Ink, Link: "open"),
            };
        }
        else if (idleLine != null)
        {
            runs = new[] { Run.Note(idleLine) };
        }
        if (runs == null) return;
        Kit.Gap(10);
        var link = Txt.Para(runs);
        var id = store.Find(info.File)?.SentId ?? info.SentId;
        if (link == "open" && id != null) Dalamud.Utility.Util.OpenLink(SiteLink.OpenUrl(id));
        if (link == "settings") OpenSettings(SettingsTab.Account);
    }

    // ---------------------------------------------------------------------------------------------
    // Settings → Account: the token.

    /// <summary>
    /// One row: "Raid Planner account · Linked · rpp_••••3f2a" with Unlink, or, not linked, the token
    /// field (masked) with Link and Get a token under its words.
    /// </summary>
    private void DrawAccountRow(int index)
    {
        const string name = "Raid Planner account";
        if (Linked)
        {
            const string unlink = "Unlink";
            SetRow(index, name, $"Linked · {SiteLink.Masked(config.SiteToken)}", Kit.ButtonWidth(unlink, BtnSize.Tiny, null, Btn.Glass), Kit.ButtonHeight(BtnSize.Tiny), p =>
            {
                if (Kit.Button("##site-unlink", p, unlink, Btn.Glass, BtnSize.Tiny, null, 0, false,
                        "Forget the token here. Revoke it on the site too if you won't use it again."))
                {
                    config.SiteToken = string.Empty;
                    config.Save();
                    sends.Clear();
                    Notify("Unlinked from the Raid Planner");
                }
            }, tip: "Send to Raid Planner puts a pull straight into your account on the site, under From the game.", icon: Icon.Link);
            return;
        }
        var width = Kit.Width - S(28);
        const string link = "Link";
        const string get = "Get a token";
        var linkW = Kit.ButtonWidth(link, BtnSize.Md, null, Btn.Primary);
        var getW = Kit.ButtonWidth(get, BtnSize.Md, Icon.ExternalLink, Btn.Glass);
        SetRow(index, name, tokenProblem ?? "Not linked yet. Make a token on the site (Settings → Raid Planner → In-game plugin) and paste it here.", width, S(36), p =>
        {
            var fieldW = MathF.Max(S(120), width - linkW - getW - S(16));
            var text = tokenDraft;
            if (Kit.Field("##site-token", p, fieldW, ref text, "rpp_…", Icon.Link, 128, ImGuiInputTextFlags.Password))
            {
                tokenDraft = text;
                tokenProblem = null;
            }
            var x = p.X + fieldW + S(8);
            if (Kit.Button("##site-link-btn", new Vector2(x, p.Y), link, Btn.Primary, BtnSize.Md, null, 0, tokenDraft.Trim().Length == 0))
                LinkToken();
            if (Kit.Button("##site-get", new Vector2(x + linkW + S(8), p.Y), get, Btn.Glass, BtnSize.Md, Icon.ExternalLink))
                Dalamud.Utility.Util.OpenLink(SiteLink.TokensUrl);
        }, below: true,
            tip: "Linking lets the plugin send pulls straight to your Raid Planner. The token is made on the site, shown once, and can be revoked there.",
            icon: Icon.Link);
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
        Notify("Linked to the Raid Planner");
    }
}
