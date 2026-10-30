using System;
using System.IO;
using WixToolset.Dtf.WindowsInstaller;

namespace CheckNet8
{
    public static class CustomActions
    {
        private const string FolderFramework = @"dotnet\shared\Microsoft.AspNetCore.App";

        // Gerbang pemasangan MSI. Properti DOTNET8ASPNETCORE hanya diisi bila ada folder
        // versi 8.x, karena WiX RegistrySearch/DirectorySearch tidak mendukung wildcard.
        [CustomAction]
        public static ActionResult PeriksaDotNet8(Session session)
        {
            session.Log("Memeriksa ASP.NET Core 8 di folder shared framework.");
            var ditemukan = false;
            var akar = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            };

            foreach (var dasar in akar)
            {
                if (!CariVersi8(Path.Combine(dasar, FolderFramework)))
                {
                    continue;
                }

                ditemukan = true;
                break;
            }

            session.Log(ditemukan
                ? "ASP.NET Core 8 ditemukan."
                : "ASP.NET Core 8 tidak ditemukan.");
            session["DOTNET8ASPNETCORE"] = ditemukan ? "1" : string.Empty;
            return ActionResult.Success;
        }

        private static bool CariVersi8(string folder)
        {
            if (!Directory.Exists(folder))
            {
                return false;
            }

            foreach (var versi in Directory.GetDirectories(folder))
            {
                if (Path.GetFileName(versi).StartsWith("8.", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
