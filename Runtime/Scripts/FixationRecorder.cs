﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if C3D_AH
using AdhawkApi;
using AdhawkApi.Numerics.Filters;
#endif

namespace Cognitive3D
{
    public class ThreadGazePoint
    {
        public Vector3 WorldPoint;
        public bool IsLocal;
        public Vector3 LocalPoint;
        public Transform Transform; //ignored in thread
        public Matrix4x4 TransformMatrix; //set in update. used in thread
    }

    //IMPROVEMENT? try removing noisy outliers when creating new fixation points
    //https://stackoverflow.com/questions/3779763/fast-algorithm-for-computing-percentiles-to-remove-outliers
    //https://www.codeproject.com/Tips/602081/%2FTips%2F602081%2FStandard-Deviation-Extension-for-Enumerable

    [HelpURL("https://docs.cognitive3d.com/fixations/")]
    [AddComponentMenu("Cognitive3D/Common/Fixation Recorder")]
    public class FixationRecorder : MonoBehaviour
    {
        //public static FixationRecorder Instance;
        private enum GazeRaycastResult
        {
            Invalid,
            HitNothing,
            HitWorld
        }

        #region EyeTracker

#if C3D_FOVE
        const int CachedEyeCaptures = 70; //FOVE
        Fove.Unity.FoveInterface fovebase;
        public bool CombinedWorldGazeRay(out Ray ray)
        {
            var r = fovebase.GetGazeRays();
            ray = new Ray((r.left.origin + r.right.origin) / 2, (r.left.direction + r.right.direction) / 2);
            return true;
        }

        public bool LeftEyeOpen() { return Fove.Unity.FoveManager.CheckEyesClosed() != Fove.Eye.Both && Fove.Unity.FoveManager.CheckEyesClosed() != Fove.Eye.Left; }
        public bool RightEyeOpen() { return Fove.Unity.FoveManager.CheckEyesClosed() != Fove.Eye.Both && Fove.Unity.FoveManager.CheckEyesClosed() != Fove.Eye.Right; }

        public long EyeCaptureTimestamp()
        {
            return (long)(Util.Timestamp() * 1000);
        }

        int lastProcessedFrame;
        //returns true if there is another data point to work on
        public bool GetNextData()
        {
            if (lastProcessedFrame != Time.frameCount)
            {
                lastProcessedFrame = Time.frameCount;
                return true;
            }
            return false;
        }
#elif C3D_TOBIIVR
        const int CachedEyeCaptures = 120; //TOBII
        Tobii.XR.IEyeTrackingProvider EyeTracker;
        Tobii.XR.TobiiXR_EyeTrackingData currentData;
        public bool CombinedWorldGazeRay(out Ray ray)
        {
            ray = new Ray();
            ray.origin = GameplayReferences.HMD.TransformPoint(currentData.GazeRay.Origin);
            ray.direction = GameplayReferences.HMD.TransformDirection(currentData.GazeRay.Direction);

            //if (currentData.GazeRay.IsValid)
                //Debug.DrawRay(ray.origin, ray.direction * 1000, Color.magenta, 5);

            return currentData.GazeRay.IsValid;
        }

        public bool LeftEyeOpen() { return !currentData.IsLeftEyeBlinking; }
        public bool RightEyeOpen() { return !currentData.IsRightEyeBlinking; }

        //unix timestamp of application start in milliseconds
        long tobiiStartTimestamp;
        
        private IEnumerator Start()
        {            
            while (EyeTracker == null)
            {
                yield return null;
                EyeTracker = Tobii.XR.TobiiXR.Internal.Provider;
                //is this fixation recorder component added immediately? or after cognitive session start?
            }
            tobiiStartTimestamp = Cognitive3D_Manager.Instance.StartupTimestampMilliseconds;
        }

        public long EyeCaptureTimestamp()
        {
            return tobiiStartTimestamp + (long)(currentData.Timestamp * 1000);
        }

        int lastProcessedFrame;
        //returns true if there is another data point to work on
        public bool GetNextData()
        {
            if (lastProcessedFrame != Time.frameCount)
            {
                currentData = Tobii.XR.TobiiXR.Internal.Provider.EyeTrackingDataLocal;
                lastProcessedFrame = Time.frameCount;
                return true;
            }
            return false;
        }
#elif C3D_PICOVR
        const int CachedEyeCaptures = 120; //PICO
        Pvr_UnitySDKAPI.EyeTrackingData data = new Pvr_UnitySDKAPI.EyeTrackingData();
        public bool CombinedWorldGazeRay(out Ray ray)
        {
            ray = new Ray();
            Pvr_UnitySDKAPI.EyeTrackingGazeRay gazeRay = new Pvr_UnitySDKAPI.EyeTrackingGazeRay();
            if (Pvr_UnitySDKAPI.System.UPvr_getEyeTrackingGazeRayWorld(ref gazeRay))
            {
                if (gazeRay.IsValid && gazeRay.Direction.sqrMagnitude > 0.1f)
                {
                    ray.direction = gazeRay.Direction;
                    ray.origin = gazeRay.Origin;
                    return true;
                }
            }
            return false;
        }

        public bool LeftEyeOpen()
        {
            Pvr_UnitySDKAPI.System.UPvr_getEyeTrackingData(ref data);
            return data.leftEyeOpenness > 0.5f;
        }
        public bool RightEyeOpen()
        {
            Pvr_UnitySDKAPI.System.UPvr_getEyeTrackingData(ref data);
            return data.rightEyeOpenness > 0.5f;
        }

        public long EyeCaptureTimestamp()
        {
            return (long)(Cognitive3D.Util.Timestamp() * 1000);
        }

