using UnityEditor;
using UnityEngine;
using System.IO;
using System;
using System.Collections.Generic;
using PSVitaUtilities.Settings;
using System.Net;
using System.IO.Compression;
using System.Net.Sockets;

namespace PSVitaUtilities
{
    public class CoreBuilder
    {
        static string buildCache = Application.dataPath.TrimEnd("Assets".ToCharArray()) + "/Build/BuildCache";
        private static string BuildPath = Application.dataPath.TrimEnd("Assets".ToCharArray()) + "/Build/TempBuild";
        public static string BuildFolder = Application.dataPath.TrimEnd("Assets".ToCharArray()) + "Build/";
        public static string fileVPK = PSVitaUtilitiesSettings.ProductName + ".vpk";
        const string MenuRoot = "Window/PSVita";
        const string buildMenu = MenuRoot + "/build";


        #region  Menu List
        [MenuItem(MenuRoot + "/Menu &%b")]
        public static void PSVitaMainMenu()
        {
            var a = EditorWindow.GetWindow<BuildSystemMenu>(true, "PSVita Main Menu");
            a.Show();
        }

        [MenuItem(MenuRoot + "/Settings")]
        static void openSettings()
        {
            PSVitaUtilitiesSettings.StartWindow();
        }

        [MenuItem(MenuRoot+"/Vita Control/Launch Game")]
        public static void LaunchGame()
        {
            PSVitaUtilitiesSettings.LoadSettings();
            if (PSVitaUtilitiesSettings.KillAllAppsBeforeLaunch)
                VitaDestroy();

            VitaControl($"launch {PSVitaUtilitiesSettings.TitleID}");
        }

        [MenuItem(MenuRoot+"/Vita Control/Launch Shell")]
        public static void VitaShellLaunch()
        {
            PSVitaUtilitiesSettings.LoadSettings();
            VitaControl("launch VITASHELL");
        }

        [MenuItem(MenuRoot + "/Vita Control/Screen On")]
        public static void ScreenOn() => VitaControl("screen on");

        [MenuItem(MenuRoot + "/Vita Control/Screen Off")]
        public static void ScreenOff() => VitaControl("screen off");

        [MenuItem(MenuRoot + "/Vita Control/Reboot")]
        public static void VitaReboot() => VitaControl("reboot");

        [MenuItem(MenuRoot + "/Vita Control/Kill All")]
        public static void VitaDestroy() => VitaControl("destroy");
        #endregion

        #region Minus Build Tools
        // ------------------------- Minus Build Tools -------------------------------------
        private static void DeleteJunk(string _buildPath)
        {
            Directory.Delete(_buildPath + "/SymbolFiles", true);
            File.Delete(_buildPath + "/configuration.psp2path");
            File.Delete(_buildPath + "/TempBuild.bat");
        }
        public static long FindPosition(Stream stream, byte[] byteSequence)
        {
            int b;
            long i = 0;
            while ((b = stream.ReadByte()) != -1)
            {
                if (b == byteSequence[i++])
                {
                    if (i == byteSequence.Length)
                        return stream.Position - byteSequence.Length;
                }
                else
                    i = b == byteSequence[0] ? 1 : 0;
            }

            return -1;
        }
        private static bool Skippable(string file)
        {
            string[] skippableEntries = new string[] { "sce_sys", "sce_module", "eboot.bin" };
            for (int i = 0; i < skippableEntries.Length; i++)
            {
                if (file.Contains(skippableEntries[i]))
                    return true;
            }
            return false;
        }

