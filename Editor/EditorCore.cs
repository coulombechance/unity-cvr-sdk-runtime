﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using System;
using Cognitive3D;
using System.IO;

//contains functions for button/label styles
//references to editor prefs
//set editor define symbols
//check for sdk updates
//pre/post build inferfaces

namespace Cognitive3D
{
    using Path = System.IO.Path;
    public enum DisplayKey
    {
        FullName,
        ShortName,
        ViewerName,
        Other,
        ManagerName,
        GatewayURL,
        DashboardURL,
        ViewerURL,
        DocumentationURL,
    }

    [InitializeOnLoad]
    public class EditorCore
    {
        static EditorCore()
        {
            //check sdk versions
            CheckForUpdates();

            string savedSDKVersion = EditorPrefs.GetString("cognitive_sdk_version", "");
            if (string.IsNullOrEmpty(savedSDKVersion) || Cognitive3D_Manager.SDK_VERSION != savedSDKVersion)
            {
                Debug.Log("Cognitive3D SDK version " + Cognitive3D_Manager.SDK_VERSION);
                EditorPrefs.SetString("cognitive_sdk_version", Cognitive3D_Manager.SDK_VERSION);
            }

            if (!Cognitive3D_Preferences.Instance.EditorHasDisplayedPopup)
            {
                Cognitive3D_Preferences.Instance.EditorHasDisplayedPopup = true;
                EditorApplication.update += UpdateInitWizard;
            }

            if (Cognitive3D.Cognitive3D_Preferences.Instance.LocalStorage && Cognitive3D_Preferences.Instance.UploadCacheOnEndPlay)
            {
                EditorApplication.playModeStateChanged -= ModeChanged;
                EditorApplication.playModeStateChanged += ModeChanged;
            }
        }

        //there's some new bug in 2021.1.15ish. creating editor window in constructor gets BaseLiveReloadAssetTracker. delay to avoid that
        static int initDelay = 4;
        static void UpdateInitWizard()
        {
            if (initDelay > 0) { initDelay--; return; }
            EditorApplication.update -= UpdateInitWizard;
            InitWizard.Init();
        }

        static void ModeChanged(PlayModeStateChange playModeState)
        {
            if (playModeState == PlayModeStateChange.EnteredEditMode)
            {
                if (Cognitive3D.Cognitive3D_Preferences.Instance.LocalStorage && Cognitive3D_Preferences.Instance.UploadCacheOnEndPlay)
                    EditorApplication.update += DelayUploadCache;
                EditorApplication.playModeStateChanged -= ModeChanged;
                uploadDelayFrames = 10;
            }
        }

        static int uploadDelayFrames = 0;
        private static void DelayUploadCache()
        {
            uploadDelayFrames--;
            if (uploadDelayFrames < 0)
            {
                EditorApplication.update -= DelayUploadCache;
                Cognitive3D.ICache ic = new Cognitive3D.DualFileCache(Application.persistentDataPath + "/c3dlocal/");
                if (ic.HasContent())
                    new Cognitive3D.EditorDataUploader(ic);
            }
        }

        public static void SpawnManager(string gameobjectName)
        {
            GameObject newManager = new GameObject(gameobjectName);
            Selection.activeGameObject = newManager;
            Undo.RegisterCreatedObjectUndo(newManager, "Create " + gameobjectName);
            newManager.AddComponent<Cognitive3D_Manager>();
            newManager.AddComponent<Cognitive3D.Components.HMDHeight>();
            newManager.AddComponent<Cognitive3D.Components.RoomSize>();
            newManager.AddComponent<Cognitive3D.Components.ArmLength>();
        }

        public static DynamicObjectIdPool[] _cachedPoolAssets;
        /// <summary>
        /// search the project database and return an array of all DynamicObjectPool assets
        /// </summary>
        public static DynamicObjectIdPool[] GetDynamicObjectPoolAssets
        {
            get
            {
                if (_cachedPoolAssets == null)
                {
                    _cachedPoolAssets = new DynamicObjectIdPool[0];
                    string[] guids = AssetDatabase.FindAssets("t:dynamicobjectidpool");
                    foreach (var guid in guids)
                    {
                        ArrayUtility.Add<DynamicObjectIdPool>(ref _cachedPoolAssets, AssetDatabase.LoadAssetAtPath<DynamicObjectIdPool>(AssetDatabase.GUIDToAssetPath(guid)));
                    }
                }
                return _cachedPoolAssets;
            }
        }

        public static bool IsDeveloperKeyValid
        {
            get
            {
                return EditorPrefs.HasKey("developerkey") && !string.IsNullOrEmpty(EditorPrefs.GetString("developerkey"));
            }
        }

        public static string DeveloperKey
        {
            get
            {
                return EditorPrefs.GetString("developerkey");
            }
        }

        public static void SetPlayerDefine(List<string> C3DSymbols)
        {
            //get all scripting define symbols
            string s = PlayerSettings.GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
            string[] ExistingSymbols = s.Split(';');

            //categorizing definition symbols
            List<string> ExistingNonC3DSymbols = new List<string>();
            foreach (var v in ExistingSymbols)
            {
                if (!v.StartsWith("C3D_"))
                {
                    ExistingNonC3DSymbols.Add(v);
                }
            }
            //foreach (var v in ExistingSymbols) Debug.Log("existing defines " + v);
            //foreach (var v in C3DSymbols) Debug.Log("C3D defines " + v);
            //foreach (var v in ExistingNonC3DSymbols) Debug.Log("existing non cvr defines " + v);

            //IMPROVEMENT check if ExistingSymbols == (C3DSymbols + ExistingNonC3DSymbols) regardless of order

            //combine symbols
            List<string> finalDefines = new List<string>();
            foreach (var v in ExistingNonC3DSymbols)
                finalDefines.Add(v);
            foreach (var v in C3DSymbols)
                finalDefines.Add(v);

            //rebuild symbols
            string alldefines = "";
            for (int i = 0; i < finalDefines.Count; i++)
            {
                if (!string.IsNullOrEmpty(finalDefines[i]))
                {
                    alldefines += finalDefines[i] + ";";
                }
            }
            PlayerSettings.SetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup, alldefines);
        }

