namespace GameGuard.Infrastructure.Persistence;

public static class GameGuardPaths
{
    private const string AppFolderName = "GameGuard";
    private const string DatabaseFileName = "gameguard.db";

    public static string GetDatabasePath(bool preferSharedProgramData = true)
    {
        if (preferSharedProgramData)
        {
            try
            {
                string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                if (!string.IsNullOrWhiteSpace(programData))
                {
                    string sharedDir = Path.Combine(programData, AppFolderName);
                    if (!Directory.Exists(sharedDir))
                    {
                        Directory.CreateDirectory(sharedDir);
                    }

                    string dbPath = Path.Combine(sharedDir, DatabaseFileName);
                    return dbPath;
                }
            }
            catch
            {
                // Fall back to LocalAppData if ProgramData is inaccessible
            }
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string userDir = Path.Combine(localAppData, AppFolderName);
        if (!Directory.Exists(userDir))
        {
            Directory.CreateDirectory(userDir);
        }

        return Path.Combine(userDir, DatabaseFileName);
    }

    public static string GetLogDirectory()
    {
        try
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (!string.IsNullOrWhiteSpace(programData))
            {
                string sharedDir = Path.Combine(programData, AppFolderName, "Logs");
                Directory.CreateDirectory(sharedDir);
                return sharedDir;
            }
        }
        catch
        {
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string userDir = Path.Combine(localAppData, AppFolderName, "Logs");
        Directory.CreateDirectory(userDir);
        return userDir;
    }
}
