using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO;
using System.Linq;
using Unity.EditorCoroutines.Editor;
using UnityEngine.SceneManagement;
using UnityEditor;
using uTinyRipperGUI;
using System.Threading;
#if VRC_SDK_VRCSDK3 || VRC_SDK_VRCSDK2
using VRC.SDKBase;
using VRC.Core;
using System.Threading;
#endif
#if VRC_SDK_VRCSDK3
using Descriptor = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor;
#elif VRC_SDK_VRCSDK2
using Descriptor = VRCSDK2.VRC_AvatarDescriptor;
#endif
using System.Reflection;

namespace Thalitech.ABRipper
{
    public class ABRipper : EditorWindow
    {
        private static ABRipper win;
        private string vrcdir = string.Empty;
        private string type = "Unknown";
        public List<FileInfo> files = new List<FileInfo>();
        public int fileindex = 0;
        private int lastIndex = 0;
        AssetBundle ab;
        private Editor editor;
        public GameObject newfbx;
        public string newFile;
        internal static string status;
        private bool folderexist;
        public string directFile;

        public static double ProgressValue { get; internal set; }


        [MenuItem("Tools/AssetBundle Tools")]
        public static void ShowWindow()
        {
            win = GetWindow<ABRipper>();
            win.titleContent = new GUIContent("AssetBundle Tools");
            win.Show();
        }

        private void RefreshFiles(string overrideFile = default)
        {
            ThreadManager.Reset();
            EditorUtility.ClearProgressBar();
            Debug.ClearDeveloperConsole();
            Ripper.exporting = false;
            Ripper.exportFolderPath = Path.Combine(Application.dataPath, "Exports");
            Ripper.editor = win;

            if (string.IsNullOrEmpty(overrideFile))
            {
                vrcdir = Path.Combine(new DirectoryInfo(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)).Parent.FullName, "LocalLow\\VRChat\\VRChat\\Cache-WindowsPlayer");
                files = new DirectoryInfo(vrcdir).GetFiles("*.*", SearchOption.AllDirectories).ToList().FindAll(x => x.Name.StartsWith("__data")).ToList();
                lastIndex = fileindex;
                ThreadManager.RunOnMainThread(() =>
                {
                    var assembly = Assembly.GetAssembly(typeof(Editor));
                    var type = assembly.GetType("UnityEditor.LogEntries");
                    var method = type.GetMethod("Clear");
                    method.Invoke(new object(), null);
                });
                ThreadManager.RunOnMainThread(() => LoadNewBundle(fileindex));
            }
            else
            {
                files = new List<FileInfo>() { new FileInfo(directFile) };
                lastIndex = fileindex;
                ThreadManager.RunOnMainThread(() =>
                {
                    var assembly = Assembly.GetAssembly(typeof(Editor));
                    var type = assembly.GetType("UnityEditor.LogEntries");
                    var method = type.GetMethod("Clear");
                    method.Invoke(new object(), null);
                });
                ThreadManager.RunOnMainThread(() => LoadNewBundle(fileindex));
            }           
        }

        private void Update() => ThreadManager.Update();