        static Cognitive3D_Preferences _prefs;
        /// <summary>
        /// Gets the Cognitive3D_preferences or creates and returns new default preferences
        /// </summary>
        public static Cognitive3D_Preferences GetPreferences()
        {
            if (_prefs == null)
            {
                _prefs = Resources.Load<Cognitive3D_Preferences>("Cognitive3D_Preferences");
                if (_prefs == null)
                {
                    _prefs = ScriptableObject.CreateInstance<Cognitive3D_Preferences>();
                    string filepath = "Assets/Resources/";

                    if (!AssetDatabase.IsValidFolder(filepath))
                    {
                        AssetDatabase.CreateFolder("Assets", "Resources");
                    }

                    //go with the base resource folder if it exists
                    //if (!RecursiveDirectorySearch("", out filepath, "Cognitive3D" + System.IO.Path.DirectorySeparatorChar + "Resources"))
                    //{ Debug.LogError("couldn't find Cognitive3D/Resources folder"); }



                    AssetDatabase.CreateAsset(_prefs, filepath + System.IO.Path.DirectorySeparatorChar + "Cognitive3D_Preferences.asset");

                    /*List<string> names = new List<string>();
                    List<string> paths = new List<string>();
                    GetAllScenes(names, paths);
                    for (int i = 0; i < names.Count; i++)
                    {
                        Cognitive3D_Preferences.AddSceneSettings(_prefs, names[i], paths[i]);
                    }*/
                    EditorUtility.SetDirty(EditorCore.GetPreferences());
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }
            }
            return _prefs;
        }

        /// <summary>
        /// used to search through project directories to find cognitive resources
        /// </summary>
        public static bool RecursiveDirectorySearch(string directory, out string filepath, string searchDir)
        {
            if (directory.EndsWith(searchDir))
            {
                filepath = "Assets" + directory.Substring(Application.dataPath.Length);
                return true;
            }
            foreach (var dir in System.IO.Directory.GetDirectories(System.IO.Path.Combine(Application.dataPath, directory)))
            {
                RecursiveDirectorySearch(dir, out filepath, searchDir);
                if (filepath != "") { return true; }
            }
            filepath = "";
            return false;
        }

        /// <summary>
        /// return a list of all scene names and paths from the project
        /// </summary>
        /// <param name="names"></param>
        /// <param name="paths"></param>
        public static void GetAllScenes(List<string> names, List<string> paths)
        {
            string[] guidList = AssetDatabase.FindAssets("t:scene");
            foreach (var v in guidList)
            {
                string path = AssetDatabase.GUIDToAssetPath(v);
                string name = path.Substring(path.LastIndexOf('/') + 1);
                name = name.Substring(0, name.Length - 6);
                names.Add(name);
                paths.Add(path);
            }
        }

        static System.Action RefreshSceneVersionComplete;
        /// <summary>
        /// make a get request for all scene versions of this scene
        /// </summary>
        /// <param name="refreshSceneVersionComplete"></param>
        public static void RefreshSceneVersion(System.Action refreshSceneVersionComplete)
        {
            //Debug.Log("refresh scene version");
            //gets the scene version from api and sets it to the current scene
            string currentSceneName = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().name;
            var currentSettings = Cognitive3D_Preferences.FindScene(currentSceneName);
            if (currentSettings != null)
            {
                if (!IsDeveloperKeyValid) { Debug.Log("Developer key invalid"); return; }
                if (currentSettings == null)
                {
                    Debug.Log("SendSceneVersionRequest no scene settings!");
                    return;
                }
                if (string.IsNullOrEmpty(currentSettings.SceneId))
                {
                    if (refreshSceneVersionComplete != null)
                        refreshSceneVersionComplete.Invoke();
                    return;
                }

                RefreshSceneVersionComplete = refreshSceneVersionComplete;
                string url = CognitiveStatics.GETSCENEVERSIONS(currentSettings.SceneId);
                Dictionary<string, string> headers = new Dictionary<string, string>();
                headers.Add("Authorization", "APIKEY:DEVELOPER " + DeveloperKey);
                EditorNetwork.Get(url, GetSceneVersionResponse, headers, true, "Get Scene Version");//AUTH
            }
            else
            {
                Debug.Log("No scene versions for scene: " + currentSceneName);
            }
        }

        private static void GetSceneVersionResponse(int responsecode, string error, string text)
        {
            if (responsecode != 200)
            {
                RefreshSceneVersionComplete = null;
                Debug.LogError("GetSettingsResponse [CODE] " + responsecode + " [ERROR] " + error);
                EditorUtility.DisplayDialog("Error Getting Scene Version", "There was an error getting data about this scene from the Cognitive3D Dashboard. Response code was " + responsecode + ".\n\nSee Console for more details", "Ok");
                return;
            }
            var settings = Cognitive3D_Preferences.FindCurrentScene();
            if (settings == null)
            {
                //this should be impossible, but might happen if changing scenes at exact time
                RefreshSceneVersionComplete = null;
                Debug.LogError("Scene version request returned 200, but current scene cannot be found");
                return;
            }

            //receive and apply scene version data to preferences
            var collection = JsonUtility.FromJson<SceneVersionCollection>(text);
            if (collection != null)
            {
                settings.VersionId = collection.GetLatestVersion().id;
                settings.VersionNumber = collection.GetLatestVersion().versionNumber;
                EditorUtility.SetDirty(Cognitive3D_Preferences.Instance);
                AssetDatabase.SaveAssets();
            }
            if (RefreshSceneVersionComplete != null)
            {
                RefreshSceneVersionComplete.Invoke();
            }
        }

#region GUI
        public static Color GreenButton = new Color(0.4f, 1f, 0.4f);
        public static Color BlueishGrey = new Color32(0xE8, 0xEB, 0xFF, 0xFF);

        static GUIStyle headerStyle;
        public static GUIStyle HeaderStyle
        {
            get
            {
                if (headerStyle == null)
                {
                    headerStyle = new GUIStyle(EditorStyles.largeLabel);
                    headerStyle.fontSize = 14;
                    headerStyle.alignment = TextAnchor.UpperCenter;
                    headerStyle.fontStyle = FontStyle.Bold;
                    headerStyle.richText = true;
                }
                return headerStyle;
            }
        }

        private static GUISkin _wizardGuiSkin;
        public static GUISkin WizardGUISkin
        {
            get
            {
                if (_wizardGuiSkin == null)
                {
                    _wizardGuiSkin = Resources.Load<GUISkin>("WizardGUISkin");
                }
                return _wizardGuiSkin;
            }
        }

