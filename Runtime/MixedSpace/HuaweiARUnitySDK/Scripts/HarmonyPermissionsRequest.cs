namespace HuaweiARUnitySDK
{
    using System;
    using System.Collections.Generic;
    using HuaweiARInternal;
    using UnityEngine;
    using UnityEngine.OpenHarmony;

    public class HarmonyPermissionsRequest
    {
        private static HarmonyPermissionsRequest m_instance;

        ///@cond EXCLUDE_FROM_DOXYGEN
        /// <summary>
        /// Instance of this class.
        /// </summary>
        public static HarmonyPermissionsRequest Instance {
            get {
                if (m_instance == null)
                {
                    m_instance = new HarmonyPermissionsRequest();
                }
                return m_instance;
            }
        }

        public static void IsPermissionGranted(string permissionName, Action OnGranted = null, Action OnDenied = null)
        {
            OpenHarmonyPermissionAPI.RequestUserPermissions(new string[] { Permission.Camera, },
                granted =>
            {
                OnGranted?.Invoke();
            },
            denied =>
            {
                OnDenied?.Invoke();
            });
        }
    }
}