        int lastProcessedFrame;
        //returns true if there is another data point to work on
        public bool GetNextData()
        {
            if (lastProcessedFrame != Time.frameCount)
            {
                lastProcessedFrame = Time.frameCount;
                return true;
            }
            return false;
        }
#elif C3D_PICOXR
        const int CachedEyeCaptures = 120; //PICO

        public bool CombinedWorldGazeRay(out Ray ray)
        {
            ray = new Ray();

            if (!Unity.XR.PXR.PXR_Manager.Instance.eyeTracking)
            {
                Debug.LogError("Cognitive3D::FixationRecorder CombineWorldGazeRay FAILED MANAGER NO EYE TRACKING");
                return false;
            }

            UnityEngine.XR.InputDevice device;
            if (!GameplayReferences.GetEyeTrackingDevice(out device))
            {
                Debug.Log("Cognitive3D::FixationRecorder CombineWorldGazeRay FAILED TRACKING DEVICE");
                return false;
            }

            Vector3 headPos;
            if (!device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out headPos))
            {
                Debug.Log("Cognitive3D::FixationRecorder CombineWorldGazeRay FAILED HEAD POSITION");
                return false;
            }
            Quaternion headRot = Quaternion.identity;
            if (!device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out headRot))
            {
                Debug.Log("Cognitive3D::FixationRecorder CombineWorldGazeRay FAILED HEAD ROTATION");
                return false;
            }

            Vector3 direction;
            if (Unity.XR.PXR.PXR_EyeTracking.GetCombineEyeGazeVector(out direction) && direction.sqrMagnitude > 0.1f)
            {
                Matrix4x4 matrix = Matrix4x4.identity;
                matrix = Matrix4x4.TRS(Vector3.zero, headRot, Vector3.one);
                direction = matrix.MultiplyPoint3x4(direction);
                ray.origin = headPos;
                ray.direction = direction;
                return true;
            }
            return false;
        }

        public bool LeftEyeOpen()
        {
            float openness = 0;
            if (Unity.XR.PXR.PXR_EyeTracking.GetLeftEyeGazeOpenness(out openness))
            {
                return openness > 0.5f;
            }
            return false;
        }
        public bool RightEyeOpen()
        {
            float openness = 0;
            if (Unity.XR.PXR.PXR_EyeTracking.GetRightEyeGazeOpenness(out openness))
            {
                return openness > 0.5f;
            }
            return false;
        }

        public long EyeCaptureTimestamp()
        {
            return (long)(Cognitive3D.Util.Timestamp() * 1000);
        }

        int lastProcessedFrame;
        //returns true if there is another data point to work on
        public bool GetNextData()
        {
            if (lastProcessedFrame != Time.frameCount)
            {
                lastProcessedFrame = Time.frameCount;
                return true;
            }
            return false;
        }
