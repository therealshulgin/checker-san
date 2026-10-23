using FolderChecker.Cli;

// Accented French messages and paths display correctly in Windows Terminal and are kept when redirected to a file.
Console.OutputEncoding = System.Text.Encoding.UTF8;
using var stop = new CancellationTokenSource();
// Ctrl+C stops the work cleanly: completed hashes are kept and the result is reported as incomplete.
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
return await CheckerSanCli.RunAsync(args, Console.Out, Console.Error, stop.Token);
