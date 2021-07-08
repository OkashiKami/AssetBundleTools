using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using UnityEditor;
using uTinyRipper;
using uTinyRipper.Converters;
using uTinyRipper.SerializedFiles;
using uTinyRipperGUI.Exporters;
using Debug = UnityEngine.Debug;
using LogType = uTinyRipper.LogType;
using Logger = uTinyRipper.Logger;
using Version = uTinyRipper.Version;
using GameObject = UnityEngine.GameObject;
using animator = UnityEditor.Animations.AnimatorController;
using Thalitech.ABRipper;
#if PUMKIN_DBONES
using Pumkin.AvatarTools;
using Pumkin.DataStructures;
#endif
#if VRC_SDK_VRCSDK3 || VRC_SDK_VRCSDK2
using VRC.SDKBase;
using VRC.Core;
#endif
#if VRC_SDK_VRCSDK3
using VRC.SDK3.Avatars.ScriptableObjects;
using Descriptor = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor;
using System.Collections.Generic;
#elif VRC_SDK_VRCSDK2
using Descriptor = VRCSDK2.VRC_AvatarDescriptor;
#endif

namespace uTinyRipperGUI
{
    public class Ripper
	{
        public static ABRipper editor;

		public static bool AssetSelector(object asset) => true;

		// =====================================================
		// Methods
		// =====================================================

		public static bool ProcessInputFiles(params string[] files)
		{
			ThreadManager.RunOnMainThread(() => Debug.Log("Processing Input Files..."));
			if (files.Length == 0)
			{
				return false;
			}

			foreach (string file in files)
			{
				if (MultiFileStream.Exists(file))
				{
					continue;
				}
				if (DirectoryUtils.Exists(file))
				{
					continue;
				}
				Logger.Log(LogType.Warning, LogCategory.General, MultiFileStream.IsMultiFile(file) ?
					$"File '{file}' doesn't have all parts for combining" :
					$"Neither file nor directory with path '{file}' exists");
				return false;
			}
			m_processingFiles = files;
			ThreadManager.RunOnMainThread(() => Debug.Log("Files have been Processed"));
			ThreadManager.RunAsync(() => LoadFiles(files));
			return true;
		}

		private static void LoadFiles(object data)
		{
			ThreadManager.RunOnMainThread(() => Debug.Log("Loading Files..."));
			string[] files = (string[])data;
			OnImportStarted();
			try
			{
				GameStructure = GameStructure.Load(files);
			}
			catch (SerializedFileException ex)
			{
				ReportCrash(ex);
				exporting = false;
				return;
			}
			catch (Exception ex)
			{
				ReportCrash(ex);
				exporting = false;
				return;
			}

			if (GameStructure.IsValid)
			{
				Validate();
			}
			OnImportFinished();

			if (GameStructure.IsValid)
			{
				ThreadManager.RunOnMainThread(() => Debug.Log("Files have been loaded"));
			}
			else
			{
				ThreadManager.RunOnMainThread(() =>
				{
					OnResetButtonClicked(null, null);
					Logger.Log(LogType.Warning, LogCategory.Import, "No game files found");
				});
			}
		}

