﻿using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;

//this window pops up when there is a new version, this new version is not skipped and the remind date is valid (or manually checked)

namespace Cognitive3D
{
    public class UpdateSDKWindow : EditorWindow
    {
        static string newVersion;
        bool reminderSet = false;
        string sdkSummary;

        public static void Init(string version, string summary)
        {
            newVersion = version;
            UpdateSDKWindow window = (UpdateSDKWindow)EditorWindow.GetWindow(typeof(UpdateSDKWindow),true,"Cognitive3D Update");
            window.sdkSummary = summary;
            window.Show();
        }

        void OnGUI()
        {
            GUI.skin.label.richText = true;
            GUILayout.Label("Cognitive3D SDK - New Version", EditorCore.HeaderStyle);
            GUILayout.Label("Current Version:<b>" + Cognitive3D_Manager.SDK_VERSION + "</b>");
            GUILayout.Label("New Version:<b>" + newVersion + "</b>");

            GUILayout.Label("Notes", EditorCore.HeaderStyle);
            GUI.skin.label.wordWrap = true;
            GUILayout.Label(sdkSummary);

            GUILayout.FlexibleSpace();

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            GUILayout.BeginVertical();

            GUI.color = EditorCore.GreenButton;

            if (GUILayout.Button("Download Latest Version", GUILayout.Height(40), GUILayout.MaxWidth(300)))
            {
                Application.OpenURL(CognitiveStatics.GITHUB_RELEASES);
            }

            GUI.color = Color.white;

            GUILayout.EndVertical();

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();



            GUILayout.FlexibleSpace();

            GUILayout.Space(10);
            GUILayout.Box("", new GUILayoutOption[] { GUILayout.ExpandWidth(true), GUILayout.Height(1) });
            GUILayout.Space(5);

            GUILayout.BeginHorizontal();



            if (GUILayout.Button("Skip this version", GUILayout.MaxWidth(200)))
            {
                EditorPrefs.SetString("c3d_skipVersion", newVersion);
                Close();
            }

            if (GUILayout.Button("Remind me next week", GUILayout.MaxWidth(300)))
            {
                reminderSet = true;
                EditorPrefs.SetString("c3d_updateRemindDate", System.DateTime.UtcNow.AddDays(7).ToString("dd-MM-yyyy"));

                Close();
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(5);
        }

        void OnDestroy()
        {
            if (!reminderSet)
            {
                EditorPrefs.SetString("c3d_updateRemindDate", System.DateTime.UtcNow.AddDays(1).ToString("dd-MM-yyyy"));
            }
        }
    }
}