﻿using UnityEngine;
using Cognitive3D;
using System.Collections;
using System.Collections.Generic;
using System;
#if  C3D_STEAMVR2
using Valve.VR;
#endif

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Cognitive3DEditor")]

/// <summary>
/// initializes Cognitive3D analytics. Add components to track additional events
/// </summary>

//init components
//update ticks + events
//level change events
//quit and destroy events

//TODO move Omnicept, Steam and Oculus specific features into components
//TODO CONSIDER static framecount variable to avoid Time.frameCount access

namespace Cognitive3D
{
    [HelpURL("https://docs.cognitive3d.com/unity/get-started/")]
    [AddComponentMenu("Cognitive3D/Common/Cognitive VR Manager",1)]
    [DefaultExecutionOrder(-1)]
    public class Cognitive3D_Manager : MonoBehaviour
    {
        private static Cognitive3D_Manager instance;
        public static Cognitive3D_Manager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindObjectOfType<Cognitive3D_Manager>();
                    if (instance == null)
                    {
                        Util.logWarning("Cognitive Manager Instance not present in scene. Creating new gameobject");
                        instance = new GameObject("Cognitive3D_Manager").AddComponent<Cognitive3D_Manager>();
                    }
                }
                return instance;
            }
        }
        YieldInstruction playerSnapshotInverval;
        YieldInstruction automaticSendInterval;
        YieldInstruction GPSUpdateInverval;

        public static bool IsQuitting = false;

        [Tooltip("Start recording analytics when this gameobject becomes active (and after the StartupDelayTime has elapsed)")]
        public bool BeginSessionAutomatically = true;

        [Tooltip("Delay before starting a session. This delay can ensure other SDKs have properly initialized")]
        public float StartupDelayTime = 2;

#if C3D_OCULUS
        [Tooltip("Used to automatically associate a profile to a participant. Allows tracking between different sessions")]
        public bool AssignOculusProfileToParticipant = true;
