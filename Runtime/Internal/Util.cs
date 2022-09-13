using UnityEngine;
using System;
using System.Text;
using System.Collections.Generic;

namespace Cognitive3D
{
	public static class Util
	{
        private const string LOG_TAG = "[COGNITIVE3D] ";

        public static void logWarning(string msg, UnityEngine.Object context = null)
        {
            if (Cognitive3D_Preferences.Instance.EnableLogging)
            {
                if (context != null)
                    Debug.LogWarning(LOG_TAG + msg, context);
                else
                    Debug.LogWarning(LOG_TAG + msg);
            }
        }

        // Internal logging.  These can be enabled by calling Util.setLogEnabled(true)
        public static void logDebug(string msg)
		{
			if (Cognitive3D_Preferences.Instance.EnableLogging)
			{
				Debug.Log(LOG_TAG + msg);
			}
		}

        // Internal logging.  These can be enabled by calling Util.setLogEnabled(true)
        public static void logDevelopment(string msg)
        {
            if (Cognitive3D_Preferences.Instance.EnableDevLogging)
            {
                Debug.Log("[COGNITIVE3D DEV] "+ msg);
            }
        }

        public static void logError(Exception e)
		{
			if (Cognitive3D_Preferences.Instance.EnableLogging)
			{
				Debug.LogException(e);
			}
		}
		
		public static void logError(string msg)
		{
			if (Cognitive3D_Preferences.Instance.EnableLogging)
			{
				Debug.LogError(LOG_TAG + msg);
			}
		}

        //static int uniqueId;
        ///// <summary>
        ///// this should be used instead of System.Guid.NewGuid(). these only need to be unique, not complicated
        ///// </summary>
        ///// <returns></returns>
        //public static string GetUniqueId()
        //{
        //    return uniqueId++.ToString();
        //}

        static DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        static double lastTime;
        static int lastFrame = -1;

        //timestamp can be cached per frame, reducing number of timespan calculations
        public static double Timestamp(int frame)
        {
            if (frame == lastFrame)
                return lastTime;
            TimeSpan span = DateTime.UtcNow - epoch;
            lastFrame = frame;
            lastTime = span.TotalSeconds;
            return span.TotalSeconds;
        }

        /// <summary>
		/// Get the Unix timestamp
		/// </summary>
		public static double Timestamp()
		{
			TimeSpan span = DateTime.UtcNow - epoch;
			return span.TotalSeconds;
		}

        //https://forum.unity3d.com/threads/how-to-load-an-array-with-jsonutility.375735/
        public static T[] GetJsonArray<T>(string json)
        {
            string newJson = "{\"array\":" + json + "}";
            Wrapper<T> wrapper = JsonUtility.FromJson<Wrapper<T>>(newJson);
            return wrapper.array;
        }

        //used for serializing object manifest data
        [Serializable]
        private class Wrapper<T>
        {
#pragma warning disable 0649
            public T[] array;
#pragma warning restore 0649
        }
    }
}