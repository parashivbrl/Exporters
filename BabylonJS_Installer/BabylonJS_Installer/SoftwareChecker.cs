using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Threading.Tasks;

namespace BabylonJS_Installer
{
    class SoftwareChecker
    {
        string latestVersionDate;
        private Dictionary<string, string[]> files = new Dictionary<string, string[]>() {
            { "Max", new string[] {
                "GDImageLibrary.dll",
                "Max2Babylon.dll",
                "Microsoft.WindowsAPICodePack.dll",
                "Microsoft.WindowsAPICodePack.Shell.dll",
                "Microsoft.WindowsAPICodePack.ShellExtensions.dll",
                "Newtonsoft.Json.dll",
                "SharpDX.dll",
                "SharpDX.Mathematics.dll",
                "TargaImage.dll",
                "TQ.Texture.dll"
            } },
            { "Maya", new string[] {
                "GDImageLibrary.dll",
                "Maya2Babylon.nll.dll",
                "Maya2Babylon.nll.deps.json",
                "openmayacs.dll",
                "openmayacs.runtimeconfig.json",
                "Newtonsoft.Json.dll",
                "TargaImage.dll",
                "TQ.Texture.dll",
                "AEbabylonAiStandardSurfaceMaterialNodeTemplate.mel",
                "AEbabylonStandardMaterialNodeTemplate.mel",
                "AEbabylonStingrayPBSMaterialNodeTemplate.mel",
                "NEbabylonAiStandardSurfaceMaterialNodeTemplate.xml",
                "NEbabylonStandardMaterialNodeTemplate.xml",
                "NEbabylonStingrayPBSMaterialNodeTemplate.xml"
            } }
        };

        // Relative to the stored location root:
        // Max  -> Maya/3ds Max install root
        // Maya -> Documents\maya\{year}\modules\Maya2Babylon\
        public Dictionary<string, string> libFolder = new Dictionary<string, string>()
        {
            { "Max", "bin\\assemblies" },
            { "Maya", "plug-ins" },
            { "MayaAE", "scripts\\AETemplates" },
            { "MayaNE", "scripts\\NETemplates" }
        };

        public MainForm form;

        public static string EnsureTrailingSlash(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            return path.EndsWith("\\") || path.EndsWith("/") ? path : path + "\\";
        }