#elif C3D_SRANIPAL
        ViveSR.anipal.Eye.SRanipal_Eye_Framework framework;        
        bool useDataQueue1;
        bool useDataQueue2;
        static System.Collections.Concurrent.ConcurrentQueue<ViveSR.anipal.Eye.EyeData> EyeDataQueue1;
        static System.Collections.Concurrent.ConcurrentQueue<ViveSR.anipal.Eye.EyeData_v2> EyeDataQueue2;
        const int CachedEyeCaptures = 120; //VIVEPROEYE
        ViveSR.anipal.Eye.EyeData currentData1;
        ViveSR.anipal.Eye.EyeData_v2 currentData2;

        static System.DateTime epoch = new System.DateTime(1970, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);
        double epochStart;
        long startTimestamp;

        private void SceneManager_sceneLoaded(UnityEngine.SceneManagement.Scene arg0, UnityEngine.SceneManagement.LoadSceneMode arg1)
        {
            //if scene changed, check that the camera is fine
            SetupCallbacks();

            //throw out current recorded eye data
            IsFixating = false;
            ActiveFixation = new Fixation();

            for (int i = 0; i < CachedEyeCaptures; i++)
            {
                EyeCaptures[i].Discard = true;
            }
        }

        private void OnDisable()
        {
            UnregisterEyeCallbacks();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= SceneManager_sceneLoaded;
        }

        void UnregisterEyeCallbacks()
        {
            if (framework != null && framework.EnableEyeDataCallback)
            {
                if (framework.EnableEyeVersion == ViveSR.anipal.Eye.SRanipal_Eye_Framework.SupportedEyeVersion.version1)
                {
                    var functionPointer = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate((ViveSR.anipal.Eye.SRanipal_Eye.CallbackBasic)EyeCallback);
                    ViveSR.anipal.Eye.SRanipal_Eye.WrapperUnRegisterEyeDataCallback(functionPointer);
                }
                else //v2
                {
                    var functionPointer = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate((ViveSR.anipal.Eye.SRanipal_Eye_v2.CallbackBasic)EyeCallback2);
                    ViveSR.anipal.Eye.SRanipal_Eye_v2.WrapperUnRegisterEyeDataCallback(functionPointer);
                }
            }
        }

        public void SetupCallbacks()
        {
            Core.OnPostSessionEnd -= PostSessionEndEvent;
            Core.OnPostSessionEnd += PostSessionEndEvent;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= SceneManager_sceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += SceneManager_sceneLoaded;

            framework = ViveSR.anipal.Eye.SRanipal_Eye_Framework.Instance;
            if (framework != null)
                framework.StartFramework();

            //if framework status is not working, registering callbacks will not work
            if (framework == null || ViveSR.anipal.Eye.SRanipal_Eye_Framework.Status != ViveSR.anipal.Eye.SRanipal_Eye_Framework.FrameworkStatus.WORKING)
            {
                Util.logWarning("FixationRecorder found SRanipal_Eye_Framework not in working status");
                return;
            }

            if (framework != null && framework.EnableEyeDataCallback)
            {
                //unregister existing callbacks
                UnregisterEyeCallbacks();

                if (framework.EnableEyeVersion == ViveSR.anipal.Eye.SRanipal_Eye_Framework.SupportedEyeVersion.version1)
                {
                    var functionPointer = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate((ViveSR.anipal.Eye.SRanipal_Eye.CallbackBasic)EyeCallback);
                    useDataQueue1 = true;
                    //EyeDataQueue1 = new Queue<ViveSR.anipal.Eye.EyeData>(4);
                    EyeDataQueue1 = new System.Collections.Concurrent.ConcurrentQueue<ViveSR.anipal.Eye.EyeData>();

                    if (ViveSR.anipal.Eye.SRanipal_Eye_Framework.Instance.EnableEyeDataCallback == true)
                    {
                        ViveSR.anipal.Eye.SRanipal_Eye.WrapperRegisterEyeDataCallback(functionPointer);
                    }
                }
                else
                {
                    var functionPointer = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate((ViveSR.anipal.Eye.SRanipal_Eye_v2.CallbackBasic)EyeCallback2);
                    useDataQueue2 = true;
                    //EyeDataQueue2 = new Queue<ViveSR.anipal.Eye.EyeData_v2>(4);
                    EyeDataQueue2 = new System.Collections.Concurrent.ConcurrentQueue<ViveSR.anipal.Eye.EyeData_v2>();

                    if (ViveSR.anipal.Eye.SRanipal_Eye_Framework.Instance.EnableEyeDataCallback == true)
                    {
                        ViveSR.anipal.Eye.SRanipal_Eye_v2.WrapperRegisterEyeDataCallback(functionPointer);
                    }
                }
            }
        }

        void PostSessionEndEvent()
        {
            if (framework != null && framework.EnableEyeDataCallback)
            {
                //unregister existing callbacks
                OnDisable();
                startTimestamp = 0;
                useDataQueue1 = false;
                useDataQueue2 = false;
                while (EyeDataQueue1 != null && !EyeDataQueue1.IsEmpty)
                    EyeDataQueue1.TryDequeue(out currentData1);
                while (EyeDataQueue2 != null && !EyeDataQueue2.IsEmpty)
                    EyeDataQueue2.TryDequeue(out currentData2);
                currentData1 = new ViveSR.anipal.Eye.EyeData();
                currentData2 = new ViveSR.anipal.Eye.EyeData_v2();
            }
        }

        //this attribute fixes the issue with il2cpp scripting backend not marshaling to the callbacks below
        internal class MonoPInvokeCallbackAttribute : System.Attribute
        {
            public MonoPInvokeCallbackAttribute() { }
        }

        [MonoPInvokeCallback]
        private static void EyeCallback(ref ViveSR.anipal.Eye.EyeData eye_data)
        {
            EyeDataQueue1.Enqueue(eye_data);
        }

        [MonoPInvokeCallback]
        private static void EyeCallback2(ref ViveSR.anipal.Eye.EyeData_v2 eye_data)
        {
            EyeDataQueue2.Enqueue(eye_data);
        }

        public bool CombinedWorldGazeRay(out Ray ray)
        {
            if (useDataQueue1)
            {
                var gazedir = currentData1.verbose_data.combined.eye_data.gaze_direction_normalized;
                gazedir.x *= -1;
                ray = new Ray(GameplayReferences.HMD.position, GameplayReferences.HMD.TransformDirection(gazedir));
                return currentData1.verbose_data.combined.eye_data.GetValidity(ViveSR.anipal.Eye.SingleEyeDataValidity.SINGLE_EYE_DATA_GAZE_DIRECTION_VALIDITY);
            }
            if (useDataQueue2)
            {
                var gazedir = currentData2.verbose_data.combined.eye_data.gaze_direction_normalized;
                gazedir.x *= -1;
                ray = new Ray(GameplayReferences.HMD.position, GameplayReferences.HMD.TransformDirection(gazedir));
                return currentData2.verbose_data.combined.eye_data.GetValidity(ViveSR.anipal.Eye.SingleEyeDataValidity.SINGLE_EYE_DATA_GAZE_DIRECTION_VALIDITY);
            }
            if (ViveSR.anipal.Eye.SRanipal_Eye.GetGazeRay(ViveSR.anipal.Eye.GazeIndex.COMBINE,out ray))
            {
                ray.direction = GameplayReferences.HMD.TransformDirection(ray.direction);
                ray.origin = GameplayReferences.HMD.position;
                return true;
            }
            return false;
        }

        public bool LeftEyeOpen()
        {
            float openness = 0;
            if (useDataQueue1)
            {
                if (!currentData1.verbose_data.left.GetValidity(ViveSR.anipal.Eye.SingleEyeDataValidity.SINGLE_EYE_DATA_GAZE_DIRECTION_VALIDITY))
                    return false;
                return currentData1.verbose_data.left.eye_openness > 0.5f;
            }
            if (useDataQueue2)
            {
                if (!currentData2.verbose_data.left.GetValidity(ViveSR.anipal.Eye.SingleEyeDataValidity.SINGLE_EYE_DATA_GAZE_DIRECTION_VALIDITY))
                    return false;
                return currentData2.verbose_data.left.eye_openness > 0.5f;
            }
            if (ViveSR.anipal.Eye.SRanipal_Eye.GetEyeOpenness(ViveSR.anipal.Eye.EyeIndex.LEFT, out openness))
            {
                return openness > 0.5f;
            }
            return false;
        }

        public bool RightEyeOpen()
        {
            float openness = 0;
            if (useDataQueue1)
            {
                if (!currentData1.verbose_data.right.GetValidity(ViveSR.anipal.Eye.SingleEyeDataValidity.SINGLE_EYE_DATA_GAZE_DIRECTION_VALIDITY))
                    return false;
                return currentData1.verbose_data.right.eye_openness > 0.5f;
            }
            if (useDataQueue2)
            {
                if (!currentData2.verbose_data.right.GetValidity(ViveSR.anipal.Eye.SingleEyeDataValidity.SINGLE_EYE_DATA_GAZE_DIRECTION_VALIDITY))
                    return false;
                return currentData2.verbose_data.right.eye_openness > 0.5f;
            }
            if (ViveSR.anipal.Eye.SRanipal_Eye.GetEyeOpenness(ViveSR.anipal.Eye.EyeIndex.RIGHT, out openness))
            {
                return openness > 0.5f;
            }
            return false;
        }

		public long EyeCaptureTimestamp()
		{
			if (useDataQueue1)
			{
				if (startTimestamp == 0)
					startTimestamp = currentData1.timestamp;
				var MsSincestart = currentData1.timestamp - startTimestamp; //milliseconds since start
				var final = epochStart * 1000 + MsSincestart;
				return (long)final;
			}
			else if (useDataQueue2)
			{
				if (startTimestamp == 0)
					startTimestamp = currentData2.timestamp;
				var MsSincestart = currentData2.timestamp - startTimestamp; //milliseconds since start
				var final = epochStart * 1000 + MsSincestart;
				return (long)final;
			}
			return (long)(Util.Timestamp() * 1000);
		}

        int lastProcessedFrame;
        //returns true if there is another data point to work on
        public bool GetNextData()
        {
            if (useDataQueue1)
            {
                if (EyeDataQueue1.Count > 0)
                {
                    //currentData1 = EyeDataQueue1.Dequeue();
                    EyeDataQueue1.TryDequeue(out currentData1);
                    return true;
                }
                return false;
            }
            else if (useDataQueue2)
            {
                if (EyeDataQueue2.Count > 0)
                {
                    //currentData2 = EyeDataQueue2.Dequeue();
                    EyeDataQueue2.TryDequeue(out currentData2);
                    return true;
                }
                return false;
            }

            if (lastProcessedFrame != Time.frameCount) //useDataQueue1 or useDataQueue2 are only true if using the callback. this is used in other cases
            {
                lastProcessedFrame = Time.frameCount;
                return true;
            }
            return false;
        }
