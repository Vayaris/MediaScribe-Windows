namespace MediaScribeRecorder.Services;

public static class PathValidator
{
    public static void EnsureWritableDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new UserFacingException("REC-OUT-001", "Le dossier de sortie est vide ou invalide.");
        }

        try
        {
            Directory.CreateDirectory(path);
            var testPath = Path.Combine(path, $".mediascribe-write-test-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(testPath, "test");
            File.Delete(testPath);
        }
        catch (Exception ex)
        {
            throw new UserFacingException("REC-OUT-001", $"Le dossier de sortie n'est pas accessible en écriture: {path}", ex);
        }
    }
}
