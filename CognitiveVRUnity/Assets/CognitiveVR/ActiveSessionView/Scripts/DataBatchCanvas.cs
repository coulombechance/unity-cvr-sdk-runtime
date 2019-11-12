﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CognitiveVR.ActiveSession
{
    public class DataBatchCanvas : MonoBehaviour
    {
        public Text EventSendText;
        public Text GazeSendText;
        public Text FixationSendText;
        public Text DynamicSendText;
        public Text SensorSendText;

        void Start()
        {
            Instrumentation.OnCustomEventSend += Instrumentation_OnCustomEventSend;
            GazeCore.OnGazeSend += GazeCore_OnGazeSend;
            FixationCore.OnFixationSend += FixationCore_OnFixationSend;
            DynamicManager.OnDynamicObjectSend += DynamicManager_OnDynamicObjectSend;
            SensorRecorder.OnSensorSend += SensorRecorder_OnSensorSend;
        }

        float EventTimeSinceSend = -1;
        float GazeTimeSinceSend = -1;
        float FixationTimeSinceSend = -1;
        float DynamicTimeSinceSend = -1;
        float SensorTimeSinceSend = -1;

        public Color noDataColor;
        public Color normalColor;
        public Color justSentColor;
        string neverSentString = "No data";
        string notYetSentString = "Not yet sent";
        string justSentString = "Just sent!";

        float timeSinceLastTick;
        int frame = 0;
        private void Update()
        {
            timeSinceLastTick += Time.deltaTime;
            frame++;
            if (frame < 10) { return; }
            frame = 0;

            #region Events
            if (EventTimeSinceSend < 0)
            {
                if (CognitiveVR.Instrumentation.CachedEvents > 0)
                {
                    //has data, not sent
                    EventSendText.color = noDataColor;
                    EventSendText.text = notYetSentString;
                }
                else
                {
                    //no data
                    EventSendText.color = noDataColor;
                    EventSendText.text = neverSentString;
                }
            }
            else
            {
                //just sent or send X seconds ago
                UpdateText(EventSendText, ref EventTimeSinceSend);
            }
            #endregion

            #region Gaze
            if (GazeTimeSinceSend < 0)
            {
                if (CognitiveVR.GazeCore.CachedGaze > 0)
                {
                    GazeSendText.color = noDataColor;
                    GazeSendText.text = notYetSentString;
                }
                else
                {
                    GazeSendText.color = noDataColor;
                    GazeSendText.text = neverSentString;
                }
            }
            else
            {
                UpdateText(GazeSendText, ref GazeTimeSinceSend);
            }
            #endregion

            #region Fixations
            if (FixationTimeSinceSend < 0)
            {
                if (CognitiveVR.FixationCore.CachedFixations > 0)
                {
                    FixationSendText.color = noDataColor;
                    FixationSendText.text = notYetSentString;
                }
                else
                {
                    FixationSendText.color = noDataColor;
                    FixationSendText.text = neverSentString;
                }
            }
            else
            {
                UpdateText(FixationSendText, ref FixationTimeSinceSend);
            }
            #endregion

            #region Dynamics
            if (DynamicTimeSinceSend < 0)
            {
                if (CognitiveVR.DynamicManager.CachedSnapshots > 0)
                {
                    DynamicSendText.color = noDataColor;
                    DynamicSendText.text = notYetSentString;
                }
                else
                {
                    DynamicSendText.color = noDataColor;
                    DynamicSendText.text = neverSentString;
                }
            }
            else
            {
                UpdateText(DynamicSendText, ref DynamicTimeSinceSend);
            }
            #endregion

            #region Sensors
            if (SensorTimeSinceSend < 0)
            {
                if (CognitiveVR.SensorRecorder.CachedSensors > 0)
                {
                    SensorSendText.color = noDataColor;
                    SensorSendText.text = notYetSentString;
                }
                else
                {
                    SensorSendText.color = noDataColor;
                    SensorSendText.text = neverSentString;
                }
            }
            else
            {
                UpdateText(SensorSendText, ref SensorTimeSinceSend);
            }
            #endregion

            timeSinceLastTick = 0;
        }

        void UpdateText(Text t, ref float sendtime)
        {
            if (sendtime < 1)
            {
                sendtime += timeSinceLastTick;
                t.color = justSentColor;
                t.text = justSentString;
            }
            else
            {
                sendtime += timeSinceLastTick;
                t.color = normalColor;
                t.text = Mathf.Floor(sendtime).ToString() + "s ago";
            }
        }

        private void SensorRecorder_OnSensorSend()
        {
            SensorTimeSinceSend = 0;
        }

        private void DynamicManager_OnDynamicObjectSend()
        {
            DynamicTimeSinceSend = 0;
        }

        private void FixationCore_OnFixationSend()
        {
            FixationTimeSinceSend = 0;
        }

        private void GazeCore_OnGazeSend()
        {
            GazeTimeSinceSend = 0;
        }

        private void Instrumentation_OnCustomEventSend()
        {
            EventTimeSinceSend = 0;
        }

        private void OnDestroy()
        {
            Instrumentation.OnCustomEventSend -= Instrumentation_OnCustomEventSend;
            GazeCore.OnGazeSend -= GazeCore_OnGazeSend;
            FixationCore.OnFixationSend -= FixationCore_OnFixationSend;
            DynamicManager.OnDynamicObjectSend -= DynamicManager_OnDynamicObjectSend;
            SensorRecorder.OnSensorSend -= SensorRecorder_OnSensorSend;
        }
    }
}