        private void OnGUI()
        {
            if (!win) ShowWindow();
            Repaint();
            if (!Ripper.editor)
                Ripper.editor = this;
            UpdateInput();
            if (editor)
                editor.OnInteractivePreviewGUI(new Rect(5, 55, position.width - 10, position.height - 205), GUIStyle.none);
            EditorGUILayout.BeginHorizontal();
            CheckDynamicBones();
            CheckPoiyomiShaders();
            CheckPumkinsAvatarTools();
            CheckFBXExport();
            CheckVRChatSDK();
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Import / Re-Import All")) ImportPackage(ImportType.All, false);


            GUILayout.BeginArea(new Rect(5, position.height - 145, position.width - 10, 145));
            EditorGUI.BeginDisabledGroup(!string.IsNullOrEmpty(directFile));
            if (GUILayout.Button("Refresh")) { RefreshFiles(); }
            EditorGUI.BeginDisabledGroup(files.Count <= 0 || Ripper.exporting || !string.IsNullOrEmpty(directFile));
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(20), GUILayout.Height(80)))
            {
                fileindex--;
                if (fileindex < 0)
                    fileindex = files.Count - 1;
            }
            EditorGUILayout.BeginVertical();
            EditorGUILayout.HelpBox("VRC Directory\n" + "File: " +fileindex + "/" +  files.Count + "\n" + "Type: " + type, MessageType.None, true);
            fileindex = EditorGUILayout.IntSlider(fileindex, 0, files.Count - 1);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            if (GUILayout.Button(">", GUILayout.Width(20), GUILayout.Height(80)))
            {
                fileindex++;
                if (fileindex > files.Count - 1)
                    fileindex = 0;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Direct File");
            directFile = EditorGUILayout.TextField(directFile);
            if (GUILayout.Button("Find", GUILayout.Width(40)))
                RefreshFiles(directFile);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space((position.width / 2) - ((50 + 20 + 50) / 2));
            EditorGUI.BeginDisabledGroup(files.Count <= 0 || Ripper.exporting);
            if (GUILayout.Button(new GUIContent("(1) Preview", "Adds preview to scene"), GUILayout.Width(80))) AddToScene(directFile);
            if (GUILayout.Button($"(2) {(folderexist ? "Re-Export" : "Export")}", GUILayout.Width(70)))
                ThreadManager.RunAsync(() => Export(directFile));
            EditorGUI.EndDisabledGroup();
            var name = string.IsNullOrEmpty(directFile) ? $"Avatar{fileindex}" : new FileInfo(directFile).Name;
            EditorGUI.BeginDisabledGroup(!AssetDatabase.IsValidFolder($"Assets/Exports/{name}"));
            GUI.color = Color.yellow;
            if (GUILayout.Button("(3) Fix", GUILayout.Width(50)))
                ThreadManager.RunAsync(() => Ripper.Cleanup($"Assets/Exports/{name}"));
            GUI.color = Color.white;
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
            GUILayout.EndArea();

            if (fileindex != lastIndex)
            {
                lastIndex = fileindex;
                ThreadManager.RunOnMainThread(() => LoadNewBundle(fileindex));
            }
            if (Input.GetKeyUp(KeyCode.R))
            {
                RefreshFiles();
            }
        }

        private void UpdateInput()
        {
            if (Input.GetKeyUp(KeyCode.LeftArrow))
            {
                fileindex -= 1;
                if (fileindex < 0)
                    fileindex = files.Count - 1;
            }
            else if (Input.GetKeyUp(KeyCode.RightArrow))
            {
                fileindex += 1;
                if (fileindex > files.Count - 1)
                    fileindex = 0;
            }
            if (Input.GetKeyUp(KeyCode.Return))
            {
                if (!folderexist)
                    ThreadManager.RunAsync(() => Export());
                else
                {
                    FileUtil.DeleteFileOrDirectory($"Assets/ABExport/Avatar{fileindex}");
                    AssetDatabase.Refresh();
                    ThreadManager.RunAsync(() => Export());
                }
            }
        }

        public void AddToScene(string overrideFile = default)
        {
            var file = string.IsNullOrEmpty(overrideFile) ? directFile : overrideFile;

            var name = string.IsNullOrEmpty(file) ? $"Avatar{(string.IsNullOrEmpty(overrideFile) ? $"{fileindex}" : "[CUSTOM]")}" : new FileInfo(file).Name;
            if (!newfbx) newfbx = GameObject.Find($"{name} (PREVIEW)");
            if (newfbx) DestroyImmediate(newfbx);
            newfbx = (GameObject)Instantiate(editor.target, Vector3.zero, Quaternion.identity);
            newfbx.name = $"{name} (PREVIEW)";
            if (Application.isPlaying)
            {
#if VRC_SDK_VRCSDK3 || VRC_SDK_VRCSDK2
                Destroy(newfbx.GetComponent<Descriptor>());
                Destroy(newfbx.GetComponent<PipelineManager>());
#endif
            }
        }
        private void Export(string overrideFile = default)
        {
            Ripper.exporting = true;
            Debug.Log("Exprting started...");
            ThreadManager.RunOnMainThread(() => Application.runInBackground = true);
            var name = string.IsNullOrEmpty(overrideFile) ? $"Avatar{files.IndexOf(files.Find(x => x.FullName == newFile))}" : new FileInfo(overrideFile).Name;
            newFile = string.IsNullOrEmpty(overrideFile) ? newFile : overrideFile;
            Thread.Sleep(500);
            if (Ripper.ProcessInputFiles(newFile))
                Ripper.OnExportButtonClicked(null, name);
        }


        private void CheckDynamicBones()
        {
            var isvalid = AssetDatabase.IsValidFolder("Assets/DynamicBone");
            GUI.color = isvalid ? Color.green : Color.red;
            if (GUILayout.Button($"Dynamic Bones\n({(!isvalid ? "Import" : "Reimport")})"))
                ImportPackage(ImportType.DynamicBones, !isvalid);
            GUI.color = Color.white;
        }
        private void CheckPoiyomiShaders()
        {
            var isvalid = AssetDatabase.IsValidFolder("Assets/_PoiyomiShaders");
            GUI.color = isvalid ? Color.green : Color.red;
            if (GUILayout.Button($"Poiyomi Shader\n({(!isvalid ? "Import" : "Reimport")})"))
            {
                var shaderpro = EditorUtility.DisplayDialog("Poiyomi Shader", "What shader would you like to import", "Pro", "Toon");
                if (shaderpro)
                    ImportPackage(ImportType.PoiyomiProShaders, !isvalid);
                else
                    ImportPackage(ImportType.PoiyomiToonShaders, !isvalid);
            }

           
            GUI.color = Color.white;
        }
        private void CheckPumkinsAvatarTools()
        {
            var isvalid = AssetDatabase.IsValidFolder("Assets/PumkinsAvatarTools");
            GUI.color = isvalid ? Color.green : Color.red;
            if (GUILayout.Button($"PumkinsAvatarTools\n({(!isvalid ? "Import" : "Reimport")})"))
                ImportPackage(ImportType.PumkinsAvatarTools, !isvalid);
            GUI.color = Color.white;
        }
        private void CheckVRChatSDK()
        {
            var issdk2valid = AssetDatabase.IsValidFolder("Assets/VRCSDK/SDK2");
            var issdk3valid = AssetDatabase.IsValidFolder("Assets/VRCSDK/SDK3A");

            GUI.color = !issdk2valid && !issdk3valid ? Color.red : !issdk2valid || !issdk3valid ? Color.yellow : Color.green;
            if (GUILayout.Button($"VRChat SDK\n(Import)"))
            {
                var sdk = EditorUtility.DisplayDialog("VRChat SDk", "What sdk would you like to import", "SDK 2.0", "SDK 3.0");

                if(sdk)
                    ImportPackage(ImportType.VRChatSDK2, !issdk2valid);
                else 
                    ImportPackage(ImportType.VRChatSDK3, !issdk3valid);
            }
            GUI.color = Color.white;
        }
        private void CheckFBXExport()
        {
            var isvalid = AssetDatabase.IsValidFolder("Packages/com.unity.formats.fbx");
            GUI.color = isvalid ? Color.green : Color.red;
            if (GUILayout.Button("FBX Exporter\n(Required)"))
            {
                if (!isvalid)
                    ImportPackage(ImportType.FBXExporter, !false);
                else
                    EditorUtility.DisplayDialog("PackageManager", "Package is already installed in this project", "Okay");
            }
            GUI.color = Color.white;
        }


        public enum ImportType
        {
            DynamicBones,
            PoiyomiProShaders,
            PoiyomiToonShaders,
            PumkinsAvatarTools,
            FBXExporter,
            VRChatSDK2,
            VRChatSDK3,
            EditorCoroutines,
            All
        }
        private void ImportPackage(ImportType type, bool userInteraction = false)
        {
            switch (type)
            {
                case ImportType.DynamicBones:
                    AssetDatabase.ImportPackage("Assets/OkashiKami/AssetBundle Tools/Packages/DynamicBone.unitypackage", userInteraction);
                    break;
                case ImportType.PoiyomiProShaders:
                    AssetDatabase.ImportPackage("Assets/OkashiKami/AssetBundle Tools/Packages/PoiyomiPro.unitypackage", userInteraction);
                    break;
                case ImportType.PoiyomiToonShaders:
                    AssetDatabase.ImportPackage("Assets/OkashiKami/AssetBundle Tools/Packages/PoiyomiToon.unitypackage", userInteraction);
                    break;
                case ImportType.PumkinsAvatarTools:
                    AssetDatabase.ImportPackage("Assets/OkashiKami/AssetBundle Tools/Packages/PumkinsAvatarTools.unitypackage", userInteraction);
                    break;
                case ImportType.VRChatSDK2:
                    AssetDatabase.ImportPackage("Assets/OkashiKami/AssetBundle Tools/Packages/VRChatSDK2.unitypackage", userInteraction);
                    break;
                case ImportType.VRChatSDK3:
                    AssetDatabase.ImportPackage("Assets/OkashiKami/AssetBundle Tools/Packages/VRChatSDK3Avatar.unitypackage", userInteraction);
                    break;
                case ImportType.FBXExporter:
                    UnityEditor.PackageManager.Client.Add("com.unity.formats.fbx");
                    break;
                case ImportType.EditorCoroutines:
                    UnityEditor.PackageManager.Client.Add("com.unity.editorcoroutines");
                    break;
                case ImportType.All:
                    AssetDatabase.ImportPackage("Assets/OkashiKami/AssetBundle Tools/Packages/DynamicBone.unitypackage", userInteraction);
                    var shaderpro = EditorUtility.DisplayDialog("Poiyomi Shader", "What shader would you like to import", "Pro", "Toon");
                    if(shaderpro)
                        AssetDatabase.ImportPackage("Assets/OkashiKami/AssetBundle Tools/Packages/PoiyomiPro.unitypackage", userInteraction);
                    else
                        AssetDatabase.ImportPackage("Assets/OkashiKami/AssetBundle Tools/Packages/PoiyomiToon.unitypackage", userInteraction);
                    AssetDatabase.ImportPackage("Assets/OkashiKami/AssetBundle Tools/Packages/PumkinsAvatarTools.unitypackage", userInteraction);
                    var sdk3 = EditorUtility.DisplayDialog("VRChat SDK", "Which sdk would you like to import", "SDK 3.0", "SDK 2.0");
                    if (sdk3)
                        AssetDatabase.ImportPackage("Assets/OkashiKami/AssetBundle Tools/Packages/VRChatSDK3Avatar.unitypackage", userInteraction);
                    else
                        AssetDatabase.ImportPackage("Assets/OkashiKami/AssetBundle Tools/Packages/VRChatSDK2.unitypackage", userInteraction);
                    UnityEditor.PackageManager.Client.Add("com.unity.formats.fbx");
                    UnityEditor.PackageManager.Client.Add("com.unity.editorcoroutines");
                    break;
            }
        }



        private void LoadNewBundle(int index, bool processfiles = false)
        {
            var obj = default(UnityEngine.Object);
            if (newfbx) DestroyImmediate(newfbx);
            if (!string.IsNullOrEmpty(newFile)) newFile = string.Empty;
            AssetBundle.UnloadAllAssetBundles(true);
            if (editor) DestroyImmediate(editor);
            type = "Unknown";
            newFile = files[index].FullName;
            Ripper.curPath = newFile;
            
            AssetBundle.UnloadAllAssetBundles(true);
            ab = AssetBundle.LoadFromFile(newFile);
            
            if (ab)
            {
                if (ab.GetAllAssetNames().Length > 0 && ab.GetAllScenePaths().Length <= 0)
                {
                    type = "VRCA";
                    obj = ab.LoadAsset(ab.GetAllAssetNames().First());
                    editor = Editor.CreateEditor(obj);
                }
                else if (ab.GetAllAssetNames().Length > 0 && ab.GetAllScenePaths().Length <= 0)
                {
                    type = "VRCW";
                    SceneManager.LoadScene(ab.GetAllScenePaths().First(), new LoadSceneParameters()
                    {
                        loadSceneMode = LoadSceneMode.Additive,
                        localPhysicsMode = LocalPhysicsMode.Physics3D
                    });
                }
            }
        }
    }
}