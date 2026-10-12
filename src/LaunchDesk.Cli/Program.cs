using System.Text;
using LaunchDesk.Core;

Console.OutputEncoding = new UTF8Encoding(false);
return await ControlCli.RunAsync(args, Console.Out, Console.Error);