        private static void RemoveTrial(string _buildPath)
        {
            using (Stream stream = File.Open(_buildPath + "/TempBuild.self", FileMode.Open))
            {
                stream.Position = 0x80;
                stream.WriteByte(0x00);
                stream.Position = 0x00;
                if (!PSVitaUtilitiesSettings.ShowTrialWatermark)
                {
                    long pos = FindPosition(stream, new byte[] { 0x74, 0x72, 0x69, 0x61, 0x6C, 0x2E, 0x70, 0x6E, 0x67 });
                    stream.Position = pos;
                    stream.WriteByte(0x00);
                }
            }
            System.IO.File.Move(_buildPath + "/TempBuild.self", _buildPath + "/eboot.bin");
        }
        private static void MakeZip()
        {
            fileVPK = BuildFolder + fileVPK;
            if (File.Exists(fileVPK))
            {
                File.Delete(fileVPK);
            }

            ZipFile.CreateFromDirectory(BuildPath, fileVPK);
        }


        //----------------------------------------------------------------------------------
        #endregion

        static void VitaControl(string action)
        {
            PSVitaUtilitiesSettings.LoadSettings();
            TcpClient client = new TcpClient(PSVitaUtilitiesSettings.PSVitaIP, (Int32)1338);

            Byte[] data = System.Text.Encoding.ASCII.GetBytes($"{action}\n");
            NetworkStream stream = client.GetStream();
            stream.Write(data, 0, data.Length);
            stream.Close();
            client.Close();
        }

        public static void makeABuild(bool zip = false)
        {
            PSVitaUtilitiesSettings.LoadSettings();
            if (!PSVitaUtilitiesSettings.CheckValidTitleID())
            {
                Debug.LogError("TitleID Error. TitleID can be changed in PlayerSettings or in the PSVita Settings");
                return;
            }

            EditorUtility.DisplayProgressBar("Building", "Starting process...", 1f / 5f);

            Directory.CreateDirectory(BuildPath);
            // if (Directory.Exists(buildCache))
            // {
            //     Directory.Delete(buildCache, true);
            // }
            EditorUtility.DisplayProgressBar("Building", "Building project...", 2f / 5f);
            BuildPipeline.BuildPlayer(EditorBuildSettings.scenes, BuildPath, BuildTarget.PSP2, BuildOptions.None);
            DeleteJunk(BuildPath);
            EditorUtility.DisplayProgressBar("Building", "Removing trial...", 4f / 5f);
            RemoveTrial(BuildPath);

            if (zip)
            {
                EditorUtility.DisplayProgressBar("Make a zip", "Building zip", 5f / 5f);
                MakeZip();
            }

            EditorUtility.ClearProgressBar();
            Debug.Log("Build Completed");
        }

        private static string[] BuildRunCache(string _filePath)
        {
            var New = Directory.GetFiles(_filePath, "*.*", SearchOption.AllDirectories);

            if (!Directory.Exists(buildCache) || !PSVitaUtilitiesSettings.SizeCheck)
                return New;

            var Old = Directory.GetFiles(buildCache, "*.*", SearchOption.AllDirectories);
            List<string> files = new List<string>();
            for (int i = 0; i < New.Length; i++)
            {
                bool samePathLength = false;
                for (int a = 0; a < Old.Length; a++)
                {
                    if (New[i].Substring(_filePath.Length) == Old[a].Substring(buildCache.Length))
                    {
                        samePathLength = true;
                        FileInfo newFile = new FileInfo(New[i]);
                        FileInfo oldFile = new FileInfo(Old[a]);
                        if (newFile.Length != oldFile.Length)
                            files.Add(New[i]);
                        else
                        {
                            //  if (!FilesAreEqual_Hash(newFile, oldFile))
                            files.Add(New[i]);
                        }
                    }
                }
                if (!samePathLength)
                    files.Add(New[i]);
            }
            bool oldFileMissing = false;
            for (int a = 0; a < Old.Length; a++)
            {
                for (int i = 0; i < New.Length; i++)
                {
                    oldFileMissing = true;
                    if (New[i].Substring(_filePath.Length) == Old[a].Substring(buildCache.Length))
                    {
                        oldFileMissing = false;
                        break;
                    }
                }
                if (oldFileMissing)
                {
                    files = new List<string>();
                    Debug.LogError("Failed file found in cache that isnt on vita");
                    break;
                }
            }
            if (files.Count == 0)
            {
                if (!EditorUtility.DisplayDialog("Continue?", "All files appear to be exactly the same do you want to continue? (File difference check can be disabled in settings)", "Yes", "No"))
                    Debug.LogError("Files the same size, not transfering anything");
                else
                    return New;
            }
            return files.ToArray();
        }
        public static void FtpFolderUpload()
        {
            string _ip = "ftp://" + PSVitaUtilitiesSettings.PSVitaIP + ":1337/ux0:/app/" + PSVitaUtilitiesSettings.TitleID;
            Uri uri = new Uri(_ip);
            WebClient client = new WebClient();

            var a = BuildRunCache(buildCache);

        }

