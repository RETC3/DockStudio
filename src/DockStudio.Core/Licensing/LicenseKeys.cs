namespace DockStudio.Core.Licensing
{
    /// <summary>
    /// Chiave pubblica con cui le app verificano le licenze. La chiave privata corrispondente non è nel
    /// repository: la conserva chi emette le licenze (strumento tools/DockStudio.LicenseTool).
    /// </summary>
    public static class LicenseKeys
    {
        public const string PublicKeyPem = @"-----BEGIN PUBLIC KEY-----
MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAENJ/ASAYfXsxCHFyTyYfvHofIJr1s
JD1TYxgbITKlEINJxq9Cu00wdb3BZ8Ro/eDsoSBIqUg/59+10msoWyxS/g==
-----END PUBLIC KEY-----";
    }
}