#elif C3D_VARJO
        const int CachedEyeCaptures = 100; //VARJO

        Varjo.XR.VarjoEyeTracking.GazeData currentData;
        static System.DateTime epoch = new System.DateTime(1970, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);

        //raw time since computer restarted. in nanoseconds
        long startTimestamp;

        //start time since epoch. in seconds
        double epochStart;

        IEnumerator Start()
        {
            while (true)
            {
                if (Varjo.XR.VarjoEyeTracking.IsGazeAllowed() && Varjo.XR.VarjoEyeTracking.IsGazeCalibrated())
                {
                    startTimestamp = Varjo.XR.VarjoEyeTracking.GetGaze().captureTime;
                    if (startTimestamp > 0)
                    {
                        System.TimeSpan span = System.DateTime.UtcNow - epoch;
                        epochStart = span.TotalSeconds;
                        break;
                    }
                }
                yield return null;
            }
        }

        public bool CombinedWorldGazeRay(out Ray ray)
        {
            ray = new Ray();
            if (Varjo.XR.VarjoEyeTracking.IsGazeAllowed() && Varjo.XR.VarjoEyeTracking.IsGazeCalibrated())
            {
                // Check if gaze data is valid and calibrated
                if (currentData.status != Varjo.XR.VarjoEyeTracking.GazeStatus.Invalid)
                {
                    ray.direction = GameplayReferences.HMD.TransformDirection(new Vector3((float)currentData.gaze.forward[0], (float)currentData.gaze.forward[1], (float)currentData.gaze.forward[2]));
                    ray.origin = GameplayReferences.HMD.TransformPoint(new Vector3((float)currentData.gaze.origin[0], (float)currentData.gaze.origin[1], (float)currentData.gaze.origin[2]));
                    return true;
                }
            }
            return false;
        }

        public bool LeftEyeOpen()
        {
            if (Varjo.XR.VarjoEyeTracking.IsGazeAllowed() && Varjo.XR.VarjoEyeTracking.IsGazeCalibrated())
                return currentData.leftStatus != Varjo.XR.VarjoEyeTracking.GazeEyeStatus.Invalid;
            return false;
        }
        public bool RightEyeOpen()
        {
            if (Varjo.XR.VarjoEyeTracking.IsGazeAllowed() && Varjo.XR.VarjoEyeTracking.IsGazeCalibrated())
                return currentData.rightStatus != Varjo.XR.VarjoEyeTracking.GazeEyeStatus.Invalid;
            return false;
        }

        public long EyeCaptureTimestamp()
        {
            //currentData.captureTime //nanoseconds. steady clock
            long sinceStart = currentData.captureTime - startTimestamp;
            sinceStart = (sinceStart / 1000000); //remove NANOSECONDS
            var final = epochStart * 1000 + sinceStart;
            return (long)final;
        }
        
        int lastQueueFrame = 0;
        Queue<Varjo.XR.VarjoEyeTracking.GazeData> queuedData = new Queue<Varjo.XR.VarjoEyeTracking.GazeData>();

        //returns true if there is another data point to work on
        public bool GetNextData()
        {
            if (Varjo.XR.VarjoEyeTracking.IsGazeAllowed() && Varjo.XR.VarjoEyeTracking.IsGazeCalibrated())
            {
                //once a frame if queuedData is empty, get latest gaze data
                if (lastQueueFrame != Time.frameCount && queuedData.Count == 0)
                {
                    List<Varjo.XR.VarjoEyeTracking.GazeData> latestData;
                    Varjo.XR.VarjoEyeTracking.GetGazeList(out latestData);
                    foreach(var v in latestData)
                    {
                        queuedData.Enqueue(v);
                    }
                    lastQueueFrame = Time.frameCount;
                }

                if (queuedData.Count > 0)
                {
                    currentData = queuedData.Dequeue();
                    return true;
                }
                else
                {
                    return false;
                }
            }
            return false;
        }