		private static void ExportFiles(object data)
		{
			m_exportPath = (string)data;
			PrepareExportDirectory(m_exportPath);

			TextureAssetExporter textureExporter = new TextureAssetExporter();
			GameStructure.FileCollection.Exporter.OverrideExporter(ClassIDType.Texture2D, textureExporter);
			GameStructure.FileCollection.Exporter.OverrideExporter(ClassIDType.Cubemap, textureExporter);
			GameStructure.FileCollection.Exporter.OverrideExporter(ClassIDType.Sprite, textureExporter);
			//GameStructure.FileCollection.Exporter.OverrideExporter(ClassIDType.Shader, new ShaderAssetExporter());
			GameStructure.FileCollection.Exporter.OverrideExporter(ClassIDType.TextAsset, new TextAssetExporter());
			//GameStructure.FileCollection.Exporter.OverrideExporter(ClassIDType.AudioClip, new AudioAssetExporter());
			GameStructure.FileCollection.Exporter.OverrideExporter(ClassIDType.Font, new FontAssetExporter());
			GameStructure.FileCollection.Exporter.OverrideExporter(ClassIDType.MovieTexture, new MovieTextureAssetExporter());

			EngineAssetExporter engineExporter = new EngineAssetExporter();
			GameStructure.FileCollection.Exporter.OverrideExporter(ClassIDType.Material, engineExporter);
			GameStructure.FileCollection.Exporter.OverrideExporter(ClassIDType.Texture2D, engineExporter);
			GameStructure.FileCollection.Exporter.OverrideExporter(ClassIDType.Mesh, engineExporter);
			//GameStructure.FileCollection.Exporter.OverrideExporter(ClassIDType.Shader, engineExporter);
			GameStructure.FileCollection.Exporter.OverrideExporter(ClassIDType.Font, engineExporter);
			GameStructure.FileCollection.Exporter.OverrideExporter(ClassIDType.Sprite, engineExporter);
			GameStructure.FileCollection.Exporter.OverrideExporter(ClassIDType.MonoBehaviour, engineExporter);

			ThreadManager.RunOnMainThread(() =>
			{
				Directory.CreateDirectory(m_exportPath);
				AssetDatabase.Refresh();
			});
			Thread.Sleep(3000);

			try
			{
				GameStructure.Export(m_exportPath, AssetSelector);
			}
			catch (SerializedFileException ex)
			{
				exporting = false;
				ReportCrash(ex);
				return;
			}
			catch (Exception ex)
			{
				exporting = false;
				ReportCrash(ex);
				return;
			}
			Logger.Log(LogType.Info, LogCategory.General, "Finished!!!");
			ThreadManager.RunOnMainThread(() =>
			{
				Debug.Log("Export is finished");
				EditorUtility.ClearProgressBar();
			});
			exporting = false;
		}

		private static void Validate()
		{
			Version[] versions = GameStructure.FileCollection.GameFiles.Values.Select(t => t.Version).Distinct().ToArray();
			if (versions.Length > 1)
			{
				Logger.Log(LogType.Warning, LogCategory.Import, $"Asset collection has versions probably incompatible with each other. Here they are:");
				foreach (Version version in versions)
				{
					Logger.Log(LogType.Warning, LogCategory.Import, version.ToString());
				}
			}
		}

		private static void PrepareExportDirectory(string path)
		{
			string directory = Directory.GetCurrentDirectory();
			if (!PermissionValidator.CheckAccess(directory))
			{
				string arguments = string.Join(" ", m_processingFiles.Select(t => $"\"{t}\""));
				PermissionValidator.RestartAsAdministrator(arguments);
			}

			if (DirectoryUtils.Exists(path))
			{
				ThreadManager.RunOnMainThread(() =>
				{
					FileUtil.DeleteFileOrDirectory(path);
					AssetDatabase.Refresh();
				});
			}
		}

		private static void ReportCrash(SerializedFileException ex) => ReportCrash(ex.ToString());
		private static void ReportCrash(Exception ex) => ReportCrash(ex.ToString());

		private static void ReportCrash(string error)
		{
			ThreadManager.RunOnMainThread(() =>
			{
				EditorUtility.ClearProgressBar();
				Debug.Log("crashed");
				Debug.LogError(error);
			});
		}

		private void OpenGameFolder(string platformName)
		{
			var folder = EditorUtility.OpenFolderPanel($"Select {platformName} game folder", ImportFolderPath, string.Empty);
			
		}