        public static void FtpUpload(string filePath)
        {


            string filename = System.IO.Path.GetFileName(filePath);
            FileInfo info = new FileInfo(filePath);

            EditorUtility.DisplayProgressBar("PSVita Transfer", "Preparing to upload file " + filename, 0f);
            string _ip = "ftp://" + PSVitaUtilitiesSettings.PSVitaIP + ":1337/" + "ux0:" + "/downloads" + "/" + filename;
            Uri host = new Uri(_ip);

            WebClient client = new WebClient();

            client.UploadProgressChanged += (send, ev) =>
            {
                long percent = (ev.BytesSent / info.Length);
                Debug.Log(ev.BytesSent + " " + info.Length);
                Debug.Log(percent);
                EditorUtility.DisplayProgressBar("Uploading", filename + " " + percent, (float)percent);
            };

            client.UploadFileCompleted += (send, ev) =>
            {
                if (ev.Error != null)
                {
                    EditorUtility.ClearProgressBar();
                    throw new Exception(ev.Error.Message + " " + host.Host);
                }

                Debug.Log("Upload Complete to " + host);
                EditorUtility.ClearProgressBar();
            };

            client.UploadFileAsync(host, filePath);
        }
    }
    public class BuildSystemMenu : EditorWindow
    {

        List<GUILayoutOption> styles = new List<GUILayoutOption>();
        string filePath;
        void OnEnable()
        {
            filePath = CoreBuilder.BuildFolder + CoreBuilder.fileVPK;
        }
        void OnGUI()
        {
            styles.Add(GUILayout.Width(120));
            styles.Add(GUILayout.Height(50));

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Build VPK", styles.ToArray()))
            {
                CoreBuilder.makeABuild(true);
            }
            if (GUILayout.Button("Build and transfer ", styles.ToArray()))
            {

            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Upload Vpk", styles.ToArray()))
            {
                PSVitaUtilitiesSettings.LoadSettings();

                if (!File.Exists(filePath))
                {
                    filePath = EditorUtility.OpenFilePanel("Open File VPK", "", "vpk");
                    if (filePath != "")
                    {
                        String filename = Path.GetFileName(filePath);
                    }
                    else
                    {
                        throw new Exception("no file to upload");
                    }
                }

                CoreBuilder.FtpUpload(filePath);
                // BuildVPK.TransferFTPVPK(filePath);
            }
            if (GUILayout.Button("Transfer Folder", styles.ToArray()))
            {
                Debug.Log("build VPK");
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Settings", styles.ToArray()))
            {
                PSVitaUtilitiesSettings.StartWindow();
            }
        }

    }

    struct FileFTPData
    {
        public string Name;
        public string Size;
    }

    public enum BuildMode
    {
        Normal = 0,
        FTP = 1,
        FTPTransfer = 2,
        Run = 3,
        USB = 4,
        USBRun = 5,
        EmuTransfer = 6,
        onlyBuild = 7
    }

    [System.Serializable]
    enum FileState
    {
        Missing,
        Same,
        Size,
    }

}