#elif C3D_NEURABLE
        const int CachedEyeCaptures = 120;
        //public Ray CombinedWorldGazeRay() { return Neurable.Core.NeurableUser.Instance.NeurableCam.GazeRay(); }
        public bool CombinedWorldGazeRay(out Ray ray)
        {
            ray = Neurable.Core.NeurableUser.Instance.NeurableCam.GazeRay();
            return true;
        }

        //TODO neurable check eye state
        public bool LeftEyeOpen() { return true; }
        public bool RightEyeOpen() { return true; }

        static System.DateTime epoch = new System.DateTime(1970, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);
        public long EyeCaptureTimestamp()
        {
            //TODO return correct timestamp - might need to use Tobii implementation
            System.TimeSpan span = System.DateTime.UtcNow - epoch;
            return (long)(span.TotalSeconds * 1000);
        }

        int lastProcessedFrame;
        //returns true if there is another data point to work on
        public bool GetNextData()
        {
            if (lastProcessedFrame != Time.frameCount)
            {
                lastProcessedFrame = Time.frameCount;
                return true;
            }
            return false;
        }
#elif C3D_AH
        const int CachedEyeCaptures = 120; //ADHAWK
        private static Calibrator ah_calibrator;
        AdhawkApi.EyeTracker eyetracker;
        public bool CombinedWorldGazeRay(out Ray ray)
        {
            Vector3 r = ah_calibrator.GetGazeVector(filterType: AdhawkApi.Numerics.Filters.FilterType.ExponentialMovingAverage);
            Vector3 x = ah_calibrator.GetGazeOrigin();
            ray = new Ray(x, r);
            return true;
        }

        public bool LeftEyeOpen() { return eyetracker.CurrentTrackingState == AdhawkApi.EyeTracker.TrackingState.TrackingLeft || eyetracker.CurrentTrackingState == AdhawkApi.EyeTracker.TrackingState.TrackingBoth; }
        public bool RightEyeOpen() { return eyetracker.CurrentTrackingState == AdhawkApi.EyeTracker.TrackingState.TrackingRight || eyetracker.CurrentTrackingState == AdhawkApi.EyeTracker.TrackingState.TrackingBoth; }

        public long EyeCaptureTimestamp()
        {
            return (long)(Util.Timestamp() * 1000);
        }

        int lastProcessedFrame;
        //returns true if there is another data point to work on
        public bool GetNextData()
        {
            if (lastProcessedFrame != Time.frameCount)
            {
                lastProcessedFrame = Time.frameCount;
                return true;
            }
            return false;
        }
#elif C3D_PUPIL
        PupilLabs.GazeController gazeController;

        void ReceiveEyeData(PupilLabs.GazeData data)
        {
            if (data.Confidence < 0.6f)
            {
                return;
            }
            PupilGazeData pgd = new PupilGazeData();

            pgd.Timestamp = (long)(Util.Timestamp() * 1000);
            pgd.LeftEyeOpen = data.IsEyeDataAvailable(1);
            pgd.RightEyeOpen = data.IsEyeDataAvailable(0);
            pgd.GazeRay = new Ray(GameplayReferences.HMD.position, GameplayReferences.HMD.TransformDirection(data.GazeDirection));
            GazeDataQueue.Enqueue(pgd);
        }

        Queue<PupilGazeData> GazeDataQueue = new Queue<PupilGazeData>(8);
        class PupilGazeData
        {
            public long Timestamp;
            public Ray GazeRay;
            public bool LeftEyeOpen;
            public bool RightEyeOpen;
        }

        private void OnDisable()
        {
            gazeController.OnReceive3dGaze -= ReceiveEyeData;
        }

        const int CachedEyeCaptures = 120; //PUPIL LABS
        public bool CombinedWorldGazeRay(out Ray ray)
        {
            //world gaze direction
            ray = currentData.GazeRay;
            return true;
        }

        public bool LeftEyeOpen() { return currentData.LeftEyeOpen; }
        public bool RightEyeOpen() { return currentData.RightEyeOpen; }

        public long EyeCaptureTimestamp()
        {
            return currentData.Timestamp;
        }

        PupilGazeData currentData;
        //returns true if there is another data point to work on
        public bool GetNextData()
        {
            if (GazeDataQueue.Count > 0)
            {
                currentData = GazeDataQueue.Dequeue();
                return true;
            }
            return false;
        }
