﻿using UnityEngine;
using UnityEditor;
using System.Collections;

namespace CognitiveVR
{
    public class CognitiveVR_NewProductPopup : PopupWindowContent
    {
        string productName = "";

        public override Vector2 GetWindowSize()
        {
            return new Vector2(292, 150);
        }

        public override void OnGUI(Rect rect)
        {
            if (CognitiveVR_Preferences.Instance.UserData.organizations.Length > 1)
            {
                GUILayout.Label("Current Organization: " + CognitiveVR_Preferences.Instance.SelectedOrganization.name);
            }

            //TODO if there are multiple organizations, add a label for which organization this will create the product for
            //or add a dropdown to change the current organization
            
            GUILayout.Label("New Product", EditorStyles.boldLabel);

            productName = CognitiveVR_SceneExportWindow.GhostTextField("MyProductName", "", productName);
            GUILayout.BeginHorizontal();

            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(productName));

            if (!string.IsNullOrEmpty(productName))
            {
                GUI.color = CognitiveVR_Settings.GreenButton;
                GUI.contentColor = Color.white;
            }

            if (GUILayout.Button("Create"))
            {
                RequestNewProduct();
                editorWindow.Close();
            }
            EditorGUI.EndDisabledGroup();

            GUI.color = Color.white;
            GUI.contentColor = Color.white;

            if (GUILayout.Button("Close"))
            {
                editorWindow.Close();                
            }

            GUILayout.EndHorizontal();
        }
        
        public void RequestNewProduct()
        {
            if (CognitiveVR_Settings.Instance == null)
            {
                Debug.Log("instance of settings window is null"); //when recompiling with the window open, instance loses it's reference
            }
            CognitiveVR_Settings.Instance.RequestNewProduct(productName);
        }
    }
}