        static Color AcceptVibrant
        {
            get
            {
                if (EditorGUIUtility.isProSkin)
                {
                    return Color.green;
                }
                return Color.green;
            }
        }

        static Color DeclineVibrant
        {
            get
            {
                if (EditorGUIUtility.isProSkin)
                {
                    return Color.red;
                }
                return Color.red;
            }
        }

        public static void GUIHorizontalLine(float size = 10)
        {
            GUILayout.Space(size);
            GUILayout.Box("", new GUILayoutOption[] { GUILayout.ExpandWidth(true), GUILayout.Height(1) });
            GUILayout.Space(size);
        }

        public static bool AcceptButtonLarge(string text)
        {
            GUI.color = AcceptVibrant;
            if (GUILayout.Button(text, GUILayout.Height(40)))
                return true;
            GUI.color = Color.white;
            return false;
        }

        public static bool DeclineButtonLarge(string text)
        {
            GUI.color = DeclineVibrant;
            if (GUILayout.Button(text, GUILayout.Height(40)))
                return true;
            GUI.color = Color.white;
            return false;
        }

        public static void DisableButtonLarge(string text)
        {
            EditorGUI.BeginDisabledGroup(true);
            GUILayout.Button(text, GUILayout.Height(40));
            EditorGUI.EndDisabledGroup();
        }

        public static bool DisableableAcceptButtonLarget(string text, bool disable)
        {
            if (disable)
            {
                DisableButtonLarge(text);
                return false;
            }
            return AcceptButtonLarge(text);
        }

