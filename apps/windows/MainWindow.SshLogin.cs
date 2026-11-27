using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Renci.SshNet.Common;

namespace Fignimble.Windows;

public partial class MainWindow
{
    private readonly Dictionary<string, InteractiveSshSession> sshSessions = new(StringComparer.Ordinal);

    private InteractiveSshSession? SignedIn(string host) =>
        sshSessions.TryGetValue(host, out var session) && session.IsConnected ? session : null;

    private async Task<InteractiveSshSession> SignIn(Window owner, string host)
    {
        if (SignedIn(host) is { } existing) return existing;
        if (sshSessions.Remove(host, out var expired)) expired.Dispose();

        var endpoint = await SshEndpoint.Resolve(host);
        var dialog = new FormDialog(owner, $"Sign in to {host}");
        dialog.Note($"SSH account: {endpoint.User}@{endpoint.Host}\nLeave the password blank to use the server’s interactive prompts.");
        var passwordBox = new PasswordBox();
        dialog.Add("Password", passwordBox);
        dialog.Loaded += (_, _) => passwordBox.Focus();
        if (!dialog.Confirm("Sign in", () => null)) throw new InvalidOperationException("SSH sign-in canceled.");
        string password = passwordBox.Password;
        passwordBox.Clear();

        string file = Path.Combine(settingsDirectory, "ssh-host-keys.json");
        var known = File.Exists(file) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? [] : [];
        string? newKey = null;
        string? hostError = null;
        InteractiveSshSession session;
        try
        {
            session = await Task.Run(() => InteractiveSshSession.Connect(host, endpoint, password, key =>
            {
                string fingerprint = "SHA256:" + key.FingerPrintSHA256;
                if (known.TryGetValue(endpoint.Key, out var saved))
                {
                    if (saved == fingerprint) return true;
                    hostError = $"The saved SSH host key for {endpoint.Host} has changed. Verify the server with its administrator.";
                    return false;
                }
                bool trusted = Dispatcher.Invoke(() => MessageBox.Show(owner,
                    $"Verify this fingerprint with your server administrator before continuing.\n\n" +
                    $"{endpoint.Host}:{endpoint.Port}\n{key.HostKeyName} {fingerprint}\n\nTrust this SSH host key?",
                    "Verify SSH host", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)) == MessageBoxResult.Yes;
                if (trusted) newKey = fingerprint;
                else hostError = "The SSH host key was declined.";
                return trusted;
            }, request => Dispatcher.Invoke(() => AnswerSshPrompt(owner, host, request))));
        }
        catch (Exception ex) when (hostError is not null)
        {
            throw new InvalidOperationException(hostError, ex);
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException("SSH sign-in canceled.");
        }

        try
        {
            if (newKey is not null)
            {
                known[endpoint.Key] = newKey;
                SaveSetting(file, known);
            }
            sshSessions.Add(host, session);
            return session;
        }
        catch { session.Dispose(); throw; }
    }

    private static void AnswerSshPrompt(Window owner, string host, AuthenticationPromptEventArgs request)
    {
        if (request.Prompts.Count == 0) return;
        var dialog = new FormDialog(owner, $"Verify SSH sign-in to {host}");
        if (!string.IsNullOrWhiteSpace(request.Instruction)) dialog.Note(request.Instruction);
        var fields = request.Prompts.Select(prompt =>
        {
            FrameworkElement field = prompt.IsEchoed ? new TextBox() : new PasswordBox();
            dialog.Add(prompt.Request, field);
            return (Prompt: prompt, Field: field);
        }).ToArray();
        dialog.Loaded += (_, _) => fields[0].Field.Focus();
        if (!dialog.Confirm("Continue", () => null)) throw new OperationCanceledException();
        foreach (var (prompt, field) in fields)
        {
            if (field is PasswordBox secret) { prompt.Response = secret.Password; secret.Clear(); }
            else if (field is TextBox input) { prompt.Response = input.Text; input.Clear(); }
        }
    }
}
