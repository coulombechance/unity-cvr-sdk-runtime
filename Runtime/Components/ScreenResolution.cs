﻿using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Adds screen resolution to device info
/// </summary>

namespace Cognitive3D.Components
{
    [AddComponentMenu("Cognitive3D/Components/Screen Resolution")]
    public class ScreenResolution : AnalyticsComponentBase
    {
        public override void Cognitive3D_Init(Error initError)
        {
            if (initError != Error.None) { return; }
            base.Cognitive3D_Init(initError);
            Core.SetSessionProperty("c3d.device.screenresolution", Screen.height + " x " + Screen.width);
        }

        public override string GetDescription()
        {
            return "Include Screen Resolution as a Session Property";
        }
    }
}