        //https://forum.unity.com/threads/how-to-copy-and-paste-in-a-custom-editor-textfield.261087/
        /// <summary>
        /// Add copy-paste functionality to any text field
        /// Returns changed text or NULL.
        /// Usage: text = HandleCopyPaste (controlID) ?? text;
        /// </summary>
        public static string HandleCopyPaste(int controlID)
        {
            if (controlID == GUIUtility.keyboardControl)
            {
                if (Event.current.type == EventType.KeyUp && (Event.current.modifiers == EventModifiers.Control || Event.current.modifiers == EventModifiers.Command))
                {
                    if (Event.current.keyCode == KeyCode.C)
                    {
                        Event.current.Use();
                        TextEditor editor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
                        editor.Copy();
                    }
                    else if (Event.current.keyCode == KeyCode.V)
                    {
                        Event.current.Use();
                        TextEditor editor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
                        editor.Paste();
                        return editor.text;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// TextField with copy-paste support
        /// </summary>
        public static string TextField(Rect rect, string value, int maxlength, string styleOverride = null)
        {
            int textFieldID = GUIUtility.GetControlID("TextField".GetHashCode(), FocusType.Keyboard) + 1;
            if (textFieldID == 0)
                return value;

#if !UNITY_2019_4_OR_NEWER
            //enables copy/paste from text fields in old versions of unity
            value = HandleCopyPaste(textFieldID) ?? value;
#endif
            if (styleOverride == null)
            {
                return GUI.TextField(rect, value, maxlength, GUI.skin.textField);
            }
            else
            {
                return GUI.TextField(rect, value, maxlength, styleOverride);
            }
        }
#endregion

#region Icons and Textures
        private static Texture2D _logo;
        public static Texture2D LogoTexture
        {
            get
            {
                if (_logo == null)
                    _logo = Resources.Load<Texture2D>("cognitive3d-editorlogo");
                return _logo;
            }
        }

        private static Texture2D _checkmark;
        public static Texture2D Checkmark
        {
            get
            {
                if (_checkmark == null)
                    _checkmark = Resources.Load<Texture2D>("checkmark_icon");
                return _checkmark;
            }
        }

        private static Texture2D _controller;
        public static Texture2D Controller
        {
            get
            {
                if (_controller == null)
                    _controller = Resources.Load<Texture2D>("controller_icon");
                return _controller;
            }
        }

        private static Texture2D _blueCheckmark;
        public static Texture2D BlueCheckmark
        {
            get
            {
                if (_blueCheckmark == null)
                    _blueCheckmark = Resources.Load<Texture2D>("blue_checkmark_icon");
                return _blueCheckmark;
            }
        }

        private static Texture2D _alert;
        public static Texture2D Alert
        {
            get
            {
                if (_alert == null)
                    _alert = Resources.Load<Texture2D>("alert_icon");
                return _alert;
            }
        }

        private static Texture2D _error;
        public static Texture2D Error
        {
            get
            {
                if (_error == null)
                    _error = Resources.Load<Texture2D>("error_icon");
                return _error;
            }
        }

        private static Texture2D _question;
        public static Texture2D Question
        {
            get
            {
                if (_question == null)
                    _question = Resources.Load<Texture2D>("question_icon");
                return _question;
            }
        }

        private static Texture2D _info;
        public static Texture2D Info
        {
            get
            {
                if (_info == null)
                    _info = Resources.Load<Texture2D>("info_icon");
                return _info;
            }
        }

        private static Texture2D _searchIcon;
        public static Texture2D SearchIcon
        {
            get
            {
                if (_searchIcon == null)
                    _searchIcon = EditorGUIUtility.FindTexture("search focused"); //TODO use an asset in package, not unity engine
                return _searchIcon;
            }
        }

        private static Texture2D _emptycheckmark;
        public static Texture2D EmptyCheckmark
        {
            get
            {
                if (_emptycheckmark == null)
                    _emptycheckmark = Resources.Load<Texture2D>("grey_circle");
                return _emptycheckmark;
            }
        }

        private static Texture2D _emptybluecheckmark;
        public static Texture2D EmptyBlueCheckmark
        {
            get
            {
                if (_emptybluecheckmark == null)
                    _emptybluecheckmark = Resources.Load<Texture2D>("blue_circle");
                return _emptybluecheckmark;
            }
        }        

        private static Texture2D _sceneHighlight;
        public static Texture2D SceneHighlight
        {
            get
            {
                if (_sceneHighlight == null)
                    _sceneHighlight = Resources.Load<Texture2D>("scene_blue");
                return _sceneHighlight;
            }
        }

        private static Texture2D _sceneBackground;
        public static Texture2D SceneBackground
        {
            get
            {
                if (_sceneBackground == null)
                    _sceneBackground = Resources.Load<Texture2D>("scene_grey");
                return _sceneBackground;
            }
        }
        private static Texture2D _sceneBackgroundHalf;
        public static Texture2D SceneBackgroundHalf
        {
            get
            {
                if (_sceneBackgroundHalf == null)
                    _sceneBackgroundHalf = Resources.Load<Texture2D>("scene_grey_half");
                return _sceneBackgroundHalf;
            }
        }
        private static Texture2D _sceneBackgroundQuarter;
        public static Texture2D SceneBackgroundQuarter
        {
            get
            {
                if (_sceneBackgroundQuarter == null)
                    _sceneBackgroundQuarter = Resources.Load<Texture2D>("scene_grey_quarter");
                return _sceneBackgroundQuarter;
            }
        }

        private static Texture2D _objectsHighlight;
        public static Texture2D ObjectsHightlight
        {
            get
            {
                if (_objectsHighlight == null)
                    _objectsHighlight = Resources.Load<Texture2D>("objects_blue");
                return _objectsHighlight;
            }
        }

        private static Texture2D _objectsBackground;
        public static Texture2D ObjectsBackground
        {
            get
            {
                if (_objectsBackground == null)
                    _objectsBackground = Resources.Load<Texture2D>("objects_grey");
                return _objectsBackground;
            }
        }

        private static Texture2D _settingsIcon;
        public static Texture2D SettingsIcon
        {
            get
            {
                if (_settingsIcon == null)
                    _settingsIcon = EditorGUIUtility.FindTexture("_Popup"); //TODO use an asset in package, not unity engine
                return _settingsIcon;
            }
        }
        private static Texture2D _filterIcon;
        public static Texture2D FilterIcon
        {
            get
            {
                if (_filterIcon == null)
                    _filterIcon = EditorGUIUtility.FindTexture("d_FilterByType"); //TODO use an asset in package, not unity engine
                return _filterIcon;
            }
        }

#endregion

#region Media

        /// <summary>
        /// make a get request to get media available to this scene
        /// </summary>
        public static void RefreshMediaSources()
        {
            Debug.Log("refresh media sources");
            //gets the scene version from api and sets it to the current scene
            string currentSceneName = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().name;
            var currentSettings = Cognitive3D_Preferences.FindScene(currentSceneName);
            if (currentSettings != null)
            {
                if (!IsDeveloperKeyValid) { Debug.Log("Developer key invalid"); return; }

                if (currentSettings == null)
                {
                    Debug.Log("SendSceneVersionRequest no scene settings!");
                    return;
                }
                string url = CognitiveStatics.GETMEDIASOURCELIST();
                Dictionary<string, string> headers = new Dictionary<string, string>();
                headers.Add("Authorization", "APIKEY:DEVELOPER " + EditorCore.DeveloperKey);
                EditorNetwork.Get(url, GetMediaSourcesResponse, headers, true, "Get Scene Version");//AUTH
            }
            else
            {
                Debug.Log("No scene versions for scene: " + currentSceneName);
            }
        }

        /// <summary>
        /// response from get media sources
        /// </summary>
        private static void GetMediaSourcesResponse(int responsecode, string error, string text)
        {
            if (responsecode != 200)
            {
                RefreshSceneVersionComplete = null;
                //internal server error
                Debug.LogError("GetMediaSourcesResponse Error [CODE] " + responsecode + " [ERROR] " + error);
                return;
            }

            MediaSource[] sources = Util.GetJsonArray<MediaSource>(text);
            Debug.Log("Response contains " + sources.Length + " media sources");
            UnityEditor.ArrayUtility.Insert<MediaSource>(ref sources, 0, new MediaSource());
            MediaSources = sources;
        }

        [Serializable]
        public class MediaSource
        {
            public string name;
            public string uploadId;
            public string description;
        }

        public static MediaSource[] MediaSources = new MediaSource[] { };
#endregion

#region Scene Setup Display Names
        static TextAsset DisplayNameAsset;
        static bool HasLoadedDisplayNames;
        public static string DisplayValue(DisplayKey key)
        {
            if (DisplayNameAsset == null && !HasLoadedDisplayNames)
            {
                HasLoadedDisplayNames = true;
                DisplayNameAsset = Resources.Load<TextAsset>("DisplayNames");
            }
            if (DisplayNameAsset == null)
            {
                //default
                switch (key)
                {
                    case DisplayKey.GatewayURL: return Cognitive3D_Preferences.Instance.Gateway;
                    case DisplayKey.DashboardURL: return Cognitive3D_Preferences.Instance.Dashboard;
                    case DisplayKey.ViewerName: return "Scene Explorer";
                    case DisplayKey.ViewerURL: return Cognitive3D_Preferences.Instance.Viewer;
                    case DisplayKey.DocumentationURL: return Cognitive3D_Preferences.Instance.Documentation;
                    case DisplayKey.FullName: return "Cognitive3D";
                    case DisplayKey.ShortName: return "Cognitive3D";
                    case DisplayKey.ManagerName: return "Cognitive3D_Manager";
                }
                return "unknown";
            }
            else
            {
                var lines = DisplayNameAsset.text.Split('\n');
                foreach (var line in lines)
                {
                    if (line.Length == 0) { continue; }
                    if (line.StartsWith("//")) { continue; }
                    string replacement = System.Text.RegularExpressions.Regex.Replace(line, @"\t|\n|\r", "");
                    var split = replacement.Split('|');
                    foreach (var keyvalue in (DisplayKey[])System.Enum.GetValues(typeof(DisplayKey)))
                    {
                        if (split[0].ToUpper() == key.ToString().ToUpper())
                        {
                            return split[1];
                        }
                    }
                }
            }
            return "unknown";
        }
#endregion

#region SDK Updates
        //data about the last sdk release on github
        public class ReleaseInfo
        {
            public string tag_name;
            public string body;
            public string created_at;
        }

        private static void SaveEditorVersion()
        {
            if (EditorPrefs.GetString("c3d_version") != Cognitive3D.Cognitive3D_Manager.SDK_VERSION)
            {
                EditorPrefs.SetString("c3d_version", Cognitive3D.Cognitive3D_Manager.SDK_VERSION);
                EditorPrefs.SetString("c3d_updateDate", System.DateTime.UtcNow.ToString("dd-MM-yyyy"));
            }
        }

        public static void ForceCheckUpdates()
        {
            EditorPrefs.SetString("c3d_skipVersion", "");
            EditorApplication.update -= UpdateCheckForUpdates;
            EditorPrefs.SetString("c3d_updateRemindDate", System.DateTime.UtcNow.AddDays(1).ToString("dd-MM-yyyy"));
            SaveEditorVersion();

            checkForUpdatesRequest = UnityEngine.Networking.UnityWebRequest.Get(CognitiveStatics.GITHUB_SDKVERSION);
            checkForUpdatesRequest.Send();
            EditorApplication.update += UpdateCheckForUpdates;
        }

        static UnityEngine.Networking.UnityWebRequest checkForUpdatesRequest;
        static void CheckForUpdates()
        {
            System.DateTime remindDate; //current date must be this or beyond to show popup window

            System.Globalization.CultureInfo culture = System.Globalization.CultureInfo.InvariantCulture;
            System.Globalization.DateTimeStyles styles = System.Globalization.DateTimeStyles.None;

            if (System.DateTime.TryParseExact(EditorPrefs.GetString("c3d_updateRemindDate", "01/01/1971"), "dd-MM-yyyy", culture, styles, out remindDate))
            {
                if (System.DateTime.UtcNow > remindDate)
                {
                    EditorPrefs.SetString("c3d_updateRemindDate", System.DateTime.UtcNow.AddDays(1).ToString("dd-MM-yyyy"));
                    SaveEditorVersion();

                    checkForUpdatesRequest = UnityEngine.Networking.UnityWebRequest.Get(CognitiveStatics.GITHUB_SDKVERSION);
                    checkForUpdatesRequest.Send();
                    EditorApplication.update += UpdateCheckForUpdates;
                }
            }
            else
            {
                EditorPrefs.SetString("c3d_updateRemindDate", System.DateTime.UtcNow.AddDays(1).ToString("dd-MM-yyyy"));
            }
        }

        static void UpdateCheckForUpdates()
        {
            if (!checkForUpdatesRequest.isDone)
            {
                //IMPROVEMENT check for timeout
            }
            else
            {
                if (!string.IsNullOrEmpty(checkForUpdatesRequest.error))
                {
                    Debug.Log("Check for Cognitive3D SDK version update error: " + checkForUpdatesRequest.error);
                }

                if (!string.IsNullOrEmpty(checkForUpdatesRequest.downloadHandler.text))
                {
                    var info = JsonUtility.FromJson<ReleaseInfo>(checkForUpdatesRequest.downloadHandler.text);

                    var version = info.tag_name;
                    string summary = info.body;

                    if (!string.IsNullOrEmpty(version))
                    {
                        string skipVersion = EditorPrefs.GetString("c3d_skipVersion");

                        if (version != skipVersion) //new version, not the skipped one
                        {
                            System.Version installedVersion = new Version(Cognitive3D_Manager.SDK_VERSION);
                            System.Version githubVersion = new Version(version);
                            if (githubVersion > installedVersion)
                            {
                                UpdateSDKWindow.Init(version, summary);
                            }
                            else
                            {
                                Debug.Log("Cognitive3D Version: " + installedVersion + ". Up to date!");
                            }
                        }
                        else if (skipVersion == version) //skip this version. limit this check to once a day
                        {
                            EditorPrefs.SetString("c3d_updateRemindDate", System.DateTime.UtcNow.AddDays(1).ToString("dd-MM-yyyy"));
                        }
                    }
                }
                EditorApplication.update -= UpdateCheckForUpdates;
            }
        }

#endregion

#region Files and Directories

        internal static bool HasDynamicExportFiles(string meshname)
        {
            string dynamicExportDirectory = Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar + "Cognitive3D_SceneExplorerExport" + Path.DirectorySeparatorChar + "Dynamic" + Path.DirectorySeparatorChar + meshname;
            var SceneExportDirExists = Directory.Exists(dynamicExportDirectory);
            return SceneExportDirExists && Directory.GetFiles(dynamicExportDirectory).Length > 0;
        }

        internal static bool HasDynamicObjectThumbnail(string meshname)
        {
            string dynamicExportDirectory = Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar + "Cognitive3D_SceneExplorerExport" + Path.DirectorySeparatorChar + "Dynamic" + Path.DirectorySeparatorChar + meshname;
            var SceneExportDirExists = Directory.Exists(dynamicExportDirectory);

            if (!SceneExportDirExists) return false;

            var files = Directory.GetFiles(dynamicExportDirectory);
            for (int i = 0;i<files.Length; i++)
            {
                if (files[i].EndsWith("cvr_object_thumbnail.png"))
                { return true; }
            }
            return false;
        }

        public static bool CreateTargetFolder(string fullName)
        {
            try
            {
                Directory.CreateDirectory("Cognitive3D_SceneExplorerExport" + Path.DirectorySeparatorChar + fullName);
            }
            catch
            {
                EditorUtility.DisplayDialog("Error!", "Failed to create folder: Cognitive3D_SceneExplorerExport" + Path.DirectorySeparatorChar + fullName, "Ok");
                return false;
            }
            return true;
        }

        /// <summary>
        /// return path to Cognitive3D_SceneExplorerExport/NAME. create if it doesn't exist
        /// </summary>
        public static string GetSubDirectoryPath(string directoryName)
        {
            CreateTargetFolder(directoryName);
            return Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar + "Cognitive3D_SceneExplorerExport" + Path.DirectorySeparatorChar + directoryName + Path.DirectorySeparatorChar;
        }

        /// <summary>
        /// returns path to Cognitive3D_SceneExplorerExport
        /// </summary>
        public static string GetBaseDirectoryPath()
        {
            return Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar + "Cognitive3D_SceneExplorerExport" + Path.DirectorySeparatorChar;
        }

        /// <summary>
        /// has scene export directory AND files for scene
        /// </summary>
        public static bool HasSceneExportFiles(Cognitive3D_Preferences.SceneSettings currentSceneSettings)
        {
            if (currentSceneSettings == null) { return false; }
            string sceneExportDirectory = Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar + "Cognitive3D_SceneExplorerExport" + Path.DirectorySeparatorChar + currentSceneSettings.SceneName + Path.DirectorySeparatorChar;
            var SceneExportDirExists = Directory.Exists(sceneExportDirectory);
            if (!SceneExportDirExists) { return false; }

            var files = Directory.GetFiles(sceneExportDirectory);
            bool hasBin = false;
            bool hasGltf = false; ;
            foreach (var f in files)
            {
                if (f.EndsWith("scene.bin"))
                    hasBin = true;
                if (f.EndsWith("scene.gltf"))
                    hasGltf = true;
            }

            return SceneExportDirExists && files.Length > 0 && hasBin && hasGltf;
        }

        /// <summary>
        /// get scene export directory. returns "" if no directory exists
        /// </summary>
        public static string GetSceneExportDirectory(Cognitive3D_Preferences.SceneSettings currentSceneSettings)
        {
            if (currentSceneSettings == null) { return ""; }
            string sceneExportDirectory = Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar + "Cognitive3D_SceneExplorerExport" + Path.DirectorySeparatorChar + currentSceneSettings.SceneName + Path.DirectorySeparatorChar;
            var SceneExportDirExists = Directory.Exists(sceneExportDirectory);
            if (!SceneExportDirExists) { return ""; }
            return sceneExportDirectory;
        }

        public static string GetDynamicExportDirectory()
        {
            string dynamicExportDirectory = Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar + "Cognitive3D_SceneExplorerExport" + Path.DirectorySeparatorChar + "Dynamic" + Path.DirectorySeparatorChar;
            var ExportDirExists = Directory.Exists(dynamicExportDirectory);
            if (!ExportDirExists) { return ""; }
            return dynamicExportDirectory;
        }

        /// <summary>
        /// has scene export directory. returns true even if there are no files in directory
        /// </summary>
        public static bool HasSceneExportFolder(Cognitive3D_Preferences.SceneSettings currentSceneSettings)
        {
            if (currentSceneSettings == null) { return false; }
            string sceneExportDirectory = Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar + "Cognitive3D_SceneExplorerExport" + Path.DirectorySeparatorChar + currentSceneSettings.SceneName + Path.DirectorySeparatorChar;
            var SceneExportDirExists = Directory.Exists(sceneExportDirectory);

            return SceneExportDirExists;
        }

        /// <summary>
        /// returns size of scene export folder in MB
        /// </summary>
        public static float GetSceneFileSize(Cognitive3D_Preferences.SceneSettings scene)
        {
            if (string.IsNullOrEmpty(GetSceneExportDirectory(scene)))
            {
                return 0;
            }
            float size = GetDirectorySize(GetSceneExportDirectory(scene)) / 1048576f;
            return size;
        }

        /// <summary>
        /// returns directory size by full string path in bytes
        /// </summary>
        public static long GetDirectorySize(string fullPath)
        {
            string[] a = System.IO.Directory.GetFiles(fullPath, "*.*", System.IO.SearchOption.AllDirectories);
            long b = 0;
            foreach (string name in a)
            {
                System.IO.FileInfo info = new System.IO.FileInfo(name);
                b += info.Length;
            }
            return b;
        }

        public static List<string> ExportedDynamicObjects;
        /// <summary>
        /// returns list of exported dynamic objects. refreshes on init window focused
        /// </summary>
        public static List<string> GetExportedDynamicObjectNames()
        {
            //cached value
            if (ExportedDynamicObjects != null)
                return ExportedDynamicObjects;

            //read dynamic object mesh names from directory
            string path = Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar + "Cognitive3D_SceneExplorerExport" + Path.DirectorySeparatorChar + "Dynamic";
            Directory.CreateDirectory(path);
            var subdirectories = Directory.GetDirectories(path);
            List<string> ObjectNames = new List<string>();
            foreach (var subdir in subdirectories)
            {
                var dirname = new DirectoryInfo(subdir).Name;
                ObjectNames.Add(dirname);
            }
            ExportedDynamicObjects = ObjectNames;
            return ObjectNames;
        }

#endregion

#region Scene Screenshot

        static RenderTexture sceneRT = null;
        public static RenderTexture GetSceneRenderTexture()
        {
            if (sceneRT == null)
                sceneRT = new RenderTexture(256, 256, 24);

            var cameras = UnityEditor.SceneView.GetAllSceneCameras();
            if (cameras != null && cameras.Length > 0 && cameras[0] != null)
            {
                cameras[0].targetTexture = sceneRT;
                cameras[0].Render();
            }
            return sceneRT;
        }

        public static void UploadCustomScreenshot()
        {
            //check that scene has been uploaded
            var currentScene = Cognitive3D_Preferences.FindCurrentScene();
            if (currentScene == null)
            {
                Debug.LogError("Could not find current scene");
                return;
            }
            if (string.IsNullOrEmpty(currentScene.SceneId))
            {
                Debug.LogError(currentScene.SceneName + " scene has not been uploaded!");
                return;
            }
            if (!IsDeveloperKeyValid)
            {
                Debug.LogError("Developer Key is invalid!");
                return;
            }

            //file select popup
            string path = EditorUtility.OpenFilePanel("Select Screenshot", "", "png");
            if (path.Length == 0)
            {
                return;
            }
            string filename = Path.GetFileName(path);

            //confirm popup and upload
            if (EditorUtility.DisplayDialog("Upload Screenshot", "Upload " + filename + " to " + currentScene.SceneName + " version " + currentScene.VersionNumber + "?", "Upload", "Cancel"))
            {
                string url = CognitiveStatics.POSTSCREENSHOT(currentScene.SceneId, currentScene.VersionNumber);
                var bytes = File.ReadAllBytes(path);
                WWWForm form = new WWWForm();
                form.AddBinaryData("screenshot", bytes, "screenshot.png");
                var headers = new Dictionary<string, string>();
                foreach (var v in form.headers)
                {
                    headers[v.Key] = v.Value;
                }
                headers.Add("Authorization", "APIKEY:DEVELOPER " + EditorCore.DeveloperKey);
                EditorNetwork.Post(url, form, UploadCustomScreenshotResponse, headers, false);
            }
        }

        /// <summary>
        /// response from posting custom screenshot
        /// </summary>
        static void UploadCustomScreenshotResponse(int responsecode, string error, string text)
        {
            if (responsecode == 200)
            {
                EditorUtility.DisplayDialog("Upload Complete", "Screenshot uploaded successfully", "Ok");
            }
            else
            {
                EditorUtility.DisplayDialog("Upload Fail", "Screenshot was not uploaded successfully. See console for details", "Ok");
                Debug.LogError("Failed to upload screenshot. [CODE] "+responsecode+" [ERROR] " + error);
            }
        }

        static Texture2D cachedScreenshot;
        /// <summary>
        /// get a screenshot from directory
        /// returns true if screenshot exists
        /// </summary>
        public static bool LoadScreenshot(string sceneName, out Texture2D returnTexture)
        {
            if (cachedScreenshot)
            {
                returnTexture = cachedScreenshot;
                return true;
            }
            //if file exists
            if (Directory.Exists("Cognitive3D_SceneExplorerExport" + Path.DirectorySeparatorChar + sceneName + Path.DirectorySeparatorChar + "screenshot"))
            {
                if (File.Exists("Cognitive3D_SceneExplorerExport" + Path.DirectorySeparatorChar + sceneName + Path.DirectorySeparatorChar + "screenshot" + Path.DirectorySeparatorChar + "screenshot.png"))
                {
                    //load texture from file
                    Texture2D tex = new Texture2D(1, 1);
                    tex.LoadImage(File.ReadAllBytes("Cognitive3D_SceneExplorerExport" + Path.DirectorySeparatorChar + sceneName + Path.DirectorySeparatorChar + "screenshot" + Path.DirectorySeparatorChar + "screenshot.png"));
                    returnTexture = tex;
                    cachedScreenshot = returnTexture;
                    return true;
                }
            }
            returnTexture = null;
            return false;
        }

        public static void SaveScreenshot(RenderTexture renderTexture, string sceneName, Action completeScreenshot)
        {
            Texture2D tex = new Texture2D(renderTexture.width, renderTexture.height);
            RenderTexture.active = renderTexture;
            tex.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            Directory.CreateDirectory("Cognitive3D_SceneExplorerExport");
            Directory.CreateDirectory("Cognitive3D_SceneExplorerExport" + Path.DirectorySeparatorChar + sceneName);
            Directory.CreateDirectory("Cognitive3D_SceneExplorerExport" + Path.DirectorySeparatorChar + sceneName + Path.DirectorySeparatorChar + "screenshot");

            //save file
            File.WriteAllBytes("Cognitive3D_SceneExplorerExport" + Path.DirectorySeparatorChar + sceneName + Path.DirectorySeparatorChar + "screenshot" + Path.DirectorySeparatorChar + "screenshot.png", tex.EncodeToPNG());
            //use editor update to delay teh screenshot 1 frame?

            if (completeScreenshot != null)
                completeScreenshot.Invoke();
            completeScreenshot = null;
        }

        public static void SceneViewCameraScreenshot(Camera cam, string sceneName, System.Action saveCameraScreenshot)
        {
            //create render texture
            RenderTexture rt = new RenderTexture(512, 512, 0);
            cam.targetTexture = rt;
            cam.Render();

            Texture2D tex = new Texture2D(512, 512);
            RenderTexture.active = cam.targetTexture;
            tex.ReadPixels(new Rect(0, 0, cam.targetTexture.width, cam.targetTexture.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            cam.targetTexture.Release();

            Directory.CreateDirectory("Cognitive3D_SceneExplorerExport");
            Directory.CreateDirectory("Cognitive3D_SceneExplorerExport" + Path.DirectorySeparatorChar + sceneName);
            Directory.CreateDirectory("Cognitive3D_SceneExplorerExport" + Path.DirectorySeparatorChar + sceneName + Path.DirectorySeparatorChar + "screenshot");

            //save file
            File.WriteAllBytes("Cognitive3D_SceneExplorerExport" + Path.DirectorySeparatorChar + sceneName + Path.DirectorySeparatorChar + "screenshot" + Path.DirectorySeparatorChar + "screenshot.png", tex.EncodeToPNG());
            //use editor update to delay teh screenshot 1 frame?

            if (saveCameraScreenshot != null)
                saveCameraScreenshot.Invoke();
            saveCameraScreenshot = null;
        }

#endregion

#region Dynamic Object Thumbnails
        //returns unused layer int. returns -1 if no unused layer is found
        public static int FindUnusedLayer()
        {
            for (int i = 31; i > 0; i--)
            {
                if (string.IsNullOrEmpty(LayerMask.LayerToName(i)))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// save a screenshot to a dynamic object's export folder from the current sceneview
        /// </summary>
        public static void SaveDynamicThumbnailSceneView(GameObject target)
        {
            SaveDynamicThumbnail(target, SceneView.lastActiveSceneView.camera.transform.position, SceneView.lastActiveSceneView.camera.transform.rotation);
        }

        /// <summary>
        /// save a screenshot to a dynamic object's export folder from a generated position
        /// </summary>
        public static void SaveDynamicThumbnailAutomatic(GameObject target)
        {
            Vector3 pos;
            Quaternion rot;
            CalcCameraTransform(target, out pos, out rot);
            SaveDynamicThumbnail(target, pos, rot);
        }

        /// <summary>
        /// recursively get child transforms, skipping nested dynamic objects
        /// </summary>
        public static void RecursivelyGetChildren(List<Transform> transforms, Transform current)
        {
            transforms.Add(current);
            for (int i = 0; i < current.childCount; i++)
            {
                var dyn = current.GetChild(i).GetComponent<DynamicObject>();
                if (!dyn)
                {
                    RecursivelyGetChildren(transforms, current.GetChild(i));
                }
            }
        }

        /// <summary>
        /// set layer mask on dynamic object, create temporary camera
        /// render to texture and save to file
        /// </summary>
        static void SaveDynamicThumbnail(GameObject target, Vector3 position, Quaternion rotation)
        {
            Dictionary<GameObject, int> originallayers = new Dictionary<GameObject, int>();
            var dynamic = target.GetComponent<Cognitive3D.DynamicObject>();

            //choose layer
            int layer = FindUnusedLayer();
            if (layer == -1) { Debug.LogWarning("couldn't find layer, don't set layers"); }

            //create camera stuff
            GameObject go = new GameObject("temp dynamic camera");
            var renderCam = go.AddComponent<Camera>();
            renderCam.clearFlags = CameraClearFlags.Color;
            renderCam.backgroundColor = Color.clear;
            renderCam.nearClipPlane = 0.01f;
            if (layer != -1)
            {
                renderCam.cullingMask = 1 << layer;
            }
            var rt = new RenderTexture(512, 512, 16);
            renderCam.targetTexture = rt;
            Texture2D tex = new Texture2D(rt.width, rt.height);

            //position camera
            go.transform.position = position;
            go.transform.rotation = rotation;

            //do this recursively, skipping nested dynamic objects
            List<Transform> relayeredTransforms = new List<Transform>();
            RecursivelyGetChildren(relayeredTransforms, target.transform);
            //set dynamic gameobject layers
            try
            {
                if (layer != -1)
                {
                    foreach (var v in relayeredTransforms)
                    {
                        originallayers.Add(v.gameObject, v.gameObject.layer);
                        v.gameObject.layer = layer;
                    }
                }
                //render to texture
                renderCam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            if (layer != -1)
            {
                //reset dynamic object layers
                foreach (var v in originallayers)
                {
                    v.Key.layer = v.Value;
                }
            }

            //remove camera
            GameObject.DestroyImmediate(renderCam.gameObject);

            //save
            File.WriteAllBytes("Cognitive3D_SceneExplorerExport" + Path.DirectorySeparatorChar + "Dynamic" + Path.DirectorySeparatorChar + dynamic.MeshName + Path.DirectorySeparatorChar + "cvr_object_thumbnail.png", tex.EncodeToPNG());
        }

        /// <summary>
        /// generate position and rotation for a dynamic object, based on its bounding box
        /// </summary>
        static void CalcCameraTransform(GameObject target, out Vector3 position, out Quaternion rotation)
        {
            var renderers = target.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds largestBounds = renderers[0].bounds;
                foreach (var renderer in renderers)
                {
                    largestBounds.Encapsulate(renderer.bounds);
                }

                if (largestBounds.size.magnitude <= 0)
                {
                    largestBounds = new Bounds(target.transform.position, Vector3.one * 5);
                }

                //IMPROVEMENT include target's rotation
                position = TransformPointUnscaled(target.transform, new Vector3(-1, 1, -1) * largestBounds.size.magnitude * 3 / 4);
                rotation = Quaternion.LookRotation(largestBounds.center - position, Vector3.up);
            }
            else //canvas dynamic objects
            {
                position = TransformPointUnscaled(target.transform, new Vector3(-1, 1, -1) * 3 / 4);
                rotation = Quaternion.LookRotation(target.transform.position - position, Vector3.up);
            }
        }
#endregion

#region Axis Input

        public class Axis
        {
            public string Name = String.Empty;
            public float Gravity = 0.0f;
            public float Deadzone = 0.001f;
            public float Sensitivity = 1.0f;
            public bool Snap = false;
            public bool Invert = false;
            public int InputType = 2;
            public int AxisNum = 0;
            public int JoystickNum = 0;
            public Axis(string name, int axis, bool inverted = false)
            {
                Name = name;
                AxisNum = axis;
                Invert = inverted;
            }
        }

        /// <summary>
        /// appends axis input data into input manager. used for steamvr2 inputs
        /// </summary>
        public static void BindAxis(Axis axis)
        {
            SerializedObject serializedObject = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/InputManager.asset")[0]);
            SerializedProperty axesProperty = serializedObject.FindProperty("m_Axes");

            SerializedProperty axisIter = axesProperty.Copy(); //m_axes
            axisIter.Next(true); //m_axes.array
            axisIter.Next(true); //m_axes.array.size
            while (axisIter.Next(false))
            {
                if (axisIter.FindPropertyRelative("m_Name").stringValue == axis.Name)
                {
                    return;
                }
            }

            axesProperty.arraySize++;
            serializedObject.ApplyModifiedProperties();

            SerializedProperty axisProperty = axesProperty.GetArrayElementAtIndex(axesProperty.arraySize - 1);
            axisProperty.FindPropertyRelative("m_Name").stringValue = axis.Name;
            axisProperty.FindPropertyRelative("gravity").floatValue = axis.Gravity;
            axisProperty.FindPropertyRelative("dead").floatValue = axis.Deadzone;
            axisProperty.FindPropertyRelative("sensitivity").floatValue = axis.Sensitivity;
            axisProperty.FindPropertyRelative("snap").boolValue = axis.Snap;
            axisProperty.FindPropertyRelative("invert").boolValue = axis.Invert;
            axisProperty.FindPropertyRelative("type").intValue = axis.InputType;
            axisProperty.FindPropertyRelative("axis").intValue = axis.AxisNum;
            axisProperty.FindPropertyRelative("joyNum").intValue = axis.JoystickNum;
            serializedObject.ApplyModifiedProperties();
        }

        public static void CheckForExpiredDeveloperKey(EditorNetwork.Response callback)
        {
            if (EditorPrefs.HasKey("developerkey"))
            {
                Dictionary<string, string> headers = new Dictionary<string, string>();
                headers.Add("Authorization", "APIKEY:DEVELOPER " + EditorPrefs.GetString("developerkey"));
                EditorNetwork.Get("https://" + EditorCore.DisplayValue(DisplayKey.GatewayURL) + "/v0/apiKeys/verify", callback, headers, true);


                /*//check if dev key is expired
                var devkeyrequest = new UnityEngine.Networking.UnityWebRequest("https://" + EditorCore.DisplayValue(DisplayKey.GatewayURL) + "/v0/apiKeys/verify");
                devkeyrequest.SetRequestHeader("Authorization", "APIKEY:DEVELOPER " + EditorPrefs.GetString("developerkey"));
                devkeyrequest.Send();
                EditorApplication.update += DevKeyCheckUpdate;*/
            }
            else
            {
                callback.Invoke(0, "invalid url", "");
            }
            //invoke the callback with the response code
        }

#endregion

        public static Vector3 TransformPointUnscaled(Transform transform, Vector3 position)
        {
            var localToWorldMatrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            return localToWorldMatrix.MultiplyPoint3x4(position);
        }

        public static Vector3 InverseTransformPointUnscaled(Transform transform, Vector3 position)
        {
            var worldToLocalMatrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one).inverse;
            return worldToLocalMatrix.MultiplyPoint3x4(position);
        }
    }
}