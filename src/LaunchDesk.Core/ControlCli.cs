using System.Text.Json;

namespace LaunchDesk.Core;

public static class ControlCli
{
    public const string Usage = "LaunchDesk.Cli list|status [name-or-id] [--json]\nLaunchDesk.Cli start|stop|restart <name-or-id>|--all [--json] [--timeout seconds]\nOpen the desktop application first. Controls the current user/login session only.";
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter errorOutput,
        Func<ControlRequest, TimeSpan, Task<ControlResponse>>? send = null)
    {
        if (args.Length == 0 || args is ["--help"] or ["-h"]) { await output.WriteLineAsync(Usage); return 0; }
        try
        {
            var command = args[0]; string? target = null; var all = false; var json = false; var timeout = 120;
            for (var i = 1; i < args.Length; i++)
                switch (args[i])
                {
                    case "--all": if (all) throw new ArgumentException("Duplicate --all."); all = true; break;
                    case "--json": json = true; break;
                    case "--timeout":
                        if (++i == args.Length || !int.TryParse(args[i], out timeout) || timeout is < 1 or > 3600) throw new ArgumentException("Timeout must be 1-3600 seconds.");
                        break;
                    default:
                        if (target is not null || args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Unexpected argument: " + args[i]);
                        target = args[i]; break;
                }
            var request = new ControlRequest(command, target, all); ControlCommands.Validate(request);
            var response = await (send ?? ((r, t) => LocalControl.SendAsync(r, t)))(request, TimeSpan.FromSeconds(timeout));
            if (json) await output.WriteLineAsync(JsonSerializer.Serialize(response, ConfigurationStore.JsonOptions));
            else if (!response.Success) await errorOutput.WriteLineAsync(response.Message);
            else
            {
                await output.WriteLineAsync("ID\tNAME\tSTATUS\tPID\tHEALTH");
                foreach (var item in response.Items)
                    await output.WriteLineAsync($"{item.Id}\t{item.Name.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ')}\t{item.Status}\t{item.Pid}\t{item.Health.Status}");
            }
            return response.Success ? 0 : 1;
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException)
        { await errorOutput.WriteLineAsync(error.Message + "\n" + Usage); return 2; }
        catch (OperationCanceledException)
        { await errorOutput.WriteLineAsync("Command timed out; it may still be running. Use status before retrying."); return 4; }
        catch (Exception error)
        { await errorOutput.WriteLineAsync(error.Message); return 3; }
    }
}
