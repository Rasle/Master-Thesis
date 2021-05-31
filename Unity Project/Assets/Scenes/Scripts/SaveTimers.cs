using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
using UnityEngine;

namespace NVIDIA.Flex
{
    [DisallowMultipleComponent]
    public class SaveTimers : MonoBehaviour
    {
        // FixedUpdate is called once per fixed timestep
        void FixedUpdate()
        {
            if (m_actor && idx < size)
            {
                total_timers[idx] = m_actor.container.timers.total;
                idx++;
            }

            if (idx == size && !saved)
            {
                SaveToFile(total_timers);
                saved = true;
                Debug.Log("Saved data to file");
            }
        }
        void OnEnable()
        {
            m_actor = GetComponent<FlexActor>();

            if (m_actor)
            {
                total_timers = new float[size];
            }
            
        }

        void SaveToFile(float[] arr)
        {
            string s = arr[0].ToString("R") + ",";

            for (int i = 1; i < size - 1; i++)
            {
                s += arr[i].ToString("R") + ",";
            }

            s += arr[size - 1].ToString("R");

            File.WriteAllText(path, s);
        }

        [SerializeField]
        int size = 100;
        [SerializeField]
        string path = "Performance/data.txt";

        bool saved = false;
        int idx = 0;
        float[] total_timers;
        FlexActor m_actor;
    }
}