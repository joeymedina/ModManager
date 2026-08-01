namespace ModManager.Cli;

internal sealed record CliOptions(string ModId, string Folder, bool Download, bool ShowHelp)
{
    public static CliOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mods");
        bool download = false;
        string modId = "wickedwhims";

        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--check":
                    download = false;
                    break;
                case "--download":
                    download = true;
                    break;
                case "--folder" when index + 1 < args.Length:
                    folder = Path.GetFullPath(args[++index]);
                    break;
                case "--mod" when index + 1 < args.Length:
                    modId = args[++index].Trim();
                    break;
                case "--help" or "-h":
                    return new CliOptions(modId, folder, download, true);
                default:
                    throw new ArgumentException($"Unknown or incomplete argument: {args[index]}");
            }
        }

        return new CliOptions(modId, folder, download, false);
    }

    public static void PrintUsage()
    {
        Console.WriteLine("Usage: dotnet run -- [--check | --download] [--mod <id>] [--folder <path>]");
        Console.WriteLine();
        Console.WriteLine("  --check              Check the installed version (default)");
        Console.WriteLine("  --download           Check and download the update when needed");
        Console.WriteLine("  --mod <id>           Mod id to process (default: wickedwhims)");
        Console.WriteLine("  --folder <path>      Mods folder (default: ~/Documents/Mods)");
    }
}
