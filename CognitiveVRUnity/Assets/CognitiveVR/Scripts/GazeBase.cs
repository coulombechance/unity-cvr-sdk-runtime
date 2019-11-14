﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CognitiveVR;

#if CVR_AH
using AdhawkApi;
using AdhawkApi.Numerics.Filters;
#endif

//deals with most generic integration stuff
//hmd removed - send data

//must implement
//1. dynamics raycast
//2. call dynamic.OnGaze(interval)
//3. fove/pupil/tobii gaze direction
//4. media
//5. gps + compass
//6. floor position

namespace CognitiveVR
{
    [AddComponentMenu("")]
    public class GazeBase : MonoBehaviour
    {
        public struct GazePoint
        {
            public bool IsLocal;
            public Vector3 WorldPoint;
            public Vector3 LocalPoint;
            public Transform Transform;
        }
        public GazePoint[] DisplayGazePoints = new GazePoint[256]; //25.6 seconds
        public int currentGazePoint { get; protected set; }
        public bool DisplayGazePointBufferFull;
#if CVR_TOBIIVR
        private static Tobii.Research.Unity.VREyeTracker _eyeTracker;
#endif
#if CVR_FOVE
        static FoveInterfaceBase _foveInstance;
        public static FoveInterfaceBase FoveInstance
        {
            get
            {
                if (_foveInstance == null)
                {
                    _foveInstance = FindObjectOfType<FoveInterfaceBase>();
                }
                return _foveInstance;
            }
        }
#endif
#if CVR_AH
        private static Calibrator ah_calibrator;
#endif

        public static Vector3 LastGazePoint;

        protected bool headsetPresent;
        protected Transform cameraRoot;

        //called immediately after construction
        public virtual void Initialize()
        {
#if CVR_STEAMVR
            CognitiveVR_Manager.PoseEvent += CognitiveVR_Manager_OnPoseEvent; //1.2
#endif
#if CVR_OCULUS
            OVRManager.HMDMounted += OVRManager_HMDMounted;
            OVRManager.HMDUnmounted += OVRManager_HMDUnmounted;
#endif
            string hmdname = "none";
#if CVR_FOVE
                    hmdname = "fove";
#elif CVR_ARKIT
                    hmdname = "arkit";
#elif CVR_ARCORE
                    hmdname = "arcore";
#elif CVR_META
                    hmdname = "meta";
#elif UNITY_2017_2_OR_NEWER
            string rawHMDName = UnityEngine.XR.XRDevice.model.ToLower();
            hmdname = CognitiveVR.Util.GetSimpleHMDName(rawHMDName);
#else
            string rawHMDName = UnityEngine.VR.VRDevice.model.ToLower();
            hmdname = CognitiveVR.Util.GetSimpleHMDName(rawHMDName);
#endif

#if CVR_TOBIIVR
            _eyeTracker = Tobii.Research.Unity.VREyeTracker.Instance;
#endif

#if CVR_AH
            ah_calibrator = Calibrator.Instance;
#endif
#if CVR_PUPIL
            gazeController = FindObjectOfType<PupilLabs.GazeController>();
            if (gazeController != null)
                gazeController.OnReceive3dGaze += ReceiveEyeData;
            else
                Debug.LogError("Pupil Labs GazeController is null!");
#endif
            GazeCore.SetHMDType(hmdname);
            cameraRoot = GameplayReferences.HMD.root;
        }

        //TODO support more sdks to send when HMD removed (wave, varjo, steamvr2)

#if CVR_STEAMVR
        void CognitiveVR_Manager_OnPoseEvent(Valve.VR.EVREventType evrevent)
        {
            if (evrevent == Valve.VR.EVREventType.VREvent_TrackedDeviceUserInteractionStarted)
            {
                headsetPresent = true;
            }
            if (evrevent == Valve.VR.EVREventType.VREvent_TrackedDeviceUserInteractionEnded)
            {
                headsetPresent = false;
                if (CognitiveVR_Preferences.Instance.SendDataOnHMDRemove)
                {
                    Core.InvokeSendDataEvent();
                }
            }
        }

