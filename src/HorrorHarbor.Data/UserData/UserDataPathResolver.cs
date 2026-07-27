namespace HorrorHarbor.Data.UserData;

public static class UserDataPathResolver
{
    public static string Resolve()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HorrorHarbor");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "user-data.db");
    }
}
