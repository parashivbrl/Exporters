using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace BabylonJS_Installer
{
    class Downloader
    {
        private static readonly string Url_github = "github.com";
        private static readonly string Api_url_github = $"api.{Url_github}";
        // Personal fork: BabylonJS upstream does not ship Maya 2025+ exporters
        private static readonly string RepoOwner = "parashivbrl";
        private static readonly string RepoName = "Exporters";
        private static readonly string Url_download = $"https://{Url_github}/{RepoOwner}/{RepoName}/releases/download";
        private static readonly string Url_github_API_releases = $"https://{Api_url_github}/repos/{RepoOwner}/{RepoName}/releases";

        private string software = "";
        private string version = "";
        private string installDir = "";
        private string installLibSubDir = "";
        private string latestRelease = "";

        public MainForm form;

        public async Task UpdateAsync(string software, string version, string installDir, string installLibSubDir)
        {
            this.form.goTab("");
            this.form.log("\n----- INSTALLING / DOWNLOADING " + software + " v" + version + " EXPORTER -----\n");

            this.software = software;
            this.version = version;
            this.installDir = SoftwareChecker.EnsureTrailingSlash(installDir);
            this.installLibSubDir = installLibSubDir;

            Action logPostInstall = () =>
            {
                if (software == "Max" && version == "2020")
                {
                    this.form.warn("\nWARNING: Max2Babylon 2020 only supports 3dsMax 2020.2 or later. Earlier versions of 3dsMax WILL crash!");
                }
                if (software == "Maya" && version == "2020")
                {
                    this.form.warn("\nWARNING: Maya2Babylon 2020 only supports Maya 2020.1 or later. Earlier versions of Maya will NOT load!");
                }
                if (software == "Maya")
                {
                    this.form.log("Maya module path: " + this.installDir);
                    this.form.log("Restart Maya (or refresh modules) if the plugin does not appear immediately.");
                }
            };

            string downloadedFileName = null;

            try
            {
                if (this.latestRelease == "")
                {
                    this.form.log("Trying to get the last version.");
                    try
                    {
                        if (!await TryRetreiveLatestReleaseAsync())
                        {
                            this.form.error("Error : Can't find the last release package.");
                            return;
                        }
                    }
                    catch
                    {
                        this.form.warn("Unable to retreive the last version.\n"
                                        + "Please, try in 1 hour. (The API limitation is 60 queries / hour)");
                        throw;
                    }
                }

                this.form.log("Downloading files : \n"
                               + Url_download + this.latestRelease);
                downloadedFileName = this.DownloadFile(this.latestRelease);
            }
            catch (Exception ex)
            {
                this.form.warn("Unable to download the files.\n"
                                + "Error message : \n"
                                + "\"" + ex.Message + "\"");
                return;
            }

            this.form.log("Download complete.\n"
                         + "Extracting files ...");

            if (!tryInstallDownloaded(downloadedFileName))
            {
                // catch and log are processed into the function.
                return;
            }

            this.form.log("\n----- " + this.software + " " + downloadedFileName + " EXPORTER UP TO DATE ----- \n");

            this.form.displayInstall(this.software, this.version);

            logPostInstall();
        }

        private async Task<bool> TryRetreiveLatestReleaseAsync()
        {
            this.form.log("Trying to get the last version ...");

            // TO DO - Parse the JSON in a more beautiful way...
            String responseBody = await this.GetJSONBodyRequest(Url_github_API_releases);
            if (string.IsNullOrEmpty(responseBody) || responseBody.IndexOf("\"prerelease\":") < 0)
            {
                return false;
            }

            String lastestReleaseInfos = responseBody.Substring(responseBody.IndexOf("\"prerelease\":") + "\"prerelease\":".Length);
            //Ensure we are on release version
            if (lastestReleaseInfos.StartsWith("false"))
            {
                //We parse the array to find the dowload URL
                this.latestRelease = lastestReleaseInfos.Substring(lastestReleaseInfos.IndexOf("\"browser_download_url\":") + "\"browser_download_url\": ".Length);

                // We split, remove & substrings to get only the URL starting with https://github.com and lasting with preRelease version
                this.latestRelease = this.latestRelease.Split('"')[0];
                this.latestRelease = this.latestRelease.Remove(this.latestRelease.LastIndexOf("/"));
                this.latestRelease = this.latestRelease.Substring(this.latestRelease.LastIndexOf("/"));
                return true;
            }
            return false;
        }

        private string DownloadFile(string releaseName)
        {
            var downloadVersion = this.version;
            if (this.software.Equals("Maya") && (this.version.Equals("2017") || this.version.Equals("2018")))
            {
                this.form.warn("Maya 2017 and 2018 have the same archive, changing version for proper download");
                downloadVersion = "2017-2018";
            }

            // Download the zip
            var srcUrl = Url_download + releaseName + "/" + this.software + "_" + downloadVersion + ".zip";
            var targetFileName = this.software + "_" + downloadVersion + ".zip";
            using (var client = new WebClient())
            {
                client.DownloadFile(srcUrl, targetFileName);
            }
            return targetFileName;
        }

        private void EnsureDirectoryForFile(string filePath)
        {
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        private bool tryInstallDownloaded(string zipFileName)
        {
            try
            {
                string pluginsDir = Path.Combine(this.installDir, this.installLibSubDir);
                string aeDir = Path.Combine(this.installDir, "scripts", "AETemplates");
                string neDir = Path.Combine(this.installDir, "scripts", "NETemplates");

                if (this.software == "Maya")
                {
                    Directory.CreateDirectory(pluginsDir);
                    Directory.CreateDirectory(aeDir);
                    Directory.CreateDirectory(neDir);
                }
                else
                {
                    Directory.CreateDirectory(Path.Combine(this.installDir, this.installLibSubDir));
                }

                using (ZipArchive myZip = ZipFile.OpenRead(zipFileName))
                {
                    foreach (ZipArchiveEntry entry in myZip.Entries)
                    {
                        if (entry.IsDirectory()) continue;
                        if (string.IsNullOrEmpty(entry.Name)) continue;

                        string destPath;
                        if (entry.Name.Length >= 9 && entry.Name.Substring(0, 9) == "AEbabylon")
                        {
                            destPath = this.software == "Maya"
                                ? Path.Combine(aeDir, entry.Name)
                                : Path.Combine(this.installDir, "scripts", "AETemplates", entry.Name);
                        }
                        else if (entry.Name.Length >= 9 && entry.Name.Substring(0, 9) == "NEbabylon")
                        {
                            destPath = this.software == "Maya"
                                ? Path.Combine(neDir, entry.Name)
                                : Path.Combine(this.installDir, "scripts", "NETemplates", entry.Name);
                        }
                        else if (entry.Name == "Maya2Babylon.dll")
                        {
                            destPath = Path.Combine(pluginsDir, "Maya2Babylon.nll.dll");
                        }
                        else
                        {
                            destPath = Path.Combine(pluginsDir, entry.Name);
                        }

                        EnsureDirectoryForFile(destPath);
                        entry.ExtractToFile(destPath, true);
                        this.form.log("Installed: " + destPath);
                    }
                }

                if (this.software == "Maya")
                {
                    WriteMayaModuleFile();
                    EnsureMayaOpenMayaCsHost(pluginsDir);
                }
            }
            catch (Exception ex)
            {
                string hint = this.software == "Maya"
                    ? "Maya installs to your Documents modules folder and should not need Administrator mode.\n"
                      + "Close Maya if it is running, then retry.\n"
                    : "If you're not, please try to run this tool in ADMINISTRATOR MODE. It's necessary to extract the files in \"Program Files\" folder (or other protected folders).\n";

                this.form.error(
                    "Can't extract the files.\n"
                    + hint
                    + "Error message : \n"
                    + "\"" + ex.Message + "\""
                    );
                return false;
            }

            this.form.log(
                "Extraction complete.\n"
                + "Deleting temporary files ..."
                );

            try
            {
                File.Delete(zipFileName);
            }
            catch (Exception ex)
            {
                this.form.error(
                    "Can't delete temporary files.\n"
                    + "Error message : \n"
                    + "\"" + ex.Message + "\""
                    );
                return false;
            }

            if (this.software == "Max")
            {
                try
                {
                    string uninstallScriptPath = this.installDir + "scripts\\Startup\\BabylonCleanUp.ms";
                    this.form.log("\nRemoving " + uninstallScriptPath + ".\n");
                    File.Delete(uninstallScriptPath);
                }
                catch (Exception ex)
                {
                    this.form.warn(
                        "Can't delete temporary script.\n"
                        + "Error message : \n"
                        + "\"" + ex.Message + "\""
                        );
                }
            }

            return true;
        }

        private void WriteMayaModuleFile()
        {
            // installDir is ...\modules\Maya2Babylon\
            string modulesDir = Directory.GetParent(this.installDir.TrimEnd('\\', '/')).FullName;
            Directory.CreateDirectory(modulesDir);
            string modFile = Path.Combine(modulesDir, "Maya2Babylon.mod");
            // Match PR #1153 / post-build deploy: plug-ins only in the .mod line
            // (scripts path is optional; AE/NE templates still live under the module)
            const string modLine = "+ Maya2Babylon 1.0 ./Maya2Babylon;plug-ins: plug-ins;scripts: scripts";
            File.WriteAllText(modFile, modLine);
            this.form.log("Wrote Maya module file: " + modFile);
        }

        /// <summary>
        /// Maya 2025+ (.NET Core) requires openmayacs.dll + openmayacs.runtimeconfig.json
        /// next to the plugin for host initialization (see BabylonJS/Exporters#1153).
        /// Prefer the local Maya install copy so the host matches that Maya year.
        /// </summary>
        private void EnsureMayaOpenMayaCsHost(string pluginsDir)
        {
            var checker = new SoftwareChecker();
            string destDll = Path.Combine(pluginsDir, "openmayacs.dll");
            string destRuntime = Path.Combine(pluginsDir, "openmayacs.runtimeconfig.json");

            string srcDll = null;
            foreach (string candidate in checker.GetMayaOpenMayaCsCandidates(this.version))
            {
                if (File.Exists(candidate))
                {
                    srcDll = candidate;
                    break;
                }
            }

            if (srcDll != null)
            {
                File.Copy(srcDll, destDll, true);
                this.form.log("Copied openmayacs.dll from Maya install: " + srcDll);

                // Prefer runtimeconfig next to the chosen DLL, then Maya bin, then keep package copy
                string srcRuntimeBesideDll = Path.Combine(Path.GetDirectoryName(srcDll) ?? "", "openmayacs.runtimeconfig.json");
                string mayaInstall = checker.getMayaInstallPath(this.version);
                string srcRuntimeBin = string.IsNullOrEmpty(mayaInstall)
                    ? null
                    : Path.Combine(mayaInstall, "bin", "openmayacs.runtimeconfig.json");

                if (File.Exists(srcRuntimeBesideDll))
                {
                    File.Copy(srcRuntimeBesideDll, destRuntime, true);
                    this.form.log("Copied openmayacs.runtimeconfig.json: " + srcRuntimeBesideDll);
                }
                else if (!string.IsNullOrEmpty(srcRuntimeBin) && File.Exists(srcRuntimeBin))
                {
                    File.Copy(srcRuntimeBin, destRuntime, true);
                    this.form.log("Copied openmayacs.runtimeconfig.json: " + srcRuntimeBin);
                }
                else if (!File.Exists(destRuntime))
                {
                    this.form.warn("openmayacs.runtimeconfig.json was not found beside Maya; keeping package copy if present.");
                }
            }
            else if (!File.Exists(destDll))
            {
                string expected = Path.Combine(
                    SoftwareChecker.GetNativeProgramFiles(),
                    "Autodesk", "Maya" + this.version, "bin", "openmayacs.dll");
                this.form.error(
                    "openmayacs.dll was not found in the Maya install or the package.\n"
                    + "Expected: " + expected + "\n"
                    + "Without this file Maya cannot start the .NET Core host for Maya2Babylon.");
            }
        }

        public async Task<string> GetJSONBodyRequest(string requestURI)
        {
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "BJS_Installer");
            try
            {
                HttpResponseMessage response = await client.GetAsync(requestURI);
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        public string GetURLGitHubAPI()
        {
            return Url_github_API_releases;
        }
    }

    public static class ZipArchiveEntryExtension
    {
        public static bool IsDirectory(this ZipArchiveEntry entry)
        {
            return entry.FullName.EndsWith("/");
        }
    }
}