        void CognitiveVR_Manager_OnPoseEventOLD(Valve.VR.EVREventType evrevent)
        {
            if (evrevent == Valve.VR.EVREventType.VREvent_TrackedDeviceUserInteractionStarted)
            {
                headsetPresent = true;
            }
            if (evrevent == Valve.VR.EVREventType.VREvent_TrackedDeviceUserInteractionEnded)
            {
                headsetPresent = false;
                if (CognitiveVR_Preferences.Instance.SendDataOnHMDRemove)
                {
                    Core.InvokeSendDataEvent();
                }
            }
        }
#endif

#if CVR_OCULUS
        private void OVRManager_HMDMounted()
        {
            headsetPresent = true;
        }

        private void OVRManager_HMDUnmounted()
        {
            headsetPresent = false;
            if (CognitiveVR_Preferences.Instance.SendDataOnHMDRemove)
            {
                Core.InvokeSendDataEvent();
            }
        }
#endif

        /// <summary>
        /// raycasts then spherecasts in a direction to find dynamic object being gazed at. returns true if hits dynamic
        /// </summary>
        /// <param name="pos"></param>
        /// <param name="direction"></param>
        /// <param name="distance"></param>
        /// <param name="radius"></param>
        /// <returns></returns>
        public virtual bool DynamicRaycast(Vector3 pos, Vector3 direction, float distance, float radius, out float hitDistance, out DynamicObject hitDynamic, out Vector3 worldHitPoint, out Vector3 localHitPoint, out Vector2 hitTextureCoord)
        {
            //TODO raycast to dynamic. if failed, spherecast with radius
            //if hit dynamic, return info

            RaycastHit hit = new RaycastHit();
            bool didhitdynamic = false;
            hitDynamic = null;
            hitDistance = 0;
            worldHitPoint = Vector3.zero;
            localHitPoint = Vector3.zero;
            hitTextureCoord = Vector2.zero;

            if (Physics.Raycast(pos, direction, out hit, distance, CognitiveVR_Preferences.Instance.DynamicLayerMask, QueryTriggerInteraction.Ignore))
            {
                if (CognitiveVR_Preferences.S_DynamicObjectSearchInParent)
                {
                    hitDynamic = hit.collider.GetComponentInParent<DynamicObject>();
                }
                else
                {
                    hitDynamic = hit.collider.GetComponent<DynamicObject>();
                }

                if (hitDynamic != null)
                {
                    didhitdynamic = true;
                    worldHitPoint = hit.point;

                    Vector3 LocalGaze = hitDynamic.transform.InverseTransformPointUnscaled(worldHitPoint);
                    float relativeScalex = hitDynamic.transform.lossyScale.x / hitDynamic.StartingScale.x;
                    float relativeScaley = hitDynamic.transform.lossyScale.y / hitDynamic.StartingScale.y;
                    float relativeScalez = hitDynamic.transform.lossyScale.z / hitDynamic.StartingScale.z;

                    localHitPoint.x = LocalGaze.x / relativeScalex;
                    localHitPoint.y = LocalGaze.y / relativeScaley;
                    localHitPoint.z = LocalGaze.z / relativeScalez;

                    //Vector3 relativeScale = new Vector3(
                    //    hitDynamic.transform.lossyScale.x / hitDynamic.StartingScale.x,
                    //    hitDynamic.transform.lossyScale.y / hitDynamic.StartingScale.y,
                    //    hitDynamic.transform.lossyScale.z / hitDynamic.StartingScale.z);
                    //localHitPoint = new Vector3(LocalGaze.x / relativeScale.x, LocalGaze.y / relativeScale.y, LocalGaze.z / relativeScale.z);

                    hitDistance = hit.distance;
                    hitTextureCoord = hit.textureCoord;
                }
            }
            if (!didhitdynamic && Physics.SphereCast(pos, radius, direction, out hit, distance, CognitiveVR_Preferences.Instance.DynamicLayerMask, QueryTriggerInteraction.Ignore))
            {
                if (CognitiveVR_Preferences.Instance.DynamicObjectSearchInParent)
                {
                    hitDynamic = hit.collider.GetComponentInParent<DynamicObject>();
                }
                else
                {
                    hitDynamic = hit.collider.GetComponent<DynamicObject>();
                }

                if (hitDynamic != null)
                {
                    didhitdynamic = true;
                    worldHitPoint = hit.point;

                    Vector3 LocalGaze = hitDynamic.transform.InverseTransformPointUnscaled(worldHitPoint);
                    float relativeScalex = hitDynamic.transform.lossyScale.x / hitDynamic.StartingScale.x;
                    float relativeScaley = hitDynamic.transform.lossyScale.y / hitDynamic.StartingScale.y;
                    float relativeScalez = hitDynamic.transform.lossyScale.z / hitDynamic.StartingScale.z;

                    localHitPoint.x = LocalGaze.x / relativeScalex;
                    localHitPoint.y = LocalGaze.y / relativeScaley;
                    localHitPoint.z = LocalGaze.z / relativeScalez;

                    //Vector3 relativeScale = new Vector3(
                    //    hitDynamic.transform.lossyScale.x / hitDynamic.StartingScale.x,
                    //    hitDynamic.transform.lossyScale.y / hitDynamic.StartingScale.y,
                    //    hitDynamic.transform.lossyScale.z / hitDynamic.StartingScale.z);
                    //localHitPoint = new Vector3(LocalGaze.x / relativeScale.x, LocalGaze.y / relativeScale.y, LocalGaze.z / relativeScale.z);
                    hitDistance = hit.distance;
                    hitTextureCoord = hit.textureCoord;
                }
            }

            return didhitdynamic;
        }