#elif C3D_OMNICEPT
        //TODO check if this is on a different thread
        Queue<SimpleGliaEyeData> trackingDataQueue = new Queue<SimpleGliaEyeData>();

        struct SimpleGliaEyeData
        {
            public float confidence;
            public long timestamp;
            public Vector3 worldPosition;
            public Vector3 worldDirection;
            public float leftEyeOpenness;
            public float rightEyeOpenness;
        }


        void RecordEyeTracking(HP.Omnicept.Messaging.Messages.EyeTracking data)
        {
            SimpleGliaEyeData d = new SimpleGliaEyeData() {
                confidence = data.CombinedGaze.Confidence,
                timestamp = data.Timestamp.SystemTimeMicroSeconds / 1000,
                worldDirection = GameplayReferences.HMD.TransformDirection(new Vector3(-data.CombinedGaze.X, data.CombinedGaze.Y, data.CombinedGaze.Z)),
                worldPosition = GameplayReferences.HMD.position,
                leftEyeOpenness = data.LeftEye.Openness,
                rightEyeOpenness = data.RightEye.Openness
            };

            trackingDataQueue.Enqueue(d);
        }
        
        SimpleGliaEyeData currentData;
        const int CachedEyeCaptures = 120;
        public bool CombinedWorldGazeRay(out Ray ray)
        {
            if (currentData.confidence < 0.5f)
            {
                ray = new Ray(Vector3.zero, Vector3.forward);
                return false;
            }
            ray = new Ray(currentData.worldPosition, currentData.worldDirection);
            return true;
        }

        public bool LeftEyeOpen() { return currentData.leftEyeOpenness > 0.4f; }
        public bool RightEyeOpen() { return currentData.rightEyeOpenness > 0.4f; }

        public long EyeCaptureTimestamp()
        {
            //check that this correctly trims the microseconds
            return currentData.timestamp;
        }

        
        //returns true if there is another data point to work on
        public bool GetNextData()
        {
            if (trackingDataQueue.Count > 0)
            {
                currentData = trackingDataQueue.Dequeue();
                return true;
            }
            return false;
        }

        void OnDestroy()
        {
            //should be on destroy or on session end
            var gliaBehaviour = GameplayReferences.GliaBehaviour;

            if (gliaBehaviour != null)
            {
                //eye tracking
                gliaBehaviour.OnEyeTracking.RemoveListener(RecordEyeTracking);
            }
        }
#else
        const int CachedEyeCaptures = 120;

        public bool CombinedWorldGazeRay(out Ray ray)
        {
            UnityEngine.XR.Eyes eyes;
            if (UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.CenterEye).TryGetFeatureValue(UnityEngine.XR.CommonUsages.eyesData, out eyes))
            {
                //first arg probably to mark which feature the value should return. type alone isn't enough to indicate the property
                Vector3 convergancePoint;
                if (eyes.TryGetFixationPoint(out convergancePoint))
                {
                    Vector3 leftPos = Vector3.zero;
                    eyes.TryGetLeftEyePosition(out leftPos);
                    Vector3 rightPos = Vector3.zero;
                    eyes.TryGetRightEyePosition(out rightPos);

                    Vector3 centerPos = (rightPos + leftPos) / 2f;

                    var worldGazeDirection = (convergancePoint - centerPos).normalized;
                    //screenGazePoint = GameplayReferences.HMDCameraComponent.WorldToScreenPoint(GameplayReferences.HMD.position + 10 * worldGazeDirection);

                    if (GameplayReferences.HMD.parent != null)
                        worldGazeDirection = GameplayReferences.HMD.parent.TransformDirection(worldGazeDirection);

                    ray = new Ray(centerPos, worldGazeDirection);

                    return true;
                }
            }
            ray = new Ray(Vector3.zero, Vector3.forward);
            return false;
        }

        public bool LeftEyeOpen()
        {
            UnityEngine.XR.Eyes eyes;
            if (UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.LeftEye).TryGetFeatureValue(UnityEngine.XR.CommonUsages.eyesData, out eyes))
            {
                float open;
                if (eyes.TryGetLeftEyeOpenAmount(out open))
                {
                    return open > 0.6f;
                }
            }
            return false;
        }
        public bool RightEyeOpen()
        {
            UnityEngine.XR.Eyes eyes;
            if (UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightEye).TryGetFeatureValue(UnityEngine.XR.CommonUsages.eyesData, out eyes))
            {
                float open;
                if (eyes.TryGetRightEyeOpenAmount(out open))
                {
                    return open > 0.6f;
                }
            }
            return false;
        }

        public long EyeCaptureTimestamp()
        {
            return (long)(Util.Timestamp() * 1000);
        }

        int lastProcessedFrame;
        //returns true if there is another data point to work on
        public bool GetNextData()
        {
            if (lastProcessedFrame != Time.frameCount)
            {
                lastProcessedFrame = Time.frameCount;
                return true;
            }
            return false;
        }
#endif