#endif

        /// <summary>
        /// sets instance of Cognitive3D_Manager
        /// </summary>
        private void OnEnable()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            if (instance == this) { return; }
            instance = this;
        }

        IEnumerator Start()
        {
            GameObject.DontDestroyOnLoad(gameObject);
            if (StartupDelayTime > 0)
            {
                yield return new WaitForSeconds(StartupDelayTime);
            }
            if (BeginSessionAutomatically)
                BeginSession();

#if C3D_OCULUS
            if (AssignOculusProfileToParticipant && (ParticipantName == string.Empty && ParticipantId == string.Empty))
            {
                if (!Oculus.Platform.Core.IsInitialized())
                    Oculus.Platform.Core.Initialize();

                Oculus.Platform.Users.GetLoggedInUser().OnComplete(delegate (Oculus.Platform.Message<Oculus.Platform.Models.User> message)
                {
                    if (message.IsError)
                    {
                        Debug.LogError(message.GetError().Message);
                    }
                    else
                    {
                        SetParticipantId(message.Data.OculusID.ToString());
                    }
                });
            }
#endif
        }

        [System.NonSerialized]
        public GazeBase gazeBase;
        [System.NonSerialized]
        public FixationRecorder fixationRecorder;

        [Obsolete("use Cognitive3D_Manager.BeginSession instead")]
        public void Initialize(string participantName="", string participantId = "", List<KeyValuePair<string,object>> participantProperties = null)
        {
            BeginSession();

            if (!string.IsNullOrEmpty(participantName))
                SetParticipantFullName(participantName);
            if (!string.IsNullOrEmpty(participantId))
                SetParticipantId(participantId);
            if (participantProperties != null)
                SetSessionProperties(participantProperties);
        }
        //TODO comment the different parts of this startup
        /// <summary>
        /// Start recording a session. Sets SceneId, records basic hardware information, starts coroutines to record other data points on intervals
        /// </summary>
        /// <param name="participantName">friendly name for identifying participant</param>
        /// <param name="participantId">unique id for identifying participant</param>
        public void BeginSession()
        {
            if (instance != null && instance != this)
            {
                Util.logDebug("Cognitive3D_Manager Initialize instance is not null and not this! Destroy");
                Destroy(gameObject);
                return;
            } //destroy if there's already another manager
            if (IsInitialized)
            {
                Util.logWarning("Cognitive3D_Manager Initialize - Already Initialized!");
                return;
            } //skip if a session has already been initialized

            if (!Cognitive3D_Preferences.Instance.IsApplicationKeyValid)
            {
                Util.logDebug("Cognitive3D_Manager Initialize does not have valid apikey");
                return;
            }

            UnityEngine.SceneManagement.SceneManager.sceneLoaded += SceneManager_SceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded += SceneManager_SceneUnloaded;

            //sets session properties for system hardware
            //also constructs network and local cache files/readers

            CognitiveStatics.Initialize();

            DeviceId = UnityEngine.SystemInfo.deviceUniqueIdentifier;

            ExitpollHandler = new ExitPollLocalDataHandler(Application.persistentDataPath + "/c3dlocal/exitpoll/");

            if (Cognitive3D_Preferences.Instance.LocalStorage)
                DataCache = new DualFileCache(Application.persistentDataPath + "/c3dlocal/");
            GameObject networkGo = new GameObject("Cognitive Network");
            networkGo.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            NetworkManager = networkGo.AddComponent<NetworkManager>();
            NetworkManager.Initialize(DataCache, ExitpollHandler);

            GameplayReferences.Initialize();
            DynamicManager.Initialize();
            CustomEvent.Initialize();
            SensorRecorder.Initialize();

            _timestamp = Util.Timestamp();
            //set session timestamp
            if (string.IsNullOrEmpty(_sessionId))
            {
                _sessionId = (int)SessionTimeStamp + "_" + DeviceId;
            }

            string hmdName = "unknown";
            var hmdDevice = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.Head);
            if (hmdDevice.isValid)
                hmdName = hmdDevice.name;

            CoreInterface.Initialize(SessionID, SessionTimeStamp, DeviceId, hmdName);
            IsInitialized = true;
            //TODO support skipping spatial gaze data but still recording session properties for XRPF

            //get all loaded scenes. if one has a sceneid, use that
            var count = UnityEngine.SceneManagement.SceneManager.sceneCount;
            UnityEngine.SceneManagement.Scene scene = new UnityEngine.SceneManagement.Scene();
            for(int i = 0; i<count;i++)
            {
                scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                var cogscene = Cognitive3D_Preferences.FindSceneByPath(scene.path);
                if (cogscene != null && !string.IsNullOrEmpty(cogscene.SceneId))
                {
                    SetTrackingScene(cogscene, false);
                    break;
                }
            }
            if (TrackingScene == null)
            {
                Util.logWarning("CogntitiveVRManager No Tracking Scene Set!");
            }

            InvokeLevelLoadedEvent(scene, UnityEngine.SceneManagement.LoadSceneMode.Single, true);

            new CustomEvent("c3d.sessionStart").Send();
            if (Cognitive3D_Preferences.Instance.TrackGPSLocation)
            {
                Input.location.Start(Cognitive3D_Preferences.Instance.GPSAccuracy, Cognitive3D_Preferences.Instance.GPSAccuracy);
                Input.compass.enabled = true;
                if (Cognitive3D_Preferences.Instance.SyncGPSWithGaze)
                {
                    //just get gaze snapshot to grab this
                }
                else
                {
                    StartCoroutine(GPSTick());
                }
            }
            playerSnapshotInverval = new WaitForSeconds(Cognitive3D_Preferences.SnapshotInterval);
            GPSUpdateInverval = new WaitForSeconds(Cognitive3D_Preferences.Instance.GPSInterval);
            automaticSendInterval = new WaitForSeconds(Cognitive3D_Preferences.Instance.AutomaticSendTimer);
            StartCoroutine(Tick());
            Util.logDebug("Cognitive3D Initialized");

            var components = GetComponentsInChildren<Cognitive3D.Components.AnalyticsComponentBase>();
            for (int i = 0; i < components.Length; i++)
            {
                components[i].Cognitive3D_Init();
            }

            //TODO support for 360 skysphere media recording
            gazeBase = gameObject.GetComponent<PhysicsGaze>();
            if (gazeBase == null)
            {
                gazeBase = gameObject.AddComponent<PhysicsGaze>();
            }
            gazeBase.Initialize();

            if (GameplayReferences.SDKSupportsEyeTracking)
            {
                fixationRecorder = gameObject.GetComponent<FixationRecorder>();
                if (fixationRecorder == null)
                {
                    fixationRecorder = gameObject.AddComponent<FixationRecorder>();
                }
                fixationRecorder.Initialize();
            }

            InvokeSessionBeginEvent();

            SetSessionProperties();

            OnPreSessionEnd += Core_EndSessionEvent;
            FlushData();
            StartCoroutine(AutomaticSendData());
        }

        /// <summary>
        /// sets automatic session properties from scripting define symbols, device ids, etc
        /// </summary>
        private void SetSessionProperties()
        {
            SetSessionProperty("c3d.app.name", Application.productName);
            SetSessionProperty("c3d.app.version", Application.version);
            SetSessionProperty("c3d.app.engine.version", Application.unityVersion);
            SetSessionProperty("c3d.device.type", SystemInfo.deviceType.ToString());
            SetSessionProperty("c3d.device.cpu", SystemInfo.processorType);
            SetSessionProperty("c3d.device.model", SystemInfo.deviceModel);
            SetSessionProperty("c3d.device.gpu", SystemInfo.graphicsDeviceName);
            SetSessionProperty("c3d.device.os", SystemInfo.operatingSystem);
            SetSessionProperty("c3d.device.memory", Mathf.RoundToInt((float)SystemInfo.systemMemorySize / 1024));
            SetSessionProperty("c3d.deviceid", DeviceId);
            SetSessionProperty("c3d.app.inEditor", Application.isEditor);
            SetSessionProperty("c3d.version", SDK_VERSION);
            SetSessionProperty("c3d.device.hmd.type", UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.Head).name);