        public void GetOptionalSnapshotData(ref Vector3 gpsloc, ref float compass, ref Vector3 floorPos)
        {
            if (CognitiveVR_Preferences.Instance.TrackGPSLocation)
            {
                CognitiveVR_Manager.Instance.GetGPSLocation(ref gpsloc, ref compass);
            }
            if (CognitiveVR_Preferences.Instance.RecordFloorPosition)
            {
                if (cameraRoot == null)
                {
                    cameraRoot = GameplayReferences.HMD.root;
                }
                RaycastHit floorhit = new RaycastHit();
                if (Physics.Raycast(GameplayReferences.HMD.position, -cameraRoot.up, out floorhit))
                {
                    floorPos = floorhit.point;
                }
            }
        }

#if CVR_PUPIL
        PupilLabs.GazeController gazeController;
        Vector3 localGazeDirection;
        Vector3 pupilViewportPosition;

        void ReceiveEyeData(PupilLabs.GazeData data)
        {
            if (data.Confidence < 0.6f) { return; }
            localGazeDirection = data.GazeDirection;
            pupilViewportPosition = data.NormPos;
        }

        private void OnDisable()
        {
            gazeController.OnReceive3dGaze -= ReceiveEyeData;
        }
#endif

        /// <summary>
        /// get the raw gaze direction in world space. includes fove/pupil labs eye tracking
        /// </summary>
        public Vector3 GetWorldGazeDirection()
        {
            Vector3 gazeDirection = GameplayReferences.HMD.forward;
#if CVR_FOVE //direction
            var eyeRays = FoveInstance.GetGazeRays();
            var ray = eyeRays.left;
            gazeDirection = new Vector3(ray.direction.x, ray.direction.y, ray.direction.z);
            gazeDirection.Normalize();
#elif CVR_PUPIL
            gazeDirection = GameplayReferences.HMD.TransformDirection(localGazeDirection);
#elif CVR_TOBIIVR
            gazeDirection = _eyeTracker.LatestProcessedGazeData.CombinedGazeRayWorld.direction;
#elif CVR_VIVEPROEYE
            var ray = new Ray();
            if (ViveSR.anipal.Eye.SRanipal_Eye.GetGazeRay(ViveSR.anipal.Eye.GazeIndex.COMBINE, out ray))
            {
                gazeDirection = GameplayReferences.HMD.TransformDirection(ray.direction);
            }
#elif CVR_VARJO
            if (Varjo.VarjoPlugin.InitGaze())
            {
                var data = Varjo.VarjoPlugin.GetGaze();
                if (data.status != Varjo.VarjoPlugin.GazeStatus.INVALID)
                {
                    var ray = data.gaze;
                    gazeDirection = GameplayReferences.HMD.TransformDirection(new Vector3((float)ray.forward[0], (float)ray.forward[1], (float)ray.forward[2]));
                }
            }
#elif CVR_NEURABLE
            gazeDirection = Neurable.Core.NeurableUser.Instance.NeurableCam.GazeRay().direction;
#elif CVR_AH
            gazeDirection = ah_calibrator.GetGazeVector(filterType: FilterType.ExponentialMovingAverage);
#elif CVR_SNAPDRAGON
            gazeDirection = SvrManager.Instance.leftCamera.transform.TransformDirection(SvrManager.Instance.EyeDirection);
#endif
            return gazeDirection;
        }