		public static void Cleanup(string dir)
		{
			var progress = 0.0f;
			ThreadManager.RunOnMainThread(() =>
			{
				RemoveBrokenShaders(dir);
				progress += .2f;
				EditorUtility.DisplayProgressBar("Ripper", $"Cleaning, Removeing Broken Shaders...", progress);

				FixBrokenMaterials(dir); 
				progress += .2f; 
				EditorUtility.DisplayProgressBar("AssetBundle Tools", $"Cleaning, Fixing Broken Materials...", progress);
			
				AddAvatarToScene(dir); 
				EditorUtility.DisplayProgressBar("AssetBundle Tools", $"Cleaning, Adding Avatar To Scene...", progress); 
				progress += .2f; 
				
				ReCreatePrefab(dir);
				progress += .2f; 
				EditorUtility.DisplayProgressBar("AssetBundle Tools", $"Cleaning, Re-Creating Prefab...", progress); 
				
				SetupAnimatorAndDescriptor(dir); 
				progress += .2f; 
				EditorUtility.DisplayProgressBar("AssetBundle Tools", $"Cleaning, SetupAnimatorAndDescriptor...", progress); 
				
				EditorUtility.ClearProgressBar();
			});


		}

        private static void SetupAnimatorAndDescriptor(string dir)
        {
			var path = Path.Combine(dir, "Assets", "AnimatorController").Replace("/", "\\");
			var parts = path.Split(new[] { "Assets\\E" }, 2, StringSplitOptions.RemoveEmptyEntries);
			path = Path.Combine(UnityEngine.Application.dataPath, $"E{parts[0]}").Replace("\\", "/");

			// Copy the vrchat animators to new directory
			//Create FX Layer
			var vrc_fx_gestures = "Assets/VRCSDK/Examples3/Animation/Controllers/vrc_AvatarV3HandsLayer.controller";
			var vrc_actions = "Assets/VRCSDK/Examples3/Animation/Controllers/vrc_AvatarV3ActionLayer.controller";
			var vrc_locomotion = "Assets/VRCSDK/Examples3/Animation/Controllers/vrc_AvatarV3LocomotionLayer.controller";
			var vrc_sitting = "Assets/VRCSDK/Examples3/Animation/Controllers/vrc_AvatarV3SittingLayer2.controller";
			var vrc_expressionMenu = "Assets/VRCSDK/Examples3/Expressions Menu/DefaultExpressionsMenu.asset";
			var vrc_expressionParams = "Assets/VRCSDK/Examples3/Expressions Menu/DefaultExpressionParameters.asset";

			var avatar_fx = $"Assets/Exports/{new DirectoryInfo(dir).Name}/Assets/AnimatorController/FX.controller";
			var avatar_gestures = $"Assets/Exports/{new DirectoryInfo(dir).Name}/Assets/AnimatorController/Gesture.controller";
			var avatar_actions = $"Assets/Exports/{new DirectoryInfo(dir).Name}/Assets/AnimatorController/Actions.controller";
			var avatar_locomotion = $"Assets/Exports/{new DirectoryInfo(dir).Name}/Assets/AnimatorController/Locomotion.controller";
			var avatar_sitting = $"Assets/Exports/{new DirectoryInfo(dir).Name}/Assets/AnimatorController/Sitting.controller";
			var avatar_expressionMenu = $"Assets/Exports/{new DirectoryInfo(dir).Name}/Assets/AnimatorController/ExpressionsMenu.asset";
			var avatar_expressionParams = $"Assets/Exports/{new DirectoryInfo(dir).Name}/Assets/AnimatorController/ExpressionParameters.asset";


			AssetDatabase.CopyAsset(vrc_fx_gestures, avatar_fx);
			AssetDatabase.CopyAsset(vrc_fx_gestures, avatar_gestures);
			AssetDatabase.CopyAsset(vrc_actions, avatar_actions);
			AssetDatabase.CopyAsset(vrc_locomotion, avatar_locomotion);
			AssetDatabase.CopyAsset(vrc_sitting, avatar_sitting);
			AssetDatabase.CopyAsset(vrc_expressionMenu, avatar_expressionMenu);
			AssetDatabase.CopyAsset(vrc_expressionParams, avatar_expressionParams);
#if VRC_SDK_VRCSDK3 || VRC_SDK_VRCSDK2
			var descriptor = prefab.GetComponent<Descriptor>();
			descriptor.lipSync = VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape;
			
			foreach (var transform in prefab.GetComponentsInChildren<UnityEngine.Transform>())
			{
				if (transform.name == "Face" || transform.name == "Body")
				{
					descriptor.VisemeSkinnedMesh = transform.GetComponent<UnityEngine.SkinnedMeshRenderer>();
					//descriptor.customEyeLookSettings.eyelidsSkinnedMesh = transform.GetComponent<UnityEngine.SkinnedMeshRenderer>();
				}
				else if (transform.name == "Head")
				{
					descriptor.ViewPosition = transform.position + new UnityEngine.Vector3(0, 0.11f, .06f);
				}
				//else if (transform.name == "LeftEye") descriptor.customEyeLookSettings.leftEye = transform;
				//else if (transform.name == "RightEye") descriptor.customEyeLookSettings.rightEye = transform;

			}
			descriptor.VisemeBlendShapes = new[]
			{
						"vrc.v_sil",
						"vrc.v_pp",
						"vrc.v_ff",
						"vrc.v_th",
						"vrc.v_dd",
						"vrc.v_kk",
						"vrc.v_ch",
						"vrc.v_ss",
						"vrc.v_nn",
						"vrc.v_rr",
						"vrc.v_aa",
						"vrc.v_e",
						"vrc.v_ih",
						"vrc.v_oh",
						"vrc.v_ou",
					};
#endif

			
			var overrideControllerPath = new DirectoryInfo($"Assets/Exports/{new DirectoryInfo(dir).Name}/Assets/AnimatorOverrideController/").GetFiles("*.overrideController").First().FullName;
			overrideControllerPath = overrideControllerPath.Replace("\\", "/");
			overrideControllerPath = overrideControllerPath.Replace(UnityEngine.Application.dataPath, "Assets");

			var oanimctrl = (UnityEngine.AnimatorOverrideController)AssetDatabase.LoadAssetAtPath(overrideControllerPath, typeof(UnityEngine.AnimatorOverrideController));
			var gesturesAnimCtrl = (animator)AssetDatabase.LoadAssetAtPath(avatar_gestures, typeof(animator));
			var actionsAnimCtrl = (animator)AssetDatabase.LoadAssetAtPath(avatar_actions, typeof(animator));
			
		}
		private static void ReCreatePrefab(string dir)
        {
			var path = Path.Combine(dir, "Assets", "AssetBundles").Replace("/", "\\");
			var parts = path.Split(new[] { "Assets\\E" }, 2, StringSplitOptions.RemoveEmptyEntries);
			path = Path.Combine(UnityEngine.Application.dataPath, $"E{parts[0]}").Replace("\\", "/");
			var _files = Directory.GetFiles(path, "*.prefab", SearchOption.AllDirectories).ToList();
			var go = PrefabUtility.SaveAsPrefabAssetAndConnect(prefab, _files.First(), InteractionMode.AutomatedAction);
			var _dir = new FileInfo(_files.First()).Directory.FullName;
			if (_dir.EndsWith(".unity3d"))
			{
				AssetDatabase.MoveAsset(_files.First(), Path.Combine(path, $"{new DirectoryInfo(dir).Name}.prefab").Replace("\\", "/"));
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
				FileUtil.DeleteFileOrDirectory(_dir);
			}
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			Selection.activeGameObject = go;
			prefab = go;
		}
        private static void AddAvatarToScene(string dir)
        {
			// Remove old scene Object
			if (GameObject.Find(new DirectoryInfo(dir).Name))
                UnityEngine.Object.DestroyImmediate(GameObject.Find(new DirectoryInfo(dir).Name));
			var path = Path.Combine(dir, "Assets", "AssetBundles").Replace("/", "\\");
			var parts = path.Split(new[] { "Assets\\E" }, 2, StringSplitOptions.RemoveEmptyEntries);
			path = Path.Combine(UnityEngine.Application.dataPath, $"E{parts[0]}").Replace("\\", "/");
			var files = Directory.GetFiles(path, "*.prefab", SearchOption.AllDirectories);
			foreach (var file in files)
			{
				var _file = file.Replace(UnityEngine.Application.dataPath, "Assets");
				var prefabAsset = (GameObject)AssetDatabase.LoadAssetAtPath(_file, typeof(GameObject));
				// Add prefab to the scene and break the prefab
				var _prefab = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset);
				_prefab.name = new DirectoryInfo(dir).Name;
				PrefabUtility.UnpackPrefabInstance(_prefab, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
				editor.AddToScene(); // Add preview to scene
				// Run pumkin script on the object
#if PUMKIN_DBONES
				PumkinsAvatarTools.Instance.Cleanup(_prefab, editor.newfbx);
#endif
#if VRC_SDK_VRCSDK3 || VRC_SDK_VRCSDK2

				if (!_prefab.GetComponent<Descriptor>())
					_prefab.AddComponent<Descriptor>();
				UnityEngine.Object.DestroyImmediate(editor.newfbx);
				prefab = _prefab;
#endif
			}
		}
        private static void FixBrokenMaterials(string dir)
        {
			var path = Path.Combine(dir, "Assets", "Material").Replace("/", "\\");
			var parts = path.Split(new[] { "Assets\\E" }, 2, StringSplitOptions.RemoveEmptyEntries);
			path = Path.Combine(UnityEngine.Application.dataPath, $"E{parts[0]}").Replace("\\", "/");
			var files = Directory.GetFiles(path, "*.mat", SearchOption.AllDirectories);
			foreach (var file in files)
			{
				var _file = file.Replace(UnityEngine.Application.dataPath, "Assets");
				var materialAsset = (UnityEngine.Material)AssetDatabase.LoadAssetAtPath(_file, typeof(UnityEngine.Material));
				if (materialAsset.shader.name == "Hidden/InternalErrorShader")
					materialAsset.shader = UnityEngine.Shader.Find(".poiyomi/★ Poiyomi Pro ★");
			}
			AssetDatabase.Refresh();
		}
        private static void RemoveBrokenShaders(string dir)
        {
			var path = Path.Combine(dir, "Assets", "Shader").Replace("/", "\\");
			var parts = path.Split(new[] { "Assets\\E" }, 2, StringSplitOptions.RemoveEmptyEntries);
			path = Path.Combine(UnityEngine.Application.dataPath, $"E{parts[0]}").Replace("\\", "/");
			if (DirectoryUtils.Exists(path))
			{
				FileUtil.DeleteFileOrDirectory(path);
			}
			AssetDatabase.Refresh();
		}