#if C3D_STEAMVR2
            //other SDKs may use steamvr as a base or for controllers (ex, hp omnicept). this may be replaced below
            SetSessionProperty("c3d.device.eyetracking.enabled", false);
            SetSessionProperty("c3d.device.eyetracking.type","None");
            SetSessionProperty("c3d.app.sdktype", "Vive");
#endif

#if C3D_OCULUS
            SetSessionProperty("c3d.device.hmd.type", OVRPlugin.GetSystemHeadsetType().ToString().Replace('_', ' '));
            SetSessionProperty("c3d.device.eyetracking.enabled", false);
            SetSessionProperty("c3d.device.eyetracking.type", "None");
            SetSessionProperty("c3d.app.sdktype", "Oculus");
#elif C3D_HOLOLENS
            SetSessionProperty("c3d.device.eyetracking.enabled", false);
            SetSessionProperty("c3d.device.eyetracking.type","None");
            SetSessionProperty("c3d.app.sdktype", "Hololens");
#elif C3D_PICOVR
            SetSessionProperty("c3d.device.eyetracking.enabled", true);
            SetSessionProperty("c3d.device.eyetracking.type","Tobii");
            SetSessionProperty("c3d.app.sdktype", "PicoVR");
            SetSessionProperty("c3d.device.model", UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.Head).name);
#elif C3D_PICOXR
            SetSessionProperty("c3d.device.eyetracking.enabled", true);
            SetSessionProperty("c3d.device.eyetracking.type","Tobii");
            SetSessionProperty("c3d.app.sdktype", "PicoXR");
            SetSessionProperty("c3d.device.model", UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.Head).name);
#elif CVR_MRTK
            SetSessionProperty("c3d.device.eyetracking.enabled", Microsoft.MixedReality.Toolkit.CoreServices.InputSystem.EyeGazeProvider.IsEyeTrackingEnabled);
            SetSessionProperty("c3d.app.sdktype", "MRTK");
#endif

            //eye tracker addons
#if C3D_SRANIPAL
            SetSessionProperty("c3d.device.eyetracking.enabled", true);
            SetSessionProperty("c3d.device.eyetracking.type","Tobii");
            SetSessionProperty("c3d.app.sdktype", "Vive Pro Eye");
#elif C3D_WINDOWSMR
            SetSessionProperty("c3d.app.sdktype", "Windows Mixed Reality");