        /// <summary>
        /// get the position of the eye gaze in normalized viewport space
        /// </summary>
        public Vector3 GetViewportGazePoint()
        {
            Vector2 screenGazePoint = Vector2.one * 0.5f;

#if CVR_FOVE //screenpoint

            //var normalizedPoint = FoveInterface.GetNormalizedViewportPosition(ray.GetPoint(1000), Fove.EFVR_Eye.Left); //Unity Plugin Version 1.3.1
            var normalizedPoint = GameplayReferences.HMDCameraComponent.WorldToViewportPoint(FoveInstance.GetGazeRays().left.GetPoint(1000));

            //Vector2 gazePoint = hmd.GetGazePoint();
            if (float.IsNaN(normalizedPoint.x))
            {
                return screenGazePoint;
            }

            screenGazePoint = new Vector2(normalizedPoint.x, normalizedPoint.y);
#elif CVR_PUPIL//screenpoint
            screenGazePoint = pupilViewportPosition;
#elif CVR_TOBIIVR
            screenGazePoint = GameplayReferences.HMDCameraComponent.WorldToViewportPoint(_eyeTracker.LatestProcessedGazeData.CombinedGazeRayWorld.GetPoint(1000));
#elif CVR_VIVEPROEYE
            var leftv2 = Vector2.zero;
            var rightv2 = Vector2.zero;

            bool leftSet = ViveSR.anipal.Eye.SRanipal_Eye.GetPupilPosition(ViveSR.anipal.Eye.EyeIndex.LEFT, out leftv2);
            bool rightSet = ViveSR.anipal.Eye.SRanipal_Eye.GetPupilPosition(ViveSR.anipal.Eye.EyeIndex.RIGHT, out rightv2);
            if (leftSet && !rightSet)
                screenGazePoint = leftv2;
            else if (!leftSet && rightSet)
                screenGazePoint = rightv2;
            else if (leftSet && rightSet)
                screenGazePoint = (leftv2 + rightv2) / 2;

#elif CVR_NEURABLE
            screenGazePoint = Neurable.Core.NeurableUser.Instance.NeurableCam.NormalizedFocalPoint;
#elif CVR_AH
            Vector3 x = ah_calibrator.GetGazeOrigin();
            Vector3 r = ah_calibrator.GetGazeVector();
            screenGazePoint = GameplayReferences.HMDCameraComponent.WorldToViewportPoint(x + 10 * r);
#elif CVR_SNAPDRAGON
            var worldgazeDirection = SvrManager.Instance.leftCamera.transform.TransformDirection(SvrManager.Instance.EyeDirection);
            screenGazePoint = GameplayReferences.HMDCameraComponent.WorldToScreenPoint(GameplayReferences.HMD.position + 10 * worldgazeDirection);
#endif
            return screenGazePoint;
        }
    }
}

public static class UnscaledTransformPoints
{
    public static Vector3 TransformPointUnscaled(this Transform transform, Vector3 position)
    {
        var localToWorldMatrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        return localToWorldMatrix.MultiplyPoint3x4(position);
    }

    public static Vector3 InverseTransformPointUnscaled(this Transform transform, Vector3 position)
    {
        var worldToLocalMatrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one).inverse;
        return worldToLocalMatrix.MultiplyPoint3x4(position);
    }
}