        // =====================================================
        // Callbacks
        // =====================================================

        private static void OnImportStarted()
		{
			ThreadManager.RunOnMainThread(() => Debug.Log("importing..."));
		}

		private static void OnImportFinished()
		{
			ThreadManager.RunOnMainThread(() => Debug.Log("import finished"));
		}

		private static void OnExportPreparationStarted()
		{
			ThreadManager.RunOnMainThread(() => Debug.Log("analyzing assets..."));
		}

		private static void OnExportPreparationFinished()
		{
			ThreadManager.RunOnMainThread(() => Debug.Log("analysis finished"));
			exporting = false;
		}

		private static void OnExportStarted()
		{
			ThreadManager.RunOnMainThread(() =>
			{
				Debug.Log("exporting...");
				EditorUtility.DisplayProgressBar("AssetBundle Tools", $"Exporting...", 0.0f);
			});
		}

		private static void OnExportProgressUpdated(int index, int count)
		{
			ThreadManager.RunOnMainThread(() =>
			{
				var progress = (float)index / count * 100.0f;
				//Debug.Log($"exporting... {index}/{count} - {progress:0.00}%");
				EditorUtility.DisplayProgressBar("AssetBundle Tools", $"Exporting... {index}/{count} - {progress:0.00}%", progress);
			});
		}