#endif
            SetSessionPropertyIfEmpty("c3d.device.eyetracking.enabled", false);
            SetSessionPropertyIfEmpty("c3d.device.eyetracking.type", "None");
            SetSessionPropertyIfEmpty("c3d.app.sdktype", "Default");

            SetSessionProperty("c3d.app.engine", "Unity");
        }

        /// <summary>
        /// registered to unity's OnSceneLoaded callback. sends outstanding data, then sets correct tracking scene id and refreshes dynamic object session manifest
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="mode"></param>
        private void SceneManager_SceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            var loadingScene = Cognitive3D_Preferences.FindScene(scene.name);
            bool replacingSceneId = false;

            if (mode == UnityEngine.SceneManagement.LoadSceneMode.Additive)
            {
                //if scene loaded has new scene id
                if (loadingScene != null && !string.IsNullOrEmpty(loadingScene.SceneId))
                {
                    replacingSceneId = true;
                }
            }
            if (mode == UnityEngine.SceneManagement.LoadSceneMode.Single || replacingSceneId)
            {
                //DynamicObject.ClearObjectIds();
                replacingSceneId = true;
            }
            
            if (replacingSceneId)
            {
                //send all immediately. anything on threads will be out of date when looking for what the current tracking scene is
                FlushData();
            }

            if (replacingSceneId)
            {
                if (loadingScene != null)
                {
                    if (!string.IsNullOrEmpty(loadingScene.SceneId))
                    {
                        SetTrackingScene(scene.name,true);
                    }
                    else
                    {
                        SetTrackingScene("", true);
                    }
                }
                else
                {
                    SetTrackingScene("", true);
                }
            }

            InvokeLevelLoadedEvent(scene, mode, replacingSceneId);
        }

        private void SceneManager_SceneUnloaded(UnityEngine.SceneManagement.Scene scene)
        {
            //TODO for unload scene async, may need to change tracking scene id
            //a situation where a scene without an ID is loaded additively, then a scene with an id is unloaded, the sceneid will persist
        }

        #region Updates and Loops

        /// <summary>
        /// start after successful session initialization
        /// </summary>
        IEnumerator Tick()
        {
            while (IsInitialized)
            {
                yield return playerSnapshotInverval;
                InvokeTickEvent();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        IEnumerator AutomaticSendData()
        {
            while (IsInitialized)
            {
                yield return automaticSendInterval;
                CoreInterface.Flush(false);
            }
        }

        void Update()
        {
            if (!IsInitialized)
            {
                return;
            }

            InvokeUpdateEvent(Time.deltaTime);
        }

#endregion

#region GPS
        public void GetGPSLocation(ref Vector4 geo)
        {
            if (Cognitive3D_Preferences.Instance.SyncGPSWithGaze)
            {
                geo.x = Input.location.lastData.latitude;
                geo.y = Input.location.lastData.longitude;
                geo.z = Input.location.lastData.altitude;
                geo.w = 360 - Input.compass.magneticHeading;
            }
            else
            {
                geo = GPSLocation;
                geo.w = CompassOrientation;
            }
        }

        Vector3 GPSLocation;
        float CompassOrientation;
        IEnumerator GPSTick()
        {
            while (IsInitialized)
            {
                yield return GPSUpdateInverval;
                GPSLocation.x = Input.location.lastData.latitude;
                GPSLocation.y = Input.location.lastData.longitude;
                GPSLocation.z = Input.location.lastData.altitude;
                CompassOrientation = 360 - Input.compass.magneticHeading;
            }
        }
#endregion

#region Application Quit, Session End and OnDestroy
        /// <summary>
        /// End the Cognitive3D session. sends any outstanding data to dashboard and sceneexplorer
        /// requires calling Initialize to create a new session id and begin recording analytics again
        /// </summary>
        public void EndSession()
        {
            if (IsInitialized)
            {
                double playtime = Util.Timestamp(Time.frameCount) - SessionTimeStamp;
                new CustomEvent("c3d.sessionEnd").SetProperty("sessionlength", playtime).Send();
                Cognitive3D.Util.logDebug("Session End. Duration: " + string.Format("{0:0.00}", playtime));

                FlushData();
                UnityEngine.SceneManagement.SceneManager.sceneLoaded -= SceneManager_SceneLoaded;
                ResetSessionData();
            }
        }

        private void Core_EndSessionEvent()
        {
            OnPreSessionEnd -= Core_EndSessionEvent;
        }

        void OnDestroy()
        {
            if (instance != this) { return; }
            if (!Application.isPlaying) { return; }

            InvokeQuitEvent();

            if (IsInitialized)
            {
                ResetSessionData();
            }

            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= SceneManager_SceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded -= SceneManager_SceneUnloaded;
        }

        void OnApplicationPause(bool paused)
        {
            if (!IsInitialized) { return; }
            new CustomEvent("c3d.pause").SetProperty("is paused", paused).Send();
            FlushData();
        }

        bool hasCanceled = false;
        void OnApplicationQuit()
        {
            if (!IsInitialized) { return; }

            IsQuitting = true;
            if (hasCanceled) { return; }

            double playtime = Util.Timestamp(Time.frameCount) - SessionTimeStamp;
            Cognitive3D.Util.logDebug("Session End. Duration: " + string.Format("{0:0.00}", playtime));            

            if (IsQuitEventBound())
            {
                new CustomEvent("Session End").SetProperty("sessionlength",playtime).Send();
                return;
            }
            new CustomEvent("Session End").SetProperty("sessionlength", playtime).Send();
            Application.CancelQuit();
            //TODO update and test with Application.wantsToQuit and Application.qutting

            InvokeQuitEvent();
            QuitEventClear();
            

            FlushData();
            ResetSessionData();
            StartCoroutine(SlowQuit());
        }

        IEnumerator SlowQuit()
        {
            yield return new WaitForSeconds(0.5f);
            hasCanceled = true;            
            Application.Quit();
        }

        #endregion


        public const string SDK_VERSION = "1.0.0";

        //data has been sent. this is used to visualize on active session view
        public delegate void onSendData(bool copyDataToCache);

        /// <summary>
        /// call this to immediately send all outstanding data to the dashboard
        /// </summary>
        public static void FlushData()
        {
            DynamicManager.SendData(true);
            CoreInterface.Flush(true);
        }

        public delegate void onSessionBegin();
        /// <summary>
        /// Cognitive3D Core.Init callback
        /// </summary>
        public static event onSessionBegin OnSessionBegin;
        public static void InvokeSessionBeginEvent() { if (OnSessionBegin != null) { OnSessionBegin.Invoke(); } }

        public delegate void onSessionEnd();
        /// <summary>
        /// Cognitive3D Core.Init callback
        /// </summary>
        public static event onSessionEnd OnPreSessionEnd;
        public static void InvokeEndSessionEvent() { if (OnPreSessionEnd != null) { OnPreSessionEnd.Invoke(); } }

        public static event onSessionEnd OnPostSessionEnd;
        public static void InvokePostEndSessionEvent() { if (OnPostSessionEnd != null) { OnPostSessionEnd.Invoke(); } }

        public delegate void onUpdate(float deltaTime);
        /// <summary>
        /// Update. Called through Manager's update function
        /// </summary>
        public static event onUpdate OnUpdate;
        public static void InvokeUpdateEvent(float deltaTime) { if (OnUpdate != null) { OnUpdate(deltaTime); } }

        public delegate void onTick();
        /// <summary>
        /// repeatedly called if the sceneid is valid. interval is Cognitive3D_Preferences.Instance.PlayerSnapshotInterval
        /// </summary>
        public static event onTick OnTick;
        public static void InvokeTickEvent() { if (OnTick != null) { OnTick(); } }

        public delegate void onQuit();
        /// <summary>
        /// called from Unity's built in OnApplicationQuit. Cancelling quit gets weird - do all application quit stuff in Manager
        /// </summary>
        public static event onQuit OnQuit;
        public static void InvokeQuitEvent() { if (OnQuit != null) { OnQuit(); } }
        public static bool IsQuitEventBound() { return OnQuit != null; }
        public static void QuitEventClear() { OnQuit = null; }

        public delegate void onLevelLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode, bool newSceneId);
        /// <summary>
        /// from Unity's SceneManager.SceneLoaded event. happens after manager sends outstanding data and updates new SceneId
        /// </summary>
        public static event onLevelLoaded OnLevelLoaded;
        public static void InvokeLevelLoadedEvent(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode, bool newSceneId) { if (OnLevelLoaded != null) { OnLevelLoaded(scene, mode, newSceneId); } }

        //public delegate void onDataSend();

        internal static ILocalExitpoll ExitpollHandler;
        internal static ICache DataCache;
        internal static NetworkManager NetworkManager;

        private static bool HasCustomSessionName;
        public static string ParticipantId { get; private set; }
        public static string ParticipantName { get; private set; }
        private static string _deviceId;
        public static string DeviceId
        {
            get
            {
                if (string.IsNullOrEmpty(_deviceId))
                {
                    _deviceId = UnityEngine.SystemInfo.deviceUniqueIdentifier;
                }
                return _deviceId;
            }
            private set { _deviceId = value; }
        }

        private static double _timestamp;
        public static double SessionTimeStamp
        {
            get
            {
                return _timestamp;
            }
        }

        private static string _sessionId;
        public static string SessionID
        {
            get
            {
                return _sessionId;
            }
        }

        public static void SetSessionId(string sessionId)
        {
            if (!IsInitialized)
                _sessionId = sessionId;
            else
                Util.logWarning("Core::SetSessionId cannot be called during a session!");
        }

        public static string TrackingSceneId
        {
            get
            {
                if (TrackingScene == null) { return ""; }
                return TrackingScene.SceneId;
            }
        }
        public static int TrackingSceneVersionNumber
        {
            get
            {
                if (TrackingScene == null) { return 0; }
                return TrackingScene.VersionNumber;
            }
        }
        public static int TrackingSceneVersionId
        {
            get
            {
                if (TrackingScene == null) { return 0; }
                return TrackingScene.VersionId;
            }
        }
        public static string TrackingSceneName
        {
            get
            {
                if (TrackingScene == null) { return ""; }
                return TrackingScene.SceneName;
            }
        }

        public static Cognitive3D_Preferences.SceneSettings TrackingScene { get; private set; }

        /// <summary>
        /// Set the SceneId for recorded data by string
        /// </summary>
        public static void SetTrackingScene(string sceneName, bool writeSceneChangeEvent)
        {
            var scene = Cognitive3D_Preferences.FindScene(sceneName);
            SetTrackingScene(scene, writeSceneChangeEvent);
        }

        private static float SceneStartTime;
        internal static bool ForceWriteSessionMetadata = false;

        /// <summary>
        /// Set the SceneId for recorded data by reference
        /// </summary>
        /// <param name="scene"></param>
        public static void SetTrackingScene(Cognitive3D_Preferences.SceneSettings scene, bool WriteSceneChangeEvent)
        {
            if (IsInitialized)
            {
                if (WriteSceneChangeEvent)
                {
                    if (scene == null || string.IsNullOrEmpty(scene.SceneId))
                    {
                        //what scene is being loaded
                        float duration = Time.time - SceneStartTime;
                        SceneStartTime = Time.time;
                        new CustomEvent("c3d.SceneChange").SetProperty("Duration", duration).Send();
                    }
                    else
                    {
                        //what scene is being loaded
                        float duration = Time.time - SceneStartTime;
                        SceneStartTime = Time.time;
                        new CustomEvent("c3d.SceneChange").SetProperty("Duration", duration).SetProperty("Scene Name", scene.SceneName).SetProperty("Scene Id", scene.SceneId).Send();
                    }
                }

                //just to send this scene change event
                if (WriteSceneChangeEvent && TrackingScene != null)
                {
                    FlushData();
                }
                ForceWriteSessionMetadata = true;
                TrackingScene = scene;
            }
            else
            {
                Util.logWarning("Trying to set scene without a session!");
            }
        }

        public static string LobbyId { get; private set; }
        public static void SetLobbyId(string lobbyId)
        {
            LobbyId = lobbyId;
        }

        public static void SetParticipantFullName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                Util.logWarning("SetParticipantFullName is empty!");
                return;
            }
            ParticipantName = name;
            SetParticipantProperty("name", name);
            if (!HasCustomSessionName)
                SetSessionProperty("c3d.sessionname", name);
        }

        public static void SetParticipantId(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                Util.logWarning("SetParticipantId is empty!");
                return;
            }
            if (id.Length > 64)
            {
                Debug.LogError("Cognitive3D SetParticipantId exceeds maximum character limit. Clipping to 64");
                id = id.Substring(0, 64);
            }
            ParticipantId = id;
            SetParticipantProperty("id", id);
        }

        /// <summary>
        /// has the Cognitive3D session started?
        /// </summary>
        public static bool IsInitialized { get; private set; }

        /// <summary>
        /// Reset all of the static vars to their default values. Used when a session ends
        /// </summary>
        internal static void ResetSessionData()
        {
            InvokeEndSessionEvent();
            CoreInterface.Reset();
            if (NetworkManager != null)
                NetworkManager.EndSession();
            ParticipantId = null;
            ParticipantName = null;
            _sessionId = null;
            _timestamp = 0;
            DeviceId = null;
            IsInitialized = false;
            TrackingScene = null;
            if (NetworkManager != null)
            {
                GameObject.Destroy(NetworkManager.gameObject);
                //NetworkManager.OnDestroy();
            }
            HasCustomSessionName = false;
            InvokePostEndSessionEvent();

            CognitiveStatics.Reset();
            DynamicManager.Reset();
        }

        public static void SetSessionProperties(List<KeyValuePair<string, object>> kvpList)
        {

            if (kvpList == null) { return; }

            for (int i = 0; i < kvpList.Count; i++)
            {
                SetSessionProperty(kvpList[i].Key, kvpList[i].Value);
            }
        }

        public static void SetSessionProperties(Dictionary<string, object> properties)
        {
            if (properties == null) { return; }

            foreach (var prop in properties)
            {
                SetSessionProperty(prop.Key, prop.Value);
            }
        }

        public static void SetSessionProperty(string key, object value)
        {
            if (value == null) { return; }
            CoreInterface.SetSessionProperty(key, value);
        }

        /// <summary>
        /// writes a value into the session properties if the key has not already been added
        /// for easy use of 'addon' sdks
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        public static void SetSessionPropertyIfEmpty(string key, object value)
        {
            if (value == null) { return; }

            CoreInterface.SetSessionPropertyIfEmpty(key, value);

        }

        /// <summary>
        /// sets a property about the participant in the current session
        /// should first call SetParticipantFullName() and SetParticipantId()
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        public static void SetParticipantProperty(string key, object value)
        {
            SetSessionProperty("c3d.participant." + key, value);
        }

        /// <summary>
        /// sets a tag to a session for filtering on the dashboard
        /// MUST contain 12 or fewer characters
        /// </summary>
        /// <param name="tag"></param>
        public static void SetSessionTag(string tag, bool setValue = true)
        {
            if (string.IsNullOrEmpty(tag))
            {
                Debug.LogWarning("Session Tag cannot be empty!");
                return;
            }
            if (tag.Length > 12)
            {
                Debug.LogWarning("Session Tag must be less that 12 characters!");
                return;
            }
            SetSessionProperty("c3d.session_tag." + tag, setValue);
        }

        class AttributeParameters
        {
            public string attributionKey;
            public string sessionId;
            public int sceneVersionId;
        }

        /// <summary>
        /// returns a formatted string to append to a web request
        /// this can be used to identify an event outside of unity
        /// requires javascript to parse this key. see the documentation for details
        /// </summary>
        public static string GetAttributionParameters()
        {
            var ap = new AttributeParameters();
            ap.attributionKey = Cognitive3D_Preferences.Instance.AttributionKey;
            ap.sessionId = SessionID;
            if (TrackingScene != null)
                ap.sceneVersionId = TrackingScene.VersionId;

            return "?c3dAtkd=AK-" + System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(ap)));
        }

        public static void SetSessionName(string sessionName)
        {
            HasCustomSessionName = true;
            SetSessionProperty("c3d.sessionname", sessionName);
        }

        public static int GetLocalStorageBatchCount()
        {
            if (DataCache == null)
                return 0;
            return DataCache.NumberOfBatches();
        }

        public static float GetLocalStorage()
        {
            return 0;
        }
    }
}