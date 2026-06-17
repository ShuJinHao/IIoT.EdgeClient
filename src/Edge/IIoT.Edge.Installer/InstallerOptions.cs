namespace IIoT.Edge.Installer;

internal sealed record InstallerOptions(
    string? InstallTo,
    bool Silent,
    bool NoLaunch)
{
    public static InstallerOptions Parse(string[] args)
    {
        string? installTo = null;
        var silent = false;
        var noLaunch = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--", StringComparison.Ordinal))
            {
                break;
            }

            if (string.Equals(arg, "--silent", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "-s", StringComparison.OrdinalIgnoreCase))
            {
                silent = true;
                continue;
            }

            if (string.Equals(arg, "--no-launch", StringComparison.OrdinalIgnoreCase))
            {
                noLaunch = true;
                continue;
            }

            if (string.Equals(arg, "--installto", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "-t", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                {
                    throw new ArgumentException("--installto requires a directory value.");
                }

                installTo = args[++i];
            }
        }

        return new InstallerOptions(installTo, silent, noLaunch);
    }
}