		private static void OnExportFinished()
		{
			ThreadManager.RunOnMainThread(() =>
			{
				Debug.Log("export finished");
				EditorUtility.ClearProgressBar();
			});
		}

		// =====================================================
		// Form callbacks
		// =====================================================

		private void OnOpenAndroidClicked(object sender, object e)
		{
			OpenGameFolder("Android");
		}

		private static void OnResetButtonClicked(object sender, object e)
		{
			m_processingFiles = null;
			exporting = false;
			GameStructure.Dispose();
		}

		private void OnExitButtonClicked(object sender, object e)
		{
			editor.Close();
		}

		private void OnCheckForUpdateButtonClicked(object sender, object e)
		{
			Process.Start("explorer.exe", ArchivePage);
		}

		private void OnReportBugClicked(object sender, object e)
		{
			//Process.Start("explorer.exe", MainWindow.IssuePage);
		}

		private void OnAboutButtonClicked(object sender, object e)
		{
			Process.Start("explorer.exe", ReadMePage);
		}

		public static void OnExportButtonClicked(object sender, object e)
		{
			string path = string.Empty;
			string folder = string.Empty;
			var efs = false;
			ThreadManager.RunOnMainThread(() =>
			{
			rechoose:
				folder = UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftAlt) ?  EditorUtility.OpenFolderPanel("Export Folder", exportFolderPath, string.Empty) : "Assets/Exports";
				path = Path.Combine(folder, string.IsNullOrEmpty(e.ToString()) ?  GameStructure.Name : e.ToString());
				if (File.Exists(path))
				{
					EditorUtility.DisplayDialog("Invalid Folder", "Unable to export assets into selected folder. Choose another one.", "Ok");
					goto rechoose;
				}
				if (Directory.Exists(path))
				{
					if (Directory.EnumerateFiles(path).Any())
					{
						var res = EditorUtility.DisplayDialog("Are you sure?", "There are files inside selected folder. They will be deleted.", "Yes", "No");
						if (!res)
						{
							return;
						}
					}
				}
				efs = true;
			});
			while (!efs) { }
			folder = folder.Replace("/", "\\");
			path = path.Replace("/", "\\");

