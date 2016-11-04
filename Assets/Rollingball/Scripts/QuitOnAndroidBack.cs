using UnityEngine;
using System.Collections;

namespace com.mh.physsim.rollingball
{
    public sealed class QuitOnAndroidBack : MonoBehaviour
    {

        void Update ()
        {
            if (Input.GetKeyUp(KeyCode.Escape))
            {
                Application.Quit();
            }
        }
    }

}


