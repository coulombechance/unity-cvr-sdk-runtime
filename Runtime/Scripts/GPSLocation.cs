﻿using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Cognitive3D;

/// <summary>
/// Adds the starting GPS location to session properties at the start of the session
/// </summary>

namespace Cognitive3D.Components
{
    [AddComponentMenu("Cognitive3D/Components/GPS Location")]
    public class GPSLocation: AnalyticsComponentBase
    {
        public override void Cognitive3D_Init()
        {
            base.Cognitive3D_Init();

            if (!Input.location.isEnabledByUser)
            {
                return;
            }

            StartCoroutine(InitializeLocation());
        }

        IEnumerator InitializeLocation()
        {
            Input.location.Start(500,500);

            int maxWait = 20;
            while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
            {
                yield return new WaitForSeconds(0.5f);
                maxWait--;
            }
            if (Input.location.status == LocationServiceStatus.Initializing)
            {
                yield break;
            }
            else if (Input.location.status == LocationServiceStatus.Failed)
            {
                yield break;
            }

            // Access granted and location value could be retrieved
            Util.logDebug("MobileLocation::InitializeLocation\nLocation: " + Input.location.lastData.latitude + " " + Input.location.lastData.longitude + " " + Input.location.lastData.altitude);

            Cognitive3D_Manager.SetSessionProperty("c3d.geo.latitude", Input.location.lastData.latitude);
            Cognitive3D_Manager.SetSessionProperty("c3d.geo.longitude", Input.location.lastData.longitude);
            Cognitive3D_Manager.SetSessionProperty("c3d.geo.altitude", Input.location.lastData.altitude);

            if (!Cognitive3D_Preferences.Instance.TrackGPSLocation)
            {
                Input.location.Stop();
            }
        }
        
        public override string GetDescription()
        {
            return "Adds the starting GPS location to session properties";
        }

        public override bool GetWarning()
        {
#if UNITY_ANDROID || C3D_ARKIT || C3D_ARCORE || UNITY_IOS
            return true;
#else
            return false;
#endif
        }
    }
}