			ThreadManager.RunOnMainThread(() =>
			{
				Debug.Log("Exporting assets...");
				exportFolderPath = folder;
			});
			ThreadPool.QueueUserWorkItem(new WaitCallback(ExportFiles), path);
		}

		// =====================================================
		// Properties
		// =====================================================

		private static GameStructure GameStructure
		{
			get => m_gameStructure;
			set
			{
				if (m_gameStructure == value)
				{
					return;
				}
				if (m_gameStructure != null && m_gameStructure.IsValid)
				{
					m_gameStructure.FileCollection.Exporter.EventExportFinished -= OnExportFinished;
					m_gameStructure.FileCollection.Exporter.EventExportProgressUpdated -= OnExportProgressUpdated;
					m_gameStructure.FileCollection.Exporter.EventExportStarted -= OnExportStarted;
					m_gameStructure.FileCollection.Exporter.EventExportPreparationFinished -= OnExportPreparationFinished;
					m_gameStructure.FileCollection.Exporter.EventExportPreparationStarted -= OnExportPreparationStarted;
				}
				m_gameStructure = value;
				if (value != null && value.IsValid)
				{
					value.FileCollection.Exporter.EventExportPreparationStarted += OnExportPreparationStarted;
					value.FileCollection.Exporter.EventExportPreparationFinished += OnExportPreparationFinished;
					value.FileCollection.Exporter.EventExportStarted += OnExportStarted;
					value.FileCollection.Exporter.EventExportProgressUpdated += OnExportProgressUpdated;
					value.FileCollection.Exporter.EventExportFinished += OnExportFinished;
				}
			}
		}


		public const string RepositoryPage = "https://github.com/mafaca/UtinyRipper/";
		public const string ReadMePage = RepositoryPage + "blob/master/README.md";
		public const string IssuePage = RepositoryPage + "issues/new";
		public const string ArchivePage = "https://sourceforge.net/projects/utinyripper/files/";

		private static GameStructure m_gameStructure;
		private static string m_exportPath;
		private static string[] m_processingFiles;
        public static bool exporting;
        public static string exportFolderPath;
        private static GameObject prefab;

        public static string curPath { get; set; }
        public string ImportFolderPath { get; private set; }
    }
}