        public string GetMayaModulesDir(string year)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "maya", year, "modules");
        }

        public string GetMayaModuleRoot(string year)
        {
            return EnsureTrailingSlash(Path.Combine(GetMayaModulesDir(year), "Maya2Babylon"));
        }

        public string GetMayaModFile(string year)
        {
            return Path.Combine(GetMayaModulesDir(year), "Maya2Babylon.mod");
        }

        public string GetMayaPluginDllPath(string moduleRoot)
        {
            return Path.Combine(EnsureTrailingSlash(moduleRoot), "plug-ins", "Maya2Babylon.nll.dll");
        }

        /// <summary>
        /// Returns the Maya install path from the registry, or a filesystem fallback.
        /// Newer Maya installs may omit Setup\InstallPath even when Program Files has Maya{year}.
        /// </summary>
        public string getMayaInstallPath(string year)
        {
            try
            {
                RegistryKey localKey = Environment.Is64BitOperatingSystem
                    ? RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                    : RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);

                object value = localKey
                    .OpenSubKey(@"SOFTWARE\Autodesk\Maya\" + year + @"\Setup\InstallPath")
                    ?.GetValue("MAYA_INSTALL_LOCATION");
                if (value != null)
                {
                    string fromRegistry = EnsureTrailingSlash(value.ToString());
                    if (Directory.Exists(fromRegistry))
                    {
                        return fromRegistry;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            // Fallback: default Autodesk layout (Maya 2025/2026 on this machine have no InstallPath key)
            string defaultPath = EnsureTrailingSlash(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Autodesk", "Maya" + year));
            if (Directory.Exists(defaultPath))
            {
                return defaultPath;
            }

            return "";
        }

        /// <summary>
        /// Candidate paths for openmayacs.dll for a given Maya year (bin first, then bin\plug-ins).
        /// </summary>
        public IEnumerable<string> GetMayaOpenMayaCsCandidates(string year)
        {
            string install = getMayaInstallPath(year);
            if (!string.IsNullOrEmpty(install))
            {
                yield return Path.Combine(install, "bin", "openmayacs.dll");
                yield return Path.Combine(install, "bin", "plug-ins", "openmayacs.dll");
            }

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            yield return Path.Combine(programFiles, "Autodesk", "Maya" + year, "bin", "openmayacs.dll");
            yield return Path.Combine(programFiles, "Autodesk", "Maya" + year, "bin", "plug-ins", "openmayacs.dll");
        }

        public string checkPath(string software, string version, string year)
        {
            RegistryKey localKey;
            if (Environment.Is64BitOperatingSystem)
                localKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            else
                localKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);

            if (software == "Max")
            {
                try
                {
                    return localKey.OpenSubKey(@"SOFTWARE\Autodesk\3dsMax\" + version + ".0").GetValue("Installdir").ToString();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    return "";
                }
            }
            else if (software == "Maya")
            {
                // Show Maya year only if Autodesk Maya is installed (or a module already exists).
                string mayaInstall = getMayaInstallPath(year);
                string moduleRoot = GetMayaModuleRoot(year);
                bool moduleExists = File.Exists(GetMayaPluginDllPath(moduleRoot))
                    || Directory.Exists(Path.Combine(moduleRoot, "plug-ins"));

                if (!string.IsNullOrEmpty(mayaInstall) || moduleExists)
                {
                    return moduleRoot;
                }
                return "";
            }
            return "";
        }

        public DateTime getInstalledExporterTimestamp(string software, string path)
        {
            try
            {
                switch (software)
                {
                    case "Max":
                        return File.GetLastWriteTime(path + "bin\\assemblies\\Max2Babylon.dll");
                    case "Maya":
                        string dll = GetMayaPluginDllPath(path);
                        if (!File.Exists(dll)) return DateTime.MinValue;
                        return File.GetLastWriteTime(dll);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            return DateTime.MinValue;
        }

        private void TryDeleteFile(string fileFullPath, string displayName, ref int errors, ref bool needElevatedProgram)
        {
            if (!File.Exists(fileFullPath))
            {
                return;
            }

            try
            {
                File.Delete(fileFullPath);
                this.form.log(displayName + " deleted.");
            }
            catch (UnauthorizedAccessException)
            {
                needElevatedProgram = true;
                errors++;
                this.form.error("Cannot access file: " + fileFullPath);
            }
            catch (Exception ex)
            {
                errors++;
                this.form.error(
                    ex.GetType().ToString() + " error while deleting the file : " + displayName + "\n"
                    + "     At : " + fileFullPath + "\n"
                    + "     " + ex.Message);
            }
        }

        public void uninstallExporter(string soft, string version, string path)
        {
            this.form.goTab("");
            this.form.log("\n----- UNINSTALLING " + soft + " v" + version + " EXPORTER -----\n");
            int errors = 0;
            bool needElevatedProgram = false;
            string fileFullPath;
            path = EnsureTrailingSlash(path);

            if (soft == "Max")
            {
                Directory.CreateDirectory(path + "scripts\\Startup");
                File.WriteAllText(
                    path + "scripts\\Startup\\BabylonCleanUp.ms",
                    "/* Remove menu \"Babylon\" from Main menu bar */\n" +
                    "try (menuMan.unRegisterMenu(menuMan.findMenu \"Babylon\")) catch ()\n" +
                    "/* Remove item \"Babylon...\" from quad */\n" +
                    "try (\n" +
                        "quadMenu = menuMan.getViewportRightClickMenu #nonePressed\n" +
                        "menu = quadMenu.getMenu 1\n" +
                        "nbItems = menu.numItems()\n" +
                        "for i = 1 to nbItems do \n" +
                                             "(\n" +
                                                "item = menu.getItem i\n" +
                            "title = item.getTitle()\n" +
                            "if title == \"Babylon...\" do menu.removeItemByPosition i\n" +
                        ")\n" +
                    ")\n" +
                    "catch ()\n" +
                    "/* Self destruction */\n" +
                    "root = getdir #maxroot\n" +
                    "filePath = root + \"scripts\\Startup\\BabylonCleanUp.ms\"\n" +
                    "deleteFile filePath"
                );
            }

            foreach (string file in this.files[soft])
            {
                if (file.Length >= 9 && file.Substring(0, 9) == "AEbabylon")
                    fileFullPath = path + this.libFolder[soft + "AE"] + "\\" + file;
                else if (file.Length >= 9 && file.Substring(0, 9) == "NEbabylon")
                    fileFullPath = path + this.libFolder[soft + "NE"] + "\\" + file;
                else
                    fileFullPath = path + this.libFolder[soft] + "\\" + file;

                TryDeleteFile(fileFullPath, file, ref errors, ref needElevatedProgram);
            }

            if (soft == "Maya")
            {
                // Remove module descriptor
                string modFile = GetMayaModFile(version);
                TryDeleteFile(modFile, Path.GetFileName(modFile), ref errors, ref needElevatedProgram);

                // Best-effort cleanup of empty module folders
                TryDeleteEmptyDirectory(Path.Combine(path, "plug-ins"));
                TryDeleteEmptyDirectory(Path.Combine(path, "scripts", "AETemplates"));
                TryDeleteEmptyDirectory(Path.Combine(path, "scripts", "NETemplates"));
                TryDeleteEmptyDirectory(Path.Combine(path, "scripts"));
                TryDeleteEmptyDirectory(path.TrimEnd('\\', '/'));

                // Clean legacy Program Files drop-in if present (may need admin)
                string mayaInstall = getMayaInstallPath(version);
                if (!string.IsNullOrEmpty(mayaInstall))
                {
                    string legacyDll = Path.Combine(mayaInstall, "bin", "plug-ins", "Maya2Babylon.nll.dll");
                    if (File.Exists(legacyDll))
                    {
                        this.form.log("Removing legacy Program Files plugin: " + legacyDll);
                        TryDeleteFile(legacyDll, "Maya2Babylon.nll.dll (legacy)", ref errors, ref needElevatedProgram);
                        foreach (string extra in new[] {
                            "Maya2Babylon.nll.deps.json",
                            "openmayacs.runtimeconfig.json",
                            "GDImageLibrary.dll",
                            "Newtonsoft.Json.dll",
                            "TargaImage.dll",
                            "TQ.Texture.dll"
                        })
                        {
                            TryDeleteFile(Path.Combine(mayaInstall, "bin", "plug-ins", extra), extra + " (legacy)", ref errors, ref needElevatedProgram);
                        }
                    }
                }
            }

            if (errors == 0)
            {
                this.form.log("\n----- UNINSTALLING COMPLETE -----\n");
            }
            else
            {
                this.form.log("\n----- UNINSTALL FAILED with " + errors + " errors -----\n");
                if (needElevatedProgram)
                {
                    this.form.error(
                    "Some files could not be removed (permission denied).\n"
                    + "For 3ds Max / legacy Program Files installs, try running as Administrator.\n"
                    + "Also close " + soft + " " + version + " if it is running, then retry.\n"
                    );
                }
            }

            this.form.displayInstall(soft, version);
        }

        private void TryDeleteEmptyDirectory(string directory)
        {
            try
            {
                if (Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length == 0)
                {
                    Directory.Delete(directory);
                    this.form.log("Removed empty folder: " + directory);
                }
            }
            catch (Exception ex)
            {
                this.form.warn("Could not remove folder " + directory + " : " + ex.Message);
            }
        }

        public void setLatestVersionDate()
        {
            Downloader downloader = new Downloader();
            Task<string> jsonRequest = Task.Run(async () => { return await downloader.GetJSONBodyRequest(downloader.GetURLGitHubAPI()); });
            //TO DO Find a better way to parse JSON aswell
            string json = jsonRequest.Result;
            if (string.IsNullOrEmpty(json))
            {
                this.latestVersionDate = DateTime.Now.ToLongTimeString();
                return;
            }

            string created_at = json.Substring(json.IndexOf("\"created_at\":"));
            created_at = created_at.Remove(created_at.IndexOf("\","));
            this.latestVersionDate = created_at.Remove(0, "\"created_at\":\"".Length);
        }

        public bool isLatestVersionInstalled(string soft, string version, string location)
        {// To ensure latest version, we compare between last modified time of files and the publish date of github release
            var latest = DateTime.Parse(this.latestVersionDate, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
            var isLatestversion = false;
            switch (soft)
            {
                case "Max":
                    if (latest <= File.GetLastWriteTime(location + "bin\\assemblies\\Max2Babylon.dll")) isLatestversion = true;
                    break;

                case "Maya":
                    string dll = GetMayaPluginDllPath(location);
                    if (File.Exists(dll) && latest <= File.GetLastWriteTime(dll)) isLatestversion = true;
                    break;

                default:
                    this.form.error("Error : software not found");
                    break;
            }
            return isLatestversion;
        }

        public bool ensureAdminMode()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        public void checkNewInstallerVersion()
        {
            string url_versionFile = "https://raw.githubusercontent.com/parashivbrl/Exporters/feat/maya-2026-support/BabylonJS_Installer/BabylonJS_Installer/BabylonJS_Installer.csproj";

            string assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();

            try
            {
                System.Net.WebClient wc = new System.Net.WebClient();
                string versionFile = wc.DownloadString(url_versionFile);
                int avFrom = versionFile.IndexOf("<ApplicationVersion>") + "<ApplicationVersion>".Length;
                int avTo = versionFile.LastIndexOf("</ApplicationVersion>");
                String serverVersion = versionFile.Substring(avFrom, avTo - avFrom);

                String[] currVersion = assemblyVersion.Split('.');
                String[] servVersion = serverVersion.Split('.');

                this.form.log("Current app version : " + currVersion[0] + '.' + currVersion[1] + '.' + currVersion[2]);
                this.form.log("Server last version : " + servVersion[0] + '.' + servVersion[1] + '.' + servVersion[2]);

                bool isUpToDate = true;
                if (int.Parse(servVersion[0]) > int.Parse(currVersion[0])) isUpToDate = false;
                else if (int.Parse(servVersion[0]) == int.Parse(currVersion[0]))
                {
                    if (int.Parse(servVersion[1]) > int.Parse(currVersion[1])) isUpToDate = false;
                    else if (int.Parse(servVersion[1]) == int.Parse(currVersion[1]))
                    {
                        if (int.Parse(servVersion[2]) > int.Parse(currVersion[2])) isUpToDate = false;
                    }
                }

                if (isUpToDate) this.form.log("Application up to date !\n\n");
                else
                {
                    this.form.warn("A new version is available here : https://github.com/parashivbrl/Exporters/releases \n\n");
                    this.form.goTab("Logs");
                }
            }
            catch (Exception ex)
            {
                this.form.error($"Error : failed to check for new installer version\n{ex.Message}\n");
                this.form.goTab("Logs");
            }
        }
    }
}