#endregion

        //if active fixation is world space, in world space, this indicates the last several positions for the average fixation position
        //if active fixation is local space, these are in local space
        List<Vector3> CachedEyeCapturePositions = new List<Vector3>();

        int index = 0;
        int GetIndex(int offset)
        {
            if (index + offset < 0)
                return (CachedEyeCaptures + index + offset) % CachedEyeCaptures;
            return (index + offset) % CachedEyeCaptures;
        }

        private EyeCapture[] EyeCaptures = new EyeCapture[CachedEyeCaptures];

        public bool IsFixating { get; set; }
        public Fixation ActiveFixation;

        [Header("Blink")]
        [Tooltip("the maximum amount of time that can be assigned as a single 'blink'. if eyes are closed for longer than this, assume that the user is consciously closing their eyes")]
        public int MaxBlinkMs = 400;
        [Tooltip("when a blink occurs, ignore gaze preceding the blink up to this far back in time")]
        public int PreBlinkDiscardMs = 20;
        [Tooltip("after a blink has ended, ignore gaze up to this long afterwards")]
        public int BlinkEndWarmupMs = 100;
        //the most recent time user has stopped blinking
        long EyeUnblinkTime;
        bool eyesClosed;

        [Header("Fixation")]
        [Tooltip("the time that gaze must be within the max fixation angle before a fixation begins")]
        public int MinFixationMs = 60;
        [Tooltip("the amount of time gaze can be discarded before a fixation is ended. gaze can be discarded if eye tracking values are outside of expected ranges")]
        public int MaxConsecutiveDiscardMs = 10;
        [Tooltip("the angle that a number of gaze samples must fall within to start a fixation event")]
        public float MaxFixationAngle = 1;
        [Tooltip("amount of time gaze can be off the transform before fixation ends. mostly useful when fixation is right on the edge of a dynamic object")]
        public int MaxConsecutiveOffDynamicMs = 500;
        [Tooltip("multiplier for SMOOTH PURSUIT fixation angle size on dynamic objects. helps reduce incorrect fixation ending")]
        public float DynamicFixationSizeMultiplier = 1.25f;
        [Tooltip("increases the size of the fixation angle as gaze gets toward the edge of the viewport. this is used to reduce the number of incorrectly ended fixations because of hardware limits at the edge of the eye tracking field of view")]
        public AnimationCurve FocusSizeFromCenter;

        [Header("Saccade")]
        [Tooltip("amount of consecutive eye samples before a fixation ends as the eye fixates elsewhere")]
        public int SaccadeFixationEndMs = 10;

        //used by RenderEyeTracking for rendering saccades
        public const int DisplayGazePointCount = 4096;
        public CircularBuffer<ThreadGazePoint> DisplayGazePoints = new CircularBuffer<ThreadGazePoint>(4096);
        public List<Vector2> SaccadeScreenPoints = new List<Vector2>(16);


        void Reset()
        {
            FocusSizeFromCenter = new AnimationCurve();
            FocusSizeFromCenter.AddKey(new Keyframe(0.01f, 1, 0, 0));
            FocusSizeFromCenter.AddKey(new Keyframe(0.5f, 2, 5, 0));
        }

        public void Initialize()
        {
            if (FocusSizeFromCenter == null) { Reset(); }

            IsFixating = false;
            ActiveFixation = new Fixation();
            for (int i = 0; i < CachedEyeCaptures; i++)
            {
                EyeCaptures[i] = new EyeCapture() { Discard = true };
            }

            CoreInterface.FixationSettings(MaxBlinkMs, PreBlinkDiscardMs, BlinkEndWarmupMs, MinFixationMs, MaxConsecutiveDiscardMs, MaxFixationAngle, MaxConsecutiveOffDynamicMs, DynamicFixationSizeMultiplier, FocusSizeFromCenter, SaccadeFixationEndMs);

#if C3D_FOVE
            fovebase = GameplayReferences.FoveInstance;
#elif C3D_SRANIPAL
            SetupCallbacks();
            System.TimeSpan span = System.DateTime.UtcNow - epoch;
            epochStart = span.TotalSeconds;
#elif C3D_AH
            ah_calibrator = Calibrator.Instance;
            eyetracker = EyeTracker.Instance;
#elif C3D_PUPIL
            gazeController = GameplayReferences.GazeController;
            if (gazeController != null)
                gazeController.OnReceive3dGaze += ReceiveEyeData;
            else
                Debug.LogError("Pupil Labs GazeController is null!");
#elif C3D_OMNICEPT

            var gliaBehaviour = GameplayReferences.GliaBehaviour;

            if (gliaBehaviour != null)
            {
                gliaBehaviour.OnEyeTracking.AddListener(RecordEyeTracking);
            }
#endif
        }

        private void Update()
        {
            if (!Cognitive3D_Manager.IsInitialized) { return; }
            if (GameplayReferences.HMD == null) { Cognitive3D.Util.logWarning("HMD is null! Fixation will not function"); return; }
            if (Cognitive3D_Manager.TrackingScene == null) { Cognitive3D.Util.logDevelopment("Missing SceneId. Skip Fixation Recorder update"); return; }

            PostGazeCallback();
        }

        //assuming command buffer will send a callback, use this when the command buffer is ready to read
        void PostGazeCallback()
        {
            while (GetNextData())
            {
                RecordEyeCapture();
            }
        }

        void RecordEyeCapture()
        {
            //this should just raycast + bring all the necessary eye data together and pass it into the CoreInterface
            

            //reset all values
            EyeCaptures[index].Discard = false;
            EyeCaptures[index].SkipPositionForFixationAverage = false;
            EyeCaptures[index].OffTransform = true;
            EyeCaptures[index].OutOfRange = false;
            EyeCaptures[index].HitDynamicId = string.Empty;

            bool areEyesClosed = AreEyesClosed();

            //set new current values
            EyeCaptures[index].EyesClosed = areEyesClosed;
            EyeCaptures[index].HmdPosition = GameplayReferences.HMD.position;
            EyeCaptures[index].Time = EyeCaptureTimestamp();
            if (EyeCaptures[index].EyesClosed) { EyeCaptures[index].SkipPositionForFixationAverage = true; }

            Vector3 world;

            DynamicObject hitDynamic = null;

            var hitresult = GazeRaycast(out world, out hitDynamic);

            if (hitresult == GazeRaycastResult.HitWorld)
            {
                //hit something as expected
                EyeCaptures[index].WorldPosition = world;
                EyeCaptures[index].ScreenPos = GameplayReferences.HMDCameraComponent.WorldToScreenPoint(world);
                SaccadeScreenPoints.Add(EyeCaptures[index].ScreenPos);

                //IMPROVEMENT allocate this at startup
                if (DisplayGazePoints[DisplayGazePoints.Count] == null)
                    DisplayGazePoints[DisplayGazePoints.Count] = new ThreadGazePoint();

                if (hitDynamic != null)
                {
                    //if hit dynamic but fixation is not local, skip position update
                    if (IsFixating && !ActiveFixation.IsLocal)
                    {
                        EyeCaptures[index].SkipPositionForFixationAverage = true;
                    }


                    EyeCaptures[index].UseCaptureMatrix = true;
                    //TODO test that this matrix is correct if dynamic is parented to offset/rotated/scaled transform
                    EyeCaptures[index].CaptureMatrix = Matrix4x4.TRS(hitDynamic.transform.position, hitDynamic.transform.rotation, hitDynamic.transform.lossyScale);
                    EyeCaptures[index].HitDynamicId = hitDynamic.GetId();

                    //scaled because dynamic obejct handles the scale so point will appear on surface
                    EyeCaptures[index].LocalPosition = hitDynamic.transform.InverseTransformPoint(world);

                    DisplayGazePoints[DisplayGazePoints.Count].WorldPoint = EyeCaptures[index].WorldPosition;
                    DisplayGazePoints[DisplayGazePoints.Count].IsLocal = true;
                    DisplayGazePoints[DisplayGazePoints.Count].LocalPoint = EyeCaptures[index].LocalPosition;
                    DisplayGazePoints[DisplayGazePoints.Count].Transform = hitDynamic.transform;
                    EyeCaptures[index].OffTransform = false;
                    EyeCaptures[index].HitDynamicTransform = hitDynamic.transform;
                    DisplayGazePoints.Update();
                }
                else
                {
                    EyeCaptures[index].UseCaptureMatrix = false;

                    DisplayGazePoints[DisplayGazePoints.Count].WorldPoint = EyeCaptures[index].WorldPosition;
                    DisplayGazePoints[DisplayGazePoints.Count].IsLocal = false;
                    EyeCaptures[index].OffTransform = false;
                    EyeCaptures[index].HitDynamicId = string.Empty;
                    DisplayGazePoints.Update();
                }
            }
            else if (hitresult == GazeRaycastResult.HitNothing)
            {
                //eye capture world point could be used for getting the direction, but position is invalid (on skybox)
                EyeCaptures[index].SkipPositionForFixationAverage = true;
                EyeCaptures[index].UseCaptureMatrix = false;
                EyeCaptures[index].WorldPosition = world;
                EyeCaptures[index].ScreenPos = GameplayReferences.HMDCameraComponent.WorldToScreenPoint(world);
                if (SaccadeScreenPoints.Count > 0)
                    SaccadeScreenPoints.RemoveAt(0);
                EyeCaptures[index].OffTransform = true;
            }
            else if (hitresult == GazeRaycastResult.Invalid)
            {
                EyeCaptures[index].SkipPositionForFixationAverage = true;
                EyeCaptures[index].UseCaptureMatrix = false;
                EyeCaptures[index].Discard = true;
                EyeCaptures[index].OffTransform = true;
                if (SaccadeScreenPoints.Count > 0)
                    SaccadeScreenPoints.RemoveAt(0);
            }

            if (SaccadeScreenPoints.Count > 15)
            {
                SaccadeScreenPoints.RemoveAt(0);
            }

            //submit eye data here
            CoreInterface.RecordEyeData(EyeCaptures[index]);


            index = (index + 1) % CachedEyeCaptures;
        }

        //the position in the world/local hit. returns true if hit something
        GazeRaycastResult GazeRaycast(out Vector3 world, out Cognitive3D.DynamicObject hitDynamic)
        {
            RaycastHit hit = new RaycastHit();
            Ray combinedWorldGaze;
            bool validRay = CombinedWorldGazeRay(out combinedWorldGaze);
            if (!validRay) { hitDynamic = null; world = Vector3.zero; return GazeRaycastResult.Invalid; }
            if (Physics.Raycast(combinedWorldGaze, out hit, 1000f, Cognitive3D_Preferences.Instance.GazeLayerMask, Cognitive3D_Preferences.Instance.TriggerInteraction))
            {
                world = hit.point;

                if (Cognitive3D_Preferences.S_DynamicObjectSearchInParent)
                {
                    hitDynamic = hit.collider.GetComponentInParent<DynamicObject>();
                }
                else
                {
                    hitDynamic = hit.collider.GetComponent<DynamicObject>();
                }
                return GazeRaycastResult.HitWorld;
            }
            else
            {
                world = combinedWorldGaze.GetPoint(Mathf.Min(100, GameplayReferences.HMDCameraComponent.farClipPlane));
                hitDynamic = null;
                return GazeRaycastResult.HitNothing;
            }
        }

        /// <summary>
        /// are eyes closed this capture?
        /// if they begin being closed, discard the last PreBlinkDiscard MS
        /// if they become opened, still discard next BlinkEndWarmup MS
        /// </summary>
        /// <returns></returns>
        bool AreEyesClosed()
        {
            if (!LeftEyeOpen() && !RightEyeOpen()) //eyes are closed / begin blink?
            {
                //when blinking started, discard previous eye capture
                if (!eyesClosed)
                {
                    //iterate through eye captures. if current time - preblinkms > capture time, discard
                    for (int i = 0; i < CachedEyeCaptures; i++)
                    {
                        if (EyeCaptures[index].Time - PreBlinkDiscardMs > EyeCaptures[GetIndex(-i)].Time)
                        {
                            EyeCaptures[GetIndex(-i)].EyesClosed = true;
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                eyesClosed = true;
                return true;
            }

            if (eyesClosed && LeftEyeOpen() && RightEyeOpen()) //end blink
            {
                //when blinking ended, discard next couple eye captures
                eyesClosed = false;
                EyeUnblinkTime = EyeCaptures[index].Time;
            }

            if (EyeUnblinkTime + BlinkEndWarmupMs > EyeCaptures[index].Time)
            {
                return true;
            }

            return false;
        }
